namespace DiskRemovalUsage.Core.Models;

public sealed record ProcessUsage(
    string ProcessName,
    int ProcessId,
    string? ExecutablePath,
    IReadOnlyList<string> LockedPaths,
    ProcessRisk Risk,
    string Recommendation,
    bool CanTerminate);
