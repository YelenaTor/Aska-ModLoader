using ModManager.Core.Models;
using ModManager.Core.Services;
using Serilog;
using System.Collections.Generic;
using Xunit;

namespace ModManager.Core.Tests;

/// <summary>
/// Integration-style tests that exercise the dependency resolver to validate detection of
/// missing references, version mismatches, and circular graphs.
/// </summary>
public class DependencyResolutionTests
{
    private readonly DependencyResolutionService _resolver;

    public DependencyResolutionTests()
    {
        var logger = new LoggerConfiguration()
            .WriteTo.Console()
            .CreateLogger();

        _resolver = new DependencyResolutionService(logger);
    }

    [Fact]
    public void ResolveDependencies_WithCircularReferences_DetectsCycle()
    {
        var mods = new[]
        {
            new ModInfo
            {
                Id = "mod-a",
                Version = "1.0.0",
                IsEnabled = true,
                Dependencies = new List<ModDependency>
                {
                    new() { Id = "mod-b", MinVersion = ">=1.0.0", Optional = false }
                }
            },
            new ModInfo
            {
                Id = "mod-b",
                Version = "1.0.0",
                IsEnabled = true,
                Dependencies = new List<ModDependency>
                {
                    new() { Id = "mod-a", MinVersion = ">=1.0.0", Optional = false }
                }
            }
        };

        var resolution = _resolver.ResolveDependencies(mods);

        Assert.NotEmpty(resolution.CircularDependencies);
        Assert.Contains(resolution.CircularDependencies, cycle => cycle.CycleDescription.Contains("mod-a") && cycle.CycleDescription.Contains("mod-b"));
    }

    [Fact]
    public void ResolveDependencies_WithMissingDependency_ReportsMissingReference()
    {
        var mods = new[]
        {
            new ModInfo
            {
                Id = "mod-a",
                Version = "1.0.0",
                IsEnabled = true,
                Dependencies = new List<ModDependency>
                {
                    new() { Id = "missing-mod", MinVersion = "1.0.0", Optional = false }
                }
            }
        };

        var resolution = _resolver.ResolveDependencies(mods);

        Assert.Contains(resolution.MissingDependencies, missing => missing.ModId == "mod-a" && missing.DependencyId == "missing-mod");
    }

    [Fact]
    public void ResolveDependencies_WithVersionConflict_ReportsConflict()
    {
        var mods = new[]
        {
            new ModInfo
            {
                Id = "dependency",
                Version = "1.0.0",
                IsEnabled = true,
                Dependencies = new List<ModDependency>()
            },
            new ModInfo
            {
                Id = "consumer",
                Version = "1.0.0",
                IsEnabled = true,
                Dependencies = new List<ModDependency>
                {
                    new() { Id = "dependency", MinVersion = ">=2.0.0", Optional = false }
                }
            }
        };

        var resolution = _resolver.ResolveDependencies(mods);

        Assert.Contains(resolution.VersionConflicts, conflict => conflict.ModId == "consumer" && conflict.DependencyName == "dependency");
    }
}
