namespace PadKey;

public readonly record struct KeyDef(ushort Scan, bool Extended, ushort VirtualKey = 0, bool Repeatable = true);

public static class KeyMap
{
    private static readonly Dictionary<int, KeyDef> Map = Build();

    public static bool TryGet(int hid, out KeyDef def) => Map.TryGetValue(hid, out def);

    public static bool IsModifier(int hid) => hid is >= 0xE0 and <= 0xE7;

    public static bool IsRepeatable(int hid) =>
        Map.TryGetValue(hid, out var d) && d.Repeatable && d.VirtualKey == 0;

    private static Dictionary<int, KeyDef> Build()
    {
        var m = new Dictionary<int, KeyDef>();

        ushort[] letterScans =
        {
            0x1E, 0x30, 0x2E, 0x20, 0x12, 0x21, 0x22, 0x23, 0x17, 0x24,
            0x25, 0x26, 0x32, 0x31, 0x18, 0x19, 0x10, 0x13, 0x1F, 0x14,
            0x16, 0x2F, 0x11, 0x2D, 0x15, 0x2C
        };
        for (int i = 0; i < 26; i++) m[0x04 + i] = new KeyDef(letterScans[i], false);

        ushort[] digitScans = { 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B };
        for (int i = 0; i < 10; i++) m[0x1E + i] = new KeyDef(digitScans[i], false);

        // 0x27 is '0' and is already covered by the digit loop.
        Add(m, 0x28, 0x1C, false);   // Enter
        Add(m, 0x29, 0x01, false);   // Escape
        Add(m, 0x2A, 0x0E, false);   // Backspace
        Add(m, 0x2B, 0x0F, false);   // Tab
        Add(m, 0x2C, 0x39, false);   // Space
        Add(m, 0x2D, 0x0C, false);   // -
        Add(m, 0x2E, 0x0D, false);   // =
        Add(m, 0x2F, 0x1A, false);   // [
        Add(m, 0x30, 0x1B, false);   // ]
        Add(m, 0x31, 0x2B, false);   // backslash
        Add(m, 0x32, 0x2B, false);   // non-US # (best effort)
        Add(m, 0x33, 0x27, false);   // ;
        Add(m, 0x34, 0x28, false);   // '
        Add(m, 0x35, 0x29, false);   // backtick
        Add(m, 0x36, 0x33, false);   // ,
        Add(m, 0x37, 0x34, false);   // .
        Add(m, 0x38, 0x35, false);   // /
        Add(m, 0x39, 0x3A, false);   // CapsLock

        m[0x3A] = new KeyDef(0x3B, false);
        m[0x3B] = new KeyDef(0x3C, false);
        m[0x3C] = new KeyDef(0x3D, false);
        m[0x3D] = new KeyDef(0x3E, false);
        m[0x3E] = new KeyDef(0x3F, false);
        m[0x3F] = new KeyDef(0x40, false);
        m[0x40] = new KeyDef(0x41, false);
        m[0x41] = new KeyDef(0x42, false);
        m[0x42] = new KeyDef(0x43, false);
        m[0x43] = new KeyDef(0x44, false);
        m[0x44] = new KeyDef(0x57, false);
        m[0x45] = new KeyDef(0x58, false);

        Add(m, 0x46, 0x37, true);
        Add(m, 0x47, 0x46, false);
        m[0x48] = new KeyDef(0x45, false);
        Add(m, 0x49, 0x52, true);
        Add(m, 0x4A, 0x47, true);
        Add(m, 0x4B, 0x49, true);
        Add(m, 0x4C, 0x53, true);
        Add(m, 0x4D, 0x4F, true);
        Add(m, 0x4E, 0x51, true);
        Add(m, 0x4F, 0x4D, true);
        Add(m, 0x50, 0x4B, true);
        Add(m, 0x51, 0x50, true);
        Add(m, 0x52, 0x48, true);

        Add(m, 0x53, 0x45, false);
        Add(m, 0x54, 0x35, true);
        Add(m, 0x55, 0x37, false);
        Add(m, 0x56, 0x4A, false);
        Add(m, 0x57, 0x4E, false);
        Add(m, 0x58, 0x1C, true);
        Add(m, 0x59, 0x4F, false);
        Add(m, 0x5A, 0x50, false);
        Add(m, 0x5B, 0x51, false);
        Add(m, 0x5C, 0x4B, false);
        Add(m, 0x5D, 0x4C, false);
        Add(m, 0x5E, 0x4D, false);
        Add(m, 0x5F, 0x47, false);
        Add(m, 0x60, 0x48, false);
        Add(m, 0x61, 0x49, false);
        Add(m, 0x62, 0x52, false);
        Add(m, 0x63, 0x53, false);
        Add(m, 0x64, 0x56, false);
        Add(m, 0x65, 0x5D, true);

        Add(m, 0xE0, 0x1D, false, false);
        Add(m, 0xE1, 0x2A, false, false);
        Add(m, 0xE2, 0x38, false, false);
        Add(m, 0xE3, 0x5B, true, false);
        Add(m, 0xE4, 0x1D, true, false);
        Add(m, 0xE5, 0x36, false, false);
        Add(m, 0xE6, 0x38, true, false);
        Add(m, 0xE7, 0x5C, true, false);

        m[0x7F] = new KeyDef(0, false, 0xAD, false);
        m[0x80] = new KeyDef(0, false, 0xAF, false);
        m[0x81] = new KeyDef(0, false, 0xAE, false);
        m[0xCD] = new KeyDef(0, false, 0xB3, false);
        m[0xB5] = new KeyDef(0, false, 0xB0, false);
        m[0xB6] = new KeyDef(0, false, 0xB1, false);
        m[0xB7] = new KeyDef(0, false, 0xB2, false);

        return m;
    }

    private static void Add(Dictionary<int, KeyDef> m, int hid, ushort scan, bool extended, bool repeatable = true) =>
        m[hid] = new KeyDef(scan, extended, 0, repeatable);
}
