using System;

namespace ModManager.Core.Models;

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

/// <summary>
/// Information about a mod available on a remote repository
/// </summary>
public class RemoteModInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public System.Uri DownloadUrl { get; set; } = new System.Uri("about:blank");
    public System.DateTime LastUpdated { get; set; }
    public System.Collections.Generic.List<string> Dependencies { get; set; } = new();
}
