using System.Runtime.InteropServices;

namespace PadKey;

/// <summary>
/// Sends keyboard and mouse input through the Win32 SendInput API.
/// Scan codes are used for keyboard keys so the active Windows layout is respected.
/// </summary>
public static class InputInjector
{
    private const uint INPUT_MOUSE = 0;
    private const uint INPUT_KEYBOARD = 1;

    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_SCANCODE = 0x0008;

    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;
    private const uint MOUSEEVENTF_HWHEEL = 0x01000;

    private const int WHEEL_DELTA = 120;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, ref INPUT pInputs, int cbSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr OpenInputDesktop(uint dwFlags, bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll")]
    private static extern bool CloseDesktop(IntPtr hDesktop);

    private static readonly object Gate = new();
    private static readonly HashSet<int> DownKeys = new();
    private static bool _left, _right, _middle;
    private static double _accX, _accY;
    private static bool _warned;
    private static long _injectedEvents;

    public static bool DryRun { get; set; }

    public static long InjectedEvents => Interlocked.Read(ref _injectedEvents);

    /// <summary>False while the Windows secure desktop (lock/PIN screen) is active.</summary>
    public static bool IsInputDesktopAvailable()
    {
        IntPtr desktop = OpenInputDesktop(0, false, 0x0100); // DESKTOP_SWITCHDESKTOP
        if (desktop == IntPtr.Zero) return false;
        CloseDesktop(desktop);
        return true;
    }

    public static bool KeyDown(int hid, bool repeat = false)
    {
        if (!KeyMap.TryGet(hid, out KeyDef def)) return false;
        lock (Gate)
        {
            if (!repeat)
            {
                if (!DownKeys.Add(hid)) return false;
            }
            if (DryRun) return true;
            bool ok = def.VirtualKey != 0
                ? SendVirtualKey(def.VirtualKey, false)
                : SendScanCode(def.Scan, def.Extended, false);
            if (ok) Interlocked.Increment(ref _injectedEvents);
            return ok;
        }
    }

    public static bool KeyUp(int hid)
    {
        if (!KeyMap.TryGet(hid, out KeyDef def)) return false;
        lock (Gate)
        {
            DownKeys.Remove(hid);
            if (DryRun) return true;
            bool ok = def.VirtualKey != 0
                ? SendVirtualKey(def.VirtualKey, true)
                : SendScanCode(def.Scan, def.Extended, true);
            if (ok) Interlocked.Increment(ref _injectedEvents);
            return ok;
        }
    }

    public static void MouseMove(double dx, double dy)
    {
        if (!double.IsFinite(dx) || !double.IsFinite(dy)) return;
        if (DryRun) return;
        lock (Gate)
        {
            _accX += dx;
            _accY += dy;
            int ix = (int)Math.Truncate(_accX);
            int iy = (int)Math.Truncate(_accY);
            _accX -= ix;
            _accY -= iy;

            while (ix != 0 || iy != 0)
            {
                int sx = Math.Clamp(ix, -600, 600);
                int sy = Math.Clamp(iy, -600, 600);
                SendMouseMove(sx, sy);
                ix -= sx;
                iy -= sy;
            }
        }
    }

    public static void MouseButton(string button, bool down)
    {
        uint flag = (button.ToLowerInvariant(), down) switch
        {
            ("left", true) => MOUSEEVENTF_LEFTDOWN,
            ("left", false) => MOUSEEVENTF_LEFTUP,
            ("right", true) => MOUSEEVENTF_RIGHTDOWN,
            ("right", false) => MOUSEEVENTF_RIGHTUP,
            ("middle", true) => MOUSEEVENTF_MIDDLEDOWN,
            ("middle", false) => MOUSEEVENTF_MIDDLEUP,
            _ => 0
        };
        if (flag == 0) return;
        if (DryRun) return;

        lock (Gate)
        {
            switch (button.ToLowerInvariant())
            {
                case "left": _left = down; break;
                case "right": _right = down; break;
                case "middle": _middle = down; break;
            }
            SendMouseFlag(flag, 0);
        }
    }

    public static void Scroll(int dxNotches, int dyNotches)
    {
        if (dxNotches == 0 && dyNotches == 0) return;
        if (DryRun) return;
        lock (Gate)
        {
            if (dyNotches != 0)
                SendMouseFlag(MOUSEEVENTF_WHEEL, unchecked((uint)(dyNotches * WHEEL_DELTA)));
            if (dxNotches != 0)
                SendMouseFlag(MOUSEEVENTF_HWHEEL, unchecked((uint)(dxNotches * WHEEL_DELTA)));
        }
    }

    public static void ReleaseAll()
    {
        if (DryRun)
        {
            DownKeys.Clear();
            _left = _right = _middle = false;
            return;
        }
        lock (Gate)
        {
            foreach (int hid in DownKeys.ToArray())
            {
                if (KeyMap.TryGet(hid, out KeyDef def))
                {
                    if (def.VirtualKey != 0) SendVirtualKey(def.VirtualKey, true);
                    else SendScanCode(def.Scan, def.Extended, true);
                }
            }
            DownKeys.Clear();

            if (_left) { SendMouseFlag(MOUSEEVENTF_LEFTUP, 0); _left = false; }
            if (_right) { SendMouseFlag(MOUSEEVENTF_RIGHTUP, 0); _right = false; }
            if (_middle) { SendMouseFlag(MOUSEEVENTF_MIDDLEUP, 0); _middle = false; }
        }
    }

    private static bool SendScanCode(ushort scan, bool extended, bool up)
    {
        uint flags = KEYEVENTF_SCANCODE;
        if (extended) flags |= KEYEVENTF_EXTENDEDKEY;
        if (up) flags |= KEYEVENTF_KEYUP;

        var input = new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT { wVk = 0, wScan = scan, dwFlags = flags, time = 0, dwExtraInfo = UIntPtr.Zero }
            }
        };
        return Send(ref input);
    }

    private static bool SendVirtualKey(ushort vk, bool up)
    {
        uint flags = up ? KEYEVENTF_KEYUP : 0;
        var input = new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT { wVk = vk, wScan = 0, dwFlags = flags, time = 0, dwExtraInfo = UIntPtr.Zero }
            }
        };
        return Send(ref input);
    }

    private static void SendMouseMove(int dx, int dy)
    {
        var input = new INPUT
        {
            type = INPUT_MOUSE,
            U = new InputUnion
            {
                mi = new MOUSEINPUT { dx = dx, dy = dy, mouseData = 0, dwFlags = MOUSEEVENTF_MOVE, time = 0, dwExtraInfo = UIntPtr.Zero }
            }
        };
        Send(ref input);
    }

    private static void SendMouseFlag(uint flag, uint data)
    {
        var input = new INPUT
        {
            type = INPUT_MOUSE,
            U = new InputUnion
            {
                mi = new MOUSEINPUT { dx = 0, dy = 0, mouseData = data, dwFlags = flag, time = 0, dwExtraInfo = UIntPtr.Zero }
            }
        };
        Send(ref input);
    }

    private static bool Send(ref INPUT input)
    {
        uint sent = SendInput(1, ref input, Marshal.SizeOf<INPUT>());
        if (sent == 1)
        {
            Interlocked.Increment(ref _injectedEvents);
            return true;
        }

        int err = Marshal.GetLastWin32Error();
        if (!_warned)
        {
            _warned = true;
            Log.Warn("SendInput 失败 (Win32 " + err + ")。若目标程序以管理员运行，请也用管理员启动服务端。");
        }
        return false;
    }
}
