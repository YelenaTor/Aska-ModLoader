using Serilog;
using System;
using System.IO;
using System.Text.Json;

namespace ModManager.Core.Services;

/// <summary>
/// Tracks game launches and detects repeated crashes.
/// If the game crashes 3 times consecutively, recommends disabling the last-enabled mod.
/// Persists state to a JSON file in the BepInEx directory.
/// </summary>
public class CrashRollbackService
{
    private readonly ILogger _logger;
    private readonly string _stateFilePath;
    private CrashRollbackState _state;

    private const int CrashThreshold = 3;

    public CrashRollbackService(ILogger logger, string askaPath)
    {
        _logger = logger;
        _stateFilePath = Path.Combine(askaPath, "BepInEx", "crash_rollback.json");
        _state = LoadState();
    }

    /// <summary>
    /// Records that the game has been launched. Call when game process is first detected.
    /// </summary>
    public void RecordGameLaunch()
    {
        _state.LastLaunchTime = DateTime.UtcNow;
        _state.GameRunning = true;
        SaveState();
        _logger.Information("Game launch recorded at {Time}", _state.LastLaunchTime);
    }

    /// <summary>
    /// Records that the game has exited. Call when game process is no longer detected.
    /// A session shorter than 60 seconds is considered a crash.
    /// </summary>
    public void RecordGameExit()
    {
        if (!_state.GameRunning) return;

        _state.GameRunning = false;
        var sessionDuration = DateTime.UtcNow - _state.LastLaunchTime;

        if (sessionDuration.TotalSeconds < 60)
        {
            // Short session = likely crash
            _state.ConsecutiveCrashes++;
            _logger.Warning("Game session lasted {Seconds:F1}s — counted as crash #{Count}",
                sessionDuration.TotalSeconds, _state.ConsecutiveCrashes);
        }
        else
        {
            // Successful session — reset counter
            _state.ConsecutiveCrashes = 0;
            _logger.Information("Game session lasted {Minutes:F1}m — healthy exit, crash counter reset",
                sessionDuration.TotalMinutes);
        }

        SaveState();
    }

    /// <summary>
    /// Records which mod was last enabled by the user, for rollback attribution.
    /// </summary>
    public void RecordModEnabled(string modId, string modName)
    {
        _state.LastEnabledModId = modId;
        _state.LastEnabledModName = modName;
        _state.LastEnabledTime = DateTime.UtcNow;
        SaveState();
        _logger.Information("Recorded last enabled mod: {ModName} ({ModId})", modName, modId);
    }

    /// <summary>
    /// Returns true if the crash threshold has been reached and a rollback should be suggested.
    /// </summary>
    public bool ShouldRollback()
    {
        return _state.ConsecutiveCrashes >= CrashThreshold
               && !string.IsNullOrEmpty(_state.LastEnabledModId);
    }

    /// <summary>
    /// Gets the mod ID of the last enabled mod (the rollback candidate).
    /// </summary>
    public string? GetRollbackModId() => _state.LastEnabledModId;

    /// <summary>
    /// Gets a human-readable description of the rollback recommendation.
    /// </summary>
    public string GetRollbackMessage()
    {
        return $"ASKA has crashed {_state.ConsecutiveCrashes} times consecutively. " +
               $"The last mod you enabled was \"{_state.LastEnabledModName}\". " +
               $"Would you like to disable it to stabilize the game?";
    }

    /// <summary>
    /// Resets the crash counter after a rollback is performed or dismissed.
    /// </summary>
    public void ResetCrashCounter()
    {
        _state.ConsecutiveCrashes = 0;
        SaveState();
        _logger.Information("Crash counter reset");
    }

    private CrashRollbackState LoadState()
    {
        try
        {
            if (File.Exists(_stateFilePath))
            {
                var json = File.ReadAllText(_stateFilePath);
                return JsonSerializer.Deserialize<CrashRollbackState>(json) ?? new CrashRollbackState();
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to load crash rollback state");
        }
        return new CrashRollbackState();
    }

    private void SaveState()
    {
        try
        {
            var directory = Path.GetDirectoryName(_stateFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            var json = JsonSerializer.Serialize(_state, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_stateFilePath, json);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to save crash rollback state");
        }
    }
}

/// <summary>
/// Persistent state for crash rollback tracking.
/// </summary>
public class CrashRollbackState
{
    public DateTime LastLaunchTime { get; set; }
    public bool GameRunning { get; set; }
    public int ConsecutiveCrashes { get; set; }
    public string? LastEnabledModId { get; set; }
    public string? LastEnabledModName { get; set; }
    public DateTime? LastEnabledTime { get; set; }
}
