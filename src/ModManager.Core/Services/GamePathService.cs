using ModManager.Core.Interfaces;
using Serilog;
using System.IO;
using System.Runtime.Versioning;
using System.Text.Json;

namespace ModManager.Core.Services;

/// <summary>
/// Service for resolving and managing game installation paths
/// </summary>
public class GamePathService : IGamePathService
{
    private readonly ILogger _logger;
    private readonly AskaSteamDetectionService _askaSteamDetectionService;
    private readonly string _settingsPath;

    private const string SETTINGS_FILENAME = "settings.json";
    private const string CONFIGURED_PATH_KEY = "GamePath";

    public GamePathService(ILogger logger, AskaSteamDetectionService askaSteamDetectionService)
    {
        _logger = logger;
        _askaSteamDetectionService = askaSteamDetectionService;
        _settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AskaModManager",
            SETTINGS_FILENAME);
    }

    public string? GetConfiguredPath()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                _logger.Debug("Settings file not found at {Path}", _settingsPath);
                return null;
            }

            var json = File.ReadAllText(_settingsPath);
            var settings = JsonSerializer.Deserialize<Dictionary<string, string>>(json);

            if (settings?.TryGetValue(CONFIGURED_PATH_KEY, out var configuredPath) == true)
            {
                _logger.Debug("Loaded configured path: {Path}", configuredPath);
                return configuredPath;
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to load configured path from {Path}", _settingsPath);
            return null;
        }
    }

    [SupportedOSPlatform("windows")]
    public string? DetectGamePath()
    {
        try
        {
            _logger.Information("Attempting to detect ASKA installation via Steam...");
            
            var installation = _askaSteamDetectionService.Detect();
            
            if (!installation.IsDetected)
            {
                _logger.Warning("ASKA installation not found: {Reason}", installation.FailureReason);
                return null;
            }

            var detectedPath = installation.InstallPath;
            _logger.Information("ASKA installation detected at: {Path}", detectedPath);
            return detectedPath;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to detect game path");
            return null;
        }
    }

    public bool ValidateGamePath(string path)
    {
        try
        {
            if (string.IsNullOrEmpty(path))
            {
                _logger.Warning("Game path is null or empty");
                return false;
            }

            if (!Directory.Exists(path))
            {
                _logger.Warning("Game directory does not exist: {Path}", path);
                return false;
            }

            // Check for Aska.exe or BepInEx folder
            var exePath = Path.Combine(path, "Aska.exe");
            var bepinexPath = Path.Combine(path, "BepInEx");
            
            var hasExe = File.Exists(exePath);
            var hasBepInEx = Directory.Exists(bepinexPath);

            if (!hasExe && !hasBepInEx)
            {
                _logger.Warning("Path does not contain Aska.exe or BepInEx folder: {Path}", path);
                return false;
            }

            // Check for BepInEx/plugins folder
            var pluginsPath = Path.Combine(path, "BepInEx", "plugins");
            if (!Directory.Exists(pluginsPath))
            {
                _logger.Information("Creating BepInEx/plugins directory at {Path}", pluginsPath);
                Directory.CreateDirectory(pluginsPath);
            }

            _logger.Information("Game path validation successful: {Path}", path);
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to validate game path: {Path}", path);
            return false;
        }
    }

    public void SaveConfiguredPath(string path)
    {
        try
        {
            var directory = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            Dictionary<string, string> settings;
            
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                settings = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
            }
            else
            {
                settings = new Dictionary<string, string>();
            }

            settings[CONFIGURED_PATH_KEY] = path;

            var jsonToWrite = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsPath, jsonToWrite);

            _logger.Information("Saved configured path: {Path}", path);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save configured path to {Path}", _settingsPath);
            throw;
        }
    }

    [SupportedOSPlatform("windows")]
    public string? ResolveGamePath()
    {
        try
        {
            // 1. Try user-configured path
            var configuredPath = GetConfiguredPath();
            if (!string.IsNullOrEmpty(configuredPath))
            {
                _logger.Information("Using configured path: {Path}", configuredPath);
                
                if (ValidateGamePath(configuredPath))
                {
                    _logger.Information("Configured path is valid: {Path}", configuredPath);
                    return configuredPath;
                }
                
                _logger.Warning("Configured path is invalid, falling back to detection");
            }

            // 2. Try auto-detection
            var detectedPath = DetectGamePath();
            if (!string.IsNullOrEmpty(detectedPath))
            {
                _logger.Information("Using detected path: {Path}", detectedPath);
                return detectedPath;
            }

            // 3. No valid path found
            _logger.Warning("No valid game path could be resolved");
            return null;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to resolve game path");
            return null;
        }
    }
}
