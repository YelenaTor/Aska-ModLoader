using ModManager.Core.Models;
using Serilog;

namespace ModManager.Core.Services;

/// <summary>
/// Service for handling mod identity normalization and validation
/// </summary>
public static class ModIdentityService
{
    /// <summary>
    /// Gets the canonical mod ID from mod information
    /// Priority: BepInEx GUID > Manifest ID > Assembly Name
    /// </summary>
    public static string GetCanonicalModId(ModInfo mod)
    {
        // Priority 1: BepInEx GUID (most reliable)
        if (mod.BepInExMetadata != null && !string.IsNullOrEmpty(mod.BepInExMetadata.Guid))
        {
            return NormalizeId(mod.BepInExMetadata.Guid);
        }

        // Priority 2: Manifest ID
        if (!string.IsNullOrEmpty(mod.Id) && mod.Id != "Unknown")
        {
            return NormalizeId(mod.Id);
        }

        // Priority 3: Assembly name (fallback)
        var assemblyName = Path.GetFileNameWithoutExtension(mod.DllPath);
        return NormalizeId(assemblyName);
    }

    /// <summary>
    /// Normalizes a mod ID to canonical form
    /// </summary>
    public static string NormalizeId(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return "unknown";
        }

        // Convert to lowercase and trim
        var normalized = id.ToLowerInvariant().Trim();

        // Replace spaces and underscores with hyphens for consistency
        normalized = normalized.Replace(' ', '-').Replace('_', '-');

        // Remove invalid characters (keep only letters, numbers, dots, hyphens)
        var validChars = normalized.Where(c => char.IsLetterOrDigit(c) || c == '.' || c == '-').ToArray();
        normalized = new string(validChars);

        // Remove consecutive dots/hyphens
        normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"[.\-]{2,}", "-");

        // Remove leading/trailing dots/hyphens
        normalized = normalized.Trim('.', '-');

        return string.IsNullOrEmpty(normalized) ? "unknown" : normalized;
    }

    /// <summary>
    /// Validates that a mod ID is in canonical format
    /// </summary>
    public static bool IsValidCanonicalId(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return false;
        }

        // Must match pattern: lowercase letters, numbers, dots, hyphens
        return System.Text.RegularExpressions.Regex.IsMatch(id, @"^[a-z0-9.\-]+$");
    }

    /// <summary>
    /// Checks if two mod IDs are equivalent (case-insensitive)
    /// </summary>
    public static bool AreEquivalentIds(string id1, string id2)
    {
        return string.Equals(NormalizeId(id1), NormalizeId(id2), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Detects duplicate mod IDs in a collection
    /// </summary>
    public static Dictionary<string, List<ModInfo>> DetectDuplicateIds(IEnumerable<ModInfo> mods)
    {
        var duplicates = new Dictionary<string, List<ModInfo>>();
        var idGroups = new Dictionary<string, List<ModInfo>>(StringComparer.OrdinalIgnoreCase);

        foreach (var mod in mods)
        {
            var canonicalId = GetCanonicalModId(mod);
            
            if (!idGroups.TryGetValue(canonicalId, out var group))
            {
                group = new List<ModInfo>();
                idGroups[canonicalId] = group;
            }
            
            group.Add(mod);
        }

        // Find groups with more than one mod
        foreach (var kvp in idGroups.Where(g => g.Value.Count > 1))
        {
            duplicates[kvp.Key] = kvp.Value;
        }

        return duplicates;
    }

    /// <summary>
    /// Resolves conflicts between duplicate mod IDs
    /// Returns the mod that should be kept based on priority rules
    /// </summary>
    public static ModInfo ResolveDuplicateConflict(List<ModInfo> conflictingMods)
    {
        if (conflictingMods.Count == 0)
        {
            throw new ArgumentException("No mods provided");
        }

        if (conflictingMods.Count == 1)
        {
            return conflictingMods[0];
        }

        // Priority rules for conflict resolution:
        // 1. Enabled mods over disabled mods
        // 2. Mods with BepInEx metadata over those without
        // 3. Higher version over lower version
        // 4. More recent installation date

        var enabledMods = conflictingMods.Where(m => m.IsEnabled).ToList();
        var candidates = enabledMods.Any() ? enabledMods : conflictingMods;

        var withMetadata = candidates.Where(m => m.BepInExMetadata != null).ToList();
        candidates = withMetadata.Any() ? withMetadata : candidates;

        // Sort by version (descending) then by install date (descending)
        var sorted = candidates.OrderByDescending(m => VersionService.ParseVersionOrDefault(m.Version))
                              .ThenByDescending(m => m.InstallDate)
                              .ToList();

        return sorted.First();
    }

    /// <summary>
    /// Updates mod IDs to canonical form and detects conflicts
    /// </summary>
    public static ModIdentityValidationResult ValidateAndNormalizeIds(IEnumerable<ModInfo> mods)
    {
        var result = new ModIdentityValidationResult();
        var modList = mods.ToList();

        // Normalize all mod IDs
        foreach (var mod in modList)
        {
            var originalId = mod.Id;
            var canonicalId = GetCanonicalModId(mod);
            
            if (originalId != canonicalId)
            {
                result.NormalizedMods.Add(new ModIdNormalization
                {
                    Mod = mod,
                    OriginalId = originalId,
                    CanonicalId = canonicalId
                });
                
                mod.Id = canonicalId;
            }

            if (!IsValidCanonicalId(canonicalId))
            {
                result.InvalidIds.Add(new InvalidModId
                {
                    Mod = mod,
                    Id = canonicalId,
                    Reason = "Contains invalid characters"
                });
            }
        }

        // Detect duplicates
        var duplicates = DetectDuplicateIds(modList);
        foreach (var kvp in duplicates)
        {
            var conflict = new DuplicateIdConflict
            {
                CanonicalId = kvp.Key,
                ConflictingMods = kvp.Value.ToList()
            };

            try
            {
                conflict.ResolvedMod = ResolveDuplicateConflict(kvp.Value);
                result.ResolvedConflicts.Add(conflict);
            }
            catch (Exception ex)
            {
                result.UnresolvedConflicts.Add(conflict);
                result.AddError($"Failed to resolve conflict for ID '{kvp.Key}': {ex.Message}");
            }
        }

        result.IsValid = result.InvalidIds.Count == 0 && result.UnresolvedConflicts.Count == 0;
        return result;
    }

    /// <summary>
    /// Creates a mod ID from a filename (for loose DLLs)
    /// </summary>
    public static string CreateIdFromFilename(string filename)
    {
        var nameWithoutExtension = Path.GetFileNameWithoutExtension(filename);
        return NormalizeId(nameWithoutExtension);
    }

    /// <summary>
    /// Validates that mod IDs are unique within a collection
    /// </summary>
    public static bool ValidateUniqueIds(IEnumerable<ModInfo> mods)
    {
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        foreach (var mod in mods)
        {
            var canonicalId = GetCanonicalModId(mod);
            if (!seenIds.Add(canonicalId))
            {
                return false; // Duplicate found
            }
        }
        
        return true;
    }
}

/// <summary>
/// Result of mod identity validation
/// </summary>
public class ModIdentityValidationResult
{
    public bool IsValid { get; set; }
    public List<ModIdNormalization> NormalizedMods { get; set; } = new();
    public List<InvalidModId> InvalidIds { get; set; } = new();
    public List<DuplicateIdConflict> ResolvedConflicts { get; set; } = new();
    public List<DuplicateIdConflict> UnresolvedConflicts { get; set; } = new();
    public List<string> Errors { get; set; } = new();

    public void AddError(string error) => Errors.Add(error);
    public bool HasIssues => InvalidIds.Count > 0 || UnresolvedConflicts.Count > 0 || Errors.Count > 0;
}

/// <summary>
/// Represents a mod ID normalization
/// </summary>
public class ModIdNormalization
{
    public ModInfo Mod { get; set; } = null!;
    public string OriginalId { get; set; } = string.Empty;
    public string CanonicalId { get; set; } = string.Empty;
}

/// <summary>
/// Represents an invalid mod ID
/// </summary>
public class InvalidModId
{
    public ModInfo Mod { get; set; } = null!;
    public string Id { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// Represents a duplicate ID conflict
/// </summary>
public class DuplicateIdConflict
{
    public string CanonicalId { get; set; } = string.Empty;
    public List<ModInfo> ConflictingMods { get; set; } = new();
    public ModInfo? ResolvedMod { get; set; }
}
