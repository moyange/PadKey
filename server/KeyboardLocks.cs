using System.Runtime.InteropServices;

namespace PadKey;

internal static class KeyboardLocks
{
    [DllImport("user32.dll")]
    private static extern short GetKeyState(int key);

    public static bool Caps => (GetKeyState(0x14) & 1) != 0;
    public static bool Num => (GetKeyState(0x90) & 1) != 0;
    public static bool Scroll => (GetKeyState(0x91) & 1) != 0;
}
