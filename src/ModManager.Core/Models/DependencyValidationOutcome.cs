namespace ModManager.Core.Models;

/// <summary>
/// Result of dependency validation operations (e.g. before installing a mod).
/// </summary>
public class DependencyValidationOutcome
{
    /// <summary>
    /// Indicates whether the validation succeeded.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Optional failure reason when validation fails.
    /// </summary>
    public string? FailureReason { get; set; }

    /// <summary>
    /// Any additional errors that occurred during validation.
    /// </summary>
    public IEnumerable<string>? Errors { get; set; }
}
