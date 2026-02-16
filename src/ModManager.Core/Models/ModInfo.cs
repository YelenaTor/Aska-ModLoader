namespace ModManager.Core.Models;

/// <summary>
/// Represents information about an installed mod
/// </summary>
public class ModInfo
{
    /// <summary>
    /// Unique identifier for the mod
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable name of the mod
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Version of the installed mod
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Author of the mod
    /// </summary>
    public string Author { get; set; } = string.Empty;

    /// <summary>
    /// Description of the mod
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Path to the mod's installation directory
    /// </summary>
    public string InstallPath { get; set; } = string.Empty;

    /// <summary>
    /// Path to the main DLL file
    /// </summary>
    public string DllPath { get; set; } = string.Empty;

    /// <summary>
    /// Whether the mod is currently enabled
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// Whether the mod supports runtime toggling
    /// </summary>
    public bool SupportsRuntimeToggle { get; set; }

    /// <summary>
    /// List of dependencies
    /// </summary>
    public List<ModDependency> Dependencies { get; set; } = new();

    /// <summary>
    /// Source information
    /// </summary>
    public ModSource Source { get; set; } = new();

    /// <summary>
    /// SHA256 checksum of the main DLL
    /// </summary>
    public string? Checksum { get; set; }

    /// <summary>
    /// Date when the mod was installed
    /// </summary>
    public DateTime InstallDate { get; set; }

    /// <summary>
    /// Date when the mod was last updated
    /// </summary>
    public DateTime LastUpdated { get; set; }

    /// <summary>
    /// BepInEx plugin metadata extracted from assembly
    /// </summary>
    public BepInExMetadata? BepInExMetadata { get; set; }

    /// <summary>
    /// Load order priority (lower numbers load first)
    /// </summary>
    public int LoadOrder { get; set; }
}

/// <summary>
/// BepInEx-specific metadata extracted from plugin assemblies
/// </summary>
public class BepInExMetadata
{
    /// <summary>
    /// GUID of the plugin
    /// </summary>
    public string Guid { get; set; } = string.Empty;

    /// <summary>
    /// Name of the plugin
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Version of the plugin
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// BepInEx dependency version
    /// </summary>
    public string BepInExVersion { get; set; } = string.Empty;

    /// <summary>
    /// Process names this plugin runs on
    /// </summary>
    public List<string> ProcessNames { get; set; } = new();
}
