using ModManager.Core.Models;
using Serilog;

namespace ModManager.Core.Services;

/// <summary>
/// Service for resolving and managing mod dependencies
/// </summary>
public class DependencyResolutionService
{
    private readonly ILogger _logger;

    public DependencyResolutionService(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Resolves dependencies for a set of mods
    /// </summary>
    public DependencyResolutionResult ResolveDependencies(IEnumerable<ModInfo> mods)
    {
        var result = new DependencyResolutionResult();
        var modDictionary = mods.ToDictionary(m => m.Id, m => m);

        try
        {
            // Build dependency graph
            var dependencyGraph = BuildDependencyGraph(mods, modDictionary);
            result.DependencyGraph = dependencyGraph;

            // Check for missing dependencies
            var missingDeps = FindMissingDependencies(mods, modDictionary);
            result.MissingDependencies.AddRange(missingDeps);

            // Check for version conflicts
            var versionConflicts = FindVersionConflicts(mods, modDictionary);
            result.VersionConflicts.AddRange(versionConflicts);

            // Check for circular dependencies
            var circularDeps = FindCircularDependencies(dependencyGraph);
            result.CircularDependencies.AddRange(circularDeps);

            // Check for incompatibilities
            var incompatibilities = FindIncompatibilities(mods, modDictionary);
            result.Incompatibilities.AddRange(incompatibilities);

            // Calculate load order
            if (result.MissingDependencies.Count == 0 && result.CircularDependencies.Count == 0)
            {
                var loadOrder = CalculateLoadOrder(dependencyGraph);
                result.LoadOrder = loadOrder;
                result.CanResolve = true;
            }
            else
            {
                result.CanResolve = false;
            }

            _logger.Information("Dependency resolution completed: {Missing} missing, {Conflicts} conflicts, {Circular} circular", 
                result.MissingDependencies.Count, result.VersionConflicts.Count, result.CircularDependencies.Count);

            return result;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error during dependency resolution");
            result.AddError("Dependency resolution failed due to an internal error");
            return result;
        }
    }

    /// <summary>
    /// Builds a dependency graph from mod information
    /// </summary>
    private DependencyGraph BuildDependencyGraph(IEnumerable<ModInfo> mods, Dictionary<string, ModInfo> modDictionary)
    {
        var graph = new DependencyGraph();

        foreach (var mod in mods)
        {
            var node = new DependencyNode(mod.Id, mod.Name, mod.Version);
            
            // Add dependencies as edges
            foreach (var dependency in mod.Dependencies)
            {
                if (modDictionary.TryGetValue(dependency.Id, out var depMod))
                {
                    // Check version compatibility
                    var versionCheck = CheckVersionCompatibility(dependency.MinVersion, depMod.Version);
                    if (versionCheck.IsCompatible)
                    {
                        node.Dependencies.Add(dependency.Id);
                    }
                    else
                    {
                        node.VersionConflicts.Add(new VersionConflict
                        {
                            ModId = mod.Id,
                            DependencyId = dependency.Id,
                            RequiredVersion = dependency.MinVersion,
                            InstalledVersion = depMod.Version,
                            ConflictType = versionCheck.ConflictType
                        });
                    }
                }
                else
                {
                    node.MissingDependencies.Add(dependency.Id);
                }
            }

            graph.AddNode(node);
        }

        // Second pass: add soft dependency edges (LoadAfter/LoadBefore)
        foreach (var mod in mods)
        {
            if (graph.Nodes.TryGetValue(mod.Id, out var node))
            {
                // LoadAfter: this mod loads after the listed mods (edge from listed → this)
                foreach (var afterId in mod.LoadAfter)
                {
                    if (graph.Nodes.ContainsKey(afterId))
                    {
                        node.Dependencies.Add(afterId);
                    }
                }

                // LoadBefore: this mod loads before the listed mods (edge from this → listed)
                foreach (var beforeId in mod.LoadBefore)
                {
                    if (graph.Nodes.TryGetValue(beforeId, out var beforeNode))
                    {
                        beforeNode.Dependencies.Add(mod.Id);
                    }
                }
            }
        }

        return graph;
    }

    /// <summary>
    /// Finds missing dependencies
    /// </summary>
    private List<MissingDependency> FindMissingDependencies(IEnumerable<ModInfo> mods, Dictionary<string, ModInfo> modDictionary)
    {
        var missing = new List<MissingDependency>();

        foreach (var mod in mods)
        {
            foreach (var dependency in mod.Dependencies)
            {
                if (!dependency.Optional && !modDictionary.ContainsKey(dependency.Id))
                {
                    missing.Add(new MissingDependency
                    {
                        ModId = mod.Id,
                        ModName = mod.Name,
                        DependencyId = dependency.Id,
                        RequiredVersion = dependency.MinVersion,
                        IsOptional = dependency.Optional
                    });
                }
            }
        }

        return missing;
    }

    /// <summary>
    /// Finds version conflicts between dependencies
    /// </summary>
    private List<VersionConflict> FindVersionConflicts(IEnumerable<ModInfo> mods, Dictionary<string, ModInfo> modDictionary)
    {
        var conflicts = new List<VersionConflict>();

        foreach (var mod in mods)
        {
            foreach (var dependency in mod.Dependencies)
            {
                if (modDictionary.TryGetValue(dependency.Id, out var depMod))
                {
                    var versionCheck = CheckVersionCompatibility(dependency.MinVersion, depMod.Version);
                    if (!versionCheck.IsCompatible)
                    {
                        conflicts.Add(new VersionConflict
                        {
                            ModId = mod.Id,
                            ModName = mod.Name,
                            DependencyId = dependency.Id,
                            DependencyName = depMod.Name,
                            RequiredVersion = dependency.MinVersion,
                            InstalledVersion = depMod.Version,
                            ConflictType = versionCheck.ConflictType
                        });
                    }
                }
            }
        }

        return conflicts;
    }

    /// <summary>
    /// Finds circular dependencies in the graph
    /// </summary>
    private List<CircularDependency> FindCircularDependencies(DependencyGraph graph)
    {
        var circular = new List<CircularDependency>();
        var visited = new HashSet<string>();
        var recursionStack = new HashSet<string>();
        var path = new List<string>();

        foreach (var node in graph.Nodes.Values)
        {
            if (!visited.Contains(node.ModId))
            {
                var cycle = DetectCycle(node, visited, recursionStack, path, graph);
                if (cycle != null)
                {
                    circular.Add(cycle);
                }
            }
        }

        return circular;
    }

    /// <summary>
    /// Detects cycles using DFS
    /// </summary>
    private CircularDependency? DetectCycle(
        DependencyNode node, 
        HashSet<string> visited, 
        HashSet<string> recursionStack, 
        List<string> path,
        DependencyGraph graph)
    {
        visited.Add(node.ModId);
        recursionStack.Add(node.ModId);
        path.Add(node.ModId);

        foreach (var depId in node.Dependencies)
        {
            if (!visited.Contains(depId))
            {
                if (graph.Nodes.TryGetValue(depId, out var depNode))
                {
                    var cycle = DetectCycle(depNode, visited, recursionStack, path, graph);
                    if (cycle != null)
                    {
                        return cycle;
                    }
                }
            }
            else if (recursionStack.Contains(depId))
            {
                // Found a cycle
                var cycleStart = path.IndexOf(depId);
                var cyclePath = path.Skip(cycleStart).Append(depId).ToList();
                
                return new CircularDependency
                {
                    ModIds = cyclePath,
                    CycleDescription = string.Join(" -> ", cyclePath.Select(id => graph.Nodes[id].Name))
                };
            }
        }

        recursionStack.Remove(node.ModId);
        path.RemoveAt(path.Count - 1);
        return null;
    }

    /// <summary>
    /// Finds incompatibilities between enabled mods
    /// </summary>
    private List<IncompatibilityConflict> FindIncompatibilities(IEnumerable<ModInfo> mods, Dictionary<string, ModInfo> modDictionary)
    {
        var conflicts = new List<IncompatibilityConflict>();
        var seenPairs = new HashSet<string>();

        foreach (var mod in mods)
        {
            foreach (var incompat in mod.IncompatibleWith)
            {
                if (modDictionary.TryGetValue(incompat.Id, out var conflictingMod))
                {
                    // Avoid duplicate pair reports (A↔B = B↔A)
                    var pairKey = string.Compare(mod.Id, incompat.Id, StringComparison.Ordinal) < 0
                        ? $"{mod.Id}|{incompat.Id}"
                        : $"{incompat.Id}|{mod.Id}";

                    if (seenPairs.Add(pairKey))
                    {
                        conflicts.Add(new IncompatibilityConflict
                        {
                            ModId = mod.Id,
                            ModName = mod.Name,
                            IncompatibleModId = conflictingMod.Id,
                            IncompatibleModName = conflictingMod.Name,
                            Reason = incompat.Reason
                        });

                        _logger.Warning("Incompatibility detected: {ModA} ↔ {ModB} ({Reason})",
                            mod.Name, conflictingMod.Name, incompat.Reason ?? "no reason given");
                    }
                }
            }
        }

        return conflicts;
    }

    /// <summary>
    /// Calculates load order using topological sort
    /// </summary>
    private List<string> CalculateLoadOrder(DependencyGraph graph)
    {
        var result = new List<string>();
        var visited = new HashSet<string>();
        var tempMarked = new HashSet<string>();

        // Sort nodes by canonical ID for consistent ordering
        var sortedNodes = graph.Nodes.Values.OrderBy(n => n.ModId).ToList();

        foreach (var node in sortedNodes)
        {
            if (!visited.Contains(node.ModId))
            {
                VisitNode(node, visited, tempMarked, result, graph);
            }
        }

        return result;
    }

    /// <summary>
    /// Visits a node for topological sort
    /// </summary>
    private void VisitNode(
        DependencyNode node, 
        HashSet<string> visited, 
        HashSet<string> tempMarked, 
        List<string> result,
        DependencyGraph graph)
    {
        if (tempMarked.Contains(node.ModId))
        {
            // This shouldn't happen if we've already checked for cycles
            return;
        }

        if (visited.Contains(node.ModId))
        {
            return;
        }

        tempMarked.Add(node.ModId);

        // Visit dependencies first
        foreach (var depId in node.Dependencies.OrderBy(id => id))
        {
            if (graph.Nodes.TryGetValue(depId, out var depNode))
            {
                VisitNode(depNode, visited, tempMarked, result, graph);
            }
        }

        tempMarked.Remove(node.ModId);
        visited.Add(node.ModId);
        result.Add(node.ModId);
    }

    /// <summary>
    /// Checks if a version satisfies a requirement
    /// </summary>
    private VersionCompatibility CheckVersionCompatibility(string requiredVersion, string installedVersion)
    {
        try
        {
            // Use VersionService for full range support with error handling
            var rangeResult = VersionService.SatisfiesRange(installedVersion, requiredVersion);

            if (rangeResult.ErrorType != VersionRangeErrorType.None)
            {
                // Convert version range errors to compatibility results
                var conflictType = rangeResult.ErrorType switch
                {
                    VersionRangeErrorType.InvalidVersion => VersionConflictType.InvalidFormat,
                    VersionRangeErrorType.InvalidRange => VersionConflictType.InvalidFormat,
                    VersionRangeErrorType.EvaluationError => VersionConflictType.InvalidFormat,
                    _ => VersionConflictType.TooOld
                };

                return new VersionCompatibility 
                { 
                    IsCompatible = false, 
                    ConflictType = conflictType,
                    ErrorMessage = rangeResult.Error
                };
            }

            if (rangeResult.IsSatisfied)
            {
                return new VersionCompatibility { IsCompatible = true };
            }

            return new VersionCompatibility 
            { 
                IsCompatible = false, 
                ConflictType = VersionConflictType.TooOld 
            };
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to compare versions: {Required} vs {Installed}", requiredVersion, installedVersion);
            return new VersionCompatibility 
            { 
                IsCompatible = false, 
                ConflictType = VersionConflictType.InvalidFormat,
                ErrorMessage = ex.Message
            };
        }
    }
}

/// <summary>
/// Result of dependency resolution
/// </summary>
public class DependencyResolutionResult
{
    public bool CanResolve { get; set; }
    public DependencyGraph? DependencyGraph { get; set; }
    public List<string> LoadOrder { get; set; } = new();
    public List<MissingDependency> MissingDependencies { get; set; } = new();
    public List<VersionConflict> VersionConflicts { get; set; } = new();
    public List<CircularDependency> CircularDependencies { get; set; } = new();
    public List<IncompatibilityConflict> Incompatibilities { get; set; } = new();
    public List<string> Errors { get; set; } = new();

    public void AddError(string error) => Errors.Add(error);
    public bool HasIssues => MissingDependencies.Count > 0 || VersionConflicts.Count > 0 || CircularDependencies.Count > 0 || Incompatibilities.Count > 0;
}

/// <summary>
/// Represents a dependency graph
/// </summary>
public class DependencyGraph
{
    public Dictionary<string, DependencyNode> Nodes { get; } = new();

    public void AddNode(DependencyNode node)
    {
        Nodes[node.ModId] = node;
    }
}

/// <summary>
/// Represents a node in the dependency graph
/// </summary>
public class DependencyNode
{
    public string ModId { get; }
    public string Name { get; }
    public string Version { get; }
    public HashSet<string> Dependencies { get; } = new();
    public HashSet<string> MissingDependencies { get; } = new();
    public List<VersionConflict> VersionConflicts { get; } = new();

    public DependencyNode(string modId, string name, string version)
    {
        ModId = modId;
        Name = name;
        Version = version;
    }
}

/// <summary>
/// Represents an incompatibility between two mods
/// </summary>
public class IncompatibilityConflict
{
    public string ModId { get; set; } = string.Empty;
    public string ModName { get; set; } = string.Empty;
    public string IncompatibleModId { get; set; } = string.Empty;
    public string IncompatibleModName { get; set; } = string.Empty;
    public string? Reason { get; set; }
}
