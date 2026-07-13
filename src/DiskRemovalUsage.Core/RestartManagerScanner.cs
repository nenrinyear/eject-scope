using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using DiskRemovalUsage.Core.Models;

namespace DiskRemovalUsage.Core;

/// <summary>Restart Manager を使い、指定ドライブを使用中として登録されているプロセスを取得します。</summary>
public sealed class RestartManagerScanner
{
    private const int ErrorMoreData = 234;

    public Task<ScanResult> ScanAsync(string drive, CancellationToken cancellationToken = default) =>
        Task.Run(() => Scan(drive, cancellationToken), cancellationToken);

    public ScanResult Scan(string drive, CancellationToken cancellationToken = default)
    {
        var root = DriveValidator.NormalizeRemovableDrive(drive);
        cancellationToken.ThrowIfCancellationRequested();
        var sessionKey = Guid.NewGuid().ToString("N");
        var startError = NativeMethods.RmStartSession(out var handle, 0, sessionKey);
        if (startError != 0) throw new Win32Exception(startError, "Restart Manager セッションを開始できませんでした。");

        try
        {
            var resources = new[] { root };
            var registerError = NativeMethods.RmRegisterResources(handle, (uint)resources.Length, resources, 0, null, 0, null);
            if (registerError != 0) throw new Win32Exception(registerError, "対象ドライブを Restart Manager に登録できませんでした。");

            uint needed = 0, count = 0;
            var listError = NativeMethods.RmGetList(handle, out needed, ref count, null, 0, out _);
            if (listError != 0 && listError != ErrorMoreData)
                throw new Win32Exception(listError, "使用中プロセスを取得できませんでした。");

            var infos = new NativeMethods.RM_PROCESS_INFO[needed];
            count = needed;
            listError = NativeMethods.RmGetList(handle, out needed, ref count, infos, 0, out _);
            if (listError != 0) throw new Win32Exception(listError, "使用中プロセスを取得できませんでした。");

            var processes = infos.Take((int)count).Select(info => ToUsage(info, root)).OrderBy(x => x.Risk).ThenBy(x => x.ProcessName).ToArray();
            var elevated = IsElevated();
            var detail = elevated
                ? "Restart Manager の検出結果です。管理者向けのハンドル列挙補助は今後追加できます。"
                : "通常権限の Restart Manager 検出結果です。管理者権限では追加情報を取得できる場合があります。";
            return new ScanResult(root, processes, elevated, detail);
        }
        finally
        {
            NativeMethods.RmEndSession(handle);
        }
    }

    private static ProcessUsage ToUsage(NativeMethods.RM_PROCESS_INFO info, string root)
    {
        var pid = info.Process.dwProcessId;
        var name = info.strAppName;
        string? executablePath = null;
        try
        {
            using var process = Process.GetProcessById(pid);
            name = process.ProcessName;
            executablePath = process.MainModule?.FileName;
        }
        catch (ArgumentException) { }
        catch (Win32Exception) { }
        catch (InvalidOperationException) { }

        var assessment = ProcessRiskEvaluator.Evaluate(name);
        // Restart Manager returns process ownership but no per-handle path. The registered scope is shown honestly.
        return new ProcessUsage(name, pid, executablePath, [root], assessment.Risk, assessment.Recommendation, assessment.CanTerminate);
    }

    private static bool IsElevated()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        return new System.Security.Principal.WindowsPrincipal(identity)
            .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }
}
