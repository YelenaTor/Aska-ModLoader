using ModManager.Core.Interfaces;
using ModManager.Core.Models;
using ModManager.Core.Services;
using ModManager.DesktopUI.Interfaces;
using ModManager.DesktopUI.Models;
using Serilog;
using System;
using System.IO;
using System.Net.Http;

namespace ModManager.DesktopUI.Services;

/// <summary>
/// Real implementation of IModManagerFacade using Core services
/// </summary>
public class RealModManagerFacade : IModManagerFacade
{
    private readonly IModRepository _modRepository;
    private readonly ILogger _logger;
    private string _statusMessage = "Ready";
    private readonly CrashDiagnosticsService _crashDiagnostics;
    private readonly string _askaPath;

    public event EventHandler<string?>? CrashLogUpdated;

    public RealModManagerFacade(IModRepository modRepository, ILogger logger, string askaPath)
    {
        _modRepository = modRepository;
        _logger = logger;
        _askaPath = askaPath;
        
        // Log the repository being used (for diagnostics)
        _logger.Information("RealModManagerFacade initialized with repository");
        _crashDiagnostics = new CrashDiagnosticsService(_logger, _modRepository, askaPath);
        _crashDiagnostics.LogUpdated += (sender, message) => CrashLogUpdated?.Invoke(this, message);
    }

    public async Task<IEnumerable<ModDisplayModel>> GetInstalledModsAsync()
    {
        try
        {
            _logger.Information("Starting mod load from Core repository");
            
            // Use real Core services to load mods
            var mods = await _modRepository.ListInstalledAsync();
            
            _logger.Information("Repository returned {Count} mods", mods.Count());
            
            var displayModels = mods.Select(ConvertToDisplayModel).ToList();
            
            _logger.Information("Mapped {Count} mods to display models", displayModels.Count);
            
            _statusMessage = $"Loaded {displayModels.Count} mods";
            _logger.Information("Successfully loaded {Count} mods from Core repository", displayModels.Count);
            
            return displayModels;
        }
        catch (Exception ex)
        {
            _statusMessage = $"Error loading mods: {ex.Message}";
            _logger.Error(ex, "Failed to load mods from Core repository");
            
            // Return empty list on error to prevent UI crashes
            return Enumerable.Empty<ModDisplayModel>();
        }
    }

    public async Task<FacadeOperationResult> InstallFromZipAsync(string zipPath)
    {
        try
        {
            _logger.Information("Facade installing mod from ZIP: {ZipPath}", zipPath);
            var installResult = await _modRepository.InstallFromZipAsync(zipPath);
            
            // Build status message with any warnings
            var message = $"Installed '{installResult.InstalledModId ?? "mod"}' successfully";
            if (installResult.Warnings.Count > 0)
            {
                message += $" ⚠ {string.Join(" | ", installResult.Warnings)}";
            }
            
            _statusMessage = message;
            return FacadeOperationResult.SuccessResult(_statusMessage);
        }
        catch (Exception ex)
        {
            _statusMessage = $"Installation failed: {ex.Message}";
            _logger.Error(ex, "Facade failed to install from ZIP: {ZipPath}", zipPath);
            return FacadeOperationResult.FailureResult(_statusMessage);
        }
    }

    public async Task<FacadeOperationResult> EnableModAsync(string modId)
    {
        try
        {
            _logger.Information("Facade enabling mod: {ModId}", modId);
            var outcome = await _modRepository.SetEnabledAsync(modId, true);
            if (!outcome.Success)
            {
                _statusMessage = outcome.FailureReason ?? $"Failed to enable {modId}";
                return MapValidationToFacadeResult(outcome);
            }
            
            _statusMessage = $"Enabled {modId}";
            return FacadeOperationResult.SuccessResult(_statusMessage);
        }
        catch (Exception ex)
        {
            _statusMessage = $"Enable failed: {ex.Message}";
            _logger.Error(ex, "Facade failed to enable mod: {ModId}", modId);
            return FacadeOperationResult.FailureResult(_statusMessage);
        }
    }

    public async Task<FacadeOperationResult> DisableModAsync(string modId)
    {
        try
        {
            _logger.Information("Facade disabling mod: {ModId}", modId);
            await _modRepository.SetEnabledAsync(modId, false);
            _statusMessage = $"Disabled {modId}";
            return FacadeOperationResult.SuccessResult(_statusMessage);
        }
        catch (InvalidOperationException ex)
        {
            _statusMessage = ex.Message;
            _logger.Warning(ex, "Failed dependency gate while disabling mod: {ModId}", modId);
            return FacadeOperationResult.FailureResult(_statusMessage);
        }
        catch (Exception ex)
        {
            _statusMessage = $"Disable failed: {ex.Message}";
            _logger.Error(ex, "Facade failed to disable mod: {ModId}", modId);
            return FacadeOperationResult.FailureResult(_statusMessage);
        }
    }

    public async Task<FacadeOperationResult> UninstallModAsync(string modId)
    {
        try
        {
            _logger.Information("Facade uninstalling mod: {ModId}", modId);
            await _modRepository.UninstallAsync(modId);
            _statusMessage = $"Uninstalled {modId}";
            return FacadeOperationResult.SuccessResult(_statusMessage);
        }
        catch (Exception ex)
        {
            _statusMessage = $"Uninstall failed: {ex.Message}";
            _logger.Error(ex, "Facade failed to uninstall mod: {ModId}", modId);
            return FacadeOperationResult.FailureResult(_statusMessage);
        }
    }

    public async Task<FacadeOperationResult> RefreshModsAsync()
    {
        try
        {
            // Force-load to ensure repository path and DB are accessible
            var mods = await _modRepository.ListInstalledAsync();
            _statusMessage = $"Loaded {mods.Count()} mods";
            return FacadeOperationResult.SuccessResult(_statusMessage);
        }
        catch (Exception ex)
        {
            _statusMessage = $"Refresh failed: {ex.Message}";
            _logger.Error(ex, "Facade failed to refresh mods");
            return FacadeOperationResult.FailureResult(_statusMessage);
        }
    }

    public string GetStatusMessage()
    {
        return _statusMessage;
    }

    public string? GetLastRuntimeError()
    {
        return _crashDiagnostics.LastRuntimeError;
    }

    public async Task<FacadeOperationResult> ValidateModEnableAsync(string modId)
    {
        try
        {
            var outcome = await _modRepository.ValidateModDetailedAsync(modId);
            _statusMessage = outcome.Success ? "Validation successful" : (outcome.FailureReason ?? "Validation failed");
            return MapValidationToFacadeResult(outcome);
        }
        catch (Exception ex)
        {
            _statusMessage = $"Validation error: {ex.Message}";
            _logger.Error(ex, "Facade dependency validation failed for {ModId}", modId);
            return FacadeOperationResult.FailureResult(_statusMessage);
        }
    }

    public async Task<DependencyValidationOutcome> ValidateModDetailedAsync(string modId)
    {
        return await _modRepository.ValidateModDetailedAsync(modId);
    }

    public Task<FacadeOperationResult> ValidateZipInstallAsync(string zipPath)
    {
        if (string.IsNullOrWhiteSpace(zipPath) || !File.Exists(zipPath))
        {
            var message = "ZIP file not found";
            _statusMessage = message;
            return Task.FromResult(FacadeOperationResult.FailureResult(message));
        }

        _statusMessage = "ZIP file ready for install";
        return Task.FromResult(FacadeOperationResult.SuccessResult(_statusMessage));
    }

    public async Task<FacadeOperationResult> InstallBepInExAsync(string gamePath)
    {
        try
        {
            _logger.Information("Facade installing BepInEx at: {GamePath}", gamePath);

            using var httpClient = new HttpClient();
            var validator = new BepInExRuntimeValidator(_logger);
            var installer = new BepInExInstallerService(_logger, httpClient, validator);

            var result = await installer.InstallAsync(gamePath);
            _statusMessage = result.Message ?? (result.Success ? "BepInEx installed" : "BepInEx install failed");

            return result.Success
                ? FacadeOperationResult.SuccessResult(_statusMessage)
                : FacadeOperationResult.FailureResult(_statusMessage);
        }
        catch (Exception ex)
        {
            _statusMessage = $"BepInEx install failed: {ex.Message}";
            _logger.Error(ex, "Facade failed to install BepInEx at {GamePath}", gamePath);
            return FacadeOperationResult.FailureResult(_statusMessage);
        }
    }

    public void SetStatusMessage(string message)
    {
        _statusMessage = message;
    }

    public Task<IEnumerable<string>> GetProfilesAsync()
    {
        try
        {
            // ProfileService is not injected yet, so instantiate locally for now
            // In a real app, this would be injected
            var profileService = new ProfileService(_logger, _modRepository, _askaPath);
            return Task.FromResult(profileService.GetProfiles().Select(p => p.Name));
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Facade failed to get profiles");
            return Task.FromResult(Enumerable.Empty<string>());
        }
    }

    public async Task<FacadeOperationResult> SwitchToProfileAsync(string profileName)
    {
        try
        {
            var profileService = new ProfileService(_logger, _modRepository, _askaPath);
            var success = await profileService.SwitchToProfileAsync(profileName);
            if (success)
            {
                _statusMessage = $"Switched to profile: {profileName}";
                return FacadeOperationResult.SuccessResult(_statusMessage);
            }
            else
            {
                _statusMessage = $"Failed to switch to profile: {profileName}";
                return FacadeOperationResult.FailureResult(_statusMessage);
            }
        }
        catch (Exception ex)
        {
            _statusMessage = $"Profile switch failed: {ex.Message}";
            _logger.Error(ex, "Facade failed to switch to profile: {Profile}", profileName);
            return FacadeOperationResult.FailureResult(_statusMessage);
        }
    }

    public async Task<FacadeOperationResult> SaveCurrentAsProfileAsync(string profileName)
    {
        try
        {
            var profileService = new ProfileService(_logger, _modRepository, _askaPath);
            var success = await profileService.SaveCurrentAsProfileAsync(profileName);
            if (success)
            {
                _statusMessage = $"Saved current mods as profile: {profileName}";
                return FacadeOperationResult.SuccessResult(_statusMessage);
            }
            else
            {
                _statusMessage = $"Failed to save profile: {profileName}";
                return FacadeOperationResult.FailureResult(_statusMessage);
            }
        }
        catch (Exception ex)
        {
            _statusMessage = $"Profile save failed: {ex.Message}";
            _logger.Error(ex, "Facade failed to save profile: {Profile}", profileName);
            return FacadeOperationResult.FailureResult(_statusMessage);
        }
    }

    public Task<string?> GetActiveProfileAsync()
    {
        try
        {
            var profileService = new ProfileService(_logger, _modRepository, _askaPath);
            return Task.FromResult(profileService.GetActiveProfile());
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Facade failed to get active profile");
            return Task.FromResult<string?>(null);
        }
    }

    private ModDisplayModel ConvertToDisplayModel(ModInfo mod)
    {
        try
        {
            return new ModDisplayModel
            {
                Id = mod.Id,
                Name = mod.Name,
                Version = NormalizeVersion(mod.Version),
                Author = mod.Author ?? "Unknown",
                Description = mod.Description ?? "No description available",
                IsEnabled = mod.IsEnabled,
                InstallPath = mod.InstallPath ?? "Unknown",
                InstallDate = mod.InstallDate,
                LastUpdated = mod.LastUpdated,
                Dependencies = mod.Dependencies?.Select(d => new ModDependencyDisplayModel
                {
                    Id = d.Id,
                    MinVersion = NormalizeVersion(d.MinVersion),
                    IsOptional = d.Optional,
                    IsSatisfied = false, // Will be determined by dependency resolution
                    StatusMessage = "Unknown"
                }).ToList() ?? new List<ModDependencyDisplayModel>(),
                Warnings = new List<string>(),
                Errors = new List<string>()
            };
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to convert ModInfo to ModDisplayModel for {ModId}", mod.Id);
            
            // Return a safe fallback model
            return new ModDisplayModel
            {
                Id = mod.Id,
                Name = mod.Name ?? "Unknown Mod",
                Version = "1.0.0",
                Author = "Unknown",
                Description = "Error loading mod information",
                IsEnabled = false,
                InstallPath = "Unknown",
                InstallDate = DateTime.UtcNow,
                LastUpdated = DateTime.UtcNow,
                Dependencies = new List<ModDependencyDisplayModel>(),
                Warnings = new List<string> { "Failed to load complete mod information" },
                Errors = new List<string> { ex.Message }
            };
        }
    }

    private string NormalizeVersion(string version)
    {
        if (string.IsNullOrEmpty(version))
            return "1.0.0";
        
        // Use VersionService for consistent version formatting
        try
        {
            return VersionService.NormalizeVersion(version);
        }
        catch
        {
            return version; // Fallback to original if normalization fails
        }
    }
    public bool IsGameRunning()
    {
        var detector = new AskaDetector();
        return detector.IsAskaRunning();
    }

    public Task<IEnumerable<RuntimeError>> GetRuntimeErrorsAsync()
    {
        return _modRepository.GetRuntimeErrorsAsync();
    }

    public async Task<string> GenerateDiagnosticBundleAsync()
    {
        var errors = await GetRuntimeErrorsAsync();
        var mods = await GetInstalledModsAsync();
        var bundle = new
        {
            Timestamp = DateTime.UtcNow,
            GamePath = _askaPath,
            Errors = errors,
            Mods = mods
        };
        return System.Text.Json.JsonSerializer.Serialize(bundle, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    }

    public async Task<FacadeOperationResult> KillGameAsync()
    {
        try
        {
            _logger.Information("Facade killing game process");
            var processes = System.Diagnostics.Process.GetProcessesByName("Aska");
            foreach (var process in processes)
            {
                _logger.Information("Terminating process: {ProcessName} (PID: {Id})", process.ProcessName, process.Id);
                process.Kill();
                await process.WaitForExitAsync();
            }
            
            _statusMessage = "Game process terminated";
            return FacadeOperationResult.SuccessResult(_statusMessage);
        }
        catch (Exception ex)
        {
            _statusMessage = $"Failed to kill game: {ex.Message}";
            _logger.Error(ex, "Facade failed to kill game process");
            return FacadeOperationResult.FailureResult(_statusMessage);
        }
    }

    private FacadeOperationResult MapValidationToFacadeResult(DependencyValidationOutcome outcome)
    {
        var result = new FacadeOperationResult
        {
            Success = outcome.Success,
            Message = outcome.FailureReason ?? (outcome.Success ? "Operation successful" : "Operation failed"),
            Errors = outcome.Errors?.ToList() ?? new List<string>(),
            DependencyErrors = outcome.MissingDependencies.Select(m => new ModDependencyDisplayModel
            {
                Id = m.DependencyId,
                MinVersion = NormalizeVersion(m.RequiredVersion),
                IsOptional = m.IsOptional,
                IsSatisfied = false,
                StatusMessage = "Missing"
            }).Concat(outcome.VersionConflicts.Select(v => new ModDependencyDisplayModel
            {
                Id = v.DependencyId,
                MinVersion = NormalizeVersion(v.RequiredVersion),
                IsSatisfied = false,
                StatusMessage = $"Conflict: installed {v.InstalledVersion}"
            })).Concat(outcome.CircularDependencies.Select(c => new ModDependencyDisplayModel
            {
                Id = "Cycle",
                StatusMessage = $"Circular: {c.CycleDescription}"
            })).ToList()
        };

        if (!outcome.Success && string.IsNullOrEmpty(result.Message) && result.DependencyErrors.Any())
        {
            result.Message = "Dependency requirements not met.";
        }

        return result;
    }

    public async Task<IEnumerable<ModUpdateInfo>> CheckForUpdatesAsync()
    {
        try
        {
            return await _modRepository.CheckForUpdatesAsync();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Facade failed to check for updates");
            return Enumerable.Empty<ModUpdateInfo>();
        }
    }

    public async Task<FacadeOperationResult> UpdateModAsync(string modId)
    {
        try
        {
            await _modRepository.UpdateModAsync(modId);
            return FacadeOperationResult.SuccessResult($"Mod {modId} updated successfully.");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Facade failed to update mod: {ModId}", modId);
            return FacadeOperationResult.FailureResult($"Failed to update mod: {ex.Message}");
        }
    }

    public async Task<IEnumerable<RemoteModInfo>> GetAvailableModsAsync()
    {
        try
        {
            return await _modRepository.GetAvailableModsAsync();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Facade failed to get available mods");
            return Enumerable.Empty<RemoteModInfo>();
        }
    }

    // --- App Self-Update ---

    private AppUpdateService? _appUpdateService;

    private AppUpdateService GetAppUpdateService()
    {
        _appUpdateService ??= new AppUpdateService(_logger);
        return _appUpdateService;
    }

    public async Task<AppUpdateInfo?> CheckForAppUpdateAsync()
    {
        try
        {
            return await GetAppUpdateService().CheckForAppUpdateAsync();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Facade failed to check for app updates");
            return null;
        }
    }

    public async Task<bool> InitiateAppUpdateAsync()
    {
        try
        {
            return await GetAppUpdateService().InitiateUpdateAsync();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Facade failed to initiate app update");
            return false;
        }
    }

    public async Task<FacadeOperationResult> InstallFromUrlAsync(Uri url, string modName)
    {
        try
        {
            _logger.Information("Facade installing remote mod '{ModName}' from {Url}", modName, url);
            var result = await _modRepository.InstallFromUrlAsync(url);
            
            if (result.Success)
            {
                var message = $"Installed {modName} successfully";
                if (result.Warnings.Any())
                {
                    message += $" (Warnings: {string.Join(", ", result.Warnings)})";
                }
                _statusMessage = message;
                return FacadeOperationResult.SuccessResult(_statusMessage);
            }
            else
            {
                _statusMessage = $"Failed to install {modName}: {string.Join(", ", result.Errors)}";
                return FacadeOperationResult.FailureResult(_statusMessage);
            }
        }
        catch (Exception ex)
        {
            _statusMessage = $"Failed to install {modName}: {ex.Message}";
            _logger.Error(ex, "Facade failed to install remote mod {ModName} from {Url}", modName, url);
            return FacadeOperationResult.FailureResult(_statusMessage);
        }


        }

    public async Task<FacadeOperationResult> InstallModWithDependenciesAsync(RemoteModInfo mod)
    {
        try
        {
            _logger.Information("Facade installing remote mod '{ModName}' and dependencies", mod.Name);
            var result = await _modRepository.InstallModWithDependenciesAsync(mod);
            
            if (result.Success)
            {
                var message = $"Installed {mod.Name} and dependencies successfully";
                if (result.Warnings.Any())
                {
                    message += $" (Warnings: {string.Join(", ", result.Warnings)})";
                }
                _statusMessage = message;
                return FacadeOperationResult.SuccessResult(_statusMessage);
            }
            else
            {
                _statusMessage = $"Failed to install {mod.Name}: {string.Join(", ", result.Errors)}";
                return FacadeOperationResult.FailureResult(_statusMessage);
            }
        }
        catch (Exception ex)
        {
             _statusMessage = $"Failed to install {mod.Name}: {ex.Message}";
            _logger.Error(ex, "Facade failed to install remote mod {ModName} recursively", mod.Name);
            return FacadeOperationResult.FailureResult(_statusMessage);
        }
    }
}
