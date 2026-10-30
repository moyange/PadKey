using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;

namespace PadKey;

/// <summary>Locally armed authorization, split by privilege scope.</summary>
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

    public static bool IsArmed => IsSecureArmed || IsElevatedArmed;
    public static bool IsSecureArmed => RemainingSeconds(secureScope: true) > 0;
    public static bool IsElevatedArmed => RemainingSeconds(secureScope: false) > 0;

    public static int RemainingSecureSeconds => RemainingSeconds(secureScope: true);
    public static int RemainingElevatedSeconds => RemainingSeconds(secureScope: false);

    private static int RemainingSeconds(bool secureScope)
    {
        try
        {
            if (!File.Exists(FilePath)) return 0;
            var state = JsonSerializer.Deserialize<State>(File.ReadAllText(FilePath));
            if (state is null) return 0;
            DateTime expires = secureScope ? state.SecureUntilUtc : state.ElevatedUntilUtc;
            return Math.Max(0, (int)Math.Ceiling((expires - DateTime.UtcNow).TotalSeconds));
        }
        catch { return 0; }
    }

    /// <summary>secureScope=true: Winlogon/UAC lease (short). false: elevated-app lease.</summary>
    public static bool Arm(int minutes, bool secureScope)
    {
        if (!IsAdministrator()) return false;
        int max = secureScope ? 60 : 720;
        if (minutes < 1) minutes = 1;
        if (minutes > max) minutes = max;
        try
        {
            EnsureDirectory();
            State state;
            try
            {
                state = File.Exists(FilePath)
                    ? JsonSerializer.Deserialize<State>(File.ReadAllText(FilePath)) ?? new State()
                    : new State();
            }
            catch { state = new State(); }

            if (secureScope) state.SecureUntilUtc = DateTime.UtcNow.AddMinutes(minutes);
            else state.ElevatedUntilUtc = DateTime.UtcNow.AddMinutes(minutes);
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

    public static bool Disarm(bool secureScope)
    {
        if (!IsAdministrator()) return false;
        try
        {
            if (!File.Exists(FilePath)) return true;
            var state = JsonSerializer.Deserialize<State>(File.ReadAllText(FilePath)) ?? new State();
            if (secureScope) state.SecureUntilUtc = DateTime.MinValue;
            else state.ElevatedUntilUtc = DateTime.MinValue;
            if (state.SecureUntilUtc <= DateTime.UtcNow && state.ElevatedUntilUtc <= DateTime.UtcNow)
            {
                File.Delete(FilePath);
            }
            else
            {
                File.WriteAllText(FilePath, JsonSerializer.Serialize(state));
                SetFileAcl();
            }
            return true;
        }
        catch { return false; }
    }

    public static bool Disarm() => Disarm(secureScope: true) && Disarm(secureScope: false);

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

    private sealed class State
    {
        public DateTime SecureUntilUtc { get; set; }
        public DateTime ElevatedUntilUtc { get; set; }
    }
}
