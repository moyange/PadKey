using System.Runtime.InteropServices;

namespace PadKey;

internal static class WorkstationState
{
    private const int WTSSessionInfoEx = 25;
    private const uint WTS_CURRENT_SESSION = 0xFFFFFFFF;

    [DllImport("wtsapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool WTSQuerySessionInformation(IntPtr server, uint sessionId, int infoClass,
        out IntPtr buffer, out uint bytesReturned);

    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr memory);

    /// <summary>SessionFlags in WTSINFOEX_LEVEL1: 0 = locked, 1 = unlocked.</summary>
    public static bool IsLocked()
    {
        IntPtr buffer = IntPtr.Zero;
        try
        {
            if (!WTSQuerySessionInformation(IntPtr.Zero, WTS_CURRENT_SESSION, WTSSessionInfoEx, out buffer, out uint bytes)
                || buffer == IntPtr.Zero || bytes < 16)
                return false;
            int level = Marshal.ReadInt32(buffer, 0);
            int flags = Marshal.ReadInt32(buffer, 16);
            return level == 1 && flags == 0;
        }
        catch { return false; }
        finally
        {
            if (buffer != IntPtr.Zero) WTSFreeMemory(buffer);
        }
    }
}
