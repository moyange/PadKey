using System.Runtime.InteropServices;

namespace PadKey;

/// <summary>Absolute cursor injection for the Winlogon helper.</summary>
internal static class SecureMouse
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    private static int _width = 1920;
    private static int _height = 1080;
    private static int _x;
    private static int _y;
    private static bool _ready;

    private static void Ensure()
    {
        if (_ready) return;
        _width = Math.Max(1, GetSystemMetrics(0));
        _height = Math.Max(1, GetSystemMetrics(1));
        _x = _width / 2;
        _y = _height / 2;
        _ready = true;
    }

    public static bool Move(double dx, double dy)
    {
        Ensure();
        _x = Math.Clamp(_x + (int)Math.Round(dx), 0, _width - 1);
        _y = Math.Clamp(_y + (int)Math.Round(dy), 0, _height - 1);
        try { return SetCursorPos(_x, _y); }
        catch { return false; }
    }
}
