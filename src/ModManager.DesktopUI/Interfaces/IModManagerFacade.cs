using ModManager.Core.Models;
using ModManager.Core.Services;
using ModManager.DesktopUI.Models;
using System;
using System.Collections.Generic;

namespace ModManager.DesktopUI.Interfaces;

/// <summary>
/// Facade interface for mod management operations
/// </summary>
public interface IModManagerFacade
{
    /// <summary>
    /// Gets all installed mods
    /// </summary>
    Task<IEnumerable<ModDisplayModel>> GetInstalledModsAsync();

    /// <summary>
    /// Installs a mod from a ZIP file
    /// </summary>
    Task<FacadeOperationResult> InstallFromZipAsync(string zipPath);

    /// <summary>
    /// Enables a mod by its ID
    /// </summary>
    Task<FacadeOperationResult> EnableModAsync(string modId);

    /// <summary>
    /// Disables a mod by its ID
    /// </summary>
    Task<FacadeOperationResult> DisableModAsync(string modId);

    /// <summary>
    /// Uninstalls a mod by its ID
    /// </summary>
    Task<FacadeOperationResult> UninstallModAsync(string modId);

    /// <summary>
    /// Refreshes the mod list
    /// </summary>
    Task<FacadeOperationResult> RefreshModsAsync();

    /// <summary>
    /// Gets the current status message
    /// </summary>
    string GetStatusMessage();

    /// <summary>
    /// Validates if a mod can be enabled (dependency check)
    /// </summary>
    Task<FacadeOperationResult> ValidateModEnableAsync(string modId);

    /// <summary>
    /// Validates if a ZIP can be installed (dependency check)
    /// </summary>
    Task<FacadeOperationResult> ValidateZipInstallAsync(string zipPath);

    /// <summary>
    /// Installs BepInEx into the configured game path
    /// </summary>
    Task<FacadeOperationResult> InstallBepInExAsync(string gamePath);

    /// <summary>
    /// Sets the status message (for error handling)
    /// </summary>
    void SetStatusMessage(string message);

    /// <summary>
    /// Gets all available profiles
    /// </summary>
    Task<IEnumerable<string>> GetProfilesAsync();

    /// <summary>
    /// Switches to a profile by name
    /// </summary>
    Task<FacadeOperationResult> SwitchToProfileAsync(string profileName);

    /// <summary>
    /// Saves current enabled mods as a new profile
    /// </summary>
    Task<FacadeOperationResult> SaveCurrentAsProfileAsync(string profileName);

    /// <summary>
    /// Gets the currently active profile name
    /// </summary>
    Task<string?> GetActiveProfileAsync();

    /// <summary>
    /// Gets the last runtime error reported by the crash diagnostics pipeline
    /// </summary>
    string? GetLastRuntimeError();

    /// <summary>
    /// Raised when the crash diagnostics service publishes a new error line
    /// </summary>
    event EventHandler<string?>? CrashLogUpdated;
    
    /// <summary>
    /// Checks if the game is currently running
    /// </summary>
    bool IsGameRunning();

    /// <summary>
    /// Gets all logged runtime errors
    /// </summary>
    Task<IEnumerable<RuntimeError>> GetRuntimeErrorsAsync();

    /// <summary>
    /// Generates a diagnostic bundle (JSON string) for support
    /// </summary>
    Task<string> GenerateDiagnosticBundleAsync();

    /// <summary>
    /// Forcefully terminates the game process if it is running
    /// </summary>
    Task<FacadeOperationResult> KillGameAsync();

    /// <summary>
    /// Checks for available updates for installed mods
    /// </summary>
    Task<IEnumerable<ModUpdateInfo>> CheckForUpdatesAsync();

    /// <summary>
    /// Updates a mod to the latest version
    /// </summary>
    Task<FacadeOperationResult> UpdateModAsync(string modId);

    /// <summary>
    /// Gets a list of mods available for discovery from remote repositories
    /// </summary>
    Task<IEnumerable<RemoteModInfo>> GetAvailableModsAsync();

    /// <summary>
    /// Checks if an update is available for the Mod Manager application itself
    /// </summary>
    Task<AppUpdateInfo?> CheckForAppUpdateAsync();

    /// <summary>
    /// Initiates the Mod Manager self-update process
    /// </summary>
    Task<bool> InitiateAppUpdateAsync();
    /// <summary>
    /// Installs a mod from a remote URL (download and install)
    /// </summary>
    Task<FacadeOperationResult> InstallFromUrlAsync(Uri url, string modName);

    /// <summary>
    /// Installs a mod and its dependencies from a remote source
    /// </summary>
    Task<FacadeOperationResult> InstallModWithDependenciesAsync(RemoteModInfo mod);
}
