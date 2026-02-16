namespace ModManager.Core.Runtime;

/// <summary>
/// Interface that mods can implement to support runtime enable/disable functionality
/// </summary>
public interface IModRuntimeController
{
    /// <summary>
    /// Gets whether the mod is currently enabled
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Attempts to enable or disable the mod at runtime
    /// </summary>
    /// <param name="enabled">Whether to enable or disable the mod</param>
    /// <returns>True if the operation succeeded, false if restart is required</returns>
    bool TrySetEnabled(bool enabled);

    /// <summary>
    /// Gets whether the mod supports runtime toggling
    /// </summary>
    bool SupportsRuntimeToggle { get; }

    /// <summary>
    /// Event fired when the mod's enabled state changes
    /// </summary>
    event EventHandler<bool>? EnabledStateChanged;
}
