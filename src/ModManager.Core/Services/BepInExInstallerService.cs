using System.Security.Cryptography;
using System.IO.Compression;
using Serilog;
using ModManager.Core.Models;
using ModManager.Core.Interfaces;
using System.Net.Http;

namespace ModManager.Core.Services;

/// <summary>
/// Production-grade BepInEx installer service for ASKA
/// </summary>
public class BepInExInstallerService
{
    private readonly ILogger _logger;
    private readonly HttpClient _httpClient;
    private readonly IBepInExRuntimeValidator _runtimeValidator;

    // Pinned BepInEx release - DO NOT CHANGE without verification
    private const string BepInExVersion = "5.4.22";
    private const string BepInExDownloadUrl = "https://github.com/BepInEx/BepInEx/releases/download/v5.4.22/BepInEx_x64_5.4.22.0.zip";
    private const string BepInExSha256Hash = "8B2D794116E5F0D5D00F9BA7DDFB66914318E403D315FABBDE9B6A288EEA19BC";

    public BepInExInstallerService(ILogger logger, HttpClient httpClient, IBepInExRuntimeValidator runtimeValidator)
    {
        _logger = logger;
        _httpClient = httpClient;
        _runtimeValidator = runtimeValidator;
    }

    /// <summary>
    /// Installs BepInEx to the specified game path
    /// </summary>
    /// <param name="gamePath">Path to the ASKA installation</param>
    /// <returns>Installation result</returns>
    public async Task<BepInExInstallResult> InstallAsync(string gamePath)
    {
        if (!OperatingSystem.IsWindows())
        {
            _logger.Warning("BepInEx installation is only supported on Windows");
            return new BepInExInstallResult
            {
                Success = false,
                FailureReason = BepInExInstallFailureReason.PlatformNotSupported,
                Message = "Platform not supported"
            };
        }

        if (string.IsNullOrWhiteSpace(gamePath) || !Directory.Exists(gamePath))
        {
            _logger.Warning("Game path is invalid: {GamePath}", gamePath);
            return new BepInExInstallResult
            {
                Success = false,
                FailureReason = BepInExInstallFailureReason.InvalidGamePath,
                Message = "Invalid game path"
            };
        }

        try
        {
            _logger.Information("Starting BepInEx installation for ASKA at: {GamePath}", gamePath);
            _logger.Information("Installing BepInEx version: {Version}", BepInExVersion);

            // Step 1: Check if already installed
            var preCheck = _runtimeValidator.Validate(gamePath);
            if (preCheck.Status == BepInExRuntimeStatus.Installed)
            {
                _logger.Warning("BepInEx is already installed at: {GamePath}", gamePath);
                return new BepInExInstallResult
                {
                    Success = false,
                    FailureReason = BepInExInstallFailureReason.AlreadyInstalled,
                    Message = "BepInEx is already installed"
                };
            }

            // Step 2: Download BepInEx
            var tempZipPath = await DownloadBepInExAsync();
            if (tempZipPath == null)
            {
                return new BepInExInstallResult
                {
                    Success = false,
                    FailureReason = BepInExInstallFailureReason.DownloadFailed,
                    Message = "Failed to download BepInEx"
                };
            }

            // Step 3: Verify checksum
            if (!VerifyChecksum(tempZipPath))
            {
                File.Delete(tempZipPath);
                return new BepInExInstallResult
                {
                    Success = false,
                    FailureReason = BepInExInstallFailureReason.ChecksumFailed,
                    Message = "BepInEx download checksum verification failed"
                };
            }

            // Step 4: Extract securely
            if (!ExtractSecurely(tempZipPath, gamePath))
            {
                File.Delete(tempZipPath);
                return new BepInExInstallResult
                {
                    Success = false,
                    FailureReason = BepInExInstallFailureReason.ExtractionFailed,
                    Message = "Failed to extract BepInEx"
                };
            }

            // Cleanup
            File.Delete(tempZipPath);

            // Step 5: Validate installation
            var validationResult = _runtimeValidator.Validate(gamePath);
            if (validationResult.Status != BepInExRuntimeStatus.Installed)
            {
                _logger.Error("BepInEx installation validation failed at: {GamePath}", gamePath);
                return new BepInExInstallResult
                {
                    Success = false,
                    FailureReason = BepInExInstallFailureReason.InstallationInvalid,
                    Message = validationResult.FailureReason ?? "Installation validation failed"
                };
            }

            _logger.Information("BepInEx installation completed successfully at: {GamePath}", gamePath);
            return new BepInExInstallResult
            {
                Success = true,
                Message = "BepInEx installed successfully"
            };
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "BepInEx installation failed at: {GamePath}", gamePath);
            return new BepInExInstallResult
            {
                Success = false,
                FailureReason = BepInExInstallFailureReason.UnknownError,
                Message = $"Installation failed: {ex.Message}"
            };
        }
    }

    private async Task<string?> DownloadBepInExAsync()
    {
        try
        {
            _logger.Information("Downloading BepInEx from: {Url}", BepInExDownloadUrl);

            var tempPath = Path.Combine(Path.GetTempPath(), $"BepInEx_{Guid.NewGuid()}.zip");

            using var response = await _httpClient.GetAsync(BepInExDownloadUrl, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
            {
                _logger.Error("BepInEx download failed with status: {StatusCode}", response.StatusCode);
                return null;
            }

            var contentLength = response.Content.Headers.ContentLength;
            _logger.Information("BepInEx download started. Size: {Size} bytes", contentLength);

            using var contentStream = await response.Content.ReadAsStreamAsync();
            using var fileStream = File.Create(tempPath);

            await contentStream.CopyToAsync(fileStream);

            _logger.Information("BepInEx download completed to: {TempPath}", tempPath);
            return tempPath;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to download BepInEx");
            return null;
        }
    }

    private bool VerifyChecksum(string filePath)
    {
        try
        {
            _logger.Information("Verifying BepInEx checksum for: {FilePath}", filePath);

            using var sha256 = SHA256.Create();
            using var fileStream = File.OpenRead(filePath);

            var hash = sha256.ComputeHash(fileStream);
            var hashString = Convert.ToHexString(hash).ToUpperInvariant();

            _logger.Information("Computed hash: {ComputedHash}", hashString);
            _logger.Information("Expected hash: {ExpectedHash}", BepInExSha256Hash);

            if (hashString != BepInExSha256Hash)
            {
                _logger.Error("Checksum mismatch for BepInEx download");
                return false;
            }

            _logger.Information("BepInEx checksum verified successfully");
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to verify BepInEx checksum");
            return false;
        }
    }

    private bool ExtractSecurely(string zipPath, string gamePath)
    {
        try
        {
            _logger.Information("Extracting BepInEx securely to: {GamePath}", gamePath);

            using var zip = System.IO.Compression.ZipFile.OpenRead(zipPath);

            foreach (var entry in zip.Entries)
            {
                // Security check: reject path traversal attempts
                var fullPath = Path.Combine(gamePath, entry.FullName);
                var normalizedFull = Path.GetFullPath(fullPath);
                var normalizedGame = Path.GetFullPath(gamePath);

                if (!normalizedFull.StartsWith(normalizedGame, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.Error("Path traversal attempt detected in ZIP entry: {EntryName}", entry.FullName);
                    return false;
                }

                // Create directory if needed
                var directory = Path.GetDirectoryName(normalizedFull);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    _logger.Information("Creating directory: {Directory}", directory);
                    Directory.CreateDirectory(directory);
                }

                // Extract file
                if (!string.IsNullOrEmpty(entry.Name))
                {
                    _logger.Debug("Extracting file: {FileName}", entry.Name);
                    entry.ExtractToFile(normalizedFull, overwrite: true);
                }
            }

            _logger.Information("BepInX extraction completed successfully");
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to extract BepInX");
            return false;
        }
    }
}

/// <summary>
/// BepInX installation failure reasons
/// </summary>
public enum BepInExInstallFailureReason
{
    /// <summary>
    /// BepInX is already installed
    /// </summary>
    AlreadyInstalled,

    /// <summary>
    /// Download failed
    /// </summary>
    DownloadFailed,

    /// <summary>
    /// Checksum verification failed
    /// </summary>
    ChecksumFailed,

    /// <summary>
    /// Extraction failed
    /// </summary>
    ExtractionFailed,

    /// <summary>
    /// Installation validation failed
    /// </summary>
    InstallationInvalid,

    /// <summary>
    /// Platform not supported
    /// </summary>
    PlatformNotSupported,

    /// <summary>
    /// Invalid game path
    /// </summary>
    InvalidGamePath,

    /// <summary>
    /// Unknown error
    /// </summary>
    UnknownError
}

/// <summary>
/// Result of BepInX installation
/// </summary>
public class BepInExInstallResult
{
    /// <summary>
    /// Whether installation succeeded
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Failure reason (if Success is false)
    /// </summary>
    public BepInExInstallFailureReason? FailureReason { get; init; }

    /// <summary>
    /// Detailed message
    /// </summary>
    public string? Message { get; init; }
}
