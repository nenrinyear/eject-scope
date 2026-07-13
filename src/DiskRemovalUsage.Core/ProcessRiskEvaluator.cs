using DiskRemovalUsage.Core.Models;

namespace DiskRemovalUsage.Core;

public static class ProcessRiskEvaluator
{
    private static readonly HashSet<string> SystemProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Registry", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "dwm"
    };

    private static readonly string[] CautionTerms = ["explorer", "code", "notepad", "word", "excel", "powerpnt", "photoshop", "backup", "sync", "onedrive", "dropbox", "googledrive"];

    public static (ProcessRisk Risk, string Recommendation, bool CanTerminate) Evaluate(string processName)
    {
        var normalized = Path.GetFileNameWithoutExtension(processName);
        if (SystemProcesses.Contains(normalized))
            return (ProcessRisk.NotRecommended, "システムプロセスのため終了できません。", false);

        if (CautionTerms.Any(term => normalized.Contains(term, StringComparison.OrdinalIgnoreCase)))
            return (ProcessRisk.Caution, "未保存データや同期処理を確認してから、アプリ側で閉じてください。", true);

        return (ProcessRisk.Safe, "内容を確認のうえ終了を検討できます。", true);
    }
}
