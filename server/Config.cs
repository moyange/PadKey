using System.Security.Cryptography;
using System.Text.Json;

namespace PadKey;

public sealed class Config
{
    public int Port { get; set; } = 8765;
    public bool Lan { get; set; }
    public string Token { get; set; } = "";
    public int RepeatDelayMs { get; set; } = 430;
    public int RepeatIntervalMs { get; set; } = 33;
    public bool Verbose { get; set; }
    public bool AllowLanUnlock { get; set; }
    public bool AllowSecureInput { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public bool DryRun { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool Tray { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool Minimized { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool InstallAutoStart { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool RemoveAutoStart { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool AutoStartStatus { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool TrayTest { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool SecureService { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool SecureHelper { get; set; }

    public static string DataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PadKey");

    public static string FilePath => Path.Combine(DataDir, "server.json");

    public static Config Load(string[] args)
    {
        Config cfg = new();
        if (File.Exists(FilePath))
        {
            try
            {
                var loaded = JsonSerializer.Deserialize<Config>(File.ReadAllText(FilePath));
                if (loaded is not null) cfg = loaded;
            }
            catch (Exception ex)
            {
                Log.Warn($"配置文件读取失败，使用默认配置: {ex.Message}");
            }
        }

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            string? value = null;
            int eq = arg.IndexOf('=');
            if (eq > 0)
            {
                value = arg[(eq + 1)..];
                arg = arg[..eq];
            }

            switch (arg.ToLowerInvariant())
            {
                case "--port":
                    value ??= NextValue(args, ref i);
                    if (!int.TryParse(value, out int port) || port < 1 || port > 65535)
                        throw new ArgumentException("端口必须是 1-65535 之间的数字");
                    cfg.Port = port;
                    break;
                case "--lan":
                    cfg.Lan = true;
                    break;
                case "--usb":
                    cfg.Lan = false;
                    break;
                case "--token":
                    cfg.Token = value ?? NextValue(args, ref i) ?? "";
                    break;
                case "--verbose":
                case "-v":
                    cfg.Verbose = true;
                    break;
                case "--dry-run":
                    cfg.DryRun = true;
                    break;
                case "--allow-lan-unlock":
                    cfg.AllowLanUnlock = true;
                    break;
                case "--tray":
                    cfg.Tray = true;
                    break;
                case "--minimized":
                    cfg.Minimized = true;
                    break;
                case "--install-autostart":
                    cfg.InstallAutoStart = true;
                    break;
                case "--remove-autostart":
                    cfg.RemoveAutoStart = true;
                    break;
                case "--autostart-status":
                    cfg.AutoStartStatus = true;
                    break;
                case "--tray-test":
                    cfg.TrayTest = true;
                    break;
                case "--secure-service":
                    cfg.SecureService = true;
                    break;
                case "--secure-helper":
                    cfg.SecureHelper = true;
                    break;
                case "--help":
                case "-h":
                    throw new HelpRequestedException();
                default:
                    throw new ArgumentException($"未知参数: {args[i]}");
            }
        }

        if (cfg.Token.Length > 0 && cfg.Token.Length < 4)
            throw new ArgumentException("Token 至少 4 个字符");

        if (string.IsNullOrWhiteSpace(cfg.Token))
            cfg.Token = RandomNumberGenerator.GetHexString(32, lowercase: false);

        return cfg;
    }

    private static string? NextValue(string[] args, ref int i)
    {
        if (i + 1 >= args.Length) throw new ArgumentException($"{args[i]} 缺少参数值");
        return args[++i];
    }

    public void Save()
    {
        Directory.CreateDirectory(DataDir);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(FilePath, json);
    }

    public static void PrintHelp()
    {
        Console.WriteLine("""
PadKey Server — 把平板变成 75% 键盘 + 触摸板

用法:
  PadKeyServer.exe [选项]

选项:
  --port <端口>     监听端口 (默认 8765)
  --lan             允许局域网连接 (需要 Token，自动生成)
  --usb             仅本机连接 (默认，配合 adb reverse 使用)
  --token <口令>    局域网访问口令
  --verbose, -v     输出按键调试日志
  --dry-run         只接收事件，不注入系统输入（测试用）
  --tray            启动后隐藏控制台，只在系统托盘运行
  --minimized       启动后最小化控制台
  --install-autostart   设置当前用户开机自启（托盘模式）
  --remove-autostart    取消开机自启
  --autostart-status    显示开机自启状态
  --help, -h        显示帮助

USB 连接:
  adb reverse tcp:8765 tcp:8765
  然后在平板 App 中连接 127.0.0.1:8765

局域网连接:
  使用 --lan 启动，在 App 中填写电脑 IP、端口和显示的 Token。
""");
    }
}

public sealed class HelpRequestedException : Exception { }
