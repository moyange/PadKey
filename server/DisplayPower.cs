using System.Runtime.InteropServices;

namespace PadKey;

internal static class DisplayPower
{
    private const uint ES_CONTINUOUS = 0x80000000;
    private const uint ES_SYSTEM_REQUIRED = 0x00000001;
    private const uint ES_DISPLAY_REQUIRED = 0x00000002;

    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint flags);

    /// <summary>Nudges Windows to turn the display back on after resume or lock.</summary>
    public static void Wake()
    {
        try
        {
            SetThreadExecutionState(ES_CONTINUOUS | ES_DISPLAY_REQUIRED | ES_SYSTEM_REQUIRED);
            Thread.Sleep(40);
            SetThreadExecutionState(ES_CONTINUOUS);
        }
        catch { }
    }
}
