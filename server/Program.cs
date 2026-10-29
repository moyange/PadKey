using System.Net;
using System.Net.Sockets;
using System.Text;

namespace PadKey;

internal static class Program
{
    private static readonly CancellationTokenSource Quit = new();

    private static async Task<int> Main(string[] args)
    {
        Log.Init();
        Config cfg;
        try
        {
            cfg = Config.Load(args);
        }
        catch (HelpRequestedException)
        {
            Config.PrintHelp();
            return 0;
        }
        catch (Exception ex)
        {
            Log.Err(ex.Message);
            Console.WriteLine();
            Config.PrintHelp();
            return 2;
        }

        if (cfg.ArmSecureMinutes > 0)
        {
            if (!SecureArm.IsAdministrator())
            {
                Log.Err("安全输入授权需要管理员权限，请从托盘菜单确认 UAC。");
                return 1;
            }
            if (!SecureArm.Arm(cfg.ArmSecureMinutes))
            {
                Log.Err("安全输入授权失败。");
                return 1;
            }
            Log.Ok($"安全输入已授权 {cfg.ArmSecureMinutes} 分钟。");
            return 0;
        }

        if (cfg.DisarmSecure)
        {
            if (!SecureArm.IsAdministrator())
            {
                Log.Err("关闭安全输入授权需要管理员权限。");
                return 1;
            }
            SecureArm.Disarm();
            Log.Ok("安全输入授权已关闭。");
            return 0;
        }

        if (cfg.SecureHelper)
        {
            SecureInputHelper.Run(cfg.SecureHelperPipe, cfg.SecureHelperRelativeMouse);
            return 0;
        }

        if (cfg.SecureService)
        {
            SecureServiceHost.Run();
            return 0;
        }

        if (cfg.TrayTest)
        {
            Log.Info("托盘诊断模式");
            var testTray = new TrayIcon(() => Log.Info("托盘退出回调"));
            bool ok = testTray.Start();
            Log.Info("托盘启动: " + ok);
            Thread.Sleep(6000);
            testTray.Dispose();
            return 0;
        }

        if (cfg.InstallAutoStart)
        {
            AutoStart.Enable();
            Log.Ok("已设置开机自启（登录后托盘运行）: " + AutoStart.Command);
            return 0;
        }
        if (cfg.RemoveAutoStart)
        {
            AutoStart.Disable();
            Log.Ok("已取消开机自启");
            return 0;
        }
        if (cfg.AutoStartStatus)
        {
            Log.Info("开机自启: " + (AutoStart.IsEnabled() ? "已开启" : "未开启"));
            Log.Info("命令: " + AutoStart.Command);
            return 0;
        }

        using var mutex = new Mutex(true, @"Local\PadKeyServer", out bool created);
        if (!created)
        {
            Log.Warn("PadKey Server 已经在运行。");
            return 1;
        }

        try
        {
            cfg.Save();
        }
        catch (Exception ex)
        {
            Log.Warn("配置保存失败: " + ex.Message);
        }

        InputInjector.DryRun = cfg.DryRun;
        Console.Title = "PadKey Server";
        PrintBanner(cfg);

        await using var server = new PadServer(cfg);
        try
        {
            await server.StartAsync();
        }
        catch (SocketException ex)
        {
            Log.Err($"无法监听端口 {cfg.Port}: {ex.Message}");
            Log.Info("端口可能被其他程序占用，请使用 --port 8766 换一个端口。");
            return 1;
        }

        string host = cfg.Lan ? "0.0.0.0" : "127.0.0.1";
        Log.Ok($"正在监听 ws://{host}:{cfg.Port}/");
        if (!cfg.Lan)
            Log.Info("USB 模式：在电脑执行 adb reverse tcp:" + cfg.Port + " tcp:" + cfg.Port);
        else
        {
            Log.Info("局域网模式：平板填写电脑 IP、端口和 Token");
            try
            {
                foreach (var addr in Dns.GetHostAddresses(Dns.GetHostName()))
                {
                    if (addr.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(addr))
                        Log.Info($"局域网地址: ws://{addr}:{cfg.Port}/");
                }
            }
            catch { }
        }
        if (cfg.Token.Length > 0)
            Log.Info("Token: " + cfg.Token);
        if (cfg.DryRun)
            Log.Warn("Dry-run 模式：按键不会注入系统");
        if (!IsElevated())
            Log.Warn("当前未以管理员身份运行；如需控制管理员权限的程序，请右键“以管理员身份运行”。");
        Console.WriteLine();
        Log.Info("命令: [r] 释放所有按键  [i] 状态  [q] 退出");

        TrayIcon? tray = null;
        try
        {
            tray = new TrayIcon(() => Quit.Cancel());
            if (tray.Start())
            {
                tray.SetStatus($"PadKey Server · 端口 {cfg.Port} · {(cfg.Lan ? "LAN" : "USB")}");
                if (cfg.Tray) tray.HideConsole();
                else if (cfg.Minimized) tray.MinimizeConsole();
                Log.Ok("系统托盘已启用（双击显示控制台，右键可最小化/开机自启/退出）");
            }
            else
            {
                Log.Warn("系统托盘不可用，保留控制台窗口");
            }
        }
        catch (Exception ex)
        {
            Log.Warn("系统托盘初始化失败: " + ex);
        }

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            Quit.Cancel();
        };

        _ = Task.Run(async () =>
        {
            while (!Quit.IsCancellationRequested)
            {
                string? line = await Console.In.ReadLineAsync();
                if (line is null) break;
                switch (line.Trim().ToLowerInvariant())
                {
                    case "q":
                    case "quit":
                    case "exit":
                        Quit.Cancel();
                        break;
                    case "r":
                    case "release":
                        InputInjector.ReleaseAll();
                        Log.Ok("已释放所有按键和鼠标按键");
                        break;
                    case "i":
                    case "info":
                        Log.Info($"设备: {server.ActiveDevice ?? "未连接"}  消息: {server.Messages:N0}  事件: {InputInjector.InjectedEvents:N0}");
                        break;
                    case "":
                        break;
                    default:
                        Log.Info("命令: [r] 释放按键  [i] 状态  [q] 退出");
                        break;
                }
            }
        });

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, Quit.Token);
        }
        catch (OperationCanceledException) { }

        Console.WriteLine();
        Log.Info("正在关闭…");
        await server.StopAsync();
        tray?.Dispose();
        Log.Ok("已安全退出，所有按键均已释放。");
        return 0;
    }

    private static bool IsElevated()
    {
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    private static void PrintBanner(Config cfg)
    {
        Console.WriteLine();
        Console.WriteLine("  ██████╗  █████╗ ██████╗ ██╗  ██╗███████╗██╗   ██╗");
        Console.WriteLine("  ██╔══██╗██╔══██╗██╔══██╗██║ ██╔╝██╔════╝╚██╗ ██╔╝");
        Console.WriteLine("  ██████╔╝███████║██║  ██║█████╔╝ █████╗   ╚████╔╝ ");
        Console.WriteLine("  ██╔═══╝ ██╔══██║██║  ██║██╔═██╗ ██╔══╝    ╚██╔╝  ");
        Console.WriteLine("  ██║     ██║  ██║██████╔╝██║  ██╗███████╗   ██║   ");
        Console.WriteLine("  ╚═╝     ╚═╝  ╚═╝╚═════╝ ╚═╝  ╚═╝╚══════╝   ╚═╝   ");
        Console.WriteLine("       75% keyboard + touchpad · PC bridge");
        Console.WriteLine("  ────────────────────────────────────────────────");
        Console.WriteLine($"  PC: {Environment.MachineName}    Port: {cfg.Port}    Mode: {(cfg.Lan ? "LAN" : "USB")}");
        Console.WriteLine($"  配置: {Config.FilePath}");
        Console.WriteLine();
    }
}
