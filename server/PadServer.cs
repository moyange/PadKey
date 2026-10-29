using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;

namespace PadKey;

/// <summary>A very small HTTP + WebSocket server tuned for one PadKey client.</summary>
public sealed class PadServer : IAsyncDisposable
{
    private const string WebSocketGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
    private readonly Config _cfg;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _activeLock = new();
    private readonly ConcurrentDictionary<Guid, byte> _connections = new();
    private TcpListener? _listener;
    private UdpClient? _discovery;
    private Task? _discoveryTask;
    private ClientSession? _active;
    private Task? _acceptTask;
    private Task? _tickTask;
    private long _messages;
    private bool? _lastLocked;
    private long _lastInputAt;
    private bool? _lastFakeSleep;
    private bool? _lastCaps;
    private bool? _lastNum;
    private bool? _lastScroll;
    private SecureInputBridge? _secureInput;
    private DateTime _startedAt = DateTime.Now;

    public PadServer(Config cfg) => _cfg = cfg;

    public int Port => _cfg.Port;
    public bool Lan => _cfg.Lan;
    public string Token => _cfg.Token;
    public long Messages => Interlocked.Read(ref _messages);
    internal int RepeatDelayMs => _cfg.RepeatDelayMs;
    internal int RepeatIntervalMs => _cfg.RepeatIntervalMs;
    public string? ActiveDevice => _active?.ClientName;
    public bool HasClient => _active is not null;

    public Task StartAsync()
    {
        IPAddress address = _cfg.Lan ? IPAddress.Any : IPAddress.Loopback;
        _listener = new TcpListener(address, _cfg.Port);
        _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _listener.Start(16);
        _startedAt = DateTime.Now;
        _secureInput = new SecureInputBridge();
        _secureInput.Start();
        _acceptTask = AcceptLoopAsync(_cts.Token);
        _tickTask = TickLoopAsync(_cts.Token);
        if (_cfg.Lan) StartDiscovery();
        return Task.CompletedTask;
    }

    private void StartDiscovery()
    {
        try
        {
            _discovery = new UdpClient(new IPEndPoint(IPAddress.Any, 8767));
            _discovery.EnableBroadcast = true;
            _discoveryTask = DiscoveryLoopAsync(_discovery, _cts.Token);
            Log.Ok("Wi-Fi 自动发现已启用 UDP 8767");
        }
        catch (Exception ex)
        {
            Log.Warn("Wi-Fi 自动发现启动失败: " + ex.Message);
        }
    }

    private async Task DiscoveryLoopAsync(UdpClient udp, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await udp.ReceiveAsync(ct);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (Exception ex)
            {
                if (_cfg.Verbose) Log.Debug("发现端口: " + ex.Message);
                continue;
            }

            if (result.Buffer.Length > 128) continue;
            string text = Encoding.UTF8.GetString(result.Buffer).Trim();
            if (!text.StartsWith("PADKEY_DISCOVER", StringComparison.Ordinal)) continue;

            try
            {
                string json = System.Text.Json.JsonSerializer.Serialize(new
                {
                    t = "padkey-server",
                    v = 1,
                    name = Environment.MachineName,
                    port = _cfg.Port,
                    lan = true,
                    tokenRequired = _cfg.Token.Length > 0
                });
                byte[] bytes = Encoding.UTF8.GetBytes(json);
                udp.Send(bytes, bytes.Length, result.RemoteEndPoint);
                if (_cfg.Verbose)
                    Log.Debug("发现请求: " + result.RemoteEndPoint);
            }
            catch (Exception ex)
            {
                if (_cfg.Verbose) Log.Debug("发现响应失败: " + ex.Message);
            }
        }
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient tcp;
            try
            {
                tcp = await _listener!.AcceptTcpClientAsync(ct);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (Exception ex)
            {
                Log.Err("接受连接失败: " + ex.Message);
                continue;
            }

            tcp.NoDelay = true;
            _ = HandleClientAsync(tcp, ct);
        }
    }

    private async Task TickLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(10));
        int ticks = 0;
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                ClientSession? active = _active;
                active?.TickRepeats();

                if (active is not null && ticks % 50 == 0)
                {
                    bool desktopLocked = WorkstationState.IsLocked() || !InputInjector.IsInputDesktopAvailable();
                    bool secure = desktopLocked && WorkstationState.IsLocked() && _secureInput?.IsConnected == true;
                    bool effectiveLocked = desktopLocked && !secure;
                    if (_lastLocked != effectiveLocked)
                    {
                        _lastLocked = effectiveLocked;
                        _ = active.SendAsync(Protocol.Locked(effectiveLocked));
                    }
                    bool fakeSleep = PowerControl.IsFakeSleeping;
                    if (_lastFakeSleep != fakeSleep)
                    {
                        _lastFakeSleep = fakeSleep;
                        _ = active.SendAsync(Protocol.FakeSleep(fakeSleep));
                    }
                    bool caps = KeyboardLocks.Caps, num = KeyboardLocks.Num, scroll = KeyboardLocks.Scroll;
                    if (_lastCaps != caps || _lastNum != num || _lastScroll != scroll)
                    {
                        _lastCaps = caps; _lastNum = num; _lastScroll = scroll;
                        _ = active.SendAsync(Protocol.Locks(caps, num, scroll));
                    }
                }

                if (++ticks % 500 == 0)
                {
                    if (active is not null && DateTime.UtcNow - active.LastSeenUtc > TimeSpan.FromSeconds(15))
                    {
                        Log.Warn("客户端 15 秒无响应，断开连接");
                        active.Abort();
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task HandleClientAsync(TcpClient tcp, CancellationToken ct)
    {
        Guid id = Guid.NewGuid();
        _connections[id] = 0;
        string remote = tcp.Client.RemoteEndPoint?.ToString() ?? "?";
        bool loopback = IsLoopback(tcp.Client.RemoteEndPoint);
        bool registered = false;
        ClientSession? registeredSession = null;

        try
        {
            using (tcp)
            using (NetworkStream stream = tcp.GetStream())
            {
                string? header = await ReadHeaderAsync(stream, ct);
                if (header is null) return;

                var request = HttpRequest.Parse(header);
                if (request is null)
                {
                    await WriteHttpAsync(stream, 400, "text/plain; charset=utf-8", "Bad Request", ct);
                    return;
                }

                if (!request.IsWebSocket)
                {
                    await ServeHttpAsync(stream, request, ct);
                    return;
                }

                // Browsers always send Origin. The native app does not. Rejecting
                // browser WebSockets stops a random web page from driving localhost.
                if (!string.IsNullOrEmpty(request.GetHeader("Origin")))
                {
                    await WriteHttpAsync(stream, 403, "text/plain; charset=utf-8", "Browser WebSocket clients are not allowed", ct);
                    return;
                }

                string? key = request.GetHeader("Sec-WebSocket-Key");
                if (key is null || !request.GetHeader("Upgrade").Contains("websocket", StringComparison.OrdinalIgnoreCase))
                {
                    await WriteHttpAsync(stream, 400, "text/plain; charset=utf-8", "Invalid WebSocket handshake", ct);
                    return;
                }

                string accept;
                using (var sha1 = SHA1.Create())
                    accept = Convert.ToBase64String(sha1.ComputeHash(Encoding.ASCII.GetBytes(key + WebSocketGuid)));

                string response =
                    "HTTP/1.1 101 Switching Protocols\r\n" +
                    "Upgrade: websocket\r\n" +
                    "Connection: Upgrade\r\n" +
                    "Sec-WebSocket-Accept: " + accept + "\r\n\r\n";
                byte[] responseBytes = Encoding.ASCII.GetBytes(response);
                await stream.WriteAsync(responseBytes, ct);
                await stream.FlushAsync(ct);

                using WebSocket ws = WebSocket.CreateFromStream(stream, true, null, TimeSpan.FromSeconds(20));
                using var helloCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                helloCts.CancelAfter(TimeSpan.FromSeconds(8));

                string? helloText;
                try
                {
                    helloText = await ReceiveTextAsync(ws, helloCts.Token);
                }
                catch (OperationCanceledException)
                {
                    Log.Warn("握手超时: " + remote);
                    return;
                }

                ClientMessage? hello = helloText is null ? null : Protocol.Parse(helloText);
                if (hello is null || hello.T != "hello")
                {
                    await CloseAsync(ws, WebSocketCloseStatus.ProtocolError, "hello required");
                    return;
                }

                if (hello.V is not null && hello.V != Protocol.Version)
                {
                    await CloseAsync(ws, WebSocketCloseStatus.ProtocolError, "protocol version mismatch");
                    return;
                }

                if (!loopback && _cfg.Token.Length > 0)
                {
                    if (!FixedTimeEquals(hello.Token ?? "", _cfg.Token))
                    {
                        Log.Warn($"拒绝来自 {remote} 的连接: Token 不匹配");
                        await CloseAsync(ws, WebSocketCloseStatus.PolicyViolation, "invalid token");
                        return;
                    }
                }

                var session = new ClientSession(this, ws, remote, loopback, hello.Name ?? "Pad")
                {
                    LastSeenUtc = DateTime.UtcNow
                };

                ClientSession? stale = null;
                lock (_activeLock)
                {
                    if (_active is not null)
                    {
                        TimeSpan age = DateTime.UtcNow - _active.LastSeenUtc;
                        if (age > TimeSpan.FromSeconds(6))
                        {
                            // The old connection is almost certainly a half-open
                            // socket left behind by sleep/resume. Let the fresh
                            // connection take over immediately.
                            stale = _active;
                            _active = session;
                            registeredSession = session;
                            registered = true;
                        }
                        else
                        {
                            Log.Warn($"拒绝 {session.ClientName}: 已有设备连接");
                            _ = CloseAsync(ws, (WebSocketCloseStatus)1013, "another device is connected");
                            return;
                        }
                    }
                    else
                    {
                        _active = session;
                        registeredSession = session;
                        registered = true;
                    }
                }
                if (stale is not null)
                {
                    Log.Warn($"替换超时连接: {stale.ClientName}");
                    stale.Abort();
                }

                Log.Ok($"已连接: {session.ClientName}  ({remote}, {session.LinkLabel})");
                await session.SendAsync(Protocol.Welcome(Environment.MachineName, session.LinkLabel, _cfg.Token.Length > 0), ct);
                DisplayPower.Wake();
                await session.SendAsync(Protocol.WakeInfo(GetWakeMacs()), ct);
                await session.SendAsync(Protocol.FakeSleep(PowerControl.IsFakeSleeping), ct);
                await session.SendAsync(Protocol.Locks(KeyboardLocks.Caps, KeyboardLocks.Num, KeyboardLocks.Scroll), ct);
                session.Start();
                await session.ReceiveLoopAsync(ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException ex)
        {
            if (_cfg.Verbose) Log.Debug("WebSocket 结束: " + ex.Message);
        }
        catch (Exception ex)
        {
            Log.Err("连接异常: " + ex.Message);
        }
        finally
        {
            if (registered && registeredSession is not null)
            {
                lock (_activeLock)
                {
                    if (ReferenceEquals(_active, registeredSession)) _active = null;
                }
                registeredSession.ReleaseAll();
                Log.Info($"已断开: {registeredSession.ClientName}");
            }
            _connections.TryRemove(id, out _);
        }
    }

    private async Task ServeHttpAsync(NetworkStream stream, HttpRequest request, CancellationToken ct)
    {
        if (request.Path.Equals("/health", StringComparison.OrdinalIgnoreCase))
        {
            string json = System.Text.Json.JsonSerializer.Serialize(new
            {
                ok = true,
                connected = HasClient,
                device = ActiveDevice,
                link = _active?.LinkLabel,
                messages = Messages,
                locked = WorkstationState.IsLocked() || !InputInjector.IsInputDesktopAvailable(),
                workstationLocked = WorkstationState.IsLocked(),
                secureInput = _secureInput?.IsConnected == true,
                fakeSleep = PowerControl.IsFakeSleeping,
                uptimeSeconds = (int)(DateTime.Now - _startedAt).TotalSeconds
            });
            await WriteHttpAsync(stream, 200, "application/json; charset=utf-8", json, ct);
            return;
        }

        string device = ActiveDevice is null ? "等待平板连接" : WebUtility.HtmlEncode(ActiveDevice);
        string state = HasClient ? "已连接" : "等待连接";
        string html = """
<!doctype html>
<html lang="zh-CN">
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>PadKey Server</title>
<style>
  :root { color-scheme: dark; }
  body { margin: 0; min-height: 100vh; display: grid; place-items: center; background: #07090d;
         color: #e9eff8; font: 15px/1.6 system-ui, "Microsoft YaHei", sans-serif; }
  main { width: min(640px, 92vw); padding: 28px; border: 1px solid #1c2633; border-radius: 20px;
         background: linear-gradient(160deg,#101722,#0b0f16); box-shadow: 0 24px 80px #0008; }
  h1 { margin: 0 0 4px; font-size: 24px; letter-spacing: .08em; }
  .sub { color: #7f8ca3; margin-bottom: 22px; }
  .row { display: flex; justify-content: space-between; padding: 10px 0; border-top: 1px solid #18212d; }
  .k { color: #8492a8; }
  .ok { color: #59e0a0; } .wait { color: #ffb454; }
  code, pre { background: #0a0f16; border: 1px solid #1b2532; border-radius: 10px; color: #8fe7ff; }
  code { padding: 2px 6px; } pre { padding: 12px; overflow: auto; }
  a { color: #5ee0ff; }
</style>
<main>
  <h1>PADKEY</h1>
  <div class="sub">75% keyboard + touchpad · PC bridge</div>
  <div class="row"><span class="k">状态</span><span class="%STATE_CLASS%">%STATE%</span></div>
  <div class="row"><span class="k">设备</span><span>%DEVICE%</span></div>
  <div class="row"><span class="k">监听</span><span>%LISTEN%</span></div>
  <div class="row"><span class="k">已处理消息</span><span>%MESSAGES%</span></div>
  <div class="row"><span class="k">运行时长</span><span>%UPTIME%</span></div>
  <h3>USB 连接</h3>
  <pre>adb reverse tcp:%PORT% tcp:%PORT%</pre>
  <p class="sub">平板 App 连接 <code>127.0.0.1:%PORT%</code>。保持 USB 数据线连接即可。</p>
</main>
</html>
""";
        html = html.Replace("%STATE_CLASS%", HasClient ? "ok" : "wait")
                   .Replace("%STATE%", state)
                   .Replace("%DEVICE%", device)
                   .Replace("%LISTEN%", (_cfg.Lan ? "0.0.0.0" : "127.0.0.1") + ":" + _cfg.Port)
                   .Replace("%MESSAGES%", Messages.ToString("N0"))
                   .Replace("%UPTIME%", FormatUptime(DateTime.Now - _startedAt))
                   .Replace("%PORT%", _cfg.Port.ToString());
        await WriteHttpAsync(stream, 200, "text/html; charset=utf-8", html, ct);
    }

    private static string FormatUptime(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours} 小时 {t.Minutes} 分" : $"{t.Minutes} 分 {t.Seconds} 秒";

    internal async Task OnMessageAsync(ClientSession session, string text, CancellationToken ct)
    {
        Interlocked.Increment(ref _messages);
        ClientMessage? msg = Protocol.Parse(text);
        if (msg?.T is "key" or "button" or "move" or "scroll") {
            bool wasFakeSleep = PowerControl.IsFakeSleeping;
            PowerControl.WakeFromFakeSleep();
            if (wasFakeSleep) _ = session.SendAsync(Protocol.FakeSleep(false), ct);
            long nowInput = Environment.TickCount64;
            if (nowInput - _lastInputAt > 1200) DisplayPower.Wake();
            _lastInputAt = nowInput;
        }
        if (msg?.T is null) return;

        if (msg.T is "key" or "button" or "move" or "scroll"
            && (WorkstationState.IsLocked() || !InputInjector.IsInputDesktopAvailable()))
        {
            bool forwarded = false;
            bool isLocked = WorkstationState.IsLocked();
            bool trusted = session.Loopback || _cfg.AllowLanUnlock;
            if (trusted && _secureInput is not null)
            {
                switch (msg.T)
                {
                    case "key" when msg.Code is int sc && msg.Down is bool sd:
                        forwarded = _secureInput.SendKey(sc, sd);
                        break;
                    case "button" when msg.B is string button && msg.Down is bool bd:
                        forwarded = _secureInput.SendButton(button, bd);
                        break;
                    case "move" when msg.Dx is double mx && msg.Dy is double my:
                        forwarded = _secureInput.SendMove(mx, my);
                        break;
                    case "scroll" when msg.Dx is double sx && msg.Dy is double sy:
                        forwarded = _secureInput.SendScroll(sx, sy);
                        break;
                }
            }
            if (!forwarded) await session.SendAsync(Protocol.Locked(true), ct);
            return;
        }

        switch (msg.T)
        {
            case "ping":
                await session.SendAsync(Protocol.Pong(msg.Ts ?? 0), ct);
                break;

            case "key":
            {
                if (msg.Code is not int code || code is < 0 or > 0xFF) return;
                bool down = msg.Down ?? false;
                if (down) session.PressKey(code);
                else session.ReleaseKey(code);
                if (_cfg.Verbose)
                    Log.Debug($"KEY {(down ? "↓" : "↑")} 0x{code:X2}  {KeyName(code)}");
                break;
            }

            case "move":
            {
                double dx = msg.Dx ?? 0, dy = msg.Dy ?? 0;
                if (Math.Abs(dx) > 1000 || Math.Abs(dy) > 1000) return;
                InputInjector.MouseMove(dx, dy);
                break;
            }

            case "button":
                if (msg.B is not null)
                {
                    session.SetButton(msg.B, msg.Down ?? false);
                    if (_cfg.Verbose) Log.Debug($"MOUSE {msg.B} {(msg.Down == true ? "↓" : "↑")}");
                }
                break;

            case "scroll":
            {
                int dx = Math.Clamp((int)(msg.Dx ?? 0), -50, 50);
                int dy = Math.Clamp((int)(msg.Dy ?? 0), -50, 50);
                InputInjector.Scroll(dx, dy);
                break;
            }

            case "release":
                session.ReleaseAll();
                Log.Info("客户端请求释放所有按键");
                break;

            case "power":
            {
                string action = msg.Action ?? "";
                if (action is not ("toggle" or "sleep" or "wake"))
                {
                    await session.SendAsync(Protocol.PowerAck(false, action, "unsupported action"), ct);
                    break;
                }
                session.ReleaseAll();
                bool on;
                if (action == "toggle")
                {
                    on = !PowerControl.IsFakeSleeping;
                }
                else
                {
                    on = action == "sleep";
                }
                if (on)
                {
                    Log.Info("收到假休眠请求");
                    PowerControl.FakeSleep();
                }
                else
                {
                    Log.Info("收到假休眠唤醒请求");
                    PowerControl.WakeFromFakeSleep();
                    DisplayPower.Wake();
                }
                await session.SendAsync(Protocol.PowerAck(true, action), ct);
                await session.SendAsync(Protocol.FakeSleep(on), ct);
                _lastFakeSleep = on;
                break;
            }

            case "hello":
                break;

            default:
                if (_cfg.Verbose) Log.Debug("未知消息: " + text);
                break;
        }
    }

    public async Task StopAsync()
    {
        _cts.Cancel();
        try { _secureInput?.Dispose(); } catch { }
        try { _discovery?.Dispose(); } catch { }
        try { _listener?.Stop(); } catch { }
        ClientSession? session = _active;
        session?.Abort();
        try { if (_acceptTask is not null) await _acceptTask; } catch { }
        try { if (_tickTask is not null) await _tickTask; } catch { }
        try { if (_discoveryTask is not null) await _discoveryTask; } catch { }
        InputInjector.ReleaseAll();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _cts.Dispose();
    }

    internal static bool IsLoopback(EndPoint? endpoint)
    {
        if (endpoint is not IPEndPoint ip) return false;
        return IPAddress.IsLoopback(ip.Address);
    }

    private static string[] GetWakeMacs()
    {
        var result = new List<string>();
        try
        {
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType is not (NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211)) continue;
                byte[] address = ni.GetPhysicalAddress().GetAddressBytes();
                if (address.Length != 6 || address.All(b => b == 0)) continue;
                string mac = string.Join(":", address.Select(b => b.ToString("X2")));
                if (!result.Contains(mac)) result.Add(mac);
            }
        }
        catch { }
        return result.ToArray();
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        byte[] ba = Encoding.UTF8.GetBytes(a);
        byte[] bb = Encoding.UTF8.GetBytes(b);
        return ba.Length == bb.Length && CryptographicOperations.FixedTimeEquals(ba, bb);
    }

    private static async Task<string?> ReadHeaderAsync(NetworkStream stream, CancellationToken ct)
    {
        var sb = new StringBuilder(512);
        byte[] one = new byte[1];
        while (sb.Length < 16384)
        {
            int n = await stream.ReadAsync(one.AsMemory(0, 1), ct);
            if (n == 0) return null;
            sb.Append((char)one[0]);
            int len = sb.Length;
            if (len >= 4 && sb[len - 4] == '\r' && sb[len - 3] == '\n' && sb[len - 2] == '\r' && sb[len - 1] == '\n')
                return sb.ToString();
        }
        return null;
    }

    private static async Task WriteHttpAsync(NetworkStream stream, int status, string contentType, string body, CancellationToken ct)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(body);
        string reason = status switch { 200 => "OK", 400 => "Bad Request", 403 => "Forbidden", 404 => "Not Found", _ => "Error" };
        string head = $"HTTP/1.1 {status} {reason}\r\nContent-Type: {contentType}\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\nCache-Control: no-store\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head), ct);
        await stream.WriteAsync(bytes, ct);
        await stream.FlushAsync(ct);
    }

    internal static async Task CloseAsync(WebSocket ws, WebSocketCloseStatus status, string reason)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await ws.CloseOutputAsync(status, reason, cts.Token);
        }
        catch { }
    }

    internal static async Task<string?> ReceiveTextAsync(WebSocket ws, CancellationToken ct)
    {
        byte[] buffer = new byte[4096];
        using var ms = new MemoryStream();
        while (true)
        {
            ValueWebSocketReceiveResult result;
            try
            {
                result = await ws.ReceiveAsync(buffer.AsMemory(), ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (WebSocketException) { return null; }

            if (result.MessageType == WebSocketMessageType.Close) return null;
            if (result.MessageType == WebSocketMessageType.Binary) return null;
            ms.Write(buffer, 0, result.Count);
            if (ms.Length > 16384) return null;
            if (result.EndOfMessage) return Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
        }
    }

    private sealed class HttpRequest
    {
        public string Method { get; init; } = "";
        public string Path { get; init; } = "/";
        public Dictionary<string, string> Headers { get; init; } = new(StringComparer.OrdinalIgnoreCase);

        public bool IsWebSocket =>
            Method == "GET" && GetHeader("Upgrade").Contains("websocket", StringComparison.OrdinalIgnoreCase);

        public string GetHeader(string name) => Headers.TryGetValue(name, out var v) ? v : "";

        public static HttpRequest? Parse(string header)
        {
            string[] lines = header.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0) return null;
            string[] parts = lines[0].Split(' ');
            if (parts.Length < 2) return null;

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 1; i < lines.Length; i++)
            {
                int colon = lines[i].IndexOf(':');
                if (colon <= 0) continue;
                headers[lines[i][..colon].Trim()] = lines[i][(colon + 1)..].Trim();
            }

            string path = parts[1];
            int query = path.IndexOf('?');
            if (query >= 0) path = path[..query];

            return new HttpRequest { Method = parts[0].ToUpperInvariant(), Path = path, Headers = headers };
        }
    }

    internal static string KeyName(int hid) => hid switch
    {
        >= 0x04 and <= 0x1D => ((char)('A' + hid - 0x04)).ToString(),
        >= 0x1E and <= 0x27 => (hid - 0x1D).ToString(),
        0x28 => "Enter", 0x29 => "Esc", 0x2A => "Backspace", 0x2B => "Tab", 0x2C => "Space",
        0x2D => "-", 0x2E => "=", 0x2F => "[", 0x30 => "]", 0x31 => "\\", 0x32 => "'",
        0x33 => ";", 0x34 => "'", 0x35 => "\u0060", 0x36 => ",", 0x37 => ".", 0x38 => "/", 0x39 => "Caps", 0x4C => "Delete", 0x4F => "Right", 0x50 => "Left", 0x51 => "Down", 0x52 => "Up",
        0xE0 => "LCtrl", 0xE1 => "LShift", 0xE2 => "LAlt", 0xE3 => "LWin",
        0xE4 => "RCtrl", 0xE5 => "RShift", 0xE6 => "RAlt", 0xE7 => "RWin",
        _ => "HID " + hid.ToString("X2")
    };
}
