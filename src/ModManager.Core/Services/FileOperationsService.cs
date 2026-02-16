using ModManager.Core.Interfaces;
using Serilog;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace ModManager.Core.Services;

/// <summary>
/// Service for safe file operations with backup and rollback capabilities
/// </summary>
public class FileOperationsService
{
    private readonly ILogger _logger;
    private readonly string _backupPath;
    private readonly string _askaPath;

    public FileOperationsService(ILogger logger, string askaPath)
    {
        _logger = logger;
        _askaPath = askaPath;
        _backupPath = Path.Combine(askaPath, "BepInEx", ".modmanager", "backups");
        Directory.CreateDirectory(_backupPath);
    }

    /// <summary>
    /// Async wrapper for legacy callers
    /// </summary>
    public Task<bool> RestoreFromBackupAsync(string originalPath, string backupFileName)
    {
        return Task.FromResult(RestoreFromBackup(originalPath, backupFileName));
    }

    /// <summary>
    /// Safely enables a mod by moving it from disabled state
    /// </summary>
    public async Task<bool> EnableModAsync(string dllPath)
    {
        try
        {
            if (!File.Exists(dllPath))
            {
                // Check if it's a disabled file
                var disabledPath = dllPath + ".disabled";
                if (File.Exists(disabledPath))
                {
                    // Create backup before enabling
                    await CreateBackupAsync(disabledPath);
                    
                    // Move to enabled state
                    File.Move(disabledPath, dllPath);
                    _logger.Information("Enabled mod: {Path}", dllPath);
                    return true;
                }
            }
            else
            {
                _logger.Warning("Mod is already enabled: {Path}", dllPath);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to enable mod: {Path}", dllPath);
            return false;
        }
    }

    // Legacy async wrappers preserved for callers expecting async signatures
    public Task<IEnumerable<string>> GetBackupsAsync(string originalPath)
    {
        return Task.FromResult(GetBackups(originalPath));
    }

    public Task CleanupOldBackupsAsync(int keepCount = 5)
    {
        CleanupOldBackups(keepCount);
        return Task.CompletedTask;
    }

    public Task<bool> SafeDeleteAsync(string filePath, bool createBackup = true)
    {
        return Task.FromResult(SafeDelete(filePath, createBackup));
    }

    /// <summary>
    /// Safely disables a mod by moving it to disabled state
    /// </summary>
    public async Task<bool> DisableModAsync(string dllPath)
    {
        try
        {
            if (!File.Exists(dllPath))
            {
                _logger.Warning("Mod file not found: {Path}", dllPath);
                return false;
            }

            var disabledPath = dllPath + ".disabled";
            
            // Create backup before disabling
            await CreateBackupAsync(dllPath);
            
            // Move to disabled state
            File.Move(dllPath, disabledPath);
            _logger.Information("Disabled mod: {Path}", dllPath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to disable mod: {Path}", dllPath);
            return false;
        }
    }

    /// <summary>
    /// Creates a backup of a file before modification
    /// </summary>
    private void CreateBackup(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return;
            }

            var fileName = Path.GetFileName(filePath);
            var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            var backupFileName = $"{fileName}.{timestamp}.backup";
            var backupFilePath = Path.Combine(_backupPath, backupFileName);

            File.Copy(filePath, backupFilePath, true);
            _logger.Debug("Created backup: {BackupPath}", backupFilePath);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to create backup for: {Path}", filePath);
        }
    }

    /// <summary>
    /// Async wrapper for legacy callers
    /// </summary>
    private Task CreateBackupAsync(string filePath)
    {
        CreateBackup(filePath);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Restores a file from backup
    /// </summary>
    public bool RestoreFromBackup(string originalPath, string backupFileName)
    {
        try
        {
            var backupFilePath = Path.Combine(_backupPath, backupFileName);
            
            if (!File.Exists(backupFilePath))
            {
                _logger.Warning("Backup file not found: {BackupPath}", backupFilePath);
                return false;
            }

            // Create backup of current file before restoring
            CreateBackup(originalPath);

            File.Copy(backupFilePath, originalPath, true);
            _logger.Information("Restored file from backup: {OriginalPath}", originalPath);
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to restore backup for: {Path}", originalPath);
            return false;
        }
    }

    /// <summary>
    /// Lists available backups for a file
    /// </summary>
    public IEnumerable<string> GetBackups(string originalPath)
    {
        try
        {
            var fileName = Path.GetFileName(originalPath);
            var backupPattern = $"{fileName}.*.backup";
            
            if (!Directory.Exists(_backupPath))
            {
                return Enumerable.Empty<string>();
            }

            return Directory.GetFiles(_backupPath, backupPattern)
                .Select(Path.GetFileName)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to get backups for: {Path}", originalPath);
            return Enumerable.Empty<string>();
        }
    }

    /// <summary>
    /// Cleans up old backups (keeps only the most recent N backups per file)
    /// </summary>
    public void CleanupOldBackups(int keepCount = 5)
    {
        try
        {
            if (!Directory.Exists(_backupPath))
            {
                return;
            }

            var backupFiles = Directory.GetFiles(_backupPath, "*.backup")
                .Select(Path.GetFileName)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!)
                .GroupBy(x => Path.GetFileNameWithoutExtension(x).Split('.').First())
                .ToList();

            foreach (var group in backupFiles)
            {
                var filesToDelete = group
                    .OrderByDescending(x => x)
                    .Skip(keepCount);

                foreach (var file in filesToDelete)
                {
                    var fullPath = Path.Combine(_backupPath, file);
                    File.Delete(fullPath);
                    _logger.Debug("Deleted old backup: {BackupFile}", file);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to cleanup old backups");
        }
    }

    /// <summary>
    /// Gets the original file name from a backup file name
    /// </summary>
    private string GetOriginalFileNameFromBackup(string backupPath)
    {
        var fileName = Path.GetFileName(backupPath);
        var parts = fileName.Split('.');
        
        // Format: original.yyyyMMddHHmmss.backup
        if (parts.Length >= 3)
        {
            return string.Join(".", parts.Take(parts.Length - 2));
        }
        
        return string.Empty;
    }

    /// <summary>
    /// Safely copies a file with overwrite protection
    /// </summary>
    public async Task<bool> SafeCopyAsync(string sourcePath, string destinationPath, bool createBackup = true)
    {
        try
        {
            if (!File.Exists(sourcePath))
            {
                _logger.Warning("Source file not found: {Path}", sourcePath);
                return false;
            }

            // Create backup of destination if it exists
            if (createBackup && File.Exists(destinationPath))
            {
                await CreateBackupAsync(destinationPath);
            }

            // Ensure destination directory exists
            var destDir = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            File.Copy(sourcePath, destinationPath, true);
            _logger.Information("Copied file: {Source} -> {Destination}", sourcePath, destinationPath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to copy file: {Source} -> {Destination}", sourcePath, destinationPath);
            return false;
        }
    }

    /// <summary>
    /// Executes an action with retry logic
    /// </summary>
    public async Task<bool> ExecuteWithRetryAsync(Func<Task<bool>> action, int maxRetries = 3, int delayMs = 500)
    {
        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                if (await action())
                {
                    return true;
                }
            }
            catch (IOException ex) when (i < maxRetries - 1)
            {
                _logger.Warning(ex, "IO Error, retrying {Attempt}/{Max}", i + 1, maxRetries);
                await Task.Delay(delayMs);
            }
        }
        return false;
    }

    /// <summary>
    /// Checks if ASKA is running
    /// </summary>
    public bool IsGameRunning()
    {
        var exeName = "Aska";
        return Process.GetProcessesByName(exeName).Any();
    }

    /// <summary>
    /// Safely deletes a file with backup
    /// </summary>
    public bool SafeDelete(string filePath, bool createBackup = true)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return true;
            }

            if (createBackup)
            {
                CreateBackup(filePath);
            }

            File.Delete(filePath);
            _logger.Debug("Deleted file: {Path}", filePath);
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to delete file: {Path}", filePath);
            return false;
        }
    }
}
