using System.Text;

namespace PadKey;

internal static class SecureLog
{
    private static readonly object Gate = new();
    private static readonly string FilePath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PadKey", "secure.log");

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.AppendAllText(FilePath, $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}", Encoding.UTF8);
            }
        }
        catch { }
    }
}
