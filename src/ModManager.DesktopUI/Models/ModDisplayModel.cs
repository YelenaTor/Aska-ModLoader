namespace ModManager.DesktopUI.Models;

/// <summary>
/// UI-friendly model for displaying mod information
/// </summary>
public class ModDisplayModel
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public string InstallPath { get; set; } = string.Empty;
    public DateTime InstallDate { get; set; }
    public DateTime LastUpdated { get; set; }
    public List<ModDependencyDisplayModel> Dependencies { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}

/// <summary>
/// UI-friendly model for displaying mod dependencies
/// </summary>
public class ModDependencyDisplayModel
{
    public string Id { get; set; } = string.Empty;
    public string MinVersion { get; set; } = string.Empty;
    public bool IsOptional { get; set; }
    public bool IsSatisfied { get; set; }
    public string StatusMessage { get; set; } = string.Empty;
}

/// <summary>
/// Result of facade operations
/// </summary>
public class FacadeOperationResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();

    public static FacadeOperationResult SuccessResult(string message)
    {
        return new FacadeOperationResult { Success = true, Message = message };
    }

    public static FacadeOperationResult FailureResult(string message, List<string>? errors = null)
    {
        return new FacadeOperationResult 
        { 
            Success = false, 
            Message = message, 
            Errors = errors ?? new List<string>()
        };
    }
}
