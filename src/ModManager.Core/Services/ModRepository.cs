using LiteDB;
using ModManager.Core.Interfaces;
using ModManager.Core.Models;
using ModManager.Core.Services;
using Serilog;

namespace ModManager.Core.Services;

/// <summary>
/// Repository for managing mod installations and metadata
/// </summary>
public class ModRepository : IModRepository
{
    private readonly ILogger _logger;
    private readonly LiteDatabase _database;
    private readonly string _pluginsPath;
    private readonly FileOperationsService _fileOps;
    private readonly ModInstallationService _installationService;
    private readonly ModScanner _scanner;
    private readonly DependencyResolutionService _dependencyResolver;

    public ModRepository(ILogger logger, string askaPath)
    {
        _logger = logger;
        _pluginsPath = Path.Combine(askaPath, "BepInEx", "plugins");
        
        // Initialize services
        _fileOps = new FileOperationsService(logger, askaPath);
        _installationService = new ModInstallationService(
            logger, 
            new ManifestService(logger), 
            _fileOps, 
            askaPath);
        _scanner = new ModScanner(logger);
        _dependencyResolver = new DependencyResolutionService(logger);
        
        // Initialize LiteDB
        var dbPath = Path.Combine(askaPath, "BepInEx", "ModManager.db");
        _database = new LiteDatabase(dbPath);
        
        // Create indexes
        var modsCollection = _database.GetCollection<ModInfo>("mods");
        modsCollection.EnsureIndex(x => x.Id, true);
        modsCollection.EnsureIndex(x => x.Name);
        modsCollection.EnsureIndex(x => x.IsEnabled);

        var errorsCollection = _database.GetCollection<RuntimeError>("errors");
        errorsCollection.EnsureIndex(x => x.Timestamp);
        errorsCollection.EnsureIndex(x => x.ModId);

        // Apply cleanup policy on initialization (keep last 100 errors)
        ClearRuntimeErrorsAsync(100).GetAwaiter().GetResult();
    }

    private bool ValidateDependencies(ModInfo mod)
    {
        return ValidateDependencies(mod, out _);
    }

    private bool ValidateDependencies(ModInfo mod, out string? failureReason)
    {
        failureReason = null;

        if (mod.Dependencies == null || mod.Dependencies.Count == 0)
        {
            return true;
        }

        var modsCollection = _database.GetCollection<ModInfo>("mods");
        var allMods = modsCollection.FindAll().ToList();

        // Check for missing/disabled deps and version ranges first
        foreach (var dep in mod.Dependencies.Where(d => !d.Optional))
        {
            var depMod = allMods.FirstOrDefault(m => string.Equals(m.Id, dep.Id, StringComparison.OrdinalIgnoreCase));
            if (depMod == null)
            {
                failureReason = $"Missing dependency '{dep.Id}' for '{mod.Name}'.";
                _logger.Warning(failureReason);
                return false;
            }

            if (!depMod.IsEnabled)
            {
                failureReason = $"Dependency '{depMod.Name}' must be enabled before '{mod.Name}'.";
                _logger.Warning(failureReason);
                return false;
            }

            var requiredRange = string.IsNullOrWhiteSpace(dep.MinVersion) ? ">=0.0.0" : dep.MinVersion;
            if (!VersionService.SatisfiesRangeLegacy(depMod.Version ?? "1.0.0", requiredRange))
            {
                failureReason = $"Dependency '{depMod.Name}' version {depMod.Version} does not satisfy requirement {requiredRange}.";
                _logger.Warning(failureReason);
                return false;
            }
        }

        // Run full dependency resolver to detect cycles/missing graph state
        var resolution = _dependencyResolver.ResolveDependencies(allMods);

        var missing = resolution.MissingDependencies.FirstOrDefault(md => md.ModId.Equals(mod.Id, StringComparison.OrdinalIgnoreCase));
        if (missing != null)
        {
            failureReason = $"Missing dependency '{missing.DependencyId}' for '{mod.Name}'.";
            return false;
        }

        var conflict = resolution.VersionConflicts.FirstOrDefault(vc => vc.ModId.Equals(mod.Id, StringComparison.OrdinalIgnoreCase));
        if (conflict != null)
        {
            failureReason = $"Dependency '{conflict.DependencyName}' version {conflict.InstalledVersion} violates required {conflict.RequiredVersion}.";
            return false;
        }

        var cycle = resolution.CircularDependencies.FirstOrDefault(cd => cd.ModIds.Contains(mod.Id));
        if (cycle != null)
        {
            failureReason = $"Circular dependency detected: {cycle.CycleDescription}.";
            return false;
        }

        return true;
    }

    // Legacy async wrapper for interface compliance
    public Task<IEnumerable<ModInfo>> ListInstalledAsync()
    {
        var result = ListInstalled();
        return Task.FromResult(result);
    }

    public IEnumerable<ModInfo> ListInstalled()
    {
        try
        {
            var modsCollection = _database.GetCollection<ModInfo>("mods");
            return modsCollection
                .FindAll()
                .OrderBy(m => m.LoadOrder)
                .ThenBy(m => m.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to list installed mods");
            return Enumerable.Empty<ModInfo>();
        }
    }

    // Legacy async wrapper for interface compliance
    public Task UninstallAsync(string modId)
    {
        Uninstall(modId);
        return Task.CompletedTask;
    }

    public ModInfo? GetMod(string modId)
    {
        try
        {
            var modsCollection = _database.GetCollection<ModInfo>("mods");
            return modsCollection.FindOne(x => x.Id == modId);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to get mod: {ModId}", modId);
            return null;
        }
    }

    // Legacy async wrapper for interface compliance
    public Task<ModInfo?> GetModAsync(string modId)
    {
        var result = GetMod(modId);
        return Task.FromResult(result);
    }

    public async Task InstallFromZipAsync(string zipPath)
    {
        try
        {
            // Prevent duplicate installs if already present in database
            var modsCollection = _database.GetCollection<ModInfo>("mods");
            var zipFileName = Path.GetFileNameWithoutExtension(zipPath);

            var result = await _installationService.InstallFromZipAsync(
                zipPath,
                overwrite: false,
                async manifest =>
                {
                    var tempModInfo = new ModInfo
                    {
                        Id = manifest.Id,
                        Name = manifest.Name,
                        Version = manifest.Version,
                        Dependencies = manifest.Dependencies ?? new List<ModDependency>(),
                        IsEnabled = true
                    };

                    var isValid = ValidateDependencies(tempModInfo, out var failureReason);
                    return await Task.FromResult(new DependencyValidationOutcome
                    {
                        Success = isValid,
                        FailureReason = failureReason
                    });
                });
            
            if (!result.Success)
            {
                var errorMessage = string.Join("; ", result.Errors);
                throw new InvalidOperationException($"Installation failed: {errorMessage}");
            }

            if (!string.IsNullOrEmpty(result.InstalledModId))
            {
                var existing = modsCollection.FindOne(x => x.Id == result.InstalledModId);
                if (existing != null)
                {
                    // Roll back newly installed files to avoid duplicate
                    var modPath = Path.Combine(_pluginsPath, result.InstalledModId);
                    if (Directory.Exists(modPath))
                    {
                        try
                        {
                            Directory.Delete(modPath, true);
                            _logger.Warning("Rolled back duplicate install for {ModId}", result.InstalledModId);
                        }
                        catch (Exception ex)
                        {
                            _logger.Warning(ex, "Failed to roll back duplicate install for {ModId}", result.InstalledModId);
                        }
                    }

                    throw new InvalidOperationException($"Mod '{result.InstalledModId}' is already installed.");
                }

                if (result.InstalledModInfo != null && !ValidateDependencies(result.InstalledModInfo))
                {
                    // Dependency check failed; roll back new install
                    var modPath = Path.Combine(_pluginsPath, result.InstalledModId);
                    if (Directory.Exists(modPath))
                    {
                        try
                        {
                            Directory.Delete(modPath, true);
                            _logger.Warning("Rolled back install for {ModId} due to dependency failure", result.InstalledModId);
                        }
                        catch (Exception ex)
                        {
                            _logger.Warning(ex, "Failed to roll back install for {ModId}", result.InstalledModId);
                        }
                    }

                    throw new InvalidOperationException($"Dependencies not satisfied for {result.InstalledModId}");
                }
            }

            // Refresh database after installation
            await RefreshDatabaseAsync();
            
            _logger.Information("Successfully installed mod: {ModId}", result.InstalledModId);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to install mod from ZIP: {ZipPath}", zipPath);
            throw;
        }
    }

    public void InstallFromUrl(Uri packageUrl)
    {
        throw new NotImplementedException("Installation from URL will be implemented in Phase 2");
    }

    // Legacy async wrapper for interface compliance
    public Task InstallFromUrlAsync(Uri packageUrl)
    {
        InstallFromUrl(packageUrl);
        return Task.CompletedTask;
    }

    public void Uninstall(string modId)
    {
        try
        {
            var mod = GetMod(modId);
            if (mod == null)
            {
                _logger.Warning("Mod not found for uninstall: {ModId}", modId);
                return;
            }

            // check if game is running
            if (OperatingSystem.IsWindows())
            {
                var detector = new AskaDetector();
                if (detector.IsAskaRunning())
                {
                    _logger.Warning("Cannot uninstall mod while game is running: {ModId}", modId);
                    throw new InvalidOperationException("Cannot uninstall mods while ASKA is running.");
                }
            }

            // Prevent uninstall if other mods depend on this mod
            var modsCollection = _database.GetCollection<ModInfo>("mods");
            var dependents = modsCollection.Find(m => m.Dependencies != null && m.Dependencies.Any(d => d.Id == modId));
            if (dependents.Any())
            {
                var dependentIds = string.Join(", ", dependents.Select(d => d.Id));
                _logger.Warning("Cannot uninstall {ModId}; depended on by: {Dependents}", modId, dependentIds);
                throw new InvalidOperationException($"Cannot uninstall {modId}; other mods depend on it: {dependentIds}");
            }

            // Backup before uninstall
            if (Directory.Exists(mod.InstallPath))
            {
                _fileOps.SafeDelete(mod.InstallPath, createBackup: true);
            }

            // Remove from database
            modsCollection.DeleteMany(x => x.Id == modId);

            _logger.Information("Uninstalled mod: {ModId}", modId);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to uninstall mod: {ModId}", modId);
            throw;
        }
    }

    public async Task SetEnabledAsync(string modId, bool enabled)
    {
        try
        {
            var mod = await GetModAsync(modId);
            if (mod == null)
            {
                _logger.Warning("Mod not found: {ModId}", modId);
                return;
            }

            // Check if game is running
            if (OperatingSystem.IsWindows())
            {
                var detector = new AskaDetector();
                if (detector.IsAskaRunning())
                {
                    _logger.Warning("Cannot change mod state while game is running: {ModId}", modId);
                    throw new InvalidOperationException("Cannot enable/disable mods while ASKA is running.");
                }
            }

            // Dependency validation before enabling
            if (enabled)
            {
                if (!ValidateDependencies(mod))
                {
                    _logger.Warning("Cannot enable mod {ModId} due to missing or incompatible dependencies", modId);
                    throw new InvalidOperationException($"Dependencies not satisfied for {modId}");
                }
            }

            bool success;
            if (enabled)
            {
                success = await _fileOps.EnableModAsync(mod.DllPath);
            }
            else
            {
                success = await _fileOps.DisableModAsync(mod.DllPath);
            }

            if (success)
            {
                // Update mod state in database
                mod.IsEnabled = enabled;
                var modsCollection = _database.GetCollection<ModInfo>("mods");
                modsCollection.Update(mod);

                _logger.Information("Mod {ModId} enabled state set to {Enabled}", modId, enabled);
            }
            else
            {
                throw new InvalidOperationException($"Failed to set enabled state for mod: {modId}");
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to set enabled state for mod: {ModId}", modId);
            throw;
        }
    }

    public void UpdateMod(string modId)
    {
        throw new NotImplementedException("Update will be implemented in Phase 2");
    }

    // Legacy async wrapper for interface compliance
    public Task UpdateModAsync(string modId)
    {
        UpdateMod(modId);
        return Task.CompletedTask;
    }

    public IEnumerable<ModUpdateInfo> CheckForUpdates()
    {
        throw new NotImplementedException("Update checking will be implemented in Phase 2");
    }

    // Legacy async wrapper for interface compliance
    public Task<IEnumerable<ModUpdateInfo>> CheckForUpdatesAsync()
    {
        var result = CheckForUpdates();
        return Task.FromResult(result);
    }

    public async Task<bool> ValidateModAsync(string modId)
    {
        try
        {
            var mod = GetMod(modId);
            if (mod == null)
            {
                throw new InvalidOperationException($"Mod '{modId}' is not installed.");
            }

            if (!ValidateDependencies(mod, out var dependencyFailure))
            {
                throw new InvalidOperationException(dependencyFailure ?? $"Dependencies not satisfied for '{modId}'.");
            }

            // Check if main DLL exists
            if (!File.Exists(mod.DllPath))
            {
                throw new InvalidOperationException($"Mod entry DLL missing: {mod.DllPath}");
            }

            // Verify checksum if available
            if (!string.IsNullOrEmpty(mod.Checksum))
            {
                using var sha256 = System.Security.Cryptography.SHA256.Create();
                using var stream = File.OpenRead(mod.DllPath);
                var hash = await sha256.ComputeHashAsync(stream);
                var currentChecksum = Convert.ToHexString(hash).ToLowerInvariant();
                if (currentChecksum != mod.Checksum.ToLowerInvariant())
                {
                    _logger.Warning("Checksum mismatch for mod {ModId}", modId);
                    throw new InvalidOperationException($"Checksum mismatch for '{modId}'. Expected {mod.Checksum}, got {currentChecksum}.");
                }
            }

            return true;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to validate mod: {ModId}", modId);
            throw new InvalidOperationException($"Validation failed for '{modId}': {ex.Message}", ex);
        }
    }

    public Task LogRuntimeErrorAsync(RuntimeError error)
    {
        try
        {
            var collection = _database.GetCollection<RuntimeError>("errors");
            collection.Insert(error);
            _logger.Debug("Logged runtime error: {ErrorId} for mod {ModId}", error.Id, error.ModId);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to log runtime error");
        }
        return Task.CompletedTask;
    }

    public Task<IEnumerable<RuntimeError>> GetRuntimeErrorsAsync()
    {
        try
        {
            var collection = _database.GetCollection<RuntimeError>("errors");
            var result = collection.FindAll()
                .OrderByDescending(x => x.Timestamp)
                .ToList();
            return Task.FromResult<IEnumerable<RuntimeError>>(result);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to get runtime errors");
            return Task.FromResult(Enumerable.Empty<RuntimeError>());
        }
    }

    public Task ClearRuntimeErrorsAsync(int keepLast = 0)
    {
        try
        {
            var collection = _database.GetCollection<RuntimeError>("errors");
            if (keepLast <= 0)
            {
                collection.DeleteAll();
            }
            else
            {
                var all = collection.FindAll()
                    .OrderByDescending(x => x.Timestamp)
                    .ToList();
                
                if (all.Count > keepLast)
                {
                    var toDelete = all.Skip(keepLast).Select(x => x.Id).ToList();
                    foreach (var id in toDelete)
                    {
                        collection.Delete(id);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to clear runtime errors");
        }
        return Task.CompletedTask;
    }

    public async Task RefreshDatabaseAsync()
    {
        try
        {
            var scannedMods = await _scanner.ScanModsAsync(_pluginsPath);

            var modsCollection = _database.GetCollection<ModInfo>("mods");

            // Clear existing data
            modsCollection.DeleteAll();

            var orderedMods = scannedMods
                .OrderBy(m => m.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();

            for (var index = 0; index < orderedMods.Count; index++)
            {
                orderedMods[index].LoadOrder = index;
                modsCollection.Insert(orderedMods[index]);
            }

            _logger.Information("Refreshed mod database with {Count} mods", scannedMods.Count());
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to refresh mod database");
            throw;
        }
    }

    /// <summary>
    /// Synchronous wrapper for RefreshDatabaseAsync - use with caution
    /// </summary>
    public void RefreshDatabase()
    {
        RefreshDatabaseAsync().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Restores a mod from backup
    /// </summary>
    public bool RestoreModFromBackup(string modId, string backupFileName)
    {
        try
        {
            var mod = GetMod(modId);
            if (mod == null)
            {
                return false;
            }

            var success = _fileOps.RestoreFromBackup(mod.DllPath, backupFileName);
            if (success)
            {
                RefreshDatabase();
            }

            return success;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to restore mod from backup: {ModId}", modId);
            return false;
        }
    }

    public void Dispose()
    {
        _database?.Dispose();
    }
}
