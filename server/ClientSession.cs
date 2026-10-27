using System.Net.WebSockets;
using System.Text;

namespace PadKey;

internal sealed class ClientSession
{
    private readonly PadServer _server;
    private readonly WebSocket _ws;
    private readonly object _lock = new();
    private readonly HashSet<int> _keys = new();
    private readonly Dictionary<int, long> _repeatAt = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private bool _left, _right, _middle;
    private bool _closed;

    public ClientSession(PadServer server, WebSocket ws, string remote, bool loopback, string clientName)
    {
        _server = server;
        _ws = ws;
        Remote = remote;
        Loopback = loopback;
        ClientName = string.IsNullOrWhiteSpace(clientName) ? "Pad" : clientName.Trim();
    }

    public string Remote { get; }
    public bool Loopback { get; }
    public string ClientName { get; }
    public string LinkLabel => Loopback ? "USB" : "LAN";
    public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;

    public void Start() { }

    public async Task ReceiveLoopAsync(CancellationToken ct)
    {
        while (!_closed && !ct.IsCancellationRequested && _ws.State == WebSocketState.Open)
        {
            string? text = await PadServer.ReceiveTextAsync(_ws, ct);
            if (text is null) break;
            LastSeenUtc = DateTime.UtcNow;
            await _server.OnMessageAsync(this, text, ct);
        }
    }

    public async Task SendAsync(string json, CancellationToken ct = default)
    {
        if (_closed || _ws.State != WebSocketState.Open) return;
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        await _sendLock.WaitAsync(ct);
        try
        {
            if (_ws.State == WebSocketState.Open)
                await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
        }
        catch (Exception) when (_closed)
        {
        }
        catch (WebSocketException)
        {
            _closed = true;
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public void PressKey(int hid)
    {
        lock (_lock)
        {
            if (_closed || !_keys.Add(hid)) return;
            InputInjector.KeyDown(hid);
            if (KeyMap.IsRepeatable(hid))
                _repeatAt[hid] = Environment.TickCount64 + _server.RepeatDelayMs;
        }
    }

    public void ReleaseKey(int hid)
    {
        lock (_lock)
        {
            if (!_keys.Remove(hid)) return;
            _repeatAt.Remove(hid);
            InputInjector.KeyUp(hid);
        }
    }

    public void TickRepeats()
    {
        lock (_lock)
        {
            if (_closed || _repeatAt.Count == 0) return;
            long now = Environment.TickCount64;
            foreach (int hid in _repeatAt.Keys.ToArray())
            {
                long next = _repeatAt[hid];
                if (now < next) continue;
                if (!_keys.Contains(hid))
                {
                    _repeatAt.Remove(hid);
                    continue;
                }
                InputInjector.KeyDown(hid, repeat: true);
                long interval = _server.RepeatIntervalMs;
                _repeatAt[hid] = next + interval <= now ? now + interval : next + interval;
            }
        }
    }

    public void SetButton(string button, bool down)
    {
        string b = button.ToLowerInvariant();
        if (b is not ("left" or "right" or "middle")) return;

        lock (_lock)
        {
            if (_closed) return;
            switch (b)
            {
                case "left":
                    if (_left == down) return;
                    _left = down;
                    break;
                case "right":
                    if (_right == down) return;
                    _right = down;
                    break;
                case "middle":
                    if (_middle == down) return;
                    _middle = down;
                    break;
            }
            InputInjector.MouseButton(b, down);
        }
    }

    public void ReleaseAll()
    {
        lock (_lock)
        {
            foreach (int hid in _keys.ToArray())
                InputInjector.KeyUp(hid);
            _keys.Clear();
            _repeatAt.Clear();

            if (_left) { _left = false; InputInjector.MouseButton("left", false); }
            if (_right) { _right = false; InputInjector.MouseButton("right", false); }
            if (_middle) { _middle = false; InputInjector.MouseButton("middle", false); }
        }
    }

    public void Abort()
    {
        _closed = true;
        try { _ws.Abort(); } catch { }
    }
}
