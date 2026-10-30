namespace PadKey;

internal enum InputRoute { Normal, Default, Winlogon, Locked }

internal interface IInputSink
{
    void KeyDown(int hid, bool repeat = false);
    void KeyUp(int hid);
    void MouseButton(string button, bool down);
    void MouseMove(double dx, double dy);
    void Scroll(int dxNotches, int dyNotches);
    void ReleaseAll();
}

/// <summary>Routes input to the armed SYSTEM helper, otherwise to the normal desktop.</summary>
internal sealed class InputRouter : IInputSink
{
    private readonly SecureInputBridge? _winlogon;
    private readonly SecureInputBridge? _default;

    public InputRouter(SecureInputBridge? winlogon, SecureInputBridge? defaultDesktop)
    {
        _winlogon = winlogon;
        _default = defaultDesktop;
    }

    public InputRoute CurrentRoute()
    {
        bool secureDesktop = !InputInjector.IsInputDesktopAvailable();
        if (secureDesktop)
        {
            if (!SecureArm.IsSecureArmed) return InputRoute.Locked;
            return _winlogon is { IsConnected: true } ? InputRoute.Winlogon : InputRoute.Locked;
        }
        if (!SecureArm.IsElevatedArmed) return InputRoute.Normal;
        return _default is { IsConnected: true } ? InputRoute.Default : InputRoute.Normal;
    }

    private SecureInputBridge? Choose() => CurrentRoute() switch
    {
        InputRoute.Winlogon => _winlogon,
        InputRoute.Default => _default,
        _ => null
    };

    public void KeyDown(int hid, bool repeat = false)
    {
        var target = Choose();
        if (target is not null)
        {
            // Repeats are re-sent as down; the helper's injected state handles them.
            target.SendKey(hid, true);
            return;
        }
        InputInjector.KeyDown(hid, repeat);
    }

    public void KeyUp(int hid)
    {
        var target = Choose();
        if (target is not null) { target.SendKey(hid, false); return; }
        InputInjector.KeyUp(hid);
    }

    public void MouseButton(string button, bool down)
    {
        var target = Choose();
        if (target is not null) { target.SendButton(button, down); return; }
        InputInjector.MouseButton(button, down);
    }

    public void MouseMove(double dx, double dy)
    {
        var target = Choose();
        if (target is not null) { target.SendMove(dx, dy); return; }
        InputInjector.MouseMove(dx, dy);
    }

    public void Scroll(int dxNotches, int dyNotches)
    {
        var target = Choose();
        if (target is not null) { target.SendScroll(dxNotches, dyNotches); return; }
        InputInjector.Scroll(dxNotches, dyNotches);
    }

    public void ReleaseAll()
    {
        try { InputInjector.ReleaseAll(); } catch { }
        try { _winlogon?.ReleaseAll(); } catch { }
        try { _default?.ReleaseAll(); } catch { }
    }
}
