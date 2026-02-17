using System.Text.Json;
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
    [JsonConverter(typeof(FlexibleModDependencyListConverter))]
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

    /// <summary>
    /// List of mod IDs that are incompatible with this mod
    /// </summary>
    [JsonPropertyName("incompatible_with")]
    public List<ModIncompatibility> IncompatibleWith { get; set; } = new();

    /// <summary>
    /// Mod IDs that this mod should load after (soft ordering hints)
    /// </summary>
    [JsonPropertyName("load_after")]
    public List<string> LoadAfter { get; set; } = new();

    /// <summary>
    /// Mod IDs that this mod should load before (soft ordering hints)
    /// </summary>
    [JsonPropertyName("load_before")]
    public List<string> LoadBefore { get; set; } = new();
}

/// <summary>
/// Represents an incompatibility declaration between mods
/// </summary>
public class ModIncompatibility
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }
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

/// <summary>
/// Handles both string and object formats for dependency arrays.
/// Strings like "com.mod.id" are converted to ModDependency { Id = "com.mod.id" }.
/// </summary>
public class FlexibleModDependencyListConverter : JsonConverter<List<ModDependency>>
{
    public override List<ModDependency> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var list = new List<ModDependency>();
        if (reader.TokenType != JsonTokenType.StartArray)
            return list;

        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                list.Add(new ModDependency { Id = reader.GetString() ?? string.Empty });
            }
            else if (reader.TokenType == JsonTokenType.StartObject)
            {
                var dep = JsonSerializer.Deserialize<ModDependency>(ref reader, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                if (dep != null) list.Add(dep);
            }
        }
        return list;
    }

    public override void Write(Utf8JsonWriter writer, List<ModDependency> value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value, options);
    }
}
