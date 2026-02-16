using NuGet.Versioning;

namespace ModManager.Core.Services;

/// <summary>
/// Service for handling semantic version operations
/// </summary>
public static class VersionService
{
    private static readonly IVersionComparer _comparer = VersionComparer.Default;

    /// <summary>
    /// Parses a version string into a NuGetVersion
    /// </summary>
    public static bool TryParseVersion(string versionString, out NuGetVersion? version)
    {
        return NuGetVersion.TryParse(versionString, out version);
    }

    /// <summary>
    /// Parses a version string or returns a default version
    /// </summary>
    public static NuGetVersion ParseVersionOrDefault(string versionString, NuGetVersion? defaultVersion = null)
    {
        if (TryParseVersion(versionString, out var version))
        {
            return version!;
        }
        return defaultVersion ?? new NuGetVersion(1, 0, 0);
    }

    /// <summary>
    /// Normalizes a version string to a valid semantic version
    /// </summary>
    public static string NormalizeVersion(string versionString)
    {
        if (TryParseVersion(versionString, out var version))
        {
            return version!.ToNormalizedString();
        }
        
        // Try to extract version numbers from invalid strings
        var numbers = System.Text.RegularExpressions.Regex.Matches(versionString, @"\d+")
            .Select(m => int.Parse(m.Value))
            .ToList();

        if (numbers.Count >= 3)
        {
            return new NuGetVersion(numbers[0], numbers[1], numbers[2]).ToNormalizedString();
        }
        else if (numbers.Count >= 2)
        {
            return new NuGetVersion(numbers[0], numbers[1], 0).ToNormalizedString();
        }
        else if (numbers.Count >= 1)
        {
            return new NuGetVersion(numbers[0], 0, 0).ToNormalizedString();
        }

        return new NuGetVersion(1, 0, 0).ToNormalizedString();
    }

    /// <summary>
    /// Parses a version range string
    /// </summary>
    public static bool TryParseRange(string rangeString, out VersionRange? range)
    {
        return VersionRange.TryParse(rangeString, out range);
    }

    /// <summary>
    /// Checks if a version satisfies a version range
    /// </summary>
    public static VersionRangeResult SatisfiesRange(string version, string versionRange)
    {
        if (!TryParseVersion(version, out var nugetVersion))
        {
            return new VersionRangeResult 
            { 
                IsSatisfied = false, 
                Error = "Invalid version format",
                ErrorType = VersionRangeErrorType.InvalidVersion
            };
        }

        if (!VersionRange.TryParse(versionRange, out var range))
        {
            return new VersionRangeResult 
            { 
                IsSatisfied = false, 
                Error = $"Invalid version range format: '{versionRange}'",
                ErrorType = VersionRangeErrorType.InvalidRange
            };
        }

        try
        {
            var isSatisfied = range!.Satisfies(nugetVersion);
            return new VersionRangeResult 
            { 
                IsSatisfied = isSatisfied,
                Error = null,
                ErrorType = VersionRangeErrorType.None
            };
        }
        catch (Exception ex)
        {
            return new VersionRangeResult 
            { 
                IsSatisfied = false, 
                Error = $"Version range evaluation error: {ex.Message}",
                ErrorType = VersionRangeErrorType.EvaluationError
            };
        }
    }

    /// <summary>
    /// Legacy method for backward compatibility - returns boolean only
    /// </summary>
    public static bool SatisfiesRangeLegacy(string version, string versionRange)
    {
        var result = SatisfiesRange(version, versionRange);
        return result.IsSatisfied;
    }

    /// <summary>
    /// Compares two versions
    /// </summary>
    public static int CompareVersions(string version1, string version2)
    {
        var v1 = ParseVersionOrDefault(version1);
        var v2 = ParseVersionOrDefault(version2);
        return _comparer.Compare(v1, v2);
    }

    /// <summary>
    /// Checks if version1 is greater than version2
    /// </summary>
    public static bool IsGreaterThan(string version1, string version2)
    {
        return CompareVersions(version1, version2) > 0;
    }

    /// <summary>
    /// Checks if version1 is less than version2
    /// </summary>
    public static bool IsLessThan(string version1, string version2)
    {
        return CompareVersions(version1, version2) < 0;
    }

    /// <summary>
    /// Checks if version1 equals version2
    /// </summary>
    public static bool AreEqual(string version1, string version2)
    {
        return CompareVersions(version1, version2) == 0;
    }

    /// <summary>
    /// Gets the minimum version that satisfies a version range
    /// </summary>
    public static SemanticVersion? GetMinimumVersion(string versionRange)
    {
        if (!VersionRange.TryParse(versionRange, out var range))
        {
            return TryParseVersion(versionRange, out var version) ? version : null;
        }

        // For simple ranges, return the lower bound
        if (range!.MinVersion != null)
        {
            return range.MinVersion;
        }

        return null;
    }

    /// <summary>
    /// Creates a version range from a minimum version
    /// </summary>
    public static string CreateMinimumVersionRange(string minimumVersion)
    {
        if (TryParseVersion(minimumVersion, out var version))
        {
            return $">={version!.ToNormalizedString()}";
        }
        return minimumVersion;
    }

    /// <summary>
    /// Validates if a version string is in a valid format
    /// </summary>
    public static bool IsValidVersion(string versionString)
    {
        return TryParseVersion(versionString, out _);
    }

    /// <summary>
    /// Gets the major version from a version string
    /// </summary>
    public static int GetMajorVersion(string versionString)
    {
        var version = ParseVersionOrDefault(versionString);
        return version.Major;
    }

    /// <summary>
    /// Gets the minor version from a version string
    /// </summary>
    public static int GetMinorVersion(string versionString)
    {
        var version = ParseVersionOrDefault(versionString);
        return version.Minor;
    }

    /// <summary>
    /// Gets the patch version from a version string
    /// </summary>
    public static int GetPatchVersion(string versionString)
    {
        var version = ParseVersionOrDefault(versionString);
        return version.Patch;
    }
}

/// <summary>
/// Result of version range evaluation
/// </summary>
public class VersionRangeResult
{
    public bool IsSatisfied { get; set; }
    public string? Error { get; set; }
    public VersionRangeErrorType ErrorType { get; set; }
}

/// <summary>
/// Types of version range errors
/// </summary>
public enum VersionRangeErrorType
{
    None,
    InvalidVersion,
    InvalidRange,
    EvaluationError
}
