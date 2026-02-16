namespace ModManager.Core.Models;

public sealed class BepInExRuntimeResult
{
    public BepInExRuntimeStatus Status { get; init; }
    public string GamePath { get; init; } = string.Empty;
    public bool GameExecutableExists { get; init; }
    public bool CoreDllExists { get; init; }
    public bool PluginsFolderExists { get; init; }
    public bool LoaderExists { get; init; }
    public string? FailureReason { get; init; }
}
