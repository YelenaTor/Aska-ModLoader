using ModManager.Core.Models;
using ModManager.Core.Services;
using ModManager.DesktopUI.Interfaces;
using ModManager.DesktopUI.Models;
using System.IO;

namespace ModManager.DesktopUI.Services;

/// <summary>
/// In-memory mock implementation of IModManagerFacade for UI testing
/// </summary>
public class MockModManagerFacade : IModManagerFacade
{
    private readonly List<ModInfo> _installedMods = new();
    private string _statusMessage = "Ready";
    public MockModManagerFacade()
    {
        InitializeSampleData();
    }

#pragma warning disable CS0067
    public event EventHandler<string?>? CrashLogUpdated;
#pragma warning restore CS0067

    public Task<IEnumerable<ModDisplayModel>> GetInstalledModsAsync()
    {
        return Task.FromResult(_installedMods.Select(ConvertToDisplayModel));
    }

    public async Task<FacadeOperationResult> InstallFromZipAsync(string zipPath)
    {
        try
        {
            _statusMessage = $"Installing from {Path.GetFileName(zipPath)}...";
            
            // Simulate installation delay
            await Task.Delay(1000);
            
            // Add a mock mod
            var newMod = new ModInfo
            {
                Id = "mock-mod-" + DateTime.Now.Ticks,
                Name = Path.GetFileNameWithoutExtension(zipPath),
                Version = "1.0.0",
                Author = "Mock Author",
                Description = "Mock mod installed for testing",
                IsEnabled = true,
                InstallPath = $"/mock/path/{Path.GetFileNameWithoutExtension(zipPath)}",
                InstallDate = DateTime.UtcNow,
                LastUpdated = DateTime.UtcNow,
                Dependencies = new List<ModDependency>()
            };
            
            _installedMods.Add(newMod);
            _statusMessage = $"Successfully installed {newMod.Name}";
            return FacadeOperationResult.SuccessResult(_statusMessage);
        }
        catch (Exception ex)
        {
            _statusMessage = $"Installation failed: {ex.Message}";
            return FacadeOperationResult.FailureResult(_statusMessage);
        }
    }

    public async Task<FacadeOperationResult> EnableModAsync(string modId)
    {
        var mod = _installedMods.FirstOrDefault(m => m.Id == modId);
        if (mod != null)
        {
            var outcome = await ValidateModDetailedAsync(modId);
            if (!outcome.Success)
            {
                return new FacadeOperationResult 
                { 
                    Success = false, 
                    Message = outcome.FailureReason ?? "Validation failed",
                    DependencyErrors = outcome.MissingDependencies.Select(m => new ModDependencyDisplayModel
                    {
                        Id = m.DependencyId,
                        MinVersion = m.RequiredVersion,
                        IsOptional = m.IsOptional,
                        IsSatisfied = false,
                        StatusMessage = "Missing"
                    }).Concat(outcome.VersionConflicts.Select(v => new ModDependencyDisplayModel
                    {
                        Id = v.DependencyId,
                        MinVersion = v.RequiredVersion,
                        IsSatisfied = false,
                        StatusMessage = $"Conflict: installed {v.InstalledVersion}"
                    })).ToList()
                };
            }

            mod.IsEnabled = true;
            _statusMessage = $"Enabled {mod.Name}";
            return FacadeOperationResult.SuccessResult(_statusMessage);
        }
        
        _statusMessage = $"Mod not found: {modId}";
        return FacadeOperationResult.FailureResult(_statusMessage);
    }

    public Task<FacadeOperationResult> DisableModAsync(string modId)
    {
        var mod = _installedMods.FirstOrDefault(m => m.Id == modId);
        if (mod != null)
        {
            mod.IsEnabled = false;
            _statusMessage = $"Disabled {mod.Name}";
            return Task.FromResult(FacadeOperationResult.SuccessResult(_statusMessage));
        }
        
        _statusMessage = $"Mod not found: {modId}";
        return Task.FromResult(FacadeOperationResult.FailureResult(_statusMessage));
    }

    public Task<FacadeOperationResult> UninstallModAsync(string modId)
    {
        var mod = _installedMods.FirstOrDefault(m => m.Id == modId);
        if (mod != null)
        {
            _installedMods.Remove(mod);
            _statusMessage = $"Uninstalled {mod.Name}";
            return Task.FromResult(FacadeOperationResult.SuccessResult(_statusMessage));
        }
        
        _statusMessage = $"Mod not found: {modId}";
        return Task.FromResult(FacadeOperationResult.FailureResult(_statusMessage));
    }

    public async Task<FacadeOperationResult> RefreshModsAsync()
    {
        _statusMessage = "Refreshing mod list...";
        await Task.Delay(500);
        _statusMessage = $"Loaded {_installedMods.Count} mods";
        return FacadeOperationResult.SuccessResult(_statusMessage);
    }

    public string GetStatusMessage()
    {
        return _statusMessage;
    }

    public string? GetLastRuntimeError()
    {
        return "No runtime errors (Mock)";
    }

    public async Task<FacadeOperationResult> ValidateModEnableAsync(string modId)
    {
        var outcome = await ValidateModDetailedAsync(modId);
        return new FacadeOperationResult
        {
            Success = outcome.Success,
            Message = outcome.FailureReason ?? (outcome.Success ? "Validation successful" : "Validation failed"),
            DependencyErrors = outcome.MissingDependencies.Select(m => new ModDependencyDisplayModel
            {
                Id = m.DependencyId,
                MinVersion = m.RequiredVersion,
                IsOptional = m.IsOptional,
                IsSatisfied = false,
                StatusMessage = "Missing"
            }).ToList()
        };
    }


    public Task<DependencyValidationOutcome> ValidateModDetailedAsync(string modId)
    {
        var mod = _installedMods.FirstOrDefault(m => m.Id == modId);
        if (mod == null)
        {
            return Task.FromResult(new DependencyValidationOutcome { Success = false, FailureReason = "Mod not found" });
        }

        // Mock dependency validation
        if (mod.Name.Contains("2"))
        {
            var outcome = new DependencyValidationOutcome
            {
                Success = false,
                FailureReason = "Cannot enable: Missing dependency 'dependency-1'"
            };
            outcome.MissingDependencies.Add(new MissingDependency
            {
                ModId = mod.Id,
                ModName = mod.Name,
                DependencyId = "dependency-1",
                RequiredVersion = "1.0.0",
                IsOptional = false
            });
            return Task.FromResult(outcome);
        }

        return Task.FromResult(new DependencyValidationOutcome { Success = true });
    }

    public Task<FacadeOperationResult> ValidateZipInstallAsync(string zipPath)
    {
        // Mock validation
        if (Path.GetFileName(zipPath).Contains("invalid"))
        {
            return Task.FromResult(FacadeOperationResult.FailureResult("Invalid ZIP: Contains malicious files"));
        }

        return Task.FromResult(FacadeOperationResult.SuccessResult("ZIP is valid for installation"));
    }

    public Task<FacadeOperationResult> InstallBepInExAsync(string gamePath)
    {
        _statusMessage = "Mock BepInEx install not supported";
        return Task.FromResult(FacadeOperationResult.FailureResult(_statusMessage));
    }

    public void SetStatusMessage(string message)
    {
        _statusMessage = message;
    }

    public Task<IEnumerable<string>> GetProfilesAsync()
    {
        return Task.FromResult<IEnumerable<string>>(new[] { "Default", "Hardcore" });
    }

    public Task<FacadeOperationResult> SwitchToProfileAsync(string profileName)
    {
        // Mock switching - just return success
        _statusMessage = $"Switched to profile: {profileName}";
        return Task.FromResult(FacadeOperationResult.SuccessResult(_statusMessage));
    }

    public Task<FacadeOperationResult> SaveCurrentAsProfileAsync(string profileName)
    {
        _statusMessage = $"Saved current mods as profile: {profileName}";
        return Task.FromResult(FacadeOperationResult.SuccessResult(_statusMessage));
    }

    public Task<string?> GetActiveProfileAsync()
    {
        return Task.FromResult<string?>("Default");
    }

    public bool IsGameRunning()
    {
        return false;
    }

    public Task<IEnumerable<RuntimeError>> GetRuntimeErrorsAsync()
    {
        return Task.FromResult<IEnumerable<RuntimeError>>(new List<RuntimeError>
        {
            new RuntimeError
            {
                ModId = "example-mod-1",
                ModName = "Example Mod 1",
                Message = "Mock error for testing",
                Severity = ErrorSeverity.Warning
            }
        });
    }

    public Task<string> GenerateDiagnosticBundleAsync()
    {
        return Task.FromResult("mock-diagnostic-bundle-json");
    }

    private ModDisplayModel ConvertToDisplayModel(ModInfo mod)
    {
        return new ModDisplayModel
        {
            Id = mod.Id,
            Name = mod.Name,
            Version = mod.Version,
            Author = mod.Author,
            Description = mod.Description,
            IsEnabled = mod.IsEnabled,
            InstallPath = mod.InstallPath,
            InstallDate = mod.InstallDate,
            LastUpdated = mod.LastUpdated,
            Dependencies = mod.Dependencies.Select(d => new ModDependencyDisplayModel
            {
                Id = d.Id,
                MinVersion = d.MinVersion,
                IsOptional = d.Optional,
                IsSatisfied = true, // Mock: always satisfied
                StatusMessage = "Available"
            }).ToList(),
            Warnings = mod.Name.Contains("2") ? new List<string> { "This mod has warnings" } : new List<string>(),
            Errors = new List<string>()
        };
    }

    private void InitializeSampleData()
    {
        // No mock data as per user request
    }

    public async Task<FacadeOperationResult> KillGameAsync()
    {
        await Task.Delay(500);
        _statusMessage = "Mock: Game process terminated";
        return FacadeOperationResult.SuccessResult(_statusMessage);
    }

    public async Task<IEnumerable<ModUpdateInfo>> CheckForUpdatesAsync()
    {
        await Task.Delay(100);
        return new List<ModUpdateInfo>();
    }

    public async Task<FacadeOperationResult> UpdateModAsync(string modId)
    {
        await Task.Delay(500); 
        _statusMessage = $"Mod update not supported in temporary mode";
        return FacadeOperationResult.FailureResult(_statusMessage);
    }

    public async Task<IEnumerable<RemoteModInfo>> GetAvailableModsAsync()
    {
        await Task.Delay(100);
        return new List<RemoteModInfo>();
    }

    public async Task<AppUpdateInfo?> CheckForAppUpdateAsync()
    {
        await Task.Delay(100);
        return null;
    }

    public async Task<bool> InitiateAppUpdateAsync()
    {
        await Task.Delay(100);
        return true;
    }

    public async Task<FacadeOperationResult> InstallFromUrlAsync(Uri url, string modName)
    {
        await Task.Delay(500);
        return FacadeOperationResult.FailureResult("Remote installation not supported in mock mode");
    }
    public async Task<FacadeOperationResult> InstallModWithDependenciesAsync(RemoteModInfo mod)
    {
        await Task.Delay(500);
        return FacadeOperationResult.FailureResult("Recursive installation not supported in mock mode");
    }
}
