using System.Text.Json;

namespace PadKey;

internal sealed class ClientMessage
{
    public string? T { get; set; }
    public int? V { get; set; }
    public string? Token { get; set; }
    public string? Name { get; set; }
    public int? Code { get; set; }
    public bool? Down { get; set; }
    public double? Dx { get; set; }
    public double? Dy { get; set; }
    public string? B { get; set; }
    public string? Action { get; set; }
    public long? Ts { get; set; }
}

internal static class Protocol
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public const int Version = 1;

    public static ClientMessage? Parse(string text)
    {
        try
        {
            return JsonSerializer.Deserialize<ClientMessage>(text, Json);
        }
        catch
        {
            return null;
        }
    }

    public static string Welcome(string pc, string link, bool tokenRequired) =>
        JsonSerializer.Serialize(new
        {
            t = "welcome",
            v = Version,
            server = "PadKey",
            pc,
            link,
            tokenRequired
        }, Json);

    public static string Pong(long ts) =>
        JsonSerializer.Serialize(new { t = "pong", ts }, Json);

    public static string Locked(bool locked) =>
        JsonSerializer.Serialize(new { t = "locked", locked }, Json);

    public static string WakeInfo(string[] macs) =>
        JsonSerializer.Serialize(new { t = "wakeInfo", macs }, Json);

    public static string PowerAck(bool ok, string action, string? message = null) =>
        JsonSerializer.Serialize(new { t = "power", ok, action, message }, Json);

    public static string FakeSleep(bool on) =>
        JsonSerializer.Serialize(new { t = "fakeSleep", on }, Json);

    public static string Error(string message) =>
        JsonSerializer.Serialize(new { t = "error", message }, Json);
}
