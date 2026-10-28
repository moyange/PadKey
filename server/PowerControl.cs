using System.Runtime.InteropServices;

namespace PadKey;

internal static class PowerControl
{
    private static readonly object FakeGate = new();
    private static CancellationTokenSource? _fakeCts;
    private static Thread? _fakeThread;
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

    /// <summary>Fake sleep: keep Windows running, but turn the monitor off.</summary>
    public static void FakeSleep()
    {
        lock (FakeGate)
        {
            if (_fakeThread is null)
            {
                _fakeCts = new CancellationTokenSource();
                CancellationToken token = _fakeCts.Token;
                _fakeThread = new Thread(() =>
                {
                    DisplayPower.HoldSystemAwake();
                    try
                    {
                        while (!token.IsCancellationRequested) Thread.Sleep(500);
                    }
                    finally
                    {
                        DisplayPower.ReleaseSystemAwake();
                    }
                })
                {
                    IsBackground = true,
                    Name = "PadKeyFakeSleep"
                };
                _fakeThread.Start();
            }
        }
        DisplayPower.MonitorOff();
        Log.Info("假休眠：保持系统运行，仅关闭显示器");
    }

    public static void WakeFromFakeSleep()
    {
        lock (FakeGate)
        {
            _fakeCts?.Cancel();
            _fakeCts = null;
            _fakeThread = null;
        }
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
