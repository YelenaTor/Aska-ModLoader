using System.Collections.Generic;

namespace ModManager.Core.Models;

/// <summary>
/// Represents a missing dependency
/// </summary>
public class MissingDependency
{
    public string ModId { get; set; } = string.Empty;
    public string ModName { get; set; } = string.Empty;
    public string DependencyId { get; set; } = string.Empty;
    public string RequiredVersion { get; set; } = string.Empty;
    public bool IsOptional { get; set; }
}

/// <summary>
/// Represents a version conflict
/// </summary>
public class VersionConflict
{
    public string ModId { get; set; } = string.Empty;
    public string ModName { get; set; } = string.Empty;
    public string DependencyId { get; set; } = string.Empty;
    public string DependencyName { get; set; } = string.Empty;
    public string RequiredVersion { get; set; } = string.Empty;
    public string InstalledVersion { get; set; } = string.Empty;
    public VersionConflictType ConflictType { get; set; }
}

/// <summary>
/// Represents a circular dependency
/// </summary>
public class CircularDependency
{
    public List<string> ModIds { get; set; } = new();
    public string CycleDescription { get; set; } = string.Empty;
}

/// <summary>
/// Result of version compatibility check
/// </summary>
public class VersionCompatibility
{
    public bool IsCompatible { get; set; }
    public VersionConflictType ConflictType { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Types of version conflicts
/// </summary>
public enum VersionConflictType
{
    TooOld,
    TooNew,
    InvalidFormat,
    IncompatibleRange
}
