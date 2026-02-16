using System.Text.Json.Serialization;

namespace ModManager.Core.Models;

/// <summary>
/// Represents the manifest metadata for a mod package
/// </summary>
public class ModManifest
{
    /// <summary>
    /// Unique identifier for the mod (reverse domain notation)
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable name of the mod
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Semantic version of the mod
    /// </summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Author of the mod
    /// </summary>
    [JsonPropertyName("author")]
    public string Author { get; set; } = string.Empty;

    /// <summary>
    /// Short description of the mod
    /// </summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Main entry point DLL file
    /// </summary>
    [JsonPropertyName("entry")]
    public string Entry { get; set; } = string.Empty;

    /// <summary>
    /// List of all files included in the mod package
    /// </summary>
    [JsonPropertyName("files")]
    public List<string> Files { get; set; } = new();

    /// <summary>
    /// Dependencies required by this mod
    /// </summary>
    [JsonPropertyName("dependencies")]
    public List<ModDependency> Dependencies { get; set; } = new();

    /// <summary>
    /// Minimum compatible BepInEx version
    /// </summary>
    [JsonPropertyName("compatible_bepinex")]
    public string CompatibleBepInEx { get; set; } = ">=5.4.0";

    /// <summary>
    /// Source information for the mod
    /// </summary>
    [JsonPropertyName("source")]
    public ModSource Source { get; set; } = new();

    /// <summary>
    /// SHA256 checksum for integrity verification
    /// </summary>
    [JsonPropertyName("checksum")]
    public string? Checksum { get; set; }

    /// <summary>
    /// Tags/categories for the mod
    /// </summary>
    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();

    /// <summary>
    /// Website URL for more information
    /// </summary>
    [JsonPropertyName("website_url")]
    public string? WebsiteUrl { get; set; }
}

/// <summary>
/// Represents a dependency requirement for a mod
/// </summary>
public class ModDependency
{
    /// <summary>
    /// Unique identifier of the dependency mod
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Minimum version required
    /// </summary>
    [JsonPropertyName("minVersion")]
    public string MinVersion { get; set; } = string.Empty;

    /// <summary>
    /// Whether this dependency is optional
    /// </summary>
    [JsonPropertyName("optional")]
    public bool Optional { get; set; }
}

/// <summary>
/// Represents the source information for a mod
/// </summary>
public class ModSource
{
    /// <summary>
    /// Type of source (thunderstore, github, local)
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// URL to the source
    /// </summary>
    [JsonPropertyName("url")]
    public string? Url { get; set; }
}
