using ModManager.Core.Interfaces;
using ModManager.Core.Models;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ModManager.Core.Services;

/// <summary>
/// Tails the BepInEx log to report the most recent runtime error and attribute it to a mod.
/// </summary>
public class CrashDiagnosticsService : IDisposable
{
    private readonly ILogger _logger;
    private readonly IModRepository _modRepository;
    private readonly string _logPath;
    private readonly FileSystemWatcher _watcher;
    private readonly object _sync = new();

    public string? LastRuntimeError { get; private set; }

    public event EventHandler<string?>? LogUpdated;

    public CrashDiagnosticsService(ILogger logger, IModRepository modRepository, string askaPath)
    {
        _logger = logger;
        _modRepository = modRepository;
        _logPath = Path.Combine(askaPath, "BepInEx", "LogOutput.log");
        var logDirectory = Path.GetDirectoryName(_logPath) ?? askaPath;
        Directory.CreateDirectory(logDirectory);

        _watcher = new FileSystemWatcher(logDirectory, "LogOutput.log")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName
        };
        _watcher.Changed += OnLogChanged;
        _watcher.Created += OnLogChanged;
        _watcher.Renamed += OnLogRenamed;
        _watcher.EnableRaisingEvents = true;

        ParseLogFile();
    }

    private void OnLogChanged(object? sender, FileSystemEventArgs e)
    {
        ParseLogFile();
    }

    private void OnLogRenamed(object? sender, RenamedEventArgs e)
    {
        ParseLogFile();
    }

    private void ParseLogFile()
    {
        lock (_sync)
        {
            try
            {
                if (!File.Exists(_logPath))
                {
                    return;
                }

                var lines = File.ReadAllLines(_logPath);
                for (var index = lines.Length - 1; index >= 0; index--)
                {
                    var line = lines[index];
                    if (IsErrorLine(line))
                    {
                        UpdateLastError(line);
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to tail crash log");
            }
        }
    }

    private bool IsErrorLine(string line)
    {
        var patterns = new[] { "Exception", "TypeLoadException", "MissingMethodException", "Failed to load" };
        return patterns.Any(pattern => line.Contains(pattern, StringComparison.OrdinalIgnoreCase));
    }

    private void UpdateLastError(string errorLine)
    {
        var mod = ResolveModFromAssembly(ExtractAssemblyName(errorLine));
        
        var error = new RuntimeError
        {
            Timestamp = DateTime.UtcNow,
            Message = errorLine,
            FullLogLine = errorLine,
            ModId = mod?.Id,
            ModName = mod?.Name
        };

        _modRepository.LogRuntimeErrorAsync(error).GetAwaiter().GetResult();

        var newMessage = mod != null
            ? $"Last runtime error attributed to: {mod.Name}"
            : $"Last runtime error: {errorLine}";

        if (!string.Equals(LastRuntimeError, newMessage, StringComparison.Ordinal))
        {
            LastRuntimeError = newMessage;
            _logger.Information("{LastError}", LastRuntimeError);
            LogUpdated?.Invoke(this, LastRuntimeError);
        }
    }

    private string? ExtractAssemblyName(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        var dllMarker = ".dll";
        var dllIndex = line.IndexOf(dllMarker, StringComparison.OrdinalIgnoreCase);
        if (dllIndex < 0)
        {
            return null;
        }

        var start = line.LastIndexOfAny(new[] { ' ', '\t', '[', '(' }, dllIndex);
        if (start < 0)
        {
            start = 0;
        }

        var assemblyFragment = line[start..(dllIndex + dllMarker.Length)];
        var assemblyName = assemblyFragment.Split(new[] { ' ', '\t', ',', ';' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        return assemblyName != null ? Path.GetFileNameWithoutExtension(assemblyName) : null;
    }

    private ModInfo? ResolveModFromAssembly(string? assemblyName)
    {
        if (string.IsNullOrWhiteSpace(assemblyName))
        {
            return null;
        }

        var mods = _modRepository.ListInstalledAsync().GetAwaiter().GetResult();
        foreach (var mod in mods)
        {
            if (string.IsNullOrWhiteSpace(mod.DllPath))
            {
                continue;
            }

            if (string.Equals(Path.GetFileNameWithoutExtension(mod.DllPath), assemblyName, StringComparison.OrdinalIgnoreCase))
            {
                return mod;
            }
        }

        return null;
    }

    public void Dispose()
    {
        _watcher.Changed -= OnLogChanged;
        _watcher.Created -= OnLogChanged;
        _watcher.Renamed -= OnLogRenamed;
        _watcher.Dispose();
    }
}
