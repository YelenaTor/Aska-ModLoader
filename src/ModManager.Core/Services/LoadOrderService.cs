using ModManager.Core.Models;
using Serilog;

namespace ModManager.Core.Services;

/// <summary>
/// Service for managing mod load order
/// </summary>
public class LoadOrderService
{
    private readonly ILogger _logger;
    private readonly string _pluginsPath;
    private readonly string _loadOrderPath;

    public LoadOrderService(ILogger logger, string askaPath)
    {
        _logger = logger;
        _pluginsPath = Path.Combine(askaPath, "BepInEx", "plugins");
        _loadOrderPath = Path.Combine(askaPath, "BepInEx", ".modmanager", "loadorder.json");
        
        // Ensure directory exists
        Directory.CreateDirectory(Path.GetDirectoryName(_loadOrderPath)!);
    }

    /// <summary>
    /// Async wrapper for legacy callers
    /// </summary>
    private Task<bool> ApplyLoadOrderAsync(List<LoadOrderEntry> loadOrder, Dictionary<string, string> currentFiles)
    {
        return Task.FromResult(ApplyLoadOrder(loadOrder, currentFiles));
    }

    /// <summary>
    /// Gets the current load order from file system
    /// </summary>
    public async Task<List<LoadOrderEntry>> GetCurrentLoadOrderAsync()
    {
        var loadOrder = new List<LoadOrderEntry>();

        try
        {
            // First, try to load from our load order file
            if (File.Exists(_loadOrderPath))
            {
                var json = await File.ReadAllTextAsync(_loadOrderPath);
                var savedOrder = System.Text.Json.JsonSerializer.Deserialize<List<LoadOrderEntry>>(json);
                if (savedOrder != null)
                {
                    loadOrder = savedOrder;
                }
            }
            else
            {
                // If no saved order, infer from file system
                loadOrder = await InferLoadOrderFromFileSystemAsync();
                await SaveLoadOrderAsync(loadOrder);
            }

            _logger.Information("Loaded {Count} mods in load order", loadOrder.Count);
            return loadOrder;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load current load order");
            return await InferLoadOrderFromFileSystemAsync();
        }
    }

    // Legacy async wrapper for interface/callers
    private Task<string> ExtractModIdFromPathAsync(string filePath)
    {
        return Task.FromResult(ExtractModIdFromPath(filePath));
    }

    /// <summary>
    /// Async wrapper for legacy callers
    /// </summary>
    private Task CreateLoadOrderBackupAsync()
    {
        CreateLoadOrderBackup();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Sets the load order by renaming files with prefixes
    /// </summary>
    public async Task<bool> SetLoadOrderAsync(List<LoadOrderEntry> loadOrder)
    {
        try
        {
            _logger.Information("Setting load order for {Count} mods", loadOrder.Count);

            // Create backup of current state
            await CreateLoadOrderBackupAsync();

            // Get current files
            var currentFiles = await GetCurrentModFilesAsync();
            
            // Apply new order by renaming files
            var success = await ApplyLoadOrderAsync(loadOrder, currentFiles);
            
            if (success)
            {
                // Save the load order to file
                await SaveLoadOrderAsync(loadOrder);
                _logger.Information("Successfully applied new load order");
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to set load order");
            return false;
        }
    }

    /// <summary>
    /// Updates load order based on dependency resolution
    /// </summary>
    public async Task<bool> UpdateLoadOrderFromDependenciesAsync(List<string> dependencyOrder)
    {
        try
        {
            var currentOrder = await GetCurrentLoadOrderAsync();
            var currentMods = currentOrder.ToDictionary(e => e.ModId, e => e);

            // Create new load order based on dependency resolution
            var newOrder = new List<LoadOrderEntry>();
            var order = 0;

            foreach (var modId in dependencyOrder)
            {
                if (currentMods.TryGetValue(modId, out var entry))
                {
                    entry.Order = order++;
                    entry.LoadOrderSource = LoadOrderSource.Dependency;
                    newOrder.Add(entry);
                }
            }

            // Add any mods not in dependency order at the end
            foreach (var entry in currentOrder.Where(e => !dependencyOrder.Contains(e.ModId)))
            {
                entry.Order = order++;
                entry.LoadOrderSource = LoadOrderSource.Manual;
                newOrder.Add(entry);
            }

            return await SetLoadOrderAsync(newOrder);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to update load order from dependencies");
            return false;
        }
    }

    /// <summary>
    /// Reorders a mod in the load order
    /// </summary>
    public async Task<bool> ReorderModAsync(string modId, int newPosition)
    {
        try
        {
            var currentOrder = await GetCurrentLoadOrderAsync();
            var modEntry = currentOrder.FirstOrDefault(e => e.ModId == modId);
            
            if (modEntry == null)
            {
                _logger.Warning("Mod not found in load order: {ModId}", modId);
                return false;
            }

            // Remove from current position
            currentOrder.Remove(modEntry);
            
            // Insert at new position
            newPosition = Math.Max(0, Math.Min(newPosition, currentOrder.Count));
            currentOrder.Insert(newPosition, modEntry);

            // Update order numbers
            for (int i = 0; i < currentOrder.Count; i++)
            {
                currentOrder[i].Order = i;
                currentOrder[i].LoadOrderSource = LoadOrderSource.Manual;
            }

            return await SetLoadOrderAsync(currentOrder);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to reorder mod: {ModId}", modId);
            return false;
        }
    }

    /// <summary>
    /// Infers load order from current file system state
    /// </summary>
    private async Task<List<LoadOrderEntry>> InferLoadOrderFromFileSystemAsync()
    {
        var loadOrder = new List<LoadOrderEntry>();
        
        try
        {
            if (!Directory.Exists(_pluginsPath))
            {
                return loadOrder;
            }

            // Get all DLL files and sort by filename
            var dllFiles = Directory.GetFiles(_pluginsPath, "*.dll", SearchOption.AllDirectories)
                .Where(f => !f.EndsWith(".disabled"))
                .OrderBy(Path.GetFileName)
                .ToList();

            int order = 0;
            foreach (var dllFile in dllFiles)
            {
                var fileName = Path.GetFileNameWithoutExtension(dllFile);
                var modId = await ExtractModIdFromPathAsync(dllFile);
                
                loadOrder.Add(new LoadOrderEntry
                {
                    ModId = modId,
                    OriginalFileName = Path.GetFileName(dllFile),
                    CurrentFileName = Path.GetFileName(dllFile),
                    Order = order++,
                    LoadOrderSource = LoadOrderSource.Inferred,
                    IsLocked = false
                });
            }

            _logger.Information("Inferred load order for {Count} mods", loadOrder.Count);
            return loadOrder;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to infer load order from file system");
            return loadOrder;
        }
    }

    /// <summary>
    /// Gets current mod files from file system
    /// </summary>
    private async Task<Dictionary<string, string>> GetCurrentModFilesAsync()
    {
        var files = new Dictionary<string, string>();

        try
        {
            if (!Directory.Exists(_pluginsPath))
            {
                return files;
            }

            var dllFiles = Directory.GetFiles(_pluginsPath, "*.dll*", SearchOption.AllDirectories);
            
            foreach (var file in dllFiles)
            {
                var modId = await ExtractModIdFromPathAsync(file);
                if (!string.IsNullOrEmpty(modId))
                {
                    files[modId] = file;
                }
            }
        }
        catch (Exception ex)
            {
            _logger.Error(ex, "Failed to get current mod files");
        }

        return files;
    }

    /// <summary>
    /// Applies load order by renaming files with numeric prefixes
    /// </summary>
    private bool ApplyLoadOrder(List<LoadOrderEntry> loadOrder, Dictionary<string, string> currentFiles)
    {
        try
        {
            var renameOperations = new List<(string source, string destination)>();

            foreach (var entry in loadOrder)
            {
                if (!currentFiles.TryGetValue(entry.ModId, out var currentPath))
                {
                    _logger.Warning("Mod file not found: {ModId}", entry.ModId);
                    continue;
                }

                var directory = Path.GetDirectoryName(currentPath)!;
                var extension = Path.GetExtension(currentPath);
                var prefix = entry.Order.ToString("D3"); // Zero-padded 3-digit prefix
                
                // Handle disabled files
                var isDisabled = currentPath.EndsWith(".disabled");
                var actualExtension = isDisabled ? ".disabled" : extension;
                
                var newFileName = $"{prefix}_{entry.OriginalFileName}{actualExtension}";
                var newPath = Path.Combine(directory, newFileName);

                if (currentPath != newPath)
                {
                    renameOperations.Add((currentPath, newPath));
                }
            }

            // Perform rename operations
            foreach (var (source, destination) in renameOperations)
            {
                if (File.Exists(destination))
                {
                    File.Delete(destination);
                }

                File.Move(source, destination);
                _logger.Debug("Renamed {Source} -> {Destination}", source, destination);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to apply load order");
            return false;
        }
    }

    /// <summary>
    /// Saves load order to file
    /// </summary>
    private async Task SaveLoadOrderAsync(List<LoadOrderEntry> loadOrder)
    {
        try
        {
            var options = new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
            };

            var json = System.Text.Json.JsonSerializer.Serialize(loadOrder, options);
            await File.WriteAllTextAsync(_loadOrderPath, json);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save load order");
        }
    }

    /// <summary>
    /// Creates backup of current load order
    /// </summary>
    private void CreateLoadOrderBackup()
    {
        try
        {
            var backupPath = _loadOrderPath + ".backup." + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            if (File.Exists(_loadOrderPath))
            {
                File.Copy(_loadOrderPath, backupPath);
                _logger.Debug("Created load order backup: {Backup}", backupPath);
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to create load order backup");
        }
    }

    /// <summary>
    /// Extracts mod ID from file path
    /// </summary>
    private string ExtractModIdFromPath(string filePath)
    {
        try
        {
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            
            // Remove numeric prefix if present (e.g., "001_ModName" -> "ModName")
            if (fileName.Length > 4 && fileName[3] == '_')
            {
                return fileName.Substring(4);
            }

            return fileName;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to extract mod ID from path: {Path}", filePath);
            return Path.GetFileNameWithoutExtension(filePath);
        }
    }

    /// <summary>
    /// Validates that the current file system state matches the expected load order
    /// </summary>
    public async Task<bool> ValidateLoadOrderAsync()
    {
        try
        {
            var expectedOrder = await GetCurrentLoadOrderAsync();
            var actualFiles = await GetCurrentModFilesAsync();

            foreach (var entry in expectedOrder)
            {
                if (!actualFiles.TryGetValue(entry.ModId, out var actualPath))
                {
                    _logger.Warning("Mod file missing: {ModId}", entry.ModId);
                    return false;
                }

                // Check if file has the expected prefix
                var expectedPrefix = entry.Order.ToString("D3") + "_";
                var fileName = Path.GetFileName(actualPath);
                
                if (!fileName.StartsWith(expectedPrefix))
                {
                    _logger.Warning("Load order prefix mismatch for {ModId}: expected '{Prefix}', got '{FileName}'", 
                        entry.ModId, expectedPrefix, fileName);
                    return false;
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to validate load order");
            return false;
        }
    }
}

/// <summary>
/// Represents an entry in the load order
/// </summary>
public class LoadOrderEntry
{
    public string ModId { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public string CurrentFileName { get; set; } = string.Empty;
    public int Order { get; set; }
    public LoadOrderSource LoadOrderSource { get; set; }
    public bool IsLocked { get; set; }
}

/// <summary>
/// Source of load order information
/// </summary>
public enum LoadOrderSource
{
    Inferred,
    Manual,
    Dependency,
    Auto
}
