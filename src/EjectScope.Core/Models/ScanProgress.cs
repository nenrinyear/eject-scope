namespace EjectScope.Core.Models;

public sealed record ScanProgress(string Phase, long Completed, long? Total);
