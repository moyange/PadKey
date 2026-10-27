using System.Text;
using Microsoft.Win32;

namespace PadKey;

/// <summary>Per-user Windows logon auto-start stored in HKCU Run.</summary>
internal static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "PadKeyServer";

    private static string LauncherPath =>
        Path.Combine(Config.DataDir, "start-tray.vbs");

    public static string Command =>
        "wscript.exe \"" + LauncherPath + "\"";

    public static bool IsEnabled()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey, false);
            return key?.GetValue(ValueName) is string value && value.Length > 0;
        }
        catch { return false; }
    }

    public static void Enable()
    {
        Directory.CreateDirectory(Config.DataDir);
        string exe = Environment.ProcessPath ?? "PadKeyServer.exe";
        string vbs =
            "Set sh = CreateObject(\"WScript.Shell\")" + Environment.NewLine +
            "sh.Run Chr(34) & \"" + exe + "\" & Chr(34) & \" --tray\", 7, False" + Environment.NewLine;
        File.WriteAllText(LauncherPath, vbs, Encoding.ASCII);
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey, true);
        key.SetValue(ValueName, Command, RegistryValueKind.String);
        RemoveLegacyStartupShortcut();
    }

    public static void Disable()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey, true);
            key?.DeleteValue(ValueName, false);
        }
        catch { }
        RemoveLegacyStartupShortcut();
    }

    private static void RemoveLegacyStartupShortcut()
    {
        try
        {
            string startup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            string link = Path.Combine(startup, "PadKey Server.lnk");
            if (File.Exists(link)) File.Delete(link);
        }
        catch { }
    }
}
