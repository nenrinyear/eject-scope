using System.Runtime.InteropServices;

namespace DiskRemovalUsage.Core;

internal static class NativeMethods
{
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    internal static extern int RmStartSession(out uint sessionHandle, int sessionFlags, string sessionKey);
    [DllImport("rstrtmgr.dll")]
    internal static extern int RmEndSession(uint sessionHandle);
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    internal static extern int RmRegisterResources(uint sessionHandle, uint fileCount, string[] fileNames, uint applicationCount, [In] RM_UNIQUE_PROCESS[]? applications, uint serviceCount, string[]? serviceNames);
    [DllImport("rstrtmgr.dll")]
    internal static extern int RmGetList(uint sessionHandle, out uint needed, ref uint count, [In, Out] RM_PROCESS_INFO[]? processInfo, uint rebootReasons, out uint rebootReason);

    [StructLayout(LayoutKind.Sequential)]
    internal struct RM_UNIQUE_PROCESS { internal int dwProcessId; internal System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct RM_PROCESS_INFO
    {
        internal RM_UNIQUE_PROCESS Process;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] internal string strAppName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] internal string strServiceShortName;
        internal uint ApplicationType;
        internal uint AppStatus;
        internal uint TSSessionId;
        [MarshalAs(UnmanagedType.Bool)] internal bool Restartable;
    }
}
