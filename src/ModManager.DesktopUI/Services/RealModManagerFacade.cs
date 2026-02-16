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

    public IEnumerable<ModDisplayModel> GetInstalledMods()
    {
        try
        {
            _logger.Information("Starting mod load from Core repository");
            
            // Use real Core services to load mods
            var mods = _modRepository.ListInstalledAsync().GetAwaiter().GetResult();
            
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

    public FacadeOperationResult InstallFromZip(string zipPath)
    {
        try
        {
            _logger.Information("Facade installing mod from ZIP: {ZipPath}", zipPath);
            _modRepository.InstallFromZipAsync(zipPath).GetAwaiter().GetResult();
            _statusMessage = "Installation completed";
            return FacadeOperationResult.SuccessResult(_statusMessage);
        }
        catch (Exception ex)
        {
            _statusMessage = $"Installation failed: {ex.Message}";
            _logger.Error(ex, "Facade failed to install from ZIP: {ZipPath}", zipPath);
            return FacadeOperationResult.FailureResult(_statusMessage);
        }
    }

    public FacadeOperationResult EnableMod(string modId)
    {
        try
        {
            _logger.Information("Facade enabling mod: {ModId}", modId);
            _modRepository.SetEnabledAsync(modId, true).GetAwaiter().GetResult();
            _statusMessage = $"Enabled {modId}";
            return FacadeOperationResult.SuccessResult(_statusMessage);
        }
        catch (InvalidOperationException ex)
        {
            _statusMessage = ex.Message;
            _logger.Warning(ex, "Dependency gate prevented enabling mod: {ModId}", modId);
            return FacadeOperationResult.FailureResult(_statusMessage);
        }
        catch (Exception ex)
        {
            _statusMessage = $"Enable failed: {ex.Message}";
            _logger.Error(ex, "Facade failed to enable mod: {ModId}", modId);
            return FacadeOperationResult.FailureResult(_statusMessage);
        }
    }

    public FacadeOperationResult DisableMod(string modId)
    {
        try
        {
            _logger.Information("Facade disabling mod: {ModId}", modId);
            _modRepository.SetEnabledAsync(modId, false).GetAwaiter().GetResult();
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

    public FacadeOperationResult UninstallMod(string modId)
    {
        try
        {
            _logger.Information("Facade uninstalling mod: {ModId}", modId);
            _modRepository.UninstallAsync(modId).GetAwaiter().GetResult();
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

    public FacadeOperationResult RefreshMods()
    {
        try
        {
            // Force-load to ensure repository path and DB are accessible
            var mods = _modRepository.ListInstalledAsync().GetAwaiter().GetResult();
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

    public FacadeOperationResult ValidateModEnable(string modId)
    {
        try
        {
            var valid = _modRepository.ValidateModAsync(modId).GetAwaiter().GetResult();
            if (!valid)
            {
                var message = $"Validation failed for {modId}";
                _statusMessage = message;
                return FacadeOperationResult.FailureResult(message);
            }

            _statusMessage = "Validation succeeded";
            return FacadeOperationResult.SuccessResult(_statusMessage);
        }
        catch (InvalidOperationException ex)
        {
            _statusMessage = ex.Message;
            _logger.Warning(ex, "Dependency validation failed for {ModId}", modId);
            return FacadeOperationResult.FailureResult(_statusMessage);
        }
        catch (Exception ex)
        {
            _statusMessage = $"Validation error: {ex.Message}";
            _logger.Error(ex, "Facade dependency validation failed for {ModId}", modId);
            return FacadeOperationResult.FailureResult(_statusMessage);
        }
    }

    public FacadeOperationResult ValidateZipInstall(string zipPath)
    {
        if (string.IsNullOrWhiteSpace(zipPath) || !File.Exists(zipPath))
        {
            var message = "ZIP file not found";
            _statusMessage = message;
            return FacadeOperationResult.FailureResult(message);
        }

        _statusMessage = "ZIP file ready for install";
        return FacadeOperationResult.SuccessResult(_statusMessage);
    }

    public FacadeOperationResult InstallBepInEx(string gamePath)
    {
        try
        {
            _logger.Information("Facade installing BepInEx at: {GamePath}", gamePath);

            using var httpClient = new HttpClient();
            var validator = new BepInExRuntimeValidator(_logger);
            var installer = new BepInExInstallerService(_logger, httpClient, validator);

            var result = installer.InstallAsync(gamePath).GetAwaiter().GetResult();
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

    public IEnumerable<string> GetProfiles()
    {
        try
        {
            // ProfileService is not injected yet, so instantiate locally for now
            // In a real app, this would be injected
            var profileService = new ProfileService(_logger, _modRepository, _askaPath);
            return profileService.GetProfiles().Select(p => p.Name);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Facade failed to get profiles");
            return Enumerable.Empty<string>();
        }
    }

    public FacadeOperationResult SwitchToProfile(string profileName)
    {
        try
        {
            var profileService = new ProfileService(_logger, _modRepository, _askaPath);
            var success = profileService.SwitchToProfileAsync(profileName).GetAwaiter().GetResult();
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

    public FacadeOperationResult SaveCurrentAsProfile(string profileName)
    {
        try
        {
            var profileService = new ProfileService(_logger, _modRepository, _askaPath);
            var success = profileService.SaveCurrentAsProfileAsync(profileName).GetAwaiter().GetResult();
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

    public string? GetActiveProfile()
    {
        try
        {
            var profileService = new ProfileService(_logger, _modRepository, _askaPath);
            return profileService.GetActiveProfile();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Facade failed to get active profile");
            return null;
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

    public IEnumerable<RuntimeError> GetRuntimeErrors()
    {
        return _modRepository.GetRuntimeErrorsAsync().GetAwaiter().GetResult();
    }

    public string GenerateDiagnosticBundle()
    {
        var errors = GetRuntimeErrors();
        var mods = GetInstalledMods();
        var bundle = new
        {
            Timestamp = DateTime.UtcNow,
            GamePath = _askaPath,
            Errors = errors,
            Mods = mods
        };
        return System.Text.Json.JsonSerializer.Serialize(bundle, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    }
}
