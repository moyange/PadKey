using System.Runtime.InteropServices;

namespace PadKey;

internal static class PowerControl
{
    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool LookupPrivilegeValue(string? system, string name, out LUID luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll,
        ref TOKEN_PRIVILEGES newState, int bufferLength, IntPtr previousState, IntPtr returnLength);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID { public uint LowPart; public int HighPart; }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_PRIVILEGES
    {
        public uint PrivilegeCount;
        public LUID Luid;
        public uint Attributes;
    }

    public static bool Sleep()
    {
        EnableShutdownPrivilege();
        bool ok = SetSuspendState(false, false, false);
        Log.Info(ok ? "电脑进入睡眠" : "SetSuspendState 失败: " + Marshal.GetLastWin32Error());
        return ok;
    }

    private static void EnableShutdownPrivilege()
    {
        IntPtr token = IntPtr.Zero;
        try
        {
            if (!OpenProcessToken(GetCurrentProcess(), 0x0020 | 0x0008, out token)) return;
            if (!LookupPrivilegeValue(null, "SeShutdownPrivilege", out LUID luid)) return;
            var tp = new TOKEN_PRIVILEGES { PrivilegeCount = 1, Luid = luid, Attributes = 0x00000002 };
            AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
        }
        catch { }
        finally { if (token != IntPtr.Zero) CloseHandle(token); }
    }
}
