using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace PadKey;

/// <summary>
/// LocalSystem service that starts PadKeyServer.exe --secure-helper on
/// winsta0\winlogon in the active console session.
/// </summary>
internal static class SecureServiceHost
{
    private const string ServiceName = "PadKeySecure";
    private const uint SERVICE_WIN32_OWN_PROCESS = 0x00000010;
    private const uint SERVICE_ACCEPT_STOP = 0x00000001;
    private const uint SERVICE_CONTROL_STOP = 0x00000001;
    private const uint SERVICE_CONTROL_SHUTDOWN = 0x00000005;
    private const uint SERVICE_CONTROL_INTERROGATE = 0x00000004;
    private const uint SERVICE_STOPPED = 1;
    private const uint SERVICE_START_PENDING = 2;
    private const uint SERVICE_STOP_PENDING = 3;
    private const uint SERVICE_RUNNING = 4;

    private static IntPtr _statusHandle;
    private static ManualResetEventSlim? _stop;
    private static CancellationTokenSource? _cts;
    private static Process? _helper;
    private static int _helperSession = -1;
    private static Process? _defaultHelper;
    private static int _defaultSession = -1;
    private static ServiceMainDelegate? _mainDelegate;
    private static HandlerDelegate? _handlerDelegate;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void ServiceMainDelegate(int argc, IntPtr argv);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint HandlerDelegate(uint control, uint eventType, IntPtr eventData, IntPtr context);

    [StructLayout(LayoutKind.Sequential)]
    private struct SERVICE_TABLE_ENTRY
    {
        public IntPtr lpServiceName;
        public IntPtr lpServiceProc;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SERVICE_STATUS
    {
        public uint dwServiceType;
        public uint dwCurrentState;
        public uint dwControlsAccepted;
        public uint dwWin32ExitCode;
        public uint dwServiceSpecificExitCode;
        public uint dwCheckPoint;
        public uint dwWaitHint;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID { public uint LowPart; public int HighPart; }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_PRIVILEGES
    {
        public uint PrivilegeCount;
        public LUID Luid;
        public uint Attributes;
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool StartServiceCtrlDispatcher(IntPtr serviceStartTable);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr RegisterServiceCtrlHandlerEx(string serviceName, HandlerDelegate handler, IntPtr context);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool SetServiceStatus(IntPtr handle, ref SERVICE_STATUS status);

    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DuplicateTokenEx(IntPtr token, uint access, IntPtr attributes,
        int impersonationLevel, int tokenType, out IntPtr newToken);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessAsUser(IntPtr token, string? applicationName, string commandLine,
        IntPtr processAttributes, IntPtr threadAttributes, bool inheritHandles, uint creationFlags,
        IntPtr environment, string? currentDirectory, ref STARTUPINFO startupInfo, out PROCESS_INFORMATION processInfo);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessWithTokenW(IntPtr token, uint logonFlags, string? applicationName,
        string commandLine, uint creationFlags, IntPtr environment, string? currentDirectory,
        ref STARTUPINFO startupInfo, out PROCESS_INFORMATION processInfo);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool CreateEnvironmentBlock(out IntPtr environment, IntPtr token, bool inherit);

    [DllImport("userenv.dll")]
    private static extern bool DestroyEnvironmentBlock(IntPtr environment);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool LookupPrivilegeValue(string? system, string name, out LUID luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll,
        ref TOKEN_PRIVILEGES newState, int bufferLength, IntPtr previousState, IntPtr returnLength);

    public static void Run()
    {
        _mainDelegate = ServiceMain;
        _handlerDelegate = Handler;
        IntPtr name = Marshal.StringToHGlobalUni(ServiceName);
        int size = Marshal.SizeOf<SERVICE_TABLE_ENTRY>();
        IntPtr table = Marshal.AllocHGlobal(size * 2);
        Marshal.StructureToPtr(new SERVICE_TABLE_ENTRY
        {
            lpServiceName = name,
            lpServiceProc = Marshal.GetFunctionPointerForDelegate(_mainDelegate)
        }, table, false);
        Marshal.StructureToPtr(new SERVICE_TABLE_ENTRY
        {
            lpServiceName = IntPtr.Zero,
            lpServiceProc = IntPtr.Zero
        }, IntPtr.Add(table, size), false);

        try
        {
            if (!StartServiceCtrlDispatcher(table))
                SecureLog.Write("StartServiceCtrlDispatcher failed: " + Marshal.GetLastWin32Error());
        }
        finally
        {
            Marshal.FreeHGlobal(table);
            Marshal.FreeHGlobal(name);
        }
    }

    private static void ServiceMain(int argc, IntPtr argv)
    {
        try
        {
            _statusHandle = RegisterServiceCtrlHandlerEx(ServiceName, _handlerDelegate!, IntPtr.Zero);
            if (_statusHandle == IntPtr.Zero)
            {
                SecureLog.Write("RegisterServiceCtrlHandlerEx failed: " + Marshal.GetLastWin32Error());
                return;
            }

            SetStatus(SERVICE_START_PENDING, 0);
            EnablePrivilege("SeAssignPrimaryTokenPrivilege");
            EnablePrivilege("SeIncreaseQuotaPrivilege");
            EnablePrivilege("SeDebugPrivilege");

            _stop = new ManualResetEventSlim(false);
            _cts = new CancellationTokenSource();
            var worker = new Thread(() => Loop(_cts.Token)) { IsBackground = true, Name = "PadKeySecureService" };
            worker.Start();
            SetStatus(SERVICE_RUNNING, SERVICE_ACCEPT_STOP);
            SecureLog.Write("service running");

            _stop.Wait();
            SetStatus(SERVICE_STOP_PENDING, 0);
            _cts.Cancel();
            worker.Join(5000);
            StopHelpers();
            SetStatus(SERVICE_STOPPED, 0);
            SecureLog.Write("service stopped");
        }
        catch (Exception ex)
        {
            SecureLog.Write("service main: " + ex);
            SetStatus(SERVICE_STOPPED, 0);
        }
    }

    private static uint Handler(uint control, uint eventType, IntPtr eventData, IntPtr context)
    {
        switch (control)
        {
            case SERVICE_CONTROL_STOP:
            case SERVICE_CONTROL_SHUTDOWN:
                _stop?.Set();
                break;
            case SERVICE_CONTROL_INTERROGATE:
                SetStatus(SERVICE_RUNNING, SERVICE_ACCEPT_STOP);
                break;
        }
        return 0;
    }

    private static void SetStatus(uint state, uint accepted)
    {
        if (_statusHandle == IntPtr.Zero) return;
        var status = new SERVICE_STATUS
        {
            dwServiceType = SERVICE_WIN32_OWN_PROCESS,
            dwCurrentState = state,
            dwControlsAccepted = accepted,
            dwCheckPoint = 0,
            dwWaitHint = 0
        };
        SetServiceStatus(_statusHandle, ref status);
    }

    private static void Loop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                uint session = WTSGetActiveConsoleSessionId();
                if (session != 0xFFFFFFFF)
                {
                    if (_helper is null || _helper.HasExited || _helperSession != (int)session)
                    {
                        if (_helper is { HasExited: false }) { try { _helper.Kill(); } catch { } }
                        _helper = LaunchHelper((int)session, SecureInputBridge.PipeName, @"winsta0\winlogon", false);
                        _helperSession = _helper is null ? -1 : (int)session;
                    }
                    if (_defaultHelper is null || _defaultHelper.HasExited || _defaultSession != (int)session)
                    {
                        if (_defaultHelper is { HasExited: false }) { try { _defaultHelper.Kill(); } catch { } }
                        _defaultHelper = LaunchHelper((int)session, SecureInputBridge.DefaultPipeName, @"winsta0\default", true);
                        _defaultSession = _defaultHelper is null ? -1 : (int)session;
                    }
                }
            }
            catch (Exception ex) { SecureLog.Write("service loop: " + ex.Message); }
            Thread.Sleep(3000);
        }
    }

    private static Process? LaunchHelper(int session, string pipeName, string desktop, bool relativeMouse)
    {
        int winlogonPid = 0;
        for (int attempt = 0; attempt < 20 && winlogonPid == 0; attempt++)
        {
            foreach (Process p in Process.GetProcessesByName("winlogon"))
            {
                if (p.SessionId == session) { winlogonPid = p.Id; break; }
            }
            if (winlogonPid == 0) Thread.Sleep(500);
        }
        if (winlogonPid == 0) { SecureLog.Write("winlogon not found"); return null; }

        IntPtr process = IntPtr.Zero, token = IntPtr.Zero, dup = IntPtr.Zero, environment = IntPtr.Zero;
        PROCESS_INFORMATION pi = default;
        try
        {
            process = OpenProcess(0x02000000, false, winlogonPid);
            if (process == IntPtr.Zero) { SecureLog.Write("OpenProcess winlogon: " + Marshal.GetLastWin32Error()); return null; }
            if (!OpenProcessToken(process, 0x000F01FF, out token)) { SecureLog.Write("OpenProcessToken: " + Marshal.GetLastWin32Error()); return null; }
            if (!DuplicateTokenEx(token, 0x02000000, IntPtr.Zero, 2, 1, out dup)) { SecureLog.Write("DuplicateTokenEx: " + Marshal.GetLastWin32Error()); return null; }

            var si = new STARTUPINFO
            {
                cb = Marshal.SizeOf<STARTUPINFO>(),
                lpDesktop = desktop,
                dwFlags = 1,
                wShowWindow = 0
            };
            if (CreateEnvironmentBlock(out environment, dup, false)) { }
            string exe = Environment.ProcessPath!;
            string command = "\"" + exe + "\" --secure-helper " + pipeName + " " + desktop + (relativeMouse ? " 1" : " 0");
            uint flags = 0x08000000 | (environment != IntPtr.Zero ? 0x00000400u : 0u);
            bool ok = CreateProcessAsUser(dup, null, command, IntPtr.Zero, IntPtr.Zero, false, flags,
                environment, Path.GetDirectoryName(exe), ref si, out pi);
            if (!ok)
            {
                int err = Marshal.GetLastWin32Error();
                SecureLog.Write("CreateProcessAsUser failed: " + err + "; trying CreateProcessWithTokenW");
                ok = CreateProcessWithTokenW(dup, 0, null, command, flags, environment, Path.GetDirectoryName(exe), ref si, out pi);
                if (!ok) SecureLog.Write("CreateProcessWithTokenW failed: " + Marshal.GetLastWin32Error());
            }
            if (!ok) return null;

            CloseHandle(pi.hThread);
            CloseHandle(pi.hProcess);
            pi.hThread = pi.hProcess = IntPtr.Zero;
            SecureLog.Write("secure helper launched pid=" + pi.dwProcessId + " session=" + session + " pipe=" + pipeName);
            return Process.GetProcessById(pi.dwProcessId);
        }
        catch (Exception ex) { SecureLog.Write("launch helper: " + ex); return null; }
        finally
        {
            if (pi.hThread != IntPtr.Zero) CloseHandle(pi.hThread);
            if (pi.hProcess != IntPtr.Zero) CloseHandle(pi.hProcess);
            if (environment != IntPtr.Zero) DestroyEnvironmentBlock(environment);
            if (dup != IntPtr.Zero) CloseHandle(dup);
            if (token != IntPtr.Zero) CloseHandle(token);
            if (process != IntPtr.Zero) CloseHandle(process);
        }
    }

    private static void StopHelpers()
    {
        try { if (_helper is { HasExited: false }) _helper.Kill(); } catch { }
        try { if (_defaultHelper is { HasExited: false }) _defaultHelper.Kill(); } catch { }
        _helper = null;
        _defaultHelper = null;
        _helperSession = -1;
        _defaultSession = -1;
    }

    private static void EnablePrivilege(string name)
    {
        IntPtr token = IntPtr.Zero;
        try
        {
            if (!OpenProcessToken(GetCurrentProcess(), 0x0020 | 0x0008, out token)) return;
            if (!LookupPrivilegeValue(null, name, out LUID luid)) return;
            var tp = new TOKEN_PRIVILEGES { PrivilegeCount = 1, Luid = luid, Attributes = 0x00000002 };
            AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
        }
        catch { }
        finally { if (token != IntPtr.Zero) CloseHandle(token); }
    }
}
