using System.Runtime.InteropServices;

namespace PadKey;

internal static class DisplayPower
{
    private const uint ES_CONTINUOUS = 0x80000000;
    private const uint ES_SYSTEM_REQUIRED = 0x00000001;
    private const uint ES_DISPLAY_REQUIRED = 0x00000002;

    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private static readonly IntPtr HWND_BROADCAST = new(0xFFFF);
    private const uint WM_SYSCOMMAND = 0x0112;
    private static readonly IntPtr SC_MONITORPOWER = new(0xF170);

    /// <summary>Nudges Windows to turn the display back on after resume or lock.</summary>
    public static void Wake()
    {
        try
        {
            SetThreadExecutionState(ES_CONTINUOUS | ES_DISPLAY_REQUIRED | ES_SYSTEM_REQUIRED);
            // -1 = turn the monitor back on; 2 = off, 1 = low power.
            PostMessage(HWND_BROADCAST, WM_SYSCOMMAND, SC_MONITORPOWER, new IntPtr(-1));
            Thread.Sleep(40);
            SetThreadExecutionState(ES_CONTINUOUS);
        }
        catch { }
    }
}
