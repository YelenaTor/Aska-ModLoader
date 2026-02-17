using ModManager.Core.Models;
using ModManager.Core.Services;

namespace ModManager.Core.Interfaces;

/// <summary>
/// Interface for mod repository operations
/// </summary>
public interface IModRepository
{
    /// <summary>
    /// Gets all installed mods
    /// </summary>
    Task<IEnumerable<ModInfo>> ListInstalledAsync();

    /// <summary>
    /// Gets a specific mod by ID
    /// </summary>
    Task<ModInfo?> GetModAsync(string modId);

    /// <summary>
    /// Installs a mod from a ZIP file
    /// </summary>
    Task<InstallationResult> InstallFromZipAsync(string zipPath);

    /// <summary>
    /// Installs a mod from a URL
    /// </summary>
    Task<InstallationResult> InstallFromUrlAsync(Uri packageUrl);

    /// <summary>
    /// Uninstalls a mod
    /// </summary>
    Task UninstallAsync(string modId);

    /// <summary>
    /// Enables or disables a mod
    /// </summary>
    Task<DependencyValidationOutcome> SetEnabledAsync(string modId, bool enabled);

    /// <summary>
    /// Updates a mod to the latest version
    /// </summary>
    Task UpdateModAsync(string modId);

    /// <summary>
    /// Checks for available updates
    /// </summary>
    Task<IEnumerable<ModUpdateInfo>> CheckForUpdatesAsync();

    /// <summary>
    /// Validates mod integrity
    /// </summary>
    Task<bool> ValidateModAsync(string modId);

    /// <summary>
    /// Performs detailed mod validation including dependencies
    /// </summary>
    Task<DependencyValidationOutcome> ValidateModDetailedAsync(string modId);

    /// <summary>
    /// Logs a runtime error to the repository
    /// </summary>
    Task LogRuntimeErrorAsync(RuntimeError error);

    /// <summary>
    /// Gets all logged runtime errors
    /// </summary>
    Task<IEnumerable<RuntimeError>> GetRuntimeErrorsAsync();

    /// <summary>
    /// Clears runtime errors, optionally keeping only the last N
    /// </summary>
    Task ClearRuntimeErrorsAsync(int keepLast = 0);

    /// <summary>
    /// Gets a list of mods available for discovery
    /// </summary>
    Task<IEnumerable<RemoteModInfo>> GetAvailableModsAsync();
    /// <summary>
    /// Installs a mod and its dependencies from a remote source
    /// </summary>
    Task<InstallationResult> InstallModWithDependenciesAsync(RemoteModInfo mod);
}

