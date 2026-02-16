using System;

namespace ModManager.Core.Models;

/// <summary>
/// Represents a runtime error attributed to a specific mod or the game.
/// </summary>
public class RuntimeError
{
    /// <summary>
    /// Unique identifier for the error record.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// The mod ID associated with this error, if it could be attributed.
    /// </summary>
    public string? ModId { get; set; }

    /// <summary>
    /// The display name of the mod.
    /// </summary>
    public string? ModName { get; set; }

    /// <summary>
    /// The primary error message.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// The full stack trace of the error.
    /// </summary>
    public string StackTrace { get; set; } = string.Empty;

    /// <summary>
    /// When the error occurred.
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// The full line from the log file.
    /// </summary>
    public string FullLogLine { get; set; } = string.Empty;

    /// <summary>
    /// The severity level of the error.
    /// </summary>
    public ErrorSeverity Severity { get; set; } = ErrorSeverity.Error;
}

/// <summary>
/// Defines the severity levels for runtime errors.
/// </summary>
public enum ErrorSeverity
{
    Info,
    Warning,
    Error,
    Critical
}
