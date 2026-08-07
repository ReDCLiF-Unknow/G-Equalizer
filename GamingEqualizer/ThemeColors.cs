namespace GamingEqualizer;

/// <summary>
/// The accent palette: a named hue plus a tone, expanded into the two-colour gradient
/// the whole UI is built from, and pushed into <c>Application.Resources</c>.
///
/// Everything accent-coloured resolves through DynamicResource, so replacing the brushes
/// here recolours the live UI without rebuilding controls. The band gradient used by the
/// sliders and visualizer is drawn in code and reads <see cref="Start"/>/<see cref="End"/>.
/// </summary>
public static class ThemeColors
{
    public const string DefaultColor = "Violet";
    public const int    DefaultTone  = 1;

    /// <summary>Named hues, in colour-wheel order so the swatch row reads as a spectrum.</summary>
    public static readonly (string Name, double Hue)[] Palette =
    {
        ("Red",     0),
        ("Orange",  24),
        ("Amber",   42),
        ("Lime",    85),
        ("Green",   142),
        ("Teal",    168),
        ("Cyan",    190),
        ("Sky",     205),
        ("Blue",    222),
        ("Indigo",  245),
        ("Violet",  262),
        ("Purple",  285),
        ("Magenta", 305),
        ("Rose",    334)
    };

    /// <summary>Saturation/lightness pairs. Index 1 reproduces the original violet.</summary>
    public static readonly (string Name, double Sat, double Light)[] Tones =
    {
        ("Deep",     0.72, 0.44),
        ("Standard", 0.83, 0.58),
        ("Bright",   0.88, 0.68),
        ("Pastel",   0.55, 0.74),
        ("Neon",     1.00, 0.60)
    };

    public static Color Start { get; private set; } = Color.Parse("#7c3aed");
    public static Color End   { get; private set; } = Color.Parse("#f472b6");

    /// <summary>Lightened accent used for text and the status label.</summary>
    public static Color AccentText => Lighten(Start, 0.45);

    public static void Apply(AppSettings settings)
    {
        double hue = Palette.FirstOrDefault(
            p => string.Equals(p.Name, settings.AccentColor, StringComparison.OrdinalIgnoreCase)).Hue;
        if (!Palette.Any(p => string.Equals(p.Name, settings.AccentColor, StringComparison.OrdinalIgnoreCase)))
            hue = Palette.First(p => p.Name == DefaultColor).Hue;

        var tone = Tones[Math.Clamp(settings.AccentTone, 0, Tones.Length - 1)];

        Start = Gradient(hue, tone, false);
        End   = Gradient(hue, tone, true);

        var resources = Application.Current?.Resources;
        if (resources == null) return;

        resources["AccentBrush"]       = new SolidColorBrush(Start);
        resources["AccentPinkBrush"]   = new SolidColorBrush(End);
        resources["AccentTextBrush"]   = new SolidColorBrush(Lighten(Start, 0.45));
        resources["AccentSoftBrush"]   = new SolidColorBrush(Lighten(Start, 0.65));
        resources["AccentDeepBrush"]   = new SolidColorBrush(Darken(Start, 0.25));
        resources["AccentChipBgBrush"] = new SolidColorBrush(Darken(Start, 0.62));

        resources["AccentBrush55"] = new SolidColorBrush(WithAlpha(Start, 0x55));
        resources["AccentBrush99"] = new SolidColorBrush(WithAlpha(Start, 0x99));
        resources["AccentBrush1A"] = new SolidColorBrush(WithAlpha(Start, 0x1a));
        resources["AccentBrush2A"] = new SolidColorBrush(WithAlpha(Start, 0x2a));
    }

    /// <summary>
    /// The gradient runs 60° around the wheel and a little lighter, which is what gives
    /// the default violet→pink sweep. Applying the same rule to every hue keeps that
    /// character whichever colour is chosen.
    /// </summary>
    private static Color Gradient(double hue, (string Name, double Sat, double Light) tone, bool far)
        => FromHsl(far ? (hue + 60) % 360 : hue,
                   tone.Sat,
                   far ? Math.Min(1, tone.Light + 0.10) : tone.Light);

    /// <summary>Swatch colour for the picker — the tone the user currently has selected.</summary>
    public static Color Swatch(double hue, int toneIndex)
    {
        var tone = Tones[Math.Clamp(toneIndex, 0, Tones.Length - 1)];
        return FromHsl(hue, tone.Sat, tone.Light);
    }

    private static Color FromHsl(double h, double s, double l)
    {
        h = ((h % 360) + 360) % 360;
        double c = (1 - Math.Abs(2 * l - 1)) * s;
        double x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        double m = l - c / 2;

        (double r, double g, double b) = h switch
        {
            < 60  => (c, x, 0d),
            < 120 => (x, c, 0d),
            < 180 => (0d, c, x),
            < 240 => (0d, x, c),
            < 300 => (x, 0d, c),
            _     => (c, 0d, x)
        };

        return Color.FromRgb(
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }

    /// <summary>Gradient position <paramref name="t"/> (0..1) between Start and End.</summary>
    public static Color Band(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromRgb(
            (byte)(Start.R + (End.R - Start.R) * t),
            (byte)(Start.G + (End.G - Start.G) * t),
            (byte)(Start.B + (End.B - Start.B) * t));
    }

    private static Color Lighten(Color c, double amount) => Color.FromRgb(
        (byte)(c.R + (255 - c.R) * amount),
        (byte)(c.G + (255 - c.G) * amount),
        (byte)(c.B + (255 - c.B) * amount));

    private static Color Darken(Color c, double amount) => Color.FromRgb(
        (byte)(c.R * (1 - amount)),
        (byte)(c.G * (1 - amount)),
        (byte)(c.B * (1 - amount)));

    private static Color WithAlpha(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);
}
