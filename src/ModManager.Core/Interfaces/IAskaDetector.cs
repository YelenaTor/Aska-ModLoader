namespace ModManager.Core.Interfaces;

/// <summary>
/// Interface for detecting Aska game installations
/// </summary>
public interface IAskaDetector
{
    /// <summary>
    /// Detects all Aska installations on the system
    /// </summary>
    Task<IEnumerable<AskaInstallation>> DetectInstallationsAsync();

    /// <summary>
    /// Validates if a path contains a valid Aska installation
    /// </summary>
    Task<bool> ValidateInstallationAsync(string path);

    /// <summary>
    /// Gets the BepInEx installation status for an Aska installation
    /// </summary>
    Task<BepInExStatus> GetBepInExStatusAsync(string askaPath);

    /// <summary>
    /// Checks if the Aska game process is currently running
    /// </summary>
    bool IsAskaRunning();
}

/// <summary>
/// Represents an Aska game installation
/// </summary>
public class AskaInstallation
{
    /// <summary>
    /// Path to the Aska installation
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Version of Aska
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Whether this is a Steam installation
    /// </summary>
    public bool IsSteamInstallation { get; set; }

    /// <summary>
    /// Steam App ID if applicable
    /// </summary>
    public string? SteamAppId { get; set; }

    /// <summary>
    /// Path to Aska.exe
    /// </summary>
    public string ExecutablePath { get; set; } = string.Empty;

    /// <summary>
    /// Installation date
    /// </summary>
    public DateTime InstallDate { get; set; }
}

/// <summary>
/// Represents the status of BepInEx installation
/// </summary>
public class BepInExStatus
{
    /// <summary>
    /// Whether BepInEx is installed
    /// </summary>
    public bool IsInstalled { get; set; }

    /// <summary>
    /// BepInEx version if installed
    /// </summary>
    public string? Version { get; set; }

    /// <summary>
    /// Path to BepInEx directory
    /// </summary>
    public string? BepInExPath { get; set; }

    /// <summary>
    /// Path to plugins directory
    /// </summary>
    public string? PluginsPath { get; set; }

    /// <summary>
    /// Path to config directory
    /// </summary>
    public string? ConfigPath { get; set; }

    /// <summary>
    /// Whether this is IL2CPP build (required for Aska)
    /// </summary>
    public bool IsIL2CPPBuild { get; set; }

    /// <summary>
    /// Path to log file
    /// </summary>
    public string? LogPath { get; set; }
}
