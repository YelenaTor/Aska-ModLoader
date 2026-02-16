using ModManager.Core.Models;
using ModManager.Core.Services;
using Serilog;
using System.Text.Json;

namespace ModManager.Core.Tests;

/// <summary>
/// Unit tests for the ModScanner service
/// </summary>
public class ModScannerTests : IDisposable
{
    private readonly ILogger _logger;
    private readonly string _testDirectory;
    private readonly ModScanner _scanner;

    public ModScannerTests()
    {
        _logger = new LoggerConfiguration()
            .WriteTo.Console()
            .CreateLogger();

        _testDirectory = Path.Combine(Path.GetTempPath(), "ModManagerTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDirectory);
        
        _scanner = new ModScanner(_logger);
    }

    [Fact]
    public async Task ScanModsAsync_EmptyDirectory_ReturnsEmptyList()
    {
        // Arrange
        var pluginsPath = Path.Combine(_testDirectory, "plugins");
        Directory.CreateDirectory(pluginsPath);

        // Act
        var result = await _scanner.ScanModsAsync(pluginsPath);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task ScanModsAsync_WithManifestJson_LoadsModInfo()
    {
        // Arrange
        var pluginsPath = Path.Combine(_testDirectory, "plugins");
        Directory.CreateDirectory(pluginsPath);

        var modDirectory = Path.Combine(pluginsPath, "TestMod");
        Directory.CreateDirectory(modDirectory);

        var manifest = new ModManifest
        {
            Id = "com.test.mod",
            Name = "Test Mod",
            Version = "1.0.0",
            Author = "Test Author",
            Description = "A test mod",
            Entry = "TestMod.dll"
        };

        var manifestPath = Path.Combine(modDirectory, "manifest.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest));

        // Create a dummy DLL
        var dllPath = Path.Combine(modDirectory, "TestMod.dll");
        await File.WriteAllTextAsync(dllPath, "dummy content");

        // Act
        var result = await _scanner.ScanModsAsync(pluginsPath);

        // Assert
        Assert.Single(result);
        var mod = result.First();
        Assert.Equal("com.test.mod", mod.Id);
        Assert.Equal("Test Mod", mod.Name);
        Assert.Equal("1.0.0", mod.Version);
    }

    [Fact]
    public async Task ScanModsAsync_WithDisabledDll_SkipsDisabledMods()
    {
        // Arrange
        var pluginsPath = Path.Combine(_testDirectory, "plugins");
        Directory.CreateDirectory(pluginsPath);

        // Create a mod directory with a manifest
        var modDirectory = Path.Combine(pluginsPath, "DisabledMod");
        Directory.CreateDirectory(modDirectory);

        var manifest = new ModManifest
        {
            Id = "com.test.disabled",
            Name = "Disabled Test Mod",
            Version = "1.0.0",
            Author = "Test Author",
            Description = "A disabled test mod",
            Entry = "DisabledMod.dll"
        };

        var manifestPath = Path.Combine(modDirectory, "manifest.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest));

        // Create a disabled DLL
        var dllPath = Path.Combine(modDirectory, "DisabledMod.dll");
        var disabledDllPath = dllPath + ".disabled";
        await File.WriteAllTextAsync(disabledDllPath, "dummy content");

        // Act
        var result = await _scanner.ScanModsAsync(pluginsPath);

        // Assert
        var mod = Assert.Single(result);
        Assert.Equal("com.test.disabled", mod.Id);
        Assert.False(mod.IsEnabled);
    }

    [Fact]
    public async Task ScanModsAsync_WithInvalidManifest_HandlesGracefully()
    {
        // Arrange
        var pluginsPath = Path.Combine(_testDirectory, "plugins");
        Directory.CreateDirectory(pluginsPath);

        var modDirectory = Path.Combine(pluginsPath, "InvalidMod");
        Directory.CreateDirectory(modDirectory);

        var manifestPath = Path.Combine(modDirectory, "manifest.json");
        await File.WriteAllTextAsync(manifestPath, "{ invalid json }");

        // Act
        var result = await _scanner.ScanModsAsync(pluginsPath);

        // Assert
        Assert.Empty(result);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}
