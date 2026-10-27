using System.Runtime.InteropServices;
using System.Text;

namespace PadKey;

/// <summary>
/// Small Win32 notification-area icon with a menu for showing the console,
/// minimizing to tray, toggling auto-start, and exiting.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private const int WM_APP = 0x8000;
    private const int WM_TRAY = WM_APP + 1;
    private const int WM_NULL = 0;
    private const int WM_COMMAND = 0x0111;
    private const int WM_CLOSE = 0x0010;
    private const int WM_DESTROY = 0x0002;
    private const int WM_CONTEXTMENU = 0x007B;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_LBUTTONDBLCLK = 0x0203;

    private const uint NIM_ADD = 0;
    private const uint NIM_MODIFY = 1;
    private const uint NIM_DELETE = 2;
    private const uint NIF_MESSAGE = 0x01;
    private const uint NIF_ICON = 0x02;
    private const uint NIF_TIP = 0x04;

    private const uint MF_STRING = 0x0000;
    private const uint MF_SEPARATOR = 0x0800;
    private const uint MF_CHECKED = 0x0008;
    private const uint MF_UNCHECKED = 0x0000;
    private const uint TPM_RETURNCMD = 0x0100;
    private const uint TPM_RIGHTBUTTON = 0x0002;

    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;
    private const int SW_MINIMIZE = 6;
    private const int SW_RESTORE = 9;
    private static readonly IntPtr IDI_APPLICATION = new(32512);

    private const int ID_SHOW = 1001;
    private const int ID_MINIMIZE = 1002;
    private const int ID_AUTOSTART = 1003;
    private const int ID_EXIT = 1004;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(uint dwExStyle, string lpClassName, string lpWindowName,
        uint dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu,
        IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int nExitCode);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, IntPtr uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenu(IntPtr hMenu, uint uFlags, int x, int y,
        int nReserved, IntPtr hWnd, IntPtr prcRect);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    private readonly Action _exit;
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly object _gate = new();
    private WndProc? _wndProc;
    private Thread? _thread;
    private IntPtr _hwnd;
    private IntPtr _icon;
    private bool _ok;
    private string _tooltip = "PadKey Server";
    private string _className = "PadKeyTrayWnd";

    public TrayIcon(Action exit) => _exit = exit;

    public bool Start()
    {
        _className = "PadKeyTrayWnd_" + Environment.ProcessId;
        _thread = new Thread(Run);
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.IsBackground = true;
        _thread.Name = "PadKeyTray";
        _thread.Start();
        _ready.Wait(3000);
        return _ok;
    }

    private void Run()
    {
        try
        {
            _wndProc = WndProcImpl;
            var wc = new WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                style = 0,
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                hInstance = GetModuleHandle(null),
                lpszClassName = _className
            };
            if (RegisterClassEx(ref wc) == 0) return;

            _hwnd = CreateWindowEx(0, _className, "PadKey", 0, 0, 0, 0, 0,
                IntPtr.Zero, IntPtr.Zero, wc.hInstance, IntPtr.Zero);
            if (_hwnd == IntPtr.Zero) return;

            _icon = LoadIcon(IntPtr.Zero, IDI_APPLICATION);
            for (int i = 0; i < 6; i++)
            {
                var data = BuildData();
                if (Shell_NotifyIcon(NIM_ADD, ref data))
                {
                    _ok = true;
                    break;
                }
                Thread.Sleep(400);
            }
            if (!_ok) return;

            _ready.Set();
            while (GetMessage(out MSG msg, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
        }
        catch (Exception ex)
        {
            Log.Warn("托盘线程异常: " + ex);
        }
        finally
        {
            if (_hwnd != IntPtr.Zero)
            {
                var data = BuildData();
                Shell_NotifyIcon(NIM_DELETE, ref data);
                DestroyWindow(_hwnd);
                _hwnd = IntPtr.Zero;
            }
            _ready.Set();
        }
    }

    private NOTIFYICONDATA BuildData() => new()
    {
        cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
        hWnd = _hwnd,
        uID = 1,
        uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
        uCallbackMessage = WM_TRAY,
        hIcon = _icon,
        szTip = _tooltip.Length > 120 ? _tooltip[..120] : _tooltip,
        szInfo = "",
        szInfoTitle = ""
    };

    private IntPtr WndProcImpl(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_TRAY)
        {
            int evt = (int)((long)lParam & 0xFFFF);
            if (evt == WM_RBUTTONUP || evt == WM_CONTEXTMENU) ShowMenu();
            else if (evt == WM_LBUTTONDBLCLK) ShowConsole();
            return IntPtr.Zero;
        }

        if (msg == WM_COMMAND)
        {
            int id = (int)((long)wParam & 0xFFFF);
            HandleCommand(id);
            return IntPtr.Zero;
        }

        if (msg == WM_CLOSE)
        {
            DestroyWindow(hWnd);
            return IntPtr.Zero;
        }

        if (msg == WM_DESTROY)
        {
            PostQuitMessage(0);
            return IntPtr.Zero;
        }

        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void ShowMenu()
    {
        IntPtr menu = CreatePopupMenu();
        if (menu == IntPtr.Zero) return;
        try
        {
            AppendMenu(menu, MF_STRING, new IntPtr(ID_SHOW), "显示控制台");
            AppendMenu(menu, MF_STRING, new IntPtr(ID_MINIMIZE), "最小化到托盘");
            AppendMenu(menu, MF_SEPARATOR, IntPtr.Zero, null);
            AppendMenu(menu,
                MF_STRING | (AutoStart.IsEnabled() ? MF_CHECKED : MF_UNCHECKED),
                new IntPtr(ID_AUTOSTART), "开机自启");
            AppendMenu(menu, MF_SEPARATOR, IntPtr.Zero, null);
            AppendMenu(menu, MF_STRING, new IntPtr(ID_EXIT), "退出 PadKey Server");

            GetCursorPos(out POINT p);
            SetForegroundWindow(_hwnd);
            int cmd = TrackPopupMenu(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON,
                p.x, p.y, 0, _hwnd, IntPtr.Zero);
            PostMessage(_hwnd, WM_NULL, IntPtr.Zero, IntPtr.Zero);
            if (cmd != 0) HandleCommand(cmd);
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    private void HandleCommand(int id)
    {
        switch (id)
        {
            case ID_SHOW:
                ShowConsole();
                break;
            case ID_MINIMIZE:
                HideConsole();
                break;
            case ID_AUTOSTART:
                if (AutoStart.IsEnabled()) AutoStart.Disable();
                else AutoStart.Enable();
                SetStatus("PadKey Server · " + (AutoStart.IsEnabled() ? "开机自启已开启" : "开机自启已关闭"));
                break;
            case ID_EXIT:
                _exit();
                PostQuitMessage(0);
                break;
        }
    }

    public void ShowConsole()
    {
        IntPtr console = GetConsoleWindow();
        if (console == IntPtr.Zero) return;
        ShowWindow(console, SW_RESTORE);
        ShowWindow(console, SW_SHOW);
        SetForegroundWindow(console);
    }

    public void HideConsole()
    {
        IntPtr console = GetConsoleWindow();
        if (console != IntPtr.Zero) ShowWindow(console, SW_HIDE);
    }

    public void MinimizeConsole()
    {
        IntPtr console = GetConsoleWindow();
        if (console != IntPtr.Zero) ShowWindow(console, SW_MINIMIZE);
    }

    public bool IsConsoleVisible => GetConsoleWindow() != IntPtr.Zero && IsWindowVisible(GetConsoleWindow());

    public void SetStatus(string text)
    {
        lock (_gate)
        {
            _tooltip = text;
            if (_hwnd != IntPtr.Zero && _ok)
            {
                var data = BuildData();
                Shell_NotifyIcon(NIM_MODIFY, ref data);
            }
        }
    }

    public void Dispose()
    {
        if (_hwnd != IntPtr.Zero) PostMessage(_hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        try { _thread?.Join(1000); } catch { }
        _ready.Dispose();
    }
}
