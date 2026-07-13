namespace DiskRemovalUsage.Core.Models;

public sealed record ScanResult(
    string DriveRoot,
    IReadOnlyList<ProcessUsage> Processes,
    bool IsElevated,
    string Detail);
