using System.Runtime.InteropServices;
using System.Numerics;

namespace DiskRemovalUsage.App;

internal static class DeviceChange
{
    internal const int Message = 0x0219; // WM_DEVICECHANGE
    internal const int QueryRemoveFailed = 0x8002; // DBT_DEVICEQUERYREMOVEFAILED
    private const int DeviceTypeVolume = 0x00000002; // DBT_DEVTYP_VOLUME

    internal static bool TryGetDriveRoot(IntPtr lParam, out string driveRoot)
    {
        driveRoot = string.Empty;
        if (lParam == IntPtr.Zero) return false;
        var header = Marshal.PtrToStructure<DEV_BROADCAST_HDR>(lParam);
        if (header.DeviceType != DeviceTypeVolume) return false;
        var volume = Marshal.PtrToStructure<DEV_BROADCAST_VOLUME>(lParam);
        if (volume.UnitMask == 0) return false;

        var index = BitOperations.TrailingZeroCount(volume.UnitMask);
        driveRoot = $"{(char)('A' + index)}:\\";
        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DEV_BROADCAST_HDR
    {
        public int Size;
        public int DeviceType;
        public int Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DEV_BROADCAST_VOLUME
    {
        public int Size;
        public int DeviceType;
        public int Reserved;
        public uint UnitMask;
        public ushort Flags;
    }
}
