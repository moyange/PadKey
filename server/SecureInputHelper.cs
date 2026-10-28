using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;

namespace PadKey;

/// <summary>Runs as SYSTEM on the Winlogon secure desktop and injects PIN keystrokes.</summary>
internal static class SecureInputHelper
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetThreadDesktop(uint threadId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool GetUserObjectInformation(IntPtr hObj, int nIndex, StringBuilder pvInfo,
        int nLength, out uint lpnLengthNeeded);

    private static long _mouseCount;
    private static long _lastMouseLog;
    private static long _lastWake;

    public static void Run()
    {
        SecureLog.Write("secure helper starting; desktop=" + DesktopName());
        while (true)
        {
            NamedPipeClientStream? pipe = null;
            try
            {
                pipe = new NamedPipeClientStream(".", SecureInputBridge.PipeName, PipeDirection.In);
                pipe.Connect(1500);
                SecureLog.Write("secure helper pipe connected");
                using var reader = new StreamReader(pipe, Encoding.ASCII);
                string? line;
                while ((line = reader.ReadLine()) is not null)
                {
                    if (line.StartsWith("K ", StringComparison.Ordinal))
                    {
                        string[] parts = line.Split(' ');
                        if (parts.Length == 3 && int.TryParse(parts[1], out int code))
                        {
                            bool down = parts[2] == "1";
                            WakeDisplay();
                            if (down) InputInjector.KeyDown(code);
                            else InputInjector.KeyUp(code);
                        }
                    }
                    else if (line.StartsWith("B ", StringComparison.Ordinal))
                    {
                        string[] parts = line.Split(' ');
                        if (parts.Length == 3)
                        {
                            WakeDisplay(); NoteMouse(); InputInjector.MouseButton(parts[1], parts[2] == "1");
                        }
                    }
                    else if (line.StartsWith("M ", StringComparison.Ordinal))
                    {
                        string[] parts = line.Split(' ');
                        if (parts.Length == 3 && int.TryParse(parts[1], out int dx) && int.TryParse(parts[2], out int dy))
                        {
                            WakeDisplay(); NoteMouse(); InputInjector.MouseMove(dx, dy);
                        }
                    }
                    else if (line.StartsWith("S ", StringComparison.Ordinal))
                    {
                        string[] parts = line.Split(' ');
                        if (parts.Length == 3 && int.TryParse(parts[1], out int dx) && int.TryParse(parts[2], out int dy))
                        {
                            WakeDisplay(); NoteMouse(); InputInjector.Scroll(dx, dy);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                SecureLog.Write("helper pipe: " + ex.Message);
                InputInjector.ReleaseAll();
            }
            finally
            {
                pipe?.Dispose();
            }
            Thread.Sleep(600);
        }
    }

    private static void NoteMouse()
    {
        Interlocked.Increment(ref _mouseCount);
        long now = Environment.TickCount64;
        if (now - _lastMouseLog > 3000)
        {
            _lastMouseLog = now;
            SecureLog.Write("secure mouse events=" + _mouseCount);
        }
    }

    private static void WakeDisplay()
    {
        long now = Environment.TickCount64;
        if (now - _lastWake > 1500)
        {
            _lastWake = now;
            DisplayPower.Wake();
        }
    }

    private static string DesktopName()
    {
        try
        {
            IntPtr desktop = GetThreadDesktop(GetCurrentThreadId());
            var sb = new StringBuilder(256);
            if (desktop != IntPtr.Zero && GetUserObjectInformation(desktop, 2, sb, 512, out _))
                return sb.ToString();
        }
        catch { }
        return "unknown";
    }
}
