namespace ModManager.Core.Interfaces;

/// <summary>
/// Service for resolving and managing game installation paths
/// </summary>
public interface IGamePathService
{
    /// <summary>
    /// Gets the user-configured game path
    /// </summary>
    string? GetConfiguredPath();

    /// <summary>
    /// Detects game installation automatically
    /// </summary>
    string? DetectGamePath();

    /// <summary>
    /// Validates if a path contains a valid Aska installation
    /// </summary>
    bool ValidateGamePath(string path);

    /// <summary>
    /// Saves the user-configured path
    /// </summary>
    void SaveConfiguredPath(string path);

    /// <summary>
    /// Resolves the game path using the proper order: Config → Detect → null
    /// </summary>
    string? ResolveGamePath();
}
