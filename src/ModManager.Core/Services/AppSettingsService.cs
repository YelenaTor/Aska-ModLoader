using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Serilog;

namespace ModManager.Core.Services;

public class AppSettings
{
    public bool CloseOnLaunch { get; set; } = false;
    public string? GamePath { get; set; }
}

public interface IAppSettingsService
{
    AppSettings Settings { get; }
    Task LoadSettingsAsync();
    Task SaveSettingsAsync();
}

public class AppSettingsService : IAppSettingsService
{
    private readonly ILogger _logger;
    private readonly string _settingsPath;
    
    public AppSettings Settings { get; private set; } = new();

    public AppSettingsService(ILogger logger)
    {
        _logger = logger;
        // Persist settings in AppData to avoid permission issues in program files
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var appFolder = Path.Combine(appData, "AskaModManager");
        Directory.CreateDirectory(appFolder);
        _settingsPath = Path.Combine(appFolder, "settings.json");
    }

    public async Task LoadSettingsAsync()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = await File.ReadAllTextAsync(_settingsPath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                if (loaded != null)
                {
                    Settings = loaded;
                    _logger.Information("Loaded application settings");
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load settings from {Path}", _settingsPath);
        }
        
        // Use defaults if load fails
        if (Settings == null) Settings = new AppSettings();
    }

    public async Task SaveSettingsAsync()
    {
        try
        {
            var json = JsonSerializer.Serialize(Settings, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_settingsPath, json);
            _logger.Information("Saved application settings");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save settings to {Path}", _settingsPath);
        }
    }
}
