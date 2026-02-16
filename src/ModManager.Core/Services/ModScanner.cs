using Mono.Cecil;
using ModManager.Core.Interfaces;
using ModManager.Core.Models;
using Serilog;
using System.Security.Cryptography;
using System.Text.Json;

namespace ModManager.Core.Services;

/// <summary>
/// Service for scanning and analyzing installed mods
/// </summary>
public class ModScanner
{
    private readonly ILogger _logger;

    public ModScanner(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Scans the BepInEx plugins directory for installed mods
    /// </summary>
    public async Task<IEnumerable<ModInfo>> ScanModsAsync(string pluginsPath)
    {
        var mods = new List<ModInfo>();

        if (!Directory.Exists(pluginsPath))
        {
            _logger.Warning("Plugins directory not found: {Path}", pluginsPath);
            return mods;
        }

        try
        {
            // Track processed mod IDs to prevent duplicates
            var processedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Step 1: Process directories (manifest-based mods)
            var directories = Directory.GetDirectories(pluginsPath);
            foreach (var dir in directories)
            {
                var manifestPath = Path.Combine(dir, "manifest.json");
                if (File.Exists(manifestPath))
                {
                    try
                    {
                        var modFromManifest = await LoadModFromManifestAsync(manifestPath);
                        if (modFromManifest != null && processedIds.Add(modFromManifest.Id))
                        {
                            mods.Add(modFromManifest);
                            continue;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning(ex, "Failed to load manifest: {Path}", manifestPath);
                    }
                }

                // No manifest or manifest failed: try to locate DLLs inside folder
                var dllCandidates = Directory.GetFiles(dir, "*.dll", SearchOption.TopDirectoryOnly)
                    .Concat(Directory.GetFiles(dir, "*.dll.disabled", SearchOption.TopDirectoryOnly))
                    .ToList();

                if (dllCandidates.Count == 0)
                {
                    continue;
                }

                var primaryDll = SelectPrimaryDll(dir, dllCandidates);
                if (!string.IsNullOrEmpty(primaryDll))
                {
                    try
                    {
                        var analyzed = await AnalyzeModAsync(primaryDll);
                        if (analyzed != null && processedIds.Add(analyzed.Id))
                        {
                            mods.Add(analyzed);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning(ex, "Failed to analyze mod folder {Folder}", dir);
                    }
                }
            }

            // Step 2: Process root-level loose DLLs (no recursion)
            var files = Directory.GetFiles(pluginsPath);
            foreach (var file in files)
            {
                if (IsLooseDll(file))
                {
                    try
                    {
                        var modInfo = await AnalyzeModAsync(file);
                        if (modInfo != null && processedIds.Add(modInfo.Id))
                        {
                            mods.Add(modInfo);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning(ex, "Failed to analyze loose DLL: {Path}", file);
                    }
                }
            }

            _logger.Information("Scanned {Count} mods from {Path}", mods.Count, pluginsPath);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error scanning mods directory: {Path}", pluginsPath);
        }

        return mods;
    }

    /// <summary>
    /// Analyzes a DLL file to extract mod information
    /// </summary>
    private async Task<ModInfo?> AnalyzeModAsync(string dllPath)
    {
        try
        {
            var modInfo = new ModInfo
            {
                DllPath = dllPath,
                InstallPath = Path.GetDirectoryName(dllPath) ?? string.Empty,
                IsEnabled = !IsDisabledFile(dllPath),
                InstallDate = File.GetCreationTime(dllPath),
                LastUpdated = File.GetLastWriteTime(dllPath),
                Checksum = await CalculateChecksumAsync(dllPath)
            };

            // Use Mono.Cecil to analyze the assembly without loading it
            // Skip analysis for disabled files that might not be valid assemblies
            AssemblyDefinition? assembly = null;
            if (modInfo.IsEnabled)
            {
                try
                {
                    assembly = AssemblyDefinition.ReadAssembly(dllPath);
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Failed to analyze assembly with Mono.Cecil: {Path}", dllPath);
                    // Continue with basic info even if assembly analysis fails
                }
            }

            if (assembly != null)
            {
                // Extract BepInEx metadata
                modInfo.BepInExMetadata = ExtractBepInExMetadata(assembly);
                
                // Set basic info from metadata if available
                if (modInfo.BepInExMetadata != null)
                {
                    modInfo.Id = modInfo.BepInExMetadata.Guid;
                    modInfo.Name = modInfo.BepInExMetadata.Name;
                    modInfo.Version = modInfo.BepInExMetadata.Version;
                    modInfo.Author = "Unknown"; // Not available in BepInEx metadata
                }
                else
                {
                    // Fallback to assembly name
                    modInfo.Id = assembly.Name.Name;
                    modInfo.Name = assembly.Name.Name;
                    modInfo.Version = assembly.Name.Version?.ToString() ?? "1.0.0";
                    modInfo.Author = "Unknown";
                }
            }
            else
            {
                // Fallback for disabled files or files that couldn't be analyzed
                var fileName = Path.GetFileNameWithoutExtension(dllPath);
                modInfo.Id = fileName;
                modInfo.Name = fileName;
                modInfo.Version = "1.0.0";
                modInfo.Author = "Unknown";
            }

            // Check for manifest.json in the same directory
            var manifestPath = Path.Combine(modInfo.InstallPath, "manifest.json");
            if (File.Exists(manifestPath))
            {
                await UpdateFromManifestAsync(modInfo, manifestPath);
            }

            return modInfo;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to analyze DLL: {Path}", dllPath);
            return null;
        }
    }

    /// <summary>
    /// Loads mod information from a manifest.json file
    /// </summary>
    private async Task<ModInfo?> LoadModFromManifestAsync(string manifestPath)
    {
        try
        {
            var json = await File.ReadAllTextAsync(manifestPath);
            var manifest = JsonSerializer.Deserialize<ModManifest>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (manifest == null)
            {
                return null;
            }

            var modInfo = new ModInfo
            {
                Id = manifest.Id,
                Name = manifest.Name,
                Version = manifest.Version,
                Author = manifest.Author,
                Description = manifest.Description,
                Dependencies = manifest.Dependencies,
                Source = manifest.Source,
                Checksum = manifest.Checksum,
                InstallPath = Path.GetDirectoryName(manifestPath) ?? string.Empty,
                InstallDate = File.GetCreationTime(manifestPath),
                LastUpdated = File.GetLastWriteTime(manifestPath),
                IsEnabled = !IsDisabledFile(manifestPath)
            };

            // Try to find the main DLL
            var dllPath = Path.Combine(modInfo.InstallPath, manifest.Entry);
            if (File.Exists(dllPath))
            {
                modInfo.DllPath = dllPath;
                modInfo.Checksum ??= await CalculateChecksumAsync(dllPath);
                modInfo.IsEnabled = !IsDisabledFile(dllPath);
                
                // Extract BepInEx metadata from the DLL
                try
                {
                    var assembly = AssemblyDefinition.ReadAssembly(dllPath);
                    modInfo.BepInExMetadata = ExtractBepInExMetadata(assembly);
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Failed to extract BepInEx metadata from {Path}", dllPath);
                }
            }
            else
            {
                // Check for disabled version
                var disabledDllPath = dllPath + ".disabled";
                if (File.Exists(disabledDllPath))
                {
                    modInfo.DllPath = disabledDllPath;
                    modInfo.IsEnabled = false;
                    modInfo.Checksum ??= await CalculateChecksumAsync(disabledDllPath);
                }
            }

            return modInfo;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to load manifest: {Path}", manifestPath);
            return null;
        }
    }

    /// <summary>
    /// Updates mod info from manifest file
    /// </summary>
    private async Task UpdateFromManifestAsync(ModInfo modInfo, string manifestPath)
    {
        try
        {
            var json = await File.ReadAllTextAsync(manifestPath);
            var manifest = JsonSerializer.Deserialize<ModManifest>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (manifest != null)
            {
                modInfo.Name = manifest.Name;
                modInfo.Version = manifest.Version;
                modInfo.Author = manifest.Author;
                modInfo.Description = manifest.Description;
                modInfo.Dependencies = manifest.Dependencies;
                modInfo.Source = manifest.Source;
                modInfo.Checksum = manifest.Checksum ?? modInfo.Checksum;
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to update from manifest: {Path}", manifestPath);
        }
    }

    /// <summary>
    /// Extracts BepInEx metadata from an assembly
    /// </summary>
    private BepInExMetadata? ExtractBepInExMetadata(AssemblyDefinition assembly)
    {
        try
        {
            // Look for BepInEx plugin attributes
            var pluginAttribute = assembly.CustomAttributes
                .FirstOrDefault(attr => attr.AttributeType.FullName == "BepInEx.BepInExPlugin");

            if (pluginAttribute == null)
            {
                return null;
            }

            var metadata = new BepInExMetadata();

            // Extract GUID
            var guidArg = pluginAttribute.ConstructorArguments.FirstOrDefault();
            if (guidArg.Value is string guid)
            {
                metadata.Guid = guid;
            }

            // Extract Name
            var nameArg = pluginAttribute.ConstructorArguments.Skip(1).FirstOrDefault();
            if (nameArg.Value is string name)
            {
                metadata.Name = name;
            }

            // Extract Version
            var versionArg = pluginAttribute.ConstructorArguments.Skip(2).FirstOrDefault();
            if (versionArg.Value is string version)
            {
                metadata.Version = version;
            }

            // Look for BepInEx dependency attribute
            var dependencyAttributes = assembly.CustomAttributes
                .Where(attr => attr.AttributeType.FullName == "BepInEx.BepInDependency");

            foreach (var dependencyAttribute in dependencyAttributes)
            {
                var bepinexVersionArg = dependencyAttribute.ConstructorArguments.FirstOrDefault();
                if (bepinexVersionArg.Value is string bepinexVersion)
                {
                    metadata.BepInExVersion = bepinexVersion;
                    break;
                }
            }

            // Look for BepInEx process attribute
            var processAttribute = assembly.CustomAttributes
                .FirstOrDefault(attr => attr.AttributeType.FullName == "BepInEx.BepInProcess");

            if (processAttribute != null)
            {
                var processArg = processAttribute.ConstructorArguments.FirstOrDefault();
                if (processArg.Value is string processName)
                {
                    metadata.ProcessNames.Add(processName);
                }
            }

            return metadata;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to extract BepInEx metadata");
            return null;
        }
    }

    /// <summary>
    /// Calculates SHA256 checksum of a file
    /// </summary>
    private async Task<string> CalculateChecksumAsync(string filePath)
    {
        try
        {
            using var sha256 = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            var hash = await sha256.ComputeHashAsync(stream);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to calculate checksum for {Path}", filePath);
            return string.Empty;
        }
    }

    /// <summary>
    /// Checks if a file is a loose DLL (directly in plugins root)
    /// </summary>
    private bool IsLooseDll(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        return fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".dll.disabled", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks if a file is disabled (has .disabled extension)
    /// </summary>
    private bool IsDisabledFile(string filePath)
    {
        return Path.GetExtension(filePath).Equals(".disabled", StringComparison.OrdinalIgnoreCase);
    }

    private string? SelectPrimaryDll(string directory, List<string> dllCandidates)
    {
        if (dllCandidates.Count == 0)
        {
            return null;
        }

        // Prefer DLL matching directory name
        var directoryName = Path.GetFileName(directory);
        var match = dllCandidates.FirstOrDefault(d => Path.GetFileNameWithoutExtension(d).Equals(directoryName, StringComparison.OrdinalIgnoreCase));
        return match ?? dllCandidates.First();
    }

    private List<ModDependency> ExtractDependenciesFromAssembly(AssemblyDefinition assembly)
    {
        var dependencies = new List<ModDependency>();

        var dependencyAttributes = assembly.CustomAttributes
            .Where(attr => attr.AttributeType.FullName == "BepInEx.BepInDependency");

        foreach (var attr in dependencyAttributes)
        {
            if (attr.ConstructorArguments.Count == 0)
            {
                continue;
            }

            var guid = attr.ConstructorArguments[0].Value as string ?? string.Empty;
            var versionRange = attr.ConstructorArguments.Count > 1
                ? attr.ConstructorArguments[1].Value as string ?? ">=0.0.0"
                : ">=0.0.0";

            if (string.IsNullOrWhiteSpace(guid))
            {
                continue;
            }

            dependencies.Add(new ModDependency
            {
                Id = guid,
                MinVersion = versionRange,
                Optional = false
            });
        }

        return dependencies;
    }
}
