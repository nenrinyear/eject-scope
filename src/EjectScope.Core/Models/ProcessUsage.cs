namespace EjectScope.Core.Models;

public sealed record ProcessUsage(
    string ProcessName,
    int ProcessId,
    string? ExecutablePath,
    IReadOnlyList<string> LockedPaths,
    ProcessRisk Risk,
    string Recommendation,
    bool CanTerminate,
    bool IsConfirmedBlocker = true)
{
    public string DetectionStatus => IsConfirmedBlocker ? "使用中" : "パス応答なし（判定不能）";
}
