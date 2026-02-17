using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Serilog;

namespace ModManager.Core.Services;

public interface IGameLauncherService
{
    Task<bool> LaunchGameAsync(string gamePath);
}

public class GameLauncherService : IGameLauncherService
{
    private readonly ILogger _logger;
    private const string AskaSteamAppId = "1898300"; // ASKA AppID on Steam

    public GameLauncherService(ILogger logger)
    {
        _logger = logger;
    }

    public async Task<bool> LaunchGameAsync(string gamePath)
    {
        _logger.Information("Attempting to launch game...");

        // Strategy 1: Launch via Steam URL protocol (preferred for Steamworks integration)
        try
        {
            _logger.Information("Trying Steam launch protocol...");
            Process.Start(new ProcessStartInfo
            {
                FileName = $"steam://run/{AskaSteamAppId}",
                UseShellExecute = true
            });
            
            // Give it a moment to see if Steam reacts (hard to detect success purely, but if no exception...)
            _logger.Information("Steam launch command issued.");
            return true;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Steam launch failed, falling back to executable.");
        }

        // Strategy 2: Launch via executable directly
        var exePath = Path.Combine(gamePath, "Aska.exe"); // Verify actual exe name
        if (!File.Exists(exePath))
        {
             _logger.Error("Game executable not found at: {Path}", exePath);
             return false;
        }

        try
        {
            _logger.Information("Launching executable directly: {Path}", exePath);
            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                WorkingDirectory = gamePath,
                UseShellExecute = false
            });
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to launch game executable");
            return false;
        }
    }
}
