using ModManager.Core.Models;

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
    Task InstallFromZipAsync(string zipPath);

    /// <summary>
    /// Installs a mod from a URL
    /// </summary>
    Task InstallFromUrlAsync(Uri packageUrl);

    /// <summary>
    /// Uninstalls a mod
    /// </summary>
    Task UninstallAsync(string modId);

    /// <summary>
    /// Enables or disables a mod
    /// </summary>
    Task SetEnabledAsync(string modId, bool enabled);

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
}

/// <summary>
/// Represents information about an available mod update
/// </summary>
public class ModUpdateInfo
{
    /// <summary>
    /// ID of the mod that has an update
    /// </summary>
    public string ModId { get; set; } = string.Empty;

    /// <summary>
    /// Current installed version
    /// </summary>
    public string CurrentVersion { get; set; } = string.Empty;

    /// <summary>
    /// Latest available version
    /// </summary>
    public string LatestVersion { get; set; } = string.Empty;

    /// <summary>
    /// URL to download the update
    /// </summary>
    public Uri DownloadUrl { get; set; } = new Uri("about:blank");

    /// <summary>
    /// Changelog for the update
    /// </summary>
    public string? Changelog { get; set; }

    /// <summary>
    /// Whether the update is mandatory
    /// </summary>
    public bool IsMandatory { get; set; }
}
