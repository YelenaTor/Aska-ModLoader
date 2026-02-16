using Serilog;
using System.Runtime.InteropServices;

namespace ModManager.Core.Services;

/// <summary>
/// Production-grade BepInEx detection service for ASKA
/// </summary>
public class BepInExDetectionService
{
    private readonly ILogger _logger;

    public BepInExDetectionService(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Detects BepInEx installation status for the given game path
    /// </summary>
    /// <param name="gamePath">Path to the ASKA installation</param>
    /// <returns>Structured detection result</returns>
    public BepInExDetectionResult Detect(string gamePath)
    {
        if (!OperatingSystem.IsWindows())
        {
            _logger.Warning("BepInX detection is only supported on Windows");
            return new BepInExDetectionResult
            {
                Status = BepInXInstallationStatus.NotInstalled,
                GamePath = gamePath,
                FailureReason = "Platform not supported"
            };
        }

        if (string.IsNullOrEmpty(gamePath))
        {
            _logger.Warning("Game path is null or empty");
            return new BepInExDetectionResult
            {
                Status = BepInXInstallationStatus.NotInstalled,
                GamePath = gamePath,
                FailureReason = "Invalid game path"
            };
        }

        try
        {
            _logger.Information("Detecting BepInX installation at: {GamePath}", gamePath);

            var requiredFiles = new[]
            {
                Path.Combine(gamePath, "BepInEx"),
                Path.Combine(gamePath, "BepInEx", "plugins"),
                Path.Combine(gamePath, "winhttp.dll"),
                Path.Combine(gamePath, "doorstop_config.ini")
            };

            var missingFiles = new List<string>();
            var existingFiles = new List<string>();

            foreach (var file in requiredFiles)
            {
                var exists = Directory.Exists(file) || File.Exists(file);
                if (exists)
                {
                    existingFiles.Add(file);
                    _logger.Debug("Required path exists: {Path}", file);
                }
                else
                {
                    missingFiles.Add(file);
                    _logger.Debug("Required path missing: {Path}", file);
                }
            }

            // Determine status based on what's present
            if (missingFiles.Count == 0)
            {
                _logger.Information("BepInX is fully installed at: {GamePath}", gamePath);
                return new BepInExDetectionResult
                {
                    Status = BepInXInstallationStatus.Installed,
                    GamePath = gamePath,
                    ExistingFiles = existingFiles,
                    MissingFiles = missingFiles
                };
            }

            // Check if it's a corrupted installation (some files present but not all)
            var bepInExFolder = Path.Combine(gamePath, "BepInEx");
            if (Directory.Exists(bepInExFolder))
            {
                _logger.Warning("BepInX installation is corrupted at: {GamePath}. Missing: {MissingFiles}", 
                    gamePath, string.Join(", ", missingFiles));
                return new BepInExDetectionResult
                {
                    Status = BepInXInstallationStatus.Corrupted,
                    GamePath = gamePath,
                    ExistingFiles = existingFiles,
                    MissingFiles = missingFiles,
                    FailureReason = "Partial installation detected"
                };
            }

            _logger.Information("BepInX is not installed at: {GamePath}", gamePath);
            return new BepInExDetectionResult
            {
                Status = BepInXInstallationStatus.NotInstalled,
                GamePath = gamePath,
                ExistingFiles = existingFiles,
                MissingFiles = missingFiles,
                FailureReason = "BepInX not installed"
            };
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to detect BepInX installation at: {GamePath}", gamePath);
            return new BepInExDetectionResult
            {
                Status = BepInXInstallationStatus.NotInstalled,
                GamePath = gamePath,
                FailureReason = $"Detection failed: {ex.Message}"
            };
        }
    }
}

/// <summary>
/// BepInX installation status
/// </summary>
public enum BepInXInstallationStatus
{
    /// <summary>
    /// BepInX is fully installed and functional
    /// </summary>
    Installed,

    /// <summary>
    /// BepInX is not installed
    /// </summary>
    NotInstalled,

    /// <summary>
    /// BepInX installation is corrupted or incomplete
    /// </summary>
    Corrupted
}

/// <summary>
/// Result of BepInX detection
/// </summary>
public class BepInExDetectionResult
{
    /// <summary>
    /// Installation status
    /// </summary>
    public BepInXInstallationStatus Status { get; init; }

    /// <summary>
    /// Game path that was checked
    /// </summary>
    public string GamePath { get; init; } = string.Empty;

    /// <summary>
    /// Files/folders that were found
    /// </summary>
    public List<string> ExistingFiles { get; init; } = new();

    /// <summary>
    /// Files/folders that were missing
    /// </summary>
    public List<string> MissingFiles { get; init; } = new();

    /// <summary>
    /// Reason for failure (if any)
    /// </summary>
    public string? FailureReason { get; init; }
}
