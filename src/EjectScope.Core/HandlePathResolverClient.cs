using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace EjectScope.Core;

internal sealed class HandlePathResolverClient(string executablePath) : IDisposable
{
    private const uint ProcessDupHandle = 0x0040;
    private const uint DuplicateSameAccess = 0x00000002;
    private static readonly TimeSpan ResolutionTimeout = TimeSpan.FromMilliseconds(750);
    private Process? _worker;
    private IntPtr _workerProcessHandle;

    public PathResolutionResult Resolve(IntPtr localHandle, CancellationToken cancellationToken)
    {
        EnsureWorker();
        if (!Native.DuplicateHandle(Native.GetCurrentProcess(), localHandle, _workerProcessHandle, out var remoteHandle, 0, false, DuplicateSameAccess))
            return default;

        try
        {
            _worker!.StandardInput.WriteLine(remoteHandle.ToInt64().ToString(CultureInfo.InvariantCulture));
            _worker.StandardInput.Flush();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ResolutionTimeout);
            try
            {
                var response = _worker.StandardOutput.ReadLineAsync(timeout.Token).AsTask().GetAwaiter().GetResult();
                if (response is null) { StopWorker(); return default; }
                if (response == "-") return default;
                return new PathResolutionResult(Encoding.UTF8.GetString(Convert.FromBase64String(response)), false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                StopWorker();
                return new PathResolutionResult(null, true);
            }
        }
        catch (OperationCanceledException) { StopWorker(); throw; }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or FormatException)
        {
            StopWorker();
            return default;
        }
    }

    private void EnsureWorker()
    {
        if (_worker is { HasExited: false } && _workerProcessHandle != IntPtr.Zero) return;
        StopWorker();
        _worker = Process.Start(new ProcessStartInfo(executablePath, "--handle-path-resolver")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        }) ?? throw new InvalidOperationException("ハンドル解決ヘルパーを起動できませんでした。");
        _workerProcessHandle = Native.OpenProcess(ProcessDupHandle, false, _worker.Id);
        if (_workerProcessHandle == IntPtr.Zero)
        {
            StopWorker();
            throw new InvalidOperationException("ハンドル解決ヘルパーへ接続できませんでした。");
        }
    }

    private void StopWorker()
    {
        if (_workerProcessHandle != IntPtr.Zero)
        {
            Native.CloseHandle(_workerProcessHandle);
            _workerProcessHandle = IntPtr.Zero;
        }
        if (_worker is not null)
        {
            try { if (!_worker.HasExited) _worker.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            finally { _worker.Dispose(); _worker = null; }
        }
    }

    public void Dispose() => StopWorker();

    internal readonly record struct PathResolutionResult(string? Path, bool TimedOut);

    private static class Native
    {
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool DuplicateHandle(IntPtr sourceProcess, IntPtr sourceHandle, IntPtr targetProcess, out IntPtr targetHandle, uint desiredAccess, bool inheritHandle, uint options);
        [DllImport("kernel32.dll")] internal static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool CloseHandle(IntPtr handle);
    }
}
