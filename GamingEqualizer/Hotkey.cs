namespace GamingEqualizer;

/// <summary>
/// A global hotkey combination, stored in settings as text ("Ctrl+Alt+E") and used at
/// registration time as the Win32 modifier/virtual-key pair RegisterHotKey expects.
/// </summary>
public readonly record struct Hotkey(uint Modifiers, uint VirtualKey)
{
    public const uint ModAlt   = 0x0001;
    public const uint ModCtrl  = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin   = 0x0008;

    public bool IsValid => VirtualKey != 0 && Modifiers != 0;

    /// <summary>Parses "Ctrl+Alt+E". Returns false on anything unrecognised.</summary>
    public static bool TryParse(string? text, out Hotkey hotkey)
    {
        hotkey = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        uint mods = 0, vk = 0;

        foreach (string rawPart in text.Split('+'))
        {
            string part = rawPart.Trim();
            if (part.Length == 0) return false;

            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control": mods |= ModCtrl;  continue;
                case "alt":               mods |= ModAlt;   continue;
                case "shift":             mods |= ModShift; continue;
                case "win":               mods |= ModWin;   continue;
            }

            if (vk != 0) return false;          // more than one non-modifier key
            if (!TryParseKeyName(part, out vk)) return false;
        }

        hotkey = new Hotkey(mods, vk);
        return hotkey.IsValid;
    }

    /// <summary>Parses just the modifier part of a string like "Ctrl+Alt".</summary>
    public static uint ParseModifiers(string? text)
    {
        uint mods = 0;
        if (string.IsNullOrWhiteSpace(text)) return mods;

        foreach (string rawPart in text.Split('+'))
        {
            switch (rawPart.Trim().ToLowerInvariant())
            {
                case "ctrl" or "control": mods |= ModCtrl;  break;
                case "alt":               mods |= ModAlt;   break;
                case "shift":             mods |= ModShift; break;
                case "win":               mods |= ModWin;   break;
            }
        }
        return mods;
    }

    private static bool TryParseKeyName(string name, out uint vk)
    {
        vk = 0;

        if (name.Length == 1)
        {
            char c = char.ToUpperInvariant(name[0]);
            if (c is >= 'A' and <= 'Z') { vk = c;        return true; }
            if (c is >= '0' and <= '9') { vk = c;        return true; }  // VK '0'..'9' == ASCII
            return false;
        }

        if (name.Length is 2 or 3 && (name[0] is 'F' or 'f') &&
            int.TryParse(name[1..], out int fn) && fn is >= 1 and <= 24)
        {
            vk = (uint)(0x70 + fn - 1);   // VK_F1 = 0x70
            return true;
        }

        return false;
    }

    /// <summary>
    /// Builds a hotkey from a keyboard event. Returns null while only modifiers are held,
    /// or for keys we cannot express as a Win32 virtual-key here.
    /// </summary>
    public static Hotkey? FromKeyEvent(KeyModifiers keyModifiers, Key key)
    {
        uint vk = key switch
        {
            >= Key.A  and <= Key.Z  => (uint)('A' + (key - Key.A)),
            >= Key.D0 and <= Key.D9 => (uint)('0' + (key - Key.D0)),
            >= Key.F1 and <= Key.F24 => (uint)(0x70 + (key - Key.F1)),
            >= Key.NumPad0 and <= Key.NumPad9 => (uint)(0x60 + (key - Key.NumPad0)),
            _ => 0
        };
        if (vk == 0) return null;

        uint mods = 0;
        if (keyModifiers.HasFlag(KeyModifiers.Control)) mods |= ModCtrl;
        if (keyModifiers.HasFlag(KeyModifiers.Alt))     mods |= ModAlt;
        if (keyModifiers.HasFlag(KeyModifiers.Shift))   mods |= ModShift;
        if (keyModifiers.HasFlag(KeyModifiers.Meta))    mods |= ModWin;

        // A bare key would be captured system-wide and make the machine unusable.
        if (mods == 0) return null;

        return new Hotkey(mods, vk);
    }

    public override string ToString()
    {
        var parts = new List<string>(4);
        if ((Modifiers & ModCtrl)  != 0) parts.Add("Ctrl");
        if ((Modifiers & ModAlt)   != 0) parts.Add("Alt");
        if ((Modifiers & ModShift) != 0) parts.Add("Shift");
        if ((Modifiers & ModWin)   != 0) parts.Add("Win");
        parts.Add(KeyName());
        return string.Join("+", parts);
    }

    private string KeyName() => VirtualKey switch
    {
        >= 'A' and <= 'Z'   => ((char)VirtualKey).ToString(),
        >= '0' and <= '9'   => ((char)VirtualKey).ToString(),
        >= 0x70 and <= 0x87 => "F" + (VirtualKey - 0x70 + 1),
        >= 0x60 and <= 0x69 => "Num" + (VirtualKey - 0x60),
        _                   => "?"
    };
}
