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
    private readonly string _tempPath;

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
        _tempPath = Path.Combine(Path.GetTempPath(), "ModManager", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempPath);
    }

    /// <summary>
    /// Installs a mod from a ZIP file
    /// </summary>
    public async Task<InstallationResult> InstallFromZipAsync(string zipPath, bool overwrite = false)
    {
        var result = new InstallationResult();

        try
        {
            if (!File.Exists(zipPath))
            {
                result.AddError($"ZIP file not found: {zipPath}");
                return result;
            }

            _logger.Information("Starting mod installation from: {ZipPath}", zipPath);

            // Extract ZIP to temporary directory
            var extractResult = ExtractZip(zipPath);
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

            var manifest = manifestResult.Manifest;

            if (manifest == null || string.IsNullOrWhiteSpace(manifest.Id))
            {
                result.AddError("Manifest is missing required Id");
                return result;
            }

            if (string.IsNullOrWhiteSpace(manifest.Entry))
            {
                result.AddError("Manifest entry point is required");
                return result;
            }

            // Check if mod already exists
            var modPath = Path.Combine(_pluginsPath, manifest.Id);
            if (Directory.Exists(modPath))
            {
                if (!overwrite)
                {
                    result.AddError($"Mod '{manifest.Id}' is already installed. Use overwrite option to replace.");
                    return result;
                }

                _logger.Information("Overwriting existing mod: {ModId}", manifest.Id);
                BackupExistingMod(modPath);
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
            var installResult = await InstallModFilesAsync(extractResult.ExtractedPath!, manifest);
            if (!installResult.Success)
            {
                result.AddErrors(installResult.Errors);
                // Roll back any partially created mod folder
                if (Directory.Exists(modPath))
                {
                    try
                    {
                        Directory.Delete(modPath, true);
                        _logger.Warning("Rolled back partial install for {ModId}", manifest.Id);
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning(ex, "Failed to roll back partial install for {ModId}", manifest.Id);
                    }
                }
                return result;
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
            result.AddError($"Installation failed: {ex.Message}");
            return result;
        }
        finally
        {
            // Clean up temporary files
            await CleanupTempFilesAsync();
        }
    }

    /// <summary>
    /// Extracts a ZIP file to temporary directory
    /// </summary>
    private ExtractionResult ExtractZip(string zipPath)
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
            var extractPath = Path.Combine(_tempPath, "extracted");
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
                    var parseResult = await _manifestService.ParseManifestAsync(await File.ReadAllTextAsync(manifestPath));
                    return new ManifestValidationResult
                    {
                        IsValid = true,
                        Manifest = parseResult.Manifest,
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
    private async Task<InstallationResult> InstallModFilesAsync(string extractedPath, ModManifest manifest)
    {
        var result = new InstallationResult();

        try
        {
            var modPath = Path.Combine(_pluginsPath, manifest.Id);
            Directory.CreateDirectory(modPath);

            // Copy all files from manifest
            foreach (var file in manifest.Files)
            {
                var sourcePath = Path.Combine(extractedPath, file);
                var destinationPath = Path.Combine(modPath, file);

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
            var manifestPath = Path.Combine(modPath, "manifest.json");
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
    private void BackupExistingMod(string modPath)
    {
        try
        {
            var backupPath = modPath + ".backup." + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            if (Directory.Exists(modPath))
            {
                Directory.Move(modPath, backupPath);
                _logger.Information("Backed up existing mod to: {BackupPath}", backupPath);
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to backup existing mod: {ModPath}", modPath);
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
    private void CleanupTempFiles()
    {
        try
        {
            if (Directory.Exists(_tempPath))
            {
                Directory.Delete(_tempPath, true);
                _logger.Debug("Cleaned up temporary files: {Path}", _tempPath);
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to cleanup temporary files");
        }
    }

    // Legacy async wrapper for callers expecting async
    private Task CleanupTempFilesAsync()
    {
        CleanupTempFiles();
        return Task.CompletedTask;
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
    public List<string> Errors { get; } = new();
    public List<string> Warnings { get; } = new();

    public void AddError(string error) => Errors.Add(error);
    public void AddWarning(string warning) => Warnings.Add(warning);
    public void AddErrors(IEnumerable<string> errors) => Errors.AddRange(errors);
    public void AddWarnings(IEnumerable<string> warnings) => Warnings.AddRange(warnings);
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
