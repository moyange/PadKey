using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;

namespace PadKey;

/// <summary>Local named-pipe endpoint for the SYSTEM helper on the Winlogon desktop.</summary>
internal sealed class SecureInputBridge : IDisposable
{
    public const string PipeName = "PadKeySecureInput";
    private readonly BlockingCollection<string> _queue = new(new ConcurrentQueue<string>(), 256);
    private readonly CancellationTokenSource _cts = new();
    private Thread? _thread;
    private volatile bool _connected;

    public bool IsConnected => _connected;

    public void Start()
    {
        _thread = new Thread(Loop) { IsBackground = true, Name = "PadKeySecurePipe" };
        _thread.Start();
    }

    private void Loop()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.Out, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                pipe.WaitForConnectionAsync(_cts.Token).GetAwaiter().GetResult();
                _connected = true;
                SecureLog.Write("secure helper connected to pipe");
                try
                {
                    while (!_cts.IsCancellationRequested)
                    {
                        if (_queue.TryTake(out string? message, 250))
                        {
                            byte[] bytes = Encoding.ASCII.GetBytes(message + "\n");
                            pipe.Write(bytes, 0, bytes.Length);
                            pipe.Flush();
                        }
                    }
                }
                catch (Exception ex) { SecureLog.Write("pipe write: " + ex.Message); }
                finally { _connected = false; }
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (Exception ex)
            {
                SecureLog.Write("pipe server: " + ex.Message);
                Thread.Sleep(500);
            }
        }
    }

    public bool SendKey(int code, bool down)
    {
        if (!_connected) return false;
        return _queue.TryAdd($"K {code} {(down ? 1 : 0)}");
    }

    public bool SendButton(string button, bool down)
    {
        if (!_connected) return false;
        return _queue.TryAdd($"B {button} {(down ? 1 : 0)}");
    }

    public bool SendMove(double dx, double dy)
    {
        if (!_connected) return false;
        return _queue.TryAdd($"M {(int)Math.Round(dx)} {(int)Math.Round(dy)}");
    }

    public bool SendScroll(double dx, double dy)
    {
        if (!_connected) return false;
        return _queue.TryAdd($"S {(int)Math.Round(dx)} {(int)Math.Round(dy)}");
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _queue.CompleteAdding(); } catch { }
        try { _thread?.Join(1000); } catch { }
        _cts.Dispose();
    }
}
