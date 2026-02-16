using Microsoft.Win32;
using ModManager.Core.Interfaces;
using ModManager.Core.Models;
using Serilog;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;

namespace ModManager.Core.Services;

/// <summary>
/// Service for detecting Aska game installations
/// </summary>
[SupportedOSPlatform("windows")]
public class AskaDetector : IAskaDetector
{
    private const string ASKA_EXE_NAME = "Aska.exe";
    private const string BEPINEX_FOLDER = "BepInEx";
    private const string STEAM_APP_ID = "1234560"; // TODO: Replace with actual Aska Steam App ID

    public async Task<IEnumerable<AskaInstallation>> DetectInstallationsAsync()
    {
        var installations = new List<AskaInstallation>();

        // Check Steam installations
        var steamInstallations = await DetectSteamInstallationsAsync();
        installations.AddRange(steamInstallations);

        // Check common installation paths
        var commonPaths = await CheckCommonPathsAsync();
        installations.AddRange(commonPaths);

        Log.Information("Detected {Count} Aska installations", installations.Count);
        return installations;
    }

    public bool ValidateInstallation(string path)
    {
        try
        {
            var exePath = Path.Combine(path, ASKA_EXE_NAME);
            if (!File.Exists(exePath))
            {
                return false;
            }

            // Additional validation could include checking file version, etc.
            var versionInfo = FileVersionInfo.GetVersionInfo(exePath);
            return !string.IsNullOrEmpty(versionInfo.FileDescription);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to validate Aska installation at {Path}", path);
            return false;
        }
    }

    public BepInExStatus GetBepInExStatus(string askaPath)
    {
        var status = new BepInExStatus();

        try
        {
            var runtimeValidator = new BepInExRuntimeValidator(Log.Logger);
            var runtimeResult = runtimeValidator.Validate(askaPath);

            status.IsInstalled = runtimeResult.Status == BepInExRuntimeStatus.Installed;

            if (!status.IsInstalled)
            {
                return status;
            }

            var bepInExPath = Path.Combine(askaPath, "BepInEx");
            status.BepInExPath = bepInExPath;
            status.PluginsPath = Path.Combine(bepInExPath, "plugins");
            status.ConfigPath = Path.Combine(bepInExPath, "config");

            // Try to determine BepInEx version
            var corePath = Path.Combine(bepInExPath, "core");
            if (Directory.Exists(corePath))
            {
                var coreDll = Path.Combine(corePath, "BepInEx.dll");
                if (File.Exists(coreDll))
                {
                    var versionInfo = FileVersionInfo.GetVersionInfo(coreDll);
                    status.Version = versionInfo.FileVersion ?? "Unknown";
                }
            }

            // Check if it's IL2CPP build (required for ASKA)
            status.IsIL2CPPBuild = CheckIL2CPPBuild(bepInExPath);

            // Log file path
            status.LogPath = Path.Combine(bepInExPath, "LogOutput.log");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to get BepInEx status for {Path}", askaPath);
        }

        return status;
    }

    public bool IsAskaRunning()
    {
        try
        {
            return Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ASKA_EXE_NAME)).Any();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to check if Aska is running");
            return false;
        }
    }

    // Legacy async wrappers to satisfy interface
    public Task<bool> ValidateInstallationAsync(string path)
    {
        return Task.FromResult(ValidateInstallation(path));
    }

    public Task<BepInExStatus> GetBepInExStatusAsync(string askaPath)
    {
        return Task.FromResult(GetBepInExStatus(askaPath));
    }

    private bool CheckIL2CPPBuild(string bepinexPath)
    {
        // Delegate to existing async implementation to preserve behavior
        return CheckIL2CPPBuildAsync(bepinexPath).GetAwaiter().GetResult();
    }

    private Task<string> GetAskaVersionAsync(string askaPath)
    {
        var result = GetAskaVersion(askaPath);
        return Task.FromResult(result);
    }

    private async Task<List<AskaInstallation>> DetectSteamInstallationsAsync()
    {
        var installations = new List<AskaInstallation>();

        try
        {
            // Get Steam installation path from registry
            var steamPath = GetSteamPathFromRegistry();
            if (string.IsNullOrEmpty(steamPath))
            {
                return installations;
            }

            // Read library folders from Steam
            var libraryFolders = await GetSteamLibraryFoldersAsync(steamPath);
            
            foreach (var libraryFolder in libraryFolders)
            {
                var askaPath = Path.Combine(libraryFolder, "steamapps", "common", "Aska");
                if (await ValidateInstallationAsync(askaPath))
                {
                    installations.Add(new AskaInstallation
                    {
                        Path = askaPath,
                        Version = await GetAskaVersionAsync(askaPath),
                        IsSteamInstallation = true,
                        SteamAppId = STEAM_APP_ID,
                        ExecutablePath = Path.Combine(askaPath, ASKA_EXE_NAME),
                        InstallDate = Directory.GetCreationTime(askaPath)
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to detect Steam installations");
        }

        return installations;
    }

    private async Task<List<AskaInstallation>> CheckCommonPathsAsync()
    {
        var installations = new List<AskaInstallation>();
        var commonPaths = new[]
        {
            @"C:\Games\Aska",
            @"C:\Program Files (x86)\Aska",
            @"C:\Program Files\Aska"
        };

        foreach (var path in commonPaths)
        {
            if (await ValidateInstallationAsync(path))
            {
                installations.Add(new AskaInstallation
                {
                    Path = path,
                    Version = await GetAskaVersionAsync(path),
                    IsSteamInstallation = false,
                    ExecutablePath = Path.Combine(path, ASKA_EXE_NAME),
                    InstallDate = Directory.GetCreationTime(path)
                });
            }
        }

        return installations;
    }

    private string? GetSteamPathFromRegistry()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Valve\Steam");
            return key?.GetValue("InstallPath") as string;
        }
        catch
        {
            return null;
        }
    }

    private async Task<List<string>> GetSteamLibraryFoldersAsync(string steamPath)
    {
        var folders = new List<string> { steamPath };
        
        try
        {
            var libraryConfigPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
            if (File.Exists(libraryConfigPath))
            {
                var content = await File.ReadAllTextAsync(libraryConfigPath);
                // Simple parsing - in production, use a proper VDF parser
                var lines = content.Split('\n');
                foreach (var line in lines)
                {
                    if (line.Contains("\"path\""))
                    {
                        var path = line.Split('"')[3];
                        if (!string.IsNullOrEmpty(path) && !folders.Contains(path))
                        {
                            folders.Add(path);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to parse Steam library folders");
        }

        return folders;
    }

    private string GetAskaVersion(string askaPath)
    {
        try
        {
            var exePath = Path.Combine(askaPath, ASKA_EXE_NAME);
            var versionInfo = FileVersionInfo.GetVersionInfo(exePath);
            return versionInfo.FileVersion ?? "Unknown";
        }
        catch
        {
            return "Unknown";
        }
    }

    private Task<bool> CheckIL2CPPBuildAsync(string bepinexPath)
    {
        try
        {
            // Check for IL2CPP-specific files
            var il2cppFiles = new[]
            {
                Path.Combine(bepinexPath, "BepInEx.IL2CPP.dll"),
                Path.Combine(bepinexPath, "BepInEx.Preloader.dll")
            };

            return Task.FromResult(il2cppFiles.Any(File.Exists));
        }
        catch
        {
            return Task.FromResult(false);
        }
    }
}
