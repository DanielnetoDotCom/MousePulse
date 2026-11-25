namespace MousePulse.UI;

/// <summary>Light, neutral palette. Falls back to system colours when Windows high contrast is on.</summary>
internal static class Theme
{
    private static bool HighContrast => SystemInformation.HighContrast;

    public static Color Background => HighContrast ? SystemColors.Control : Color.FromArgb(0xF3, 0xF4, 0xF6);
    public static Color Card => HighContrast ? SystemColors.Window : Color.White;
    public static Color CardBorder => HighContrast ? SystemColors.WindowFrame : Color.FromArgb(0xE2, 0xE5, 0xEA);
    public static Color Text => HighContrast ? SystemColors.WindowText : Color.FromArgb(0x1F, 0x29, 0x37);
    public static Color Muted => HighContrast ? SystemColors.GrayText : Color.FromArgb(0x5B, 0x64, 0x72);

    public static Color Accent => HighContrast ? SystemColors.Highlight : Color.FromArgb(0x25, 0x63, 0xEB);
    public static Color AccentMuted => HighContrast ? SystemColors.GrayText : Color.FromArgb(0x93, 0xB4, 0xF5);
    public static Color AccentText => HighContrast ? SystemColors.HighlightText : Color.White;
    public static Color Secondary => HighContrast ? SystemColors.Control : Color.FromArgb(0xE5, 0xE7, 0xEB);
    public static Color Disabled => HighContrast ? SystemColors.Control : Color.FromArgb(0xF1, 0xF2, 0xF4);

    public static Color Good => HighContrast ? SystemColors.WindowText : Color.FromArgb(0x15, 0x80, 0x3D);
    public static Color Warning => HighContrast ? SystemColors.WindowText : Color.FromArgb(0xB4, 0x53, 0x09);
    public static Color Info => HighContrast ? SystemColors.WindowText : Color.FromArgb(0x25, 0x63, 0xEB);
    public static Color Neutral => HighContrast ? SystemColors.GrayText : Color.FromArgb(0x6B, 0x72, 0x80);

    private static readonly Dictionary<(string Family, float Size), Font> Fonts = [];

    public static Font Regular(float size = 9f) => GetFont("Segoe UI", size);
    public static Font Semibold(float size = 9f) => GetFont("Segoe UI Semibold", size);

    // Fonts are cached for the lifetime of the process (a handful of instances, UI thread only).
    private static Font GetFont(string family, float size)
    {
        if (!Fonts.TryGetValue((family, size), out Font? font))
        {
            font = new Font(family, size, FontStyle.Regular);
            Fonts[(family, size)] = font;
        }

        return font;
    }
}
