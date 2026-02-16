using ModManager.Core.Models;
using Serilog;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ModManager.Core.Services;

/// <summary>
/// Service for validating and parsing mod manifests
/// </summary>
public class ManifestService
{
    private readonly ILogger _logger;

    // Regex patterns for validation
    private static readonly Regex IdPattern = new Regex(@"^[a-z0-9_.-]+$", RegexOptions.Compiled);
    private static readonly Regex SemanticVersionPattern = new Regex(@"^\d+\.\d+\.\d+(-[a-zA-Z0-9.-]+)?(\+[a-zA-Z0-9.-]+)?$", RegexOptions.Compiled);

    public ManifestService(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Validates a mod manifest
    /// </summary>
    public ManifestValidationResult ValidateManifest(ModManifest manifest)
    {
        var result = new ManifestValidationResult();

        try
        {
            // Validate required fields
            if (string.IsNullOrWhiteSpace(manifest.Id))
            {
                result.AddError("ID is required");
            }
            else if (!IdPattern.IsMatch(manifest.Id))
            {
                result.AddError($"ID '{manifest.Id}' is invalid. Must contain only lowercase letters, numbers, dots, hyphens, and underscores.");
            }

            if (string.IsNullOrWhiteSpace(manifest.Name))
            {
                result.AddError("Name is required");
            }

            if (string.IsNullOrWhiteSpace(manifest.Version))
            {
                result.AddError("Version is required");
            }
            else if (!SemanticVersionPattern.IsMatch(manifest.Version))
            {
                result.AddError($"Version '{manifest.Version}' is not a valid semantic version (e.g., '1.2.3' or '1.2.3-beta')");
            }

            if (string.IsNullOrWhiteSpace(manifest.Author))
            {
                result.AddError("Author is required");
            }

            if (string.IsNullOrWhiteSpace(manifest.Entry))
            {
                result.AddError("Entry point is required");
            }

            // Validate dependencies
            foreach (var dependency in manifest.Dependencies)
            {
                var depResult = ValidateDependency(dependency);
                result.Merge(depResult);
            }

            // Validate source if present
            if (manifest.Source != null)
            {
                var sourceResult = ValidateSource(manifest.Source);
                result.Merge(sourceResult);
            }

            // Validate checksum if present
            if (!string.IsNullOrEmpty(manifest.Checksum))
            {
                if (!IsValidChecksum(manifest.Checksum))
                {
                    result.AddError($"Checksum '{manifest.Checksum}' is not a valid SHA256 hash");
                }
            }

            // Validate files list
            if (manifest.Files.Count == 0)
            {
                result.AddWarning("No files listed in manifest");
            }

            result.IsValid = result.Errors.Count == 0;
            result.Success = result.IsValid;
            return result;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error validating manifest");
            result.AddError("Validation failed due to an internal error");
            return result;
        }
    }

    /// <summary>
    /// Parses a manifest from JSON string
    /// </summary>
    public ManifestParseResult ParseManifest(string json)
    {
        var result = new ManifestParseResult();

        try
        {
            var manifest = JsonSerializer.Deserialize<ModManifest>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = true,
                ReadCommentHandling = JsonCommentHandling.Skip
            });

            if (manifest == null)
            {
                result.AddError("Failed to parse manifest JSON");
                return result;
            }

            result.Manifest = manifest;
            result.Success = true;

            _logger.Debug("Successfully parsed manifest for mod: {ModId}", manifest.Id ?? "unknown");
            return result;
        }
        catch (JsonException ex)
        {
            result.AddError($"JSON parsing error: {ex.Message}");
            _logger.Warning(ex, "Failed to parse manifest JSON");
        }
        catch (Exception ex)
        {
            result.AddError($"Unexpected error: {ex.Message}");
            _logger.Error(ex, "Unexpected error parsing manifest");
        }

        return result;
    }

    /// <summary>
    /// Legacy async wrapper for callers expecting async
    /// </summary>
    public Task<ManifestParseResult> ParseManifestAsync(string json)
    {
        var result = ParseManifest(json);
        return Task.FromResult(result);
    }

    /// <summary>
    /// Parses and validates a manifest from file
    /// </summary>
    public async Task<ManifestValidationResult> ParseAndValidateManifestAsync(string manifestPath)
    {
        try
        {
            if (!File.Exists(manifestPath))
            {
                var validationResult = new ManifestValidationResult();
                validationResult.AddError($"Manifest file not found: {manifestPath}");
                return validationResult;
            }

            var json = await File.ReadAllTextAsync(manifestPath);
            var parseResult = await ParseManifestAsync(json);

            if (parseResult.Success && parseResult.Manifest != null)
            {
                var validation = ValidateManifest(parseResult.Manifest);
                return validation;
            }

            var finalResult = new ManifestValidationResult();
            finalResult.AddErrors(parseResult.Errors);
            return finalResult;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error reading manifest file: {Path}", manifestPath);
            var result = new ManifestValidationResult();
            result.AddError("Failed to read manifest file");
            return result;
        }
    }

    /// <summary>
    /// Creates a default manifest for a mod
    /// </summary>
    public ModManifest CreateDefaultManifest(string modId, string name, string version, string author)
    {
        return new ModManifest
        {
            Id = modId,
            Name = name,
            Version = version,
            Author = author,
            Description = string.Empty,
            Entry = $"{modId}.dll",
            Files = new List<string> { $"{modId}.dll" },
            Dependencies = new List<ModDependency>(),
            CompatibleBepInEx = ">=5.4.0",
            Source = new ModSource(),
            Tags = new List<string>()
        };
    }

    /// <summary>
    /// Serializes a manifest to JSON
    /// </summary>
    public string SerializeManifest(ModManifest manifest)
    {
        try
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            return JsonSerializer.Serialize(manifest, options);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error serializing manifest");
            throw new InvalidOperationException("Failed to serialize manifest", ex);
        }
    }

    /// <summary>
    /// Saves a manifest to file
    /// </summary>
    public async Task<bool> SaveManifestAsync(ModManifest manifest, string manifestPath)
    {
        try
        {
            var json = SerializeManifest(manifest);
            await File.WriteAllTextAsync(manifestPath, json);
            _logger.Information("Saved manifest: {Path}", manifestPath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save manifest: {Path}", manifestPath);
            return false;
        }
    }

    private ManifestValidationResult ValidateDependency(ModDependency dependency)
    {
        var result = new ManifestValidationResult();

        if (string.IsNullOrWhiteSpace(dependency.Id))
        {
            result.AddError("Dependency ID is required");
        }
        else if (!IdPattern.IsMatch(dependency.Id))
        {
            result.AddError($"Dependency ID '{dependency.Id}' is invalid");
        }

        if (!string.IsNullOrEmpty(dependency.MinVersion) && !SemanticVersionPattern.IsMatch(dependency.MinVersion))
        {
            result.AddError($"Dependency minimum version '{dependency.MinVersion}' is not a valid semantic version");
        }

        return result;
    }

    private ManifestValidationResult ValidateSource(ModSource source)
    {
        var result = new ManifestValidationResult();

        if (string.IsNullOrWhiteSpace(source.Type))
        {
            result.AddError("Source type is required");
        }
        else
        {
            var validTypes = new[] { "thunderstore", "github", "local", "url" };
            if (!validTypes.Contains(source.Type.ToLowerInvariant()))
            {
                result.AddError($"Source type '{source.Type}' is not valid. Valid types: {string.Join(", ", validTypes)}");
            }
        }

        if (!string.IsNullOrEmpty(source.Url))
        {
            if (!Uri.TryCreate(source.Url, UriKind.Absolute, out var uri))
            {
                result.AddError($"Source URL '{source.Url}' is not a valid absolute URL");
            }
        }

        return result;
    }

    private bool IsValidChecksum(string checksum)
    {
        // SHA256 should be 64 hex characters
        return checksum.Length == 64 && checksum.All(c => char.IsLetterOrDigit(c));
    }
}

/// <summary>
/// Result of manifest validation
/// </summary>
public class ManifestValidationResult
{
    public bool Success { get; set; }
    public bool IsValid { get; set; }
    public ModManifest? Manifest { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();

    public void AddError(string error) => Errors.Add(error);
    public void AddWarning(string warning) => Warnings.Add(warning);
    public void AddErrors(IEnumerable<string> errors) => Errors.AddRange(errors);
    public void AddWarnings(IEnumerable<string> warnings) => Warnings.AddRange(warnings);

    public void Merge(ManifestValidationResult other)
    {
        AddErrors(other.Errors);
        AddWarnings(other.Warnings);
    }
}

/// <summary>
/// Result of manifest parsing
/// </summary>
public class ManifestParseResult
{
    public bool Success { get; set; }
    public ModManifest? Manifest { get; set; }
    public List<string> Errors { get; } = new();

    public void AddError(string error) => Errors.Add(error);
    public void AddErrors(IEnumerable<string> errors) => Errors.AddRange(errors);
}
