using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;

namespace PadKey;

/// <summary>Locally armed, time-limited authorization for protected desktop input.</summary>
internal static class SecureArm
{
    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PadKey");
    private static readonly string FilePath = Path.Combine(Dir, "secure-arm.json");

    public static bool IsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    public static bool IsArmed => RemainingSeconds > 0;

    public static int RemainingSeconds
    {
        get
        {
            try
            {
                if (!File.Exists(FilePath)) return 0;
                var state = JsonSerializer.Deserialize<State>(File.ReadAllText(FilePath));
                if (state is null) return 0;
                int seconds = (int)Math.Ceiling((state.ExpiresUtc - DateTime.UtcNow).TotalSeconds);
                return Math.Max(0, seconds);
            }
            catch { return 0; }
        }
    }

    public static bool Arm(int minutes)
    {
        if (!IsAdministrator()) return false;
        if (minutes < 1) minutes = 1;
        if (minutes > 120) minutes = 120;
        try
        {
            EnsureDirectory();
            var state = new State { ExpiresUtc = DateTime.UtcNow.AddMinutes(minutes) };
            File.WriteAllText(FilePath, JsonSerializer.Serialize(state));
            SetFileAcl();
            return true;
        }
        catch (Exception ex)
        {
            SecureLog.Write("arm failed: " + ex.Message);
            return false;
        }
    }

    public static bool Disarm()
    {
        if (!IsAdministrator()) return false;
        try { if (File.Exists(FilePath)) File.Delete(FilePath); return true; }
        catch { return false; }
    }

    private static void EnsureDirectory()
    {
        Directory.CreateDirectory(Dir);
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(true, false);
        AddDir(security, WellKnownSidType.LocalSystemSid, FileSystemRights.FullControl);
        AddDir(security, WellKnownSidType.BuiltinAdministratorsSid, FileSystemRights.FullControl);
        AddDir(security, WellKnownSidType.BuiltinUsersSid, FileSystemRights.ReadAndExecute);
        new DirectoryInfo(Dir).SetAccessControl(security);
    }

    private static void SetFileAcl()
    {
        var security = new FileSecurity();
        security.SetAccessRuleProtection(true, false);
        AddFile(security, WellKnownSidType.LocalSystemSid, FileSystemRights.FullControl);
        AddFile(security, WellKnownSidType.BuiltinAdministratorsSid, FileSystemRights.FullControl);
        AddFile(security, WellKnownSidType.BuiltinUsersSid, FileSystemRights.Read);
        new FileInfo(FilePath).SetAccessControl(security);
    }

    private static void AddDir(DirectorySecurity s, WellKnownSidType sid, FileSystemRights rights) =>
        s.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid, null), rights,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None,
            AccessControlType.Allow));

    private static void AddFile(FileSecurity s, WellKnownSidType sid, FileSystemRights rights) =>
        s.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid, null), rights,
            AccessControlType.Allow));

    private sealed class State { public DateTime ExpiresUtc { get; set; } }
}
