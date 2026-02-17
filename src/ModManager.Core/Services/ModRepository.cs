using LiteDB;
using ModManager.Core.Interfaces;
using ModManager.Core.Models;
using ModManager.Core.Services;
using Serilog;
using System.Net.Http;

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
    private readonly DiscoveryService _discoveryService;
    private readonly HttpClient _httpClient;

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
        
        _httpClient = new HttpClient();
        var thunderstoreClient = new ThunderstoreClient(_httpClient, logger);
        _discoveryService = new DiscoveryService(logger, thunderstoreClient);
        
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

    private DependencyValidationOutcome ValidateDependencies(ModInfo mod)
    {
        var outcome = new DependencyValidationOutcome { Success = true };

        if (mod.Dependencies == null || mod.Dependencies.Count == 0)
        {
            return outcome;
        }

        var modsCollection = _database.GetCollection<ModInfo>("mods");
        var allMods = modsCollection.FindAll().ToList();

        // Check for missing/disabled deps and version ranges first
        foreach (var dep in mod.Dependencies.Where(d => !d.Optional))
        {
            // BepInEx framework dependencies are auto-satisfied when BepInEx is installed
            if (dep.Id.StartsWith("BepInEx", StringComparison.OrdinalIgnoreCase))
            {
                var bepinexCorePath = Path.Combine(Path.GetDirectoryName(_pluginsPath)!, "core");
                if (Directory.Exists(bepinexCorePath))
                {
                    _logger.Debug("Framework dependency '{DepId}' satisfied by installed BepInEx", dep.Id);
                    continue;
                }
            }

            var depMod = allMods.FirstOrDefault(m => string.Equals(m.Id, dep.Id, StringComparison.OrdinalIgnoreCase));
            if (depMod == null)
            {
                var failure = $"Missing dependency '{dep.Id}' for '{mod.Name}'.";
                _logger.Warning(failure);
                outcome.Success = false;
                outcome.FailureReason = failure;
                outcome.MissingDependencies.Add(new MissingDependency
                {
                    ModId = mod.Id,
                    ModName = mod.Name,
                    DependencyId = dep.Id,
                    RequiredVersion = dep.MinVersion,
                    IsOptional = dep.Optional
                });
            }
            else if (!depMod.IsEnabled)
            {
                var failure = $"Dependency '{depMod.Name}' must be enabled before '{mod.Name}'.";
                _logger.Warning(failure);
                outcome.Success = false;
                outcome.FailureReason = failure;
                // Treat disabled as "missing" for UI purposes
                outcome.MissingDependencies.Add(new MissingDependency
                {
                    ModId = mod.Id,
                    ModName = mod.Name,
                    DependencyId = dep.Id,
                    IsOptional = dep.Optional
                });
            }
            else
            {
                var requiredRange = string.IsNullOrWhiteSpace(dep.MinVersion) ? ">=0.0.0" : dep.MinVersion;
                if (!VersionService.SatisfiesRangeLegacy(depMod.Version ?? "1.0.0", requiredRange))
                {
                    var failure = $"Dependency '{depMod.Name}' version {depMod.Version} does not satisfy requirement {requiredRange}.";
                    _logger.Warning(failure);
                    outcome.Success = false;
                    outcome.FailureReason = failure;
                    outcome.VersionConflicts.Add(new VersionConflict
                    {
                        ModId = mod.Id,
                        ModName = mod.Name,
                        DependencyId = dep.Id,
                        DependencyName = depMod.Name,
                        RequiredVersion = requiredRange,
                        InstalledVersion = depMod.Version,
                        ConflictType = VersionConflictType.TooOld
                    });
                }
            }
        }

        // Run full dependency resolver to detect cycles/missing graph state
        var resolution = _dependencyResolver.ResolveDependencies(allMods);

        var missing = resolution.MissingDependencies.Where(md => md.ModId.Equals(mod.Id, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var m in missing)
        {
            if (!outcome.MissingDependencies.Any(x => x.DependencyId == m.DependencyId))
            {
                outcome.Success = false;
                outcome.MissingDependencies.Add(m);
            }
        }

        var conflicts = resolution.VersionConflicts.Where(vc => vc.ModId.Equals(mod.Id, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var c in conflicts)
        {
            if (!outcome.VersionConflicts.Any(x => x.DependencyId == c.DependencyId))
            {
                outcome.Success = false;
                outcome.VersionConflicts.Add(c);
            }
        }

        var cycle = resolution.CircularDependencies.FirstOrDefault(cd => cd.ModIds.Contains(mod.Id));
        if (cycle != null)
        {
            outcome.Success = false;
            if (string.IsNullOrEmpty(outcome.FailureReason))
            {
                outcome.FailureReason = $"Circular dependency detected: {cycle.CycleDescription}.";
            }
            outcome.CircularDependencies.Add(cycle);
        }

        return outcome;
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

    public async Task<InstallationResult> InstallFromZipAsync(string zipPath)
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

                    var outcome = ValidateDependencies(tempModInfo);
                    return await Task.FromResult(outcome);
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

                var validation = ValidateDependencies(result.InstalledModInfo);
                if (result.InstalledModInfo != null && !validation.Success)
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
            return result;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to install mod from ZIP: {ZipPath}", zipPath);
            throw;
        }
    }

    public async Task<InstallationResult> InstallFromUrlAsync(Uri packageUrl)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"aska_mod_{Guid.NewGuid()}.zip");
        try
        {
            _logger.Information("Downloading mod from {Url} to {TempPath}", packageUrl, tempPath);
            using (var response = await _httpClient.GetAsync(packageUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                if (!response.IsSuccessStatusCode)
                {
                    _logger.Error("Download failed with status code: {StatusCode}", response.StatusCode);
                    return new InstallationResult 
                    { 
                        Success = false, 
                        Errors = new List<string> { $"Download failed: {response.StatusCode}" },
                        Warnings = new List<string>()
                    };
                }
                
                using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await response.Content.CopyToAsync(fs);
                }
            }

            _logger.Information("Download complete. Installing from ZIP.");
            return await InstallFromZipAsync(tempPath);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to install mod from URL");
            return new InstallationResult
            {
                Success = false,
                Errors = new List<string> { $"Install failed: {ex.Message}" },
                Warnings = new List<string>()
            };
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try 
                { 
                    File.Delete(tempPath); 
                } 
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Failed to clean up temp file {TempPath}", tempPath);
                }
            }
        }
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

    public async Task<DependencyValidationOutcome> SetEnabledAsync(string modId, bool enabled)
    {
        try
        {
            var mod = await GetModAsync(modId);
            if (mod == null)
            {
                _logger.Warning("Mod not found: {ModId}", modId);
                return new DependencyValidationOutcome { Success = false, FailureReason = "Mod not found" };
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
                var validation = ValidateDependencies(mod);
                if (!validation.Success)
                {
                    _logger.Warning("Cannot enable mod {ModId} due to missing or incompatible dependencies", modId);
                    return validation;
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
                return new DependencyValidationOutcome { Success = true };
            }
            else
            {
                return new DependencyValidationOutcome { Success = false, FailureReason = $"Failed to set enabled state for mod: {modId}" };
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to set enabled state for mod: {ModId}", modId);
            throw;
        }
    }

    public async Task UpdateModAsync(string modId)
    {
        try
        {
            _logger.Information("Updating mod: {ModId}", modId);
            var updates = await CheckForUpdatesAsync();
            var update = updates.FirstOrDefault(u => u.ModId == modId);

            if (update == null)
            {
                _logger.Warning("No update found for mod: {ModId}", modId);
                return;
            }

            // Real update implementation pending
            _logger.Warning("Update functionality is not yet fully implemented for {ModId}", modId);
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to update mod: {ModId}", modId);
            throw;
        }
    }

    public async Task<IEnumerable<ModUpdateInfo>> CheckForUpdatesAsync()
    {
        try
        {
            _logger.Information("Checking for mod updates");
            var installedMods = await ListInstalledAsync();
            var availableMods = await _discoveryService.GetAvailableModsAsync();

            var updateList = new List<ModUpdateInfo>();

            foreach (var installed in installedMods)
            {
                var remote = availableMods.FirstOrDefault(m => m.Id == installed.Id);
                if (remote != null && IsNewerVersion(remote.Version, installed.Version))
                {
                    updateList.Add(new ModUpdateInfo
                    {
                        ModId = installed.Id,
                        CurrentVersion = installed.Version,
                        LatestVersion = remote.Version,
                        DownloadUrl = remote.DownloadUrl,
                        Changelog = $"Version {remote.Version} released."
                    });
                }
            }

            _logger.Information("Found {Count} updates available", updateList.Count);
            return updateList;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to check for updates");
            return Enumerable.Empty<ModUpdateInfo>();
        }
    }

    private bool IsNewerVersion(string remote, string local)
    {
        if (Version.TryParse(remote, out var rVer) && Version.TryParse(local, out var lVer))
        {
            return rVer > lVer;
        }
        // Fallback to string comparison if versioning is non-standard
        return string.Compare(remote, local, StringComparison.OrdinalIgnoreCase) > 0;
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

            var validation = ValidateDependencies(mod);
            if (!validation.Success)
            {
                throw new InvalidOperationException(validation.FailureReason ?? $"Dependencies not satisfied for '{modId}'.");
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

    public async Task<DependencyValidationOutcome> ValidateModDetailedAsync(string modId)
    {
        try
        {
            var mod = GetMod(modId);
            if (mod == null)
            {
                return new DependencyValidationOutcome { Success = false, FailureReason = $"Mod '{modId}' is not installed." };
            }

            return ValidateDependencies(mod);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to perform detailed validation: {ModId}", modId);
            return new DependencyValidationOutcome { Success = false, FailureReason = ex.Message };
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

    public async Task<IEnumerable<RemoteModInfo>> GetAvailableModsAsync()
    {
        return await _discoveryService.GetAvailableModsAsync();
    }

    public async Task<InstallationResult> InstallModWithDependenciesAsync(RemoteModInfo mod)
    {
        try
        {
            _logger.Information("Starting recursive installation for {ModId}", mod.Id);
            
            // 1. Get all available mods to resolve dependencies
            var allMods = await _discoveryService.GetAvailableModsAsync();
            var modMap = allMods.ToDictionary(m => m.Id, m => m, StringComparer.OrdinalIgnoreCase);

            // 2. Resolve dependencies
            var toInstall = new List<RemoteModInfo>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var stack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (!ResolveDependencies(mod, modMap, toInstall, visited, stack))
            {
                return new InstallationResult
                {
                    Success = false,
                    Errors = new List<string> { "Circular dependency detected or dependency missing." }
                };
            }

            // 3. Install in order
            var result = new InstallationResult { Success = true };
            
            foreach (var modToInstall in toInstall)
            {
                 // Check if already installed
                 var existing = GetMod(modToInstall.Id); // ID might differ slightly (Namespace-Name vs Name)
                 // RemoteModInfo.Id is usually "Namespace-Name".
                 // ModInfo.Id is usually "Namespace-Name" (if from manifest).
                 // We should check if we need to update or install.
                 
                 // For now, let's just reinstall if it's the requested one, or install if missing.
                 // Ideally skip if already installed and version matches.
                 if (existing != null && existing.Version == modToInstall.Version)
                 {
                     _logger.Information("Mod {ModId} already installed at version {Version}. Skipping.", modToInstall.Id, modToInstall.Version);
                     continue;
                 }

                 _logger.Information("Installing dependency {ModId}...", modToInstall.Id);
                 var installResult = await InstallFromUrlAsync(modToInstall.DownloadUrl);
                 
                 if (!installResult.Success)
                 {
                     result.Success = false;
                     result.Errors.AddRange(installResult.Errors);
                     return result;
                 }
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Recursive installation failed for {ModId}", mod.Id);
            return new InstallationResult
            {
                Success = false,
                Errors = new List<string> { $"Recursive install failed: {ex.Message}" }
            };
        }
    }

    private bool ResolveDependencies(
        RemoteModInfo current, 
        Dictionary<string, RemoteModInfo> allMods, 
        List<RemoteModInfo> installList, 
        HashSet<string> visited,
        HashSet<string> stack)
    {
        if (visited.Contains(current.Id)) return true;
        if (stack.Contains(current.Id)) return false; // Cycle

        stack.Add(current.Id);

        foreach (var depString in current.Dependencies)
        {
            // depString format: "Namespace-Name-Version"
            var parts = depString.Split('-');
            if (parts.Length < 3) continue;
            
            // Reconstruct ID: Namespace-Name (ignoring version for now to find the package)
            // Or maybe ID IS Namespace-Name?
            // Thunderstore ID is usually "Author-Name".
            // depString is "Author-Name-Version".
            
            var depId = $"{parts[0]}-{parts[1]}";
            
            if (depId.Equals("BepInEx-BepInExPack", StringComparison.OrdinalIgnoreCase)) continue; // Skip BepInEx as it's manageable separately
            
            if (allMods.TryGetValue(depId, out var depMod))
            {
                if (!ResolveDependencies(depMod, allMods, installList, visited, stack))
                    return false;
            }
            else
            {
                _logger.Warning("Dependency {DepId} not found in index.", depId);
                // We proceed, hoping it might be optional or handled elsewhere, 
                // but strictly we should maybe fail? For now, log warning.
            }
        }

        stack.Remove(current.Id);
        visited.Add(current.Id);
        installList.Add(current);
        
        return true;
    }

    public void Dispose()
    {
        _database?.Dispose();
    }
}
