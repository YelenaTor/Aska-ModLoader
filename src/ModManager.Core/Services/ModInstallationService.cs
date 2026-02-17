using ModManager.Core.Interfaces;
using ModManager.Core.Models;
using Serilog;
using System.IO.Compression;
using System.Security.Cryptography;

namespace ModManager.Core.Services;

/// <summary>
/// Service for installing mods from ZIP files
/// </summary>
public class ModInstallationService
{
    private readonly ILogger _logger;
    private readonly ManifestService _manifestService;
    private readonly FileOperationsService _fileOps;
    private readonly string _pluginsPath;
    private readonly string _tempRoot;

    public ModInstallationService(
        ILogger logger, 
        ManifestService manifestService, 
        FileOperationsService fileOps,
        string askaPath)
    {
        _logger = logger;
        _manifestService = manifestService;
        _fileOps = fileOps;
        _pluginsPath = Path.Combine(askaPath, "BepInEx", "plugins");
        _tempRoot = Path.Combine(Path.GetTempPath(), "ModManager", "InstallSessions");
        Directory.CreateDirectory(_tempRoot);
    }

    /// <summary>
    /// Installs a mod from a ZIP file
    /// </summary>
    public async Task<InstallationResult> InstallFromZipAsync(
        string zipPath,
        bool overwrite = false,
        Func<ModManifest, Task<DependencyValidationOutcome>>? dependencyValidator = null)
    {
        var result = new InstallationResult();
        var sessionTempPath = Path.Combine(_tempRoot, Guid.NewGuid().ToString());
        Directory.CreateDirectory(sessionTempPath);
        var stagingPath = Path.Combine(sessionTempPath, "staging");
        Directory.CreateDirectory(stagingPath);
        string? backupPath = null;
        var modPath = string.Empty;

        ModManifest? manifest = null;
        try
        {
            if (!File.Exists(zipPath))
            {
                result.AddError($"ZIP file not found: {zipPath}");
                return result;
            }

            if (_fileOps.IsGameRunning())
            {
                result.AddError("Cannot install mods while ASKA is running. Please close the game and try again.");
                return result;
            }

            _logger.Information("Starting mod installation from: {ZipPath}", zipPath);

            // Extract ZIP to temporary directory
            var extractResult = ExtractZip(zipPath, sessionTempPath);
            if (!extractResult.Success)
            {
                result.AddErrors(extractResult.Errors);
                return result;
            }

            // Find and validate manifest
            var manifestResult = await FindAndValidateManifestAsync(extractResult.ExtractedPath!);
            if (!manifestResult.Success || manifestResult.Manifest == null)
            {
                result.AddErrors(manifestResult.Errors);
                return result;
            }

            manifest = manifestResult.Manifest;

            if (manifest == null || string.IsNullOrWhiteSpace(manifest.Id))
            {
                result.AddError("Manifest is missing required Id");
                return result;
            }

            if (string.IsNullOrWhiteSpace(manifest.Entry))
            {
                // Auto-detect entry from extracted DLL files
                var extractedDir = Path.GetDirectoryName(
                    Directory.GetFiles(Path.GetDirectoryName(zipPath) ?? sessionTempPath, "manifest.json", SearchOption.AllDirectories).FirstOrDefault()
                ) ?? sessionTempPath;
                var dlls = Directory.GetFiles(sessionTempPath, "*.dll", SearchOption.AllDirectories);
                if (dlls.Length > 0)
                {
                    manifest.Entry = Path.GetFileName(dlls[0]);
                    if (!manifest.Files.Contains(manifest.Entry))
                        manifest.Files.Add(manifest.Entry);
                    _logger.Information("Auto-detected entry point: {Entry}", manifest.Entry);
                }
                else
                {
                    result.AddError("No entry point specified and no DLL files found in archive");
                    return result;
                }
            }

            // Detect mod source from manifest metadata
            result.DetectedSource = DetectPackageSource(manifest);
            switch (result.DetectedSource)
            {
                case ModPackageSource.Thunderstore:
                    _logger.Information("Detected Thunderstore package: {ModId}", manifest.Id);
                    break;
                case ModPackageSource.NexusMods:
                    _logger.Information("Detected Nexus Mods package: {ModId}", manifest.Id);
                    result.AddWarning("This mod was sourced from Nexus Mods. It will be installed, but update tracking and dependency resolution may be limited.");
                    break;
                default:
                    _logger.Information("Unknown mod source for: {ModId}", manifest.Id);
                    result.AddWarning("This mod is from an unrecognized source. It will be installed, but full compatibility is not guaranteed.");
                    break;
            }

            // Validate dependencies before touching plugins
            if (dependencyValidator != null)
            {
                var dependencyOutcome = await dependencyValidator(manifest);
                if (!dependencyOutcome.Success)
                {
                    result.AddError(dependencyOutcome.FailureReason ?? "Dependency validation failed");
                    return result;
                }
            }

            // Check if mod already exists
            modPath = Path.Combine(_pluginsPath, manifest.Id);
            if (Directory.Exists(modPath))
            {
                if (!overwrite)
                {
                    result.AddError($"Mod '{manifest.Id}' is already installed. Use overwrite option to replace.");
                    return result;
                }

                _logger.Information("Overwriting existing mod: {ModId}", manifest.Id);
                backupPath = await BackupExistingModAsync(modPath);
            }

            // Ensure entry is part of files list
            if (!manifest.Files.Contains(manifest.Entry))
            {
                manifest.Files.Add(manifest.Entry);
            }

            // Verify files and checksums
            var verifyResult = await VerifyModFilesAsync(extractResult.ExtractedPath!, manifest);
            if (!verifyResult.Success)
            {
                result.AddErrors(verifyResult.Errors);
                return result;
            }

            // Install the mod
             var stagedModPath = Path.Combine(stagingPath, manifest.Id);
             _logger.Information("Staging mod files at: {StagingPath}", stagedModPath);
             
             var installResult = await InstallModFilesAsync(extractResult.ExtractedPath!, manifest, stagedModPath);
             if (!installResult.Success)
             {
                 _logger.Error("Failed to stage mod files: {Errors}", string.Join(", ", installResult.Errors));
                 result.AddErrors(installResult.Errors);
                 return result;
             }

             // Move staged install into plugins directory
             _logger.Information("Moving staged mod to final location: {ModPath}", modPath);
             if (Directory.Exists(modPath))
             {
                 _logger.Information("Removing existing mod directory before move");
                 Directory.Delete(modPath, true);
             }

             // Use FileOperationsService to handle cross-volume moves
             if (!await _fileOps.MoveDirectoryAsync(stagedModPath, modPath))
             {
                 var error = $"Failed to move mod to final location: {stagedModPath} -> {modPath}";
                 _logger.Error(error);
                 result.AddError(error);
                 return result;
             }

             // Remove backup snapshot after success
             if (!string.IsNullOrEmpty(backupPath) && Directory.Exists(backupPath))
             {
                 _logger.Information("Installation successful, removing backup");
                 Directory.Delete(backupPath, true);
             }

             result.Success = true;
             result.InstalledModId = manifest.Id;
             result.InstalledModInfo = await CreateModInfoAsync(manifest, modPath);

             _logger.Information("Successfully installed mod: {ModId} v{Version}", manifest.Id, manifest.Version);
             return result;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to install mod from ZIP: {ZipPath}", zipPath);
            
            string errorDetail = ex switch
            {
                IOException ioEx when _fileOps.IsGameRunning() => "The game is running and locking mod files. Please close ASKA.",
                IOException ioEx => $"File system error: {ioEx.Message}. Check if any files are locked.",
                UnauthorizedAccessException => "Access denied. Try running the manager as Administrator.",
                _ => ex.Message
            };

            result.AddError($"Installation failed: {errorDetail}");

            // Roll back staged install
            if (!string.IsNullOrEmpty(modPath) && Directory.Exists(modPath))
            {
                try
                {
                    Directory.Delete(modPath, true);
                }
                catch (Exception deleteEx)
                {
                    _logger.Warning(deleteEx, "Failed to delete partial install at {Path}", modPath);
                }
            }

            var stagedModPath = (manifest != null)
                ? Path.Combine(stagingPath, manifest.Id)
                : Path.Combine(stagingPath, "unknown");
            if (!string.IsNullOrEmpty(stagedModPath) && Directory.Exists(stagedModPath))
            {
                try
                {
                    Directory.Delete(stagedModPath, true);
                }
                catch (Exception deleteEx)
                {
                    _logger.Warning(deleteEx, "Failed to delete staged install at {Path}", stagedModPath);
                }
            }

            // Restore backup if we created one
            if (!string.IsNullOrEmpty(backupPath) && Directory.Exists(backupPath))
            {
                try
                {
                    if (await _fileOps.MoveDirectoryAsync(backupPath, modPath))
                    {
                        _logger.Information("Restored backup for {ModId}", manifest?.Id ?? "unknown");
                    }
                    else
                    {
                        throw new IOException("Failed to restore backup directory");
                    }
                }
                catch (Exception restoreEx)
                {
                    _logger.Warning(restoreEx, "Failed to restore backup for {ModId}", manifest?.Id ?? "unknown");
                }
            }

            return result;
        }
        finally
        {
            // Clean up temporary files
            CleanupSessionTemp(sessionTempPath);
        }
    }

    /// <summary>
    /// Extracts a ZIP file to temporary directory
    /// </summary>
    private ExtractionResult ExtractZip(string zipPath, string sessionTempPath)
    {
        var result = new ExtractionResult();

        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            
            // Check for malicious paths
            var entries = archive.Entries.Where(e => !string.IsNullOrEmpty(e.FullName)).ToList();
            var maliciousPaths = entries.Where(e => IsMaliciousPath(e.FullName)).ToList();
            
            if (maliciousPaths.Any())
            {
                result.AddError("ZIP contains potentially malicious file paths");
                return result;
            }

            // Extract files
            var extractPath = Path.Combine(sessionTempPath, "extracted");
            Directory.CreateDirectory(extractPath);

            foreach (var entry in entries)
            {
                var destinationPath = Path.Combine(extractPath, entry.FullName);
                
                // Ensure directory exists
                var destinationDir = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(destinationDir))
                {
                    Directory.CreateDirectory(destinationDir);
                }

                // Extract file
                if (!entry.FullName.EndsWith("/"))
                {
                    entry.ExtractToFile(destinationPath, overwrite: true);
                }
            }

            result.Success = true;
            result.ExtractedPath = extractPath;
            _logger.Debug("Extracted ZIP to: {Path}", extractPath);
            return result;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to extract ZIP: {ZipPath}", zipPath);
            result.AddError($"Failed to extract ZIP: {ex.Message}");
            return result;
        }
    }

    /// <summary>
    /// Finds and validates the manifest in extracted files
    /// </summary>
    private async Task<ManifestValidationResult> FindAndValidateManifestAsync(string extractedPath)
    {
        // Look for manifest.json in root and subdirectories
        var manifestPaths = new[]
        {
            Path.Combine(extractedPath, "manifest.json"),
            Path.Combine(extractedPath, "manifest", "manifest.json")
        };

        foreach (var manifestPath in manifestPaths)
        {
            if (File.Exists(manifestPath))
            {
                var validationResult = await _manifestService.ParseAndValidateManifestAsync(manifestPath);
                if (validationResult.IsValid)
                {
                    // Use the manifest from validation (preserves auto-detected Entry)
                    if (validationResult.Manifest == null)
                    {
                        // Fallback: re-parse if somehow manifest wasn't preserved
                        var parseResult = await _manifestService.ParseManifestAsync(await File.ReadAllTextAsync(manifestPath));
                        validationResult.Manifest = parseResult.Manifest;
                    }
                    return new ManifestValidationResult
                    {
                        IsValid = true,
                        Success = true,
                        Manifest = validationResult.Manifest,
                        Errors = validationResult.Errors,
                        Warnings = validationResult.Warnings
                    };
                }
                return validationResult;
            }
        }

        var result = new ManifestValidationResult();
        result.AddError("No valid manifest.json found in ZIP file");
        return result;
    }

    /// <summary>
    /// Detects the origin platform of a mod package from its manifest metadata
    /// </summary>
    private ModPackageSource DetectPackageSource(ModManifest manifest)
    {
        // Primary heuristic: website_url
        if (!string.IsNullOrWhiteSpace(manifest.WebsiteUrl))
        {
            var url = manifest.WebsiteUrl.ToLowerInvariant();
            if (url.Contains("thunderstore.io"))
                return ModPackageSource.Thunderstore;
            if (url.Contains("nexusmods.com"))
                return ModPackageSource.NexusMods;
        }

        // Secondary heuristic: Thunderstore dependency format (Author-ModName-Version)
        if (manifest.Dependencies?.Any(d =>
            !string.IsNullOrEmpty(d.Id) &&
            d.Id.Count(c => c == '-') >= 2 &&
            d.Id.StartsWith("BepInEx", StringComparison.OrdinalIgnoreCase)) == true)
        {
            return ModPackageSource.Thunderstore;
        }

        // Tertiary: if the manifest has a source block with type, use that
        if (manifest.Source != null && !string.IsNullOrWhiteSpace(manifest.Source.Type))
        {
            var type = manifest.Source.Type.ToLowerInvariant();
            if (type.Contains("thunderstore")) return ModPackageSource.Thunderstore;
            if (type.Contains("nexus")) return ModPackageSource.NexusMods;
        }

        return ModPackageSource.Unknown;
    }

    /// <summary>
    /// Verifies mod files against manifest
    /// </summary>
    private async Task<VerificationResult> VerifyModFilesAsync(string extractedPath, ModManifest manifest)
    {
        var result = new VerificationResult();

        try
        {
            // Check entry point exists
            var entryPath = Path.Combine(extractedPath, manifest.Entry);
            if (!File.Exists(entryPath))
            {
                result.AddError($"Entry point not found: {manifest.Entry}");
                return result;
            }

            // Verify all listed files exist
            foreach (var file in manifest.Files)
            {
                var filePath = Path.Combine(extractedPath, file);
                if (!File.Exists(filePath))
                {
                    result.AddError($"Required file not found: {file}");
                }
            }

            // Verify checksum if provided
            if (!string.IsNullOrEmpty(manifest.Checksum))
            {
                var entryChecksum = await CalculateFileChecksumAsync(entryPath);
                if (!string.Equals(entryChecksum, manifest.Checksum, StringComparison.OrdinalIgnoreCase))
                {
                    result.AddError($"Checksum mismatch for entry point. Expected: {manifest.Checksum}, Actual: {entryChecksum}");
                }
            }

            result.Success = result.Errors.Count == 0;
            return result;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error verifying mod files");
            result.AddError("Failed to verify mod files");
            return result;
        }
    }

    /// <summary>
    /// Installs mod files to plugins directory
    /// </summary>
    private async Task<InstallationResult> InstallModFilesAsync(string extractedPath, ModManifest manifest, string targetPath)
    {
        var result = new InstallationResult();

        try
        {
            Directory.CreateDirectory(targetPath);

            // Copy all files from manifest
            foreach (var file in manifest.Files)
            {
                var sourcePath = Path.Combine(extractedPath, file);
                var destinationPath = Path.Combine(targetPath, file);

                if (File.Exists(sourcePath))
                {
                    var success = await _fileOps.SafeCopyAsync(sourcePath, destinationPath, createBackup: false);
                    if (!success)
                    {
                        result.AddError($"Failed to copy file: {file}");
                    }
                }
                else
                {
                    result.AddError($"Source file not found: {file}");
                }
            }

            // Save manifest
            var manifestPath = Path.Combine(targetPath, "manifest.json");
            var manifestSaved = await _manifestService.SaveManifestAsync(manifest, manifestPath);
            if (!manifestSaved)
            {
                result.AddError("Failed to save manifest file");
            }

            result.Success = result.Errors.Count == 0;
            return result;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error installing mod files");
            result.AddError("Failed to install mod files");
            return result;
        }
    }

    /// <summary>
    /// Creates backup of existing mod
    /// </summary>
    private async Task<string?> BackupExistingModAsync(string modPath)
    {
        try
        {
            var backupPath = modPath + ".backup." + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            if (Directory.Exists(modPath))
            {
                if (await _fileOps.MoveDirectoryAsync(modPath, backupPath))
                {
                    _logger.Information("Backed up existing mod to: {BackupPath}", backupPath);
                    return backupPath;
                }
                else
                {
                    _logger.Warning("Failed to move mod for backup: {ModPath} -> {BackupPath}", modPath, backupPath);
                    return null;
                }
            }
            return null; // Should not happen if Directory.Exists checked before calling, but for safety
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to backup existing mod: {ModPath}", modPath);
            return null;
        }
    }

    /// <summary>
    /// Creates ModInfo from installed manifest
    /// </summary>
    private ModInfo CreateModInfo(ModManifest manifest, string modPath)
    {
        var dllPath = Path.Combine(modPath, manifest.Entry);
        
        return new ModInfo
        {
            Id = manifest.Id,
            Name = manifest.Name,
            Version = manifest.Version,
            Author = manifest.Author,
            Description = manifest.Description,
            Dependencies = manifest.Dependencies,
            Source = manifest.Source,
            Checksum = manifest.Checksum,
            InstallPath = modPath,
            DllPath = dllPath,
            IsEnabled = File.Exists(dllPath),
            InstallDate = DateTime.UtcNow,
            LastUpdated = DateTime.UtcNow
        };
    }

    // Legacy async wrapper for callers expecting async
    private Task<ModInfo> CreateModInfoAsync(ModManifest manifest, string modPath)
    {
        var result = CreateModInfo(manifest, modPath);
        return Task.FromResult(result);
    }

    /// <summary>
    /// Calculates SHA256 checksum of a file
    /// </summary>
    private async Task<string> CalculateFileChecksumAsync(string filePath)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var hash = await sha256.ComputeHashAsync(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Checks if a file path is potentially malicious
    /// </summary>
    private bool IsMaliciousPath(string path)
    {
        // Check for path traversal attempts
        if (path.Contains("..") || path.Contains("\\..") || path.Contains("../"))
        {
            return true;
        }

        // Check for absolute paths
        if (Path.IsPathRooted(path))
        {
            return true;
        }

        // Check for suspicious file extensions
        var suspiciousExtensions = new[] { ".exe", ".bat", ".cmd", ".scr", ".vbs", ".js", ".jar" };
        var extension = Path.GetExtension(path).ToLowerInvariant();
        
        return suspiciousExtensions.Contains(extension);
    }

    /// <summary>
    /// Cleans up temporary files
    /// </summary>
    private void CleanupSessionTemp(string sessionTempPath)
    {
        try
        {
            if (Directory.Exists(sessionTempPath))
            {
                Directory.Delete(sessionTempPath, true);
                _logger.Debug("Cleaned up temporary files: {Path}", sessionTempPath);
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to cleanup temporary files at {Path}", sessionTempPath);
        }
    }
}

/// <summary>
/// Result of mod installation
/// </summary>
public class InstallationResult
{
    public bool Success { get; set; }
    public string? InstalledModId { get; set; }
    public ModInfo? InstalledModInfo { get; set; }
    public ModPackageSource DetectedSource { get; set; } = ModPackageSource.Unknown;
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();

    public void AddError(string error) => Errors.Add(error);
    public void AddWarning(string warning) => Warnings.Add(warning);
    public void AddErrors(IEnumerable<string> errors) => Errors.AddRange(errors);
    public void AddWarnings(IEnumerable<string> warnings) => Warnings.AddRange(warnings);
}

/// <summary>
/// Detected origin platform for a mod package
/// </summary>
public enum ModPackageSource
{
    /// <summary>Thunderstore package — fully supported</summary>
    Thunderstore,
    /// <summary>Nexus Mods package — installed but with limited support</summary>
    NexusMods,
    /// <summary>Unknown origin — installed but compatibility is not guaranteed</summary>
    Unknown
}

/// <summary>
/// Result of ZIP extraction
/// </summary>
public class ExtractionResult
{
    public bool Success { get; set; }
    public string? ExtractedPath { get; set; }
    public List<string> Errors { get; } = new();

    public void AddError(string error) => Errors.Add(error);
    public void AddErrors(IEnumerable<string> errors) => Errors.AddRange(errors);
}

/// <summary>
/// Result of file verification
/// </summary>
public class VerificationResult
{
    public bool Success { get; set; }
    public List<string> Errors { get; } = new();

    public void AddError(string error) => Errors.Add(error);
    public void AddErrors(IEnumerable<string> errors) => Errors.AddRange(errors);
}
