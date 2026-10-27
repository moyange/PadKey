using System.Runtime.InteropServices;

namespace PadKey;

internal static class Log
{
    private static readonly object Gate = new();
    private static bool _colors = true;

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll")]
    private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll")]
    private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

    public static void Init()
    {
        try
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            IntPtr h = GetStdHandle(-11);
            if (GetConsoleMode(h, out uint mode)) SetConsoleMode(h, mode | 0x0004);
            _colors = !Console.IsOutputRedirected;
        }
        catch
        {
            _colors = false;
        }
    }

    private static void Write(string tag, string color, string message, ConsoleColor fallback)
    {
        lock (Gate)
        {
            string time = DateTime.Now.ToString("HH:mm:ss");
            if (_colors)
            {
                Console.WriteLine($"\u001b[90m{time}\u001b[0m  \u001b[{color}m{tag}\u001b[0m  {message}");
            }
            else
            {
                var old = Console.ForegroundColor;
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write($"{time}  ");
                Console.ForegroundColor = fallback;
                Console.Write($"{tag}  ");
                Console.ForegroundColor = old;
                Console.WriteLine(message);
            }
        }
    }

    public static void Info(string message) => Write("·", "36", message, ConsoleColor.Cyan);
    public static void Ok(string message) => Write("✓", "32", message, ConsoleColor.Green);
    public static void Warn(string message) => Write("!", "33", message, ConsoleColor.Yellow);
    public static void Err(string message) => Write("×", "31", message, ConsoleColor.Red);
    public static void Debug(string message) => Write("·", "90", message, ConsoleColor.DarkGray);
}
