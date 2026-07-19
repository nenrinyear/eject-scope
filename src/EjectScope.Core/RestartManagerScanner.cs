using EjectScope.Core.Models;

namespace EjectScope.Core;

/// <summary>指定ドライブを使用中のプロセスを取得します。</summary>
public sealed class RestartManagerScanner
{
    private readonly string _resolverExecutablePath;

    public RestartManagerScanner(string? resolverExecutablePath = null)
    {
        _resolverExecutablePath = resolverExecutablePath ?? Environment.ProcessPath
            ?? throw new InvalidOperationException("ハンドル解決ヘルパーの実行ファイルを特定できません。");
    }

    public Task<ScanResult> ScanAsync(string drive, CancellationToken cancellationToken = default) =>
        ScanAsync(drive, progress: null, cancellationToken);

    public Task<ScanResult> ScanAsync(string drive, IProgress<ScanProgress>? progress, CancellationToken cancellationToken = default) =>
        Task.Run(() => Scan(drive, progress, cancellationToken), cancellationToken);

    public ScanResult Scan(string drive, IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var root = DriveValidator.NormalizeRemovableDrive(drive);
        cancellationToken.ThrowIfCancellationRequested();
        var elevated = IsElevated();
        if (!elevated)
            return new ScanResult(root, [], false, "Restart Manager はドライブルートなどのディレクトリを直接検査できません。［管理者として再起動］後に、ファイルハンドルの列挙でスキャンしてください。");

        var handleScan = new HandleEnumerationScanner().Scan(root, _resolverExecutablePath, progress, cancellationToken);
        var timeoutDetail = handleScan.TimedOutHandles == 0
            ? string.Empty
            : $" 応答しないハンドル {handleScan.TimedOutHandles} 件をスキップしました（除外プロセス {handleScan.SkippedProcesses} 件）。";
        return new ScanResult(root, handleScan.Processes, true,
            handleScan.Processes.Count == 0
                ? $"管理者権限でファイルハンドルを調べましたが、対象ドライブを使用中のプロセスは検出されませんでした。{timeoutDetail}"
                : $"管理者権限でファイルハンドルを調べた結果です。表示されたパスを閉じてから、安全な取り外しを再試行してください。{timeoutDetail}");

    }

    private static bool IsElevated()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        return new System.Security.Principal.WindowsPrincipal(identity)
            .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }
}
