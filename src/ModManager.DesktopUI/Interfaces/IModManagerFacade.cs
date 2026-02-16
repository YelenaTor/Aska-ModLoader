using ModManager.DesktopUI.Models;

namespace ModManager.DesktopUI.Interfaces;

/// <summary>
/// Facade interface for mod management operations
/// </summary>
public interface IModManagerFacade
{
    /// <summary>
    /// Gets all installed mods
    /// </summary>
    IEnumerable<ModDisplayModel> GetInstalledMods();

    /// <summary>
    /// Installs a mod from a ZIP file
    /// </summary>
    FacadeOperationResult InstallFromZip(string zipPath);

    /// <summary>
    /// Enables a mod by its ID
    /// </summary>
    FacadeOperationResult EnableMod(string modId);

    /// <summary>
    /// Disables a mod by its ID
    /// </summary>
    FacadeOperationResult DisableMod(string modId);

    /// <summary>
    /// Uninstalls a mod by its ID
    /// </summary>
    FacadeOperationResult UninstallMod(string modId);

    /// <summary>
    /// Refreshes the mod list
    /// </summary>
    FacadeOperationResult RefreshMods();

    /// <summary>
    /// Gets the current status message
    /// </summary>
    string GetStatusMessage();

    /// <summary>
    /// Validates if a mod can be enabled (dependency check)
    /// </summary>
    FacadeOperationResult ValidateModEnable(string modId);

    /// <summary>
    /// Validates if a ZIP can be installed (dependency check)
    /// </summary>
    FacadeOperationResult ValidateZipInstall(string zipPath);

    /// <summary>
    /// Installs BepInEx into the configured game path
    /// </summary>
    FacadeOperationResult InstallBepInEx(string gamePath);

    /// <summary>
    /// Sets the status message (for error handling)
    /// </summary>
    void SetStatusMessage(string message);
}
