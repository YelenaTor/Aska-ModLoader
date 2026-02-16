using Microsoft.Win32;
using ModManager.Core.Interfaces;
using Serilog;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Runtime.InteropServices;

namespace ModManager.Core.Services;

/// <summary>
/// Production-grade Steam auto-detection framework for ASKA (AppID 1898300)
/// </summary>
[SupportedOSPlatform("windows")]
public class AskaSteamDetectionService
{
    private const int AskaAppId = 1898300;
    private readonly ILogger _logger;

    public AskaSteamDetectionService(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Detects ASKA installation through Steam
    /// </summary>
    /// <returns>Structured detection result</returns>
    public AskaSteamInstallationResult Detect()
    {
        if (!OperatingSystem.IsWindows())
        {
            _logger.Warning("ASKA Steam detection is only supported on Windows");
            return new AskaSteamInstallationResult 
            { 
                IsDetected = false, 
                FailureReason = "Platform not supported" 
            };
        }

        try
        {
            _logger.Information("Starting ASKA Steam detection for AppID {AppId}", AskaAppId);

            // Step 1: Detect Steam path from registry
            var steamPath = GetSteamPath();
            if (string.IsNullOrEmpty(steamPath))
            {
                _logger.Warning("Steam installation not found in registry");
                return new AskaSteamInstallationResult 
                { 
                    IsDetected = false, 
                    FailureReason = "Steam not installed or registry keys missing" 
                };
            }

            _logger.Information("Steam path resolved: {SteamPath}", steamPath);

            // Step 2: Parse libraryfolders.vdf
            var libraryPaths = GetSteamLibraryPaths(steamPath);
            if (libraryPaths.Count == 0)
            {
                _logger.Warning("No Steam libraries found in libraryfolders.vdf");
                return new AskaSteamInstallationResult 
                { 
                    IsDetected = false, 
                    FailureReason = "Unable to parse Steam library folders" 
                };
            }

            _logger.Information("Found {Count} Steam libraries", libraryPaths.Count);

            // Step 3: Search for ASKA in each library
            foreach (var libraryPath in libraryPaths)
            {
                var installation = FindAskaInLibrary(libraryPath);
                if (installation.IsDetected)
                {
                    _logger.Information("ASKA detected at: {InstallPath}", installation.InstallPath);
                    return installation;
                }
            }

            _logger.Warning("ASKA not found in any Steam library");
            return new AskaSteamInstallationResult 
            { 
                IsDetected = false, 
                FailureReason = "ASKA not installed in any Steam library" 
            };
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Unexpected error during ASKA Steam detection");
            return new AskaSteamInstallationResult 
            { 
                IsDetected = false, 
                FailureReason = $"Detection failed: {ex.Message}" 
            };
        }
    }

    private string? GetSteamPath()
    {
        // Primary: HKCU\Software\Valve\Steam -> SteamPath
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            if (key != null)
            {
                var steamPath = key.GetValue("SteamPath") as string;
                if (!string.IsNullOrEmpty(steamPath))
                {
                    _logger.Information("Found Steam path in HKCU: {SteamPath}", steamPath);
                    return steamPath;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to read Steam path from HKCU registry");
        }

        // Fallback: HKLM\SOFTWARE\WOW6432Node\Valve\Steam -> InstallPath
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam");
            if (key != null)
            {
                var installPath = key.GetValue("InstallPath") as string;
                if (!string.IsNullOrEmpty(installPath))
                {
                    _logger.Information("Found Steam path in HKLM: {SteamPath}", installPath);
                    return installPath;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to read Steam path from HKLM registry");
        }

        return null;
    }

    private List<string> GetSteamLibraryPaths(string steamPath)
    {
        var libraryPaths = new List<string>();
        var libraryFoldersPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");

        if (!File.Exists(libraryFoldersPath))
        {
            _logger.Warning("libraryfolders.vdf not found at: {Path}", libraryFoldersPath);
            return libraryPaths;
        }

        try
        {
            _logger.Information("Parsing libraryfolders.vdf at: {Path}", libraryFoldersPath);
            
            var content = File.ReadAllText(libraryFoldersPath);
            var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                
                // Look for library path entries
                // Format: "path"\t"<library_path>"
                if (trimmed.StartsWith("\"path\""))
                {
                    var pathValue = ExtractQuotedValue(trimmed);
                    if (!string.IsNullOrEmpty(pathValue))
                    {
                        var steamAppsPath = Path.Combine(pathValue, "steamapps");
                        if (Directory.Exists(steamAppsPath))
                        {
                            libraryPaths.Add(pathValue);
                            _logger.Information("Found Steam library: {LibraryPath}", pathValue);
                        }
                        else
                        {
                            _logger.Warning("Steam library path does not exist: {LibraryPath}", steamAppsPath);
                        }
                    }
                }
            }

            // Always include the default Steam installation as a library
            var defaultLibraryPath = Path.Combine(steamPath, "steamapps");
            if (Directory.Exists(defaultLibraryPath) && !libraryPaths.Contains(steamPath))
            {
                libraryPaths.Add(steamPath);
                _logger.Information("Added default Steam library: {LibraryPath}", steamPath);
            }

            // Ensure deterministic processing
            return libraryPaths
                .Select(Path.GetFullPath)
                .Distinct()
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to parse libraryfolders.vdf");
            return new List<string>();
        }
    }

    private AskaSteamInstallationResult FindAskaInLibrary(string libraryPath)
    {
        var manifestPath = Path.Combine(libraryPath, "steamapps", $"appmanifest_{AskaAppId}.acf");
        
        _logger.Information("Checking for ASKA manifest at: {ManifestPath}", manifestPath);

        if (!File.Exists(manifestPath))
        {
            _logger.Debug("ASKA manifest not found in library: {LibraryPath}", libraryPath);
            return new AskaSteamInstallationResult 
            { 
                IsDetected = false, 
                FailureReason = "App manifest not found" 
            };
        }

        try
        {
            var content = File.ReadAllText(manifestPath);
            var installDir = ExtractInstallDir(content);
            
            if (string.IsNullOrEmpty(installDir))
            {
                _logger.Warning("Could not extract installdir from manifest: {ManifestPath}", manifestPath);
                return new AskaSteamInstallationResult 
                { 
                    IsDetected = false, 
                    FailureReason = "Invalid app manifest format" 
                };
            }

            var installPath = Path.Combine(libraryPath, "steamapps", "common", installDir);
            
            _logger.Information("Extracted installdir: {InstallDir}, resolved path: {InstallPath}", installDir, installPath);

            if (!Directory.Exists(installPath))
            {
                _logger.Warning("Install directory does not exist: {InstallPath}", installPath);
                return new AskaSteamInstallationResult 
                { 
                    IsDetected = false, 
                    FailureReason = "Install directory not found" 
                };
            }

            return new AskaSteamInstallationResult
            {
                IsDetected = true,
                InstallPath = installPath,
                SteamLibraryPath = libraryPath
            };
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to parse ASKA manifest: {ManifestPath}", manifestPath);
            return new AskaSteamInstallationResult 
            { 
                IsDetected = false, 
                FailureReason = $"Manifest parse error: {ex.Message}" 
            };
        }
    }

    private string? ExtractQuotedValue(string line)
    {
        // Extract quoted value from a line like: "key"\t"value"
        var firstQuote = line.IndexOf('"');
        if (firstQuote == -1) return null;
        
        var secondQuote = line.IndexOf('"', firstQuote + 1);
        if (secondQuote == -1) return null;
        
        var tabChar = line.IndexOf('\t', secondQuote);
        if (tabChar == -1) return null;
        
        var valueStart = line.IndexOf('"', tabChar);
        if (valueStart == -1) return null;
        
        var valueEnd = line.LastIndexOf('"');
        if (valueEnd <= valueStart) return null;
        
        return line.Substring(valueStart + 1, valueEnd - valueStart - 1);
    }

    private string? ExtractInstallDir(string manifestContent)
    {
        // Simple VDF parser for installdir value
        // Looking for: "installdir"\t"ASKA"
        var lines = manifestContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("\"installdir\""))
            {
                return ExtractQuotedValue(trimmed);
            }
        }
        
        return null;
    }
}

/// <summary>
/// Represents the result of ASKA installation detection
/// </summary>
public sealed class AskaSteamInstallationResult
{
    /// <summary>
    /// Path to the ASKA installation
    /// </summary>
    public string InstallPath { get; init; } = string.Empty;

    /// <summary>
    /// Path to the Steam library containing ASKA
    /// </summary>
    public string SteamLibraryPath { get; init; } = string.Empty;

    /// <summary>
    /// Whether ASKA was detected
    /// </summary>
    public bool IsDetected { get; init; }

    /// <summary>
    /// Reason for detection failure (if any)
    /// </summary>
    public string? FailureReason { get; init; }
}
