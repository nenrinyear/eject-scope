using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace EjectScope.Core;

/// <summary>Runs the isolated handle-path resolver protocol used by the desktop app.</summary>
public static class HandlePathResolverHost
{
    private const uint FileNameOpened = 0x8;
    private const uint VolumeNameNt = 0x2;

    public static void Run(TextReader? input = null, TextWriter? output = null)
    {
        input ??= Console.In;
        output ??= Console.Out;
        string? request;
        while ((request = input.ReadLine()) is not null)
        {
            if (!long.TryParse(request, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                WriteResponse(output, null);
                continue;
            }

            var handle = new IntPtr(value);
            try { WriteResponse(output, Resolve(handle)); }
            finally { Native.CloseHandle(handle); }
        }
    }

    private static string? Resolve(IntPtr handle)
    {
        var path = new char[32_768];
        var length = Native.GetFinalPathNameByHandle(handle, path, (uint)path.Length, FileNameOpened | VolumeNameNt);
        return length is 0 or >= 32_768 ? null : new string(path, 0, (int)length);
    }

    private static void WriteResponse(TextWriter output, string? path)
    {
        output.WriteLine(path is null ? "-" : Convert.ToBase64String(Encoding.UTF8.GetBytes(path)));
        output.Flush();
    }

    private static class Native
    {
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern uint GetFinalPathNameByHandle(IntPtr handle, [Out] char[] path, uint length, uint flags);
    }
}
