using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using EjectScope.Core.Models;

namespace EjectScope.Core;

/// <summary>管理者権限でシステムのファイルハンドルを調べ、指定ドライブを使用中のプロセスを返します。</summary>
internal sealed class HandleEnumerationScanner
{
    private const int SystemExtendedHandleInformation = 64;
    private const int StatusInfoLengthMismatch = unchecked((int)0xC0000004);
    private const uint ProcessDupHandle = 0x0040;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint DuplicateSameAccess = 0x00000002;
    private const uint FileTypeDisk = 1;

    public HandleScanResult Scan(string driveRoot, string resolverExecutablePath, IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
    {
        progress?.Report(new ScanProgress("システムのハンドル一覧を取得中", 0, null));
        using var buffer = QueryHandles(cancellationToken);
        using var resolver = new HandlePathResolverClient(resolverExecutablePath);
        var count = Marshal.ReadIntPtr(buffer.Pointer).ToInt64();
        progress?.Report(new ScanProgress("ファイルハンドルを確認中", 0, count));
        var offset = IntPtr.Size * 2;
        var size = Marshal.SizeOf<SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX>();
        var pathsByProcess = new Dictionary<int, HashSet<string>>();
        var processHandles = new Dictionary<int, IntPtr>();
        var timedOutHandlesByProcess = new Dictionary<int, int>();
        var skippedProcesses = new HashSet<int>();
        var targetDevicePath = GetTargetDevicePath(driveRoot);
        var timedOutHandles = 0;

        try
        {
            for (long index = 0; index < count; index++)
            {
                if ((index & 127) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    progress?.Report(new ScanProgress("ファイルハンドルを確認中", index, count, timedOutHandles));
                }
                var entryPointer = IntPtr.Add(buffer.Pointer, checked((int)(offset + index * size)));
                var entry = Marshal.PtrToStructure<SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX>(entryPointer);
                var pid64 = entry.UniqueProcessId.ToInt64();
                if (pid64 <= 4 || pid64 > int.MaxValue) continue;
                var pid = (int)pid64;
                if (skippedProcesses.Contains(pid)) continue;

                var localHandle = TryDuplicateDiskHandle(pid, entry.HandleValue, processHandles);
                if (localHandle == IntPtr.Zero) continue;
                try
                {
                    var resolution = resolver.Resolve(localHandle, cancellationToken);
                    if (resolution.TimedOut)
                    {
                        timedOutHandles++;
                        var processTimeouts = timedOutHandlesByProcess.GetValueOrDefault(pid) + 1;
                        timedOutHandlesByProcess[pid] = processTimeouts;
                        if (processTimeouts >= 3) skippedProcesses.Add(pid);
                        continue;
                    }
                    var path = ToDrivePath(resolution.Path, targetDevicePath, driveRoot);
                    if (path is null) continue;
                    if (!pathsByProcess.TryGetValue(pid, out var paths)) pathsByProcess[pid] = paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    paths.Add(path);
                }
                finally { Native.CloseHandle(localHandle); }
            }
        }
        finally
        {
            foreach (var processHandle in processHandles.Values.Where(handle => handle != IntPtr.Zero)) Native.CloseHandle(processHandle);
        }

        progress?.Report(new ScanProgress("検出結果を整理中", count, count, timedOutHandles));
        var processes = pathsByProcess.Select(pair => ToUsage(pair.Key, pair.Value.Order().ToArray()))
            .Where(usage => usage is not null)
            .Cast<ProcessUsage>()
            .OrderBy(usage => usage.Risk)
            .ThenBy(usage => usage.ProcessName)
            .ToArray();
        return new HandleScanResult(processes, timedOutHandles, skippedProcesses.Count);
    }

    private static ProcessUsage? ToUsage(int pid, IReadOnlyList<string> paths)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            var assessment = ProcessRiskEvaluator.Evaluate(process.ProcessName);
            string? executable = null;
            try { executable = process.MainModule?.FileName; } catch (Win32Exception) { }
            return new ProcessUsage(process.ProcessName, pid, executable, paths, assessment.Risk, assessment.Recommendation, assessment.CanTerminate);
        }
        catch (ArgumentException) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    private static IntPtr TryDuplicateDiskHandle(int pid, IntPtr sourceHandle, Dictionary<int, IntPtr> processHandles)
    {
        if (!processHandles.TryGetValue(pid, out var process))
        {
            process = Native.OpenProcess(ProcessDupHandle | ProcessQueryLimitedInformation, false, pid);
            processHandles[pid] = process;
        }
        if (process == IntPtr.Zero) return IntPtr.Zero;
        if (!Native.DuplicateHandle(process, sourceHandle, Native.GetCurrentProcess(), out var duplicate, 0, false, DuplicateSameAccess)) return IntPtr.Zero;
        if (Native.GetFileType(duplicate) == FileTypeDisk) return duplicate;
        Native.CloseHandle(duplicate);
        return IntPtr.Zero;
    }

    private static string GetTargetDevicePath(string driveRoot)
    {
        var target = driveRoot.TrimEnd('\\');
        var buffer = new StringBuilder(32_768);
        if (Native.QueryDosDevice(target, buffer, buffer.Capacity) == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "対象ドライブのデバイスパスを取得できませんでした。");
        return buffer.ToString().TrimEnd('\\');
    }

    private static string? ToDrivePath(string? ntPath, string targetDevicePath, string driveRoot)
    {
        if (ntPath is null || !ntPath.StartsWith(targetDevicePath, StringComparison.OrdinalIgnoreCase)) return null;
        if (ntPath.Length > targetDevicePath.Length && ntPath[targetDevicePath.Length] != '\\') return null;
        return driveRoot + ntPath[targetDevicePath.Length..].TrimStart('\\');
    }

    private static SafeHGlobalBuffer QueryHandles(CancellationToken cancellationToken)
    {
        var length = 1 << 20;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var buffer = new SafeHGlobalBuffer(length);
            var status = Native.NtQuerySystemInformation(SystemExtendedHandleInformation, buffer.Pointer, length, out var needed);
            if (status == 0) return buffer;
            buffer.Dispose();
            if (status != StatusInfoLengthMismatch) throw new Win32Exception(status, "システムハンドルを列挙できませんでした。");
            length = Math.Max(length * 2, needed + 4096);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX
    {
        public IntPtr Object;
        public IntPtr UniqueProcessId;
        public IntPtr HandleValue;
        public uint GrantedAccess;
        public ushort CreatorBackTraceIndex;
        public ushort ObjectTypeIndex;
        public uint HandleAttributes;
        public uint Reserved;
    }

    private sealed class SafeHGlobalBuffer(int length) : IDisposable
    {
        public IntPtr Pointer { get; } = Marshal.AllocHGlobal(length);
        public void Dispose() { if (Pointer != IntPtr.Zero) Marshal.FreeHGlobal(Pointer); }
    }

    internal sealed record HandleScanResult(IReadOnlyList<ProcessUsage> Processes, int TimedOutHandles, int SkippedProcesses);

    private static class Native
    {
        [DllImport("ntdll.dll")] internal static extern int NtQuerySystemInformation(int informationClass, IntPtr information, int informationLength, out int returnLength);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool DuplicateHandle(IntPtr sourceProcess, IntPtr sourceHandle, IntPtr targetProcess, out IntPtr targetHandle, uint desiredAccess, bool inheritHandle, uint options);
        [DllImport("kernel32.dll")] internal static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern uint GetFileType(IntPtr handle);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern uint QueryDosDevice(string deviceName, StringBuilder targetPath, int maxLength);
    }
}
