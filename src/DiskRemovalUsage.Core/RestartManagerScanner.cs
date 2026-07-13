using DiskRemovalUsage.Core.Models;

namespace DiskRemovalUsage.Core;

/// <summary>指定ドライブを使用中のプロセスを取得します。</summary>
public sealed class RestartManagerScanner
{
    public Task<ScanResult> ScanAsync(string drive, CancellationToken cancellationToken = default) =>
        Task.Run(() => Scan(drive, cancellationToken), cancellationToken);

    public ScanResult Scan(string drive, CancellationToken cancellationToken = default)
    {
        var root = DriveValidator.NormalizeRemovableDrive(drive);
        cancellationToken.ThrowIfCancellationRequested();
        var elevated = IsElevated();
        if (!elevated)
            return new ScanResult(root, [], false, "Restart Manager はドライブルートなどのディレクトリを直接検査できません。［管理者として再起動］後に、ファイルハンドルの列挙でスキャンしてください。");

        var handleProcesses = new HandleEnumerationScanner().Scan(root, cancellationToken);
        return new ScanResult(root, handleProcesses, true,
            handleProcesses.Count == 0
                ? "管理者権限でファイルハンドルを調べましたが、対象ドライブを使用中のプロセスは検出されませんでした。"
                : "管理者権限でファイルハンドルを調べた結果です。表示されたパスを閉じてから、安全な取り外しを再試行してください。");

    }

    private static bool IsElevated()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        return new System.Security.Principal.WindowsPrincipal(identity)
            .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }
}
