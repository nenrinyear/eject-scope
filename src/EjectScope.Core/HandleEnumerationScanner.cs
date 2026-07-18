using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
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

    public IReadOnlyList<ProcessUsage> Scan(string driveRoot, IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
    {
        progress?.Report(new ScanProgress("システムのハンドル一覧を取得中", 0, null));
        using var buffer = QueryHandles(cancellationToken);
        var count = Marshal.ReadIntPtr(buffer.Pointer).ToInt64();
        progress?.Report(new ScanProgress("ファイルハンドルを確認中", 0, count));
        var offset = IntPtr.Size * 2;
        var size = Marshal.SizeOf<SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX>();
        var pathsByProcess = new Dictionary<int, HashSet<string>>();

        for (long index = 0; index < count; index++)
        {
            if ((index & 1023) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new ScanProgress("ファイルハンドルを確認中", index, count));
            }
            var entryPointer = IntPtr.Add(buffer.Pointer, checked((int)(offset + index * size)));
            var entry = Marshal.PtrToStructure<SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX>(entryPointer);
            var pid64 = entry.UniqueProcessId.ToInt64();
            if (pid64 <= 4 || pid64 > int.MaxValue) continue;

            var path = TryGetFilePath((int)pid64, entry.HandleValue);
            if (path is null || !IsOnDrive(path, driveRoot)) continue;
            if (!pathsByProcess.TryGetValue((int)pid64, out var paths)) pathsByProcess[(int)pid64] = paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            paths.Add(path);
        }

        progress?.Report(new ScanProgress("検出結果を整理中", count, count));
        return pathsByProcess.Select(pair => ToUsage(pair.Key, pair.Value.Order().ToArray()))
            .Where(usage => usage is not null)
            .Cast<ProcessUsage>()
            .OrderBy(usage => usage.Risk)
            .ThenBy(usage => usage.ProcessName)
            .ToArray();
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

    private static string? TryGetFilePath(int pid, IntPtr sourceHandle)
    {
        var process = Native.OpenProcess(ProcessDupHandle | ProcessQueryLimitedInformation, false, pid);
        if (process == IntPtr.Zero) return null;
        try
        {
            if (!Native.DuplicateHandle(process, sourceHandle, Native.GetCurrentProcess(), out var duplicate, 0, false, DuplicateSameAccess)) return null;
            try
            {
                if (Native.GetFileType(duplicate) != FileTypeDisk) return null;
                var path = new char[32_768];
                var length = Native.GetFinalPathNameByHandle(duplicate, path, (uint)path.Length, 0);
                return length is 0 or >= 32_768 ? null : new string(path, 0, (int)length);
            }
            finally { Native.CloseHandle(duplicate); }
        }
        finally { Native.CloseHandle(process); }
    }

    private static bool IsOnDrive(string path, string driveRoot)
    {
        var normalizedPath = path.StartsWith("\\\\?\\", StringComparison.Ordinal) ? path[4..] : path;
        return normalizedPath.StartsWith(driveRoot, StringComparison.OrdinalIgnoreCase);
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

    private static class Native
    {
        [DllImport("ntdll.dll")] internal static extern int NtQuerySystemInformation(int informationClass, IntPtr information, int informationLength, out int returnLength);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool DuplicateHandle(IntPtr sourceProcess, IntPtr sourceHandle, IntPtr targetProcess, out IntPtr targetHandle, uint desiredAccess, bool inheritHandle, uint options);
        [DllImport("kernel32.dll")] internal static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern uint GetFileType(IntPtr handle);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern uint GetFinalPathNameByHandle(IntPtr handle, [Out] char[] path, uint length, uint flags);
    }
}
