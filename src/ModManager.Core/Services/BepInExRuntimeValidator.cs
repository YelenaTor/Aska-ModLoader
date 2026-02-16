using ModManager.Core.Interfaces;
using ModManager.Core.Models;
using Serilog;
using System.IO;

namespace ModManager.Core.Services;

public class BepInExRuntimeValidator : IBepInExRuntimeValidator
{
    private readonly ILogger _logger;

    public BepInExRuntimeValidator(ILogger logger)
    {
        _logger = logger;
    }

    public BepInExRuntimeResult Validate(string gamePath)
    {
        if (string.IsNullOrWhiteSpace(gamePath))
        {
            return new BepInExRuntimeResult
            {
                Status = BepInExRuntimeStatus.NotInstalled,
                GamePath = gamePath ?? string.Empty,
                FailureReason = "Game path is not provided"
            };
        }

        _logger.Information("Starting BepInEx runtime validation for {GamePath}", gamePath);

        try
        {
            var exePath = Path.Combine(gamePath, "Aska.exe");
            var gameExecutableExists = File.Exists(exePath);

            var bepinexRoot = Path.Combine(gamePath, "BepInEx");
            var bepinexCoreDll = Path.Combine(bepinexRoot, "core", "BepInEx.dll");
            var pluginsPath = Path.Combine(bepinexRoot, "plugins");
            var loaderWinHttp = Path.Combine(gamePath, "winhttp.dll");
            var loaderDoorstop = Path.Combine(gamePath, "doorstop_config.ini");

            var coreDllExists = File.Exists(bepinexCoreDll);
            var pluginsFolderExists = Directory.Exists(pluginsPath);
            var loaderExists = File.Exists(loaderWinHttp) || File.Exists(loaderDoorstop);

            // Create plugins directory if BepInEx exists but plugins is missing
            if (Directory.Exists(bepinexRoot) && !pluginsFolderExists)
            {
                Directory.CreateDirectory(pluginsPath);
                pluginsFolderExists = true;
            }

            var hasBepInExRoot = Directory.Exists(bepinexRoot);

            BepInExRuntimeStatus status;
            string? failureReason = null;

            if (!hasBepInExRoot)
            {
                status = BepInExRuntimeStatus.NotInstalled;
                failureReason = "BepInEx folder not found";
            }
            else if (!coreDllExists)
            {
                status = BepInExRuntimeStatus.Corrupt;
                failureReason = "Missing BepInEx core DLL";
            }
            else if (!loaderExists)
            {
                status = BepInExRuntimeStatus.Corrupt;
                failureReason = "Missing loader (winhttp.dll or doorstop_config.ini)";
            }
            else if (!gameExecutableExists)
            {
                status = BepInExRuntimeStatus.Corrupt;
                failureReason = "Game executable not found";
            }
            else
            {
                status = BepInExRuntimeStatus.Installed;
            }

            var result = new BepInExRuntimeResult
            {
                Status = status,
                GamePath = gamePath,
                GameExecutableExists = gameExecutableExists,
                CoreDllExists = coreDllExists,
                PluginsFolderExists = pluginsFolderExists,
                LoaderExists = loaderExists,
                FailureReason = failureReason
            };

            _logger.Information("BepInEx runtime validation completed: {Status}", result.Status);
            return result;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "BepInEx runtime validation failed for {GamePath}", gamePath);
            return new BepInExRuntimeResult
            {
                Status = BepInExRuntimeStatus.Corrupt,
                GamePath = gamePath,
                FailureReason = ex.Message
            };
        }
    }
}
