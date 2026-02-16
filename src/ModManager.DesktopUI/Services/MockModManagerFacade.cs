using ModManager.Core.Models;
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
        // Initialize with sample data for UI testing
        InitializeSampleData();
    }

    public IEnumerable<ModDisplayModel> GetInstalledMods()
    {
        return _installedMods.Select(ConvertToDisplayModel);
    }

    public FacadeOperationResult InstallFromZip(string zipPath)
    {
        try
        {
            _statusMessage = $"Installing from {Path.GetFileName(zipPath)}...";
            
            // Simulate installation delay
            System.Threading.Thread.Sleep(1000);
            
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

    public FacadeOperationResult EnableMod(string modId)
    {
        var mod = _installedMods.FirstOrDefault(m => m.Id == modId);
        if (mod != null)
        {
            mod.IsEnabled = true;
            _statusMessage = $"Enabled {mod.Name}";
            return FacadeOperationResult.SuccessResult(_statusMessage);
        }
        
        _statusMessage = $"Mod not found: {modId}";
        return FacadeOperationResult.FailureResult(_statusMessage);
    }

    public FacadeOperationResult DisableMod(string modId)
    {
        var mod = _installedMods.FirstOrDefault(m => m.Id == modId);
        if (mod != null)
        {
            mod.IsEnabled = false;
            _statusMessage = $"Disabled {mod.Name}";
            return FacadeOperationResult.SuccessResult(_statusMessage);
        }
        
        _statusMessage = $"Mod not found: {modId}";
        return FacadeOperationResult.FailureResult(_statusMessage);
    }

    public FacadeOperationResult UninstallMod(string modId)
    {
        var mod = _installedMods.FirstOrDefault(m => m.Id == modId);
        if (mod != null)
        {
            _installedMods.Remove(mod);
            _statusMessage = $"Uninstalled {mod.Name}";
            return FacadeOperationResult.SuccessResult(_statusMessage);
        }
        
        _statusMessage = $"Mod not found: {modId}";
        return FacadeOperationResult.FailureResult(_statusMessage);
    }

    public FacadeOperationResult RefreshMods()
    {
        _statusMessage = "Refreshing mod list...";
        System.Threading.Thread.Sleep(500);
        _statusMessage = $"Loaded {_installedMods.Count} mods";
        return FacadeOperationResult.SuccessResult(_statusMessage);
    }

    public string GetStatusMessage()
    {
        return _statusMessage;
    }

    public FacadeOperationResult ValidateModEnable(string modId)
    {
        var mod = _installedMods.FirstOrDefault(m => m.Id == modId);
        if (mod == null)
        {
            return FacadeOperationResult.FailureResult("Mod not found");
        }

        // Mock dependency validation
        if (mod.Name.Contains("2"))
        {
            return FacadeOperationResult.FailureResult("Cannot enable: Missing dependency 'dependency-1'");
        }

        return FacadeOperationResult.SuccessResult("Mod can be enabled");
    }

    public FacadeOperationResult ValidateZipInstall(string zipPath)
    {
        // Mock validation
        if (Path.GetFileName(zipPath).Contains("invalid"))
        {
            return FacadeOperationResult.FailureResult("Invalid ZIP: Contains malicious files");
        }

        return FacadeOperationResult.SuccessResult("ZIP is valid for installation");
    }

    public FacadeOperationResult InstallBepInEx(string gamePath)
    {
        _statusMessage = "Mock BepInEx install not supported";
        return FacadeOperationResult.FailureResult(_statusMessage);
    }

    public void SetStatusMessage(string message)
    {
        _statusMessage = message;
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
        _installedMods.AddRange(new[]
        {
            new ModInfo
            {
                Id = "example-mod-1",
                Name = "Example Mod 1",
                Version = "2.1.0",
                Author = "Example Author",
                Description = "This is an example mod for testing the UI functionality",
                IsEnabled = true,
                InstallPath = "/path/to/example-mod-1",
                InstallDate = DateTime.UtcNow.AddDays(-5),
                LastUpdated = DateTime.UtcNow.AddDays(-1),
                Dependencies = new List<ModDependency>
                {
                    new ModDependency { Id = "dependency-1", MinVersion = "1.0.0", Optional = false }
                }
            },
            new ModInfo
            {
                Id = "example-mod-2",
                Name = "Example Mod 2",
                Version = "1.5.2",
                Author = "Another Author",
                Description = "Another example mod with different configuration",
                IsEnabled = false,
                InstallPath = "/path/to/example-mod-2",
                InstallDate = DateTime.UtcNow.AddDays(-10),
                LastUpdated = DateTime.UtcNow.AddDays(-3),
                Dependencies = new List<ModDependency>
                {
                    new ModDependency { Id = "dependency-2", MinVersion = "2.0.0", Optional = true }
                }
            },
            new ModInfo
            {
                Id = "utility-mod",
                Name = "Utility Mod",
                Version = "3.0.0",
                Author = "Utility Developer",
                Description = "A utility mod that provides helpful functions",
                IsEnabled = true,
                InstallPath = "/path/to/utility-mod",
                InstallDate = DateTime.UtcNow.AddDays(-2),
                LastUpdated = DateTime.UtcNow,
                Dependencies = new List<ModDependency>()
            }
        });
    }
}
