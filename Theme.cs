using System.Drawing;

namespace ResourceMonitor;

public static class Theme
{
    public static readonly Color BgColor = Color.FromArgb(25, 25, 30);
    public static readonly Color CardBg = Color.FromArgb(30, 30, 35);
    public static readonly Color Border = Color.FromArgb(50, 50, 55);
    public static readonly Color TextDim = Color.FromArgb(140, 140, 145);
    public static readonly Color TextBright = Color.FromArgb(220, 220, 225);
    public static readonly Color TextLabel = Color.FromArgb(180, 180, 185);

    /// <summary>
    /// Maps a 0–100 percentage to a smooth green→yellow→orange→red gradient.
    /// Used by LEDs and value text throughout the UI.
    /// </summary>
    public static Color PercentToColor(float percent)
    {
        percent = Math.Clamp(percent, 0f, 100f);

        if (percent <= 30f)
        {
            float t = percent / 30f;
            return LerpColor(Color.FromArgb(30, 180, 50), Color.FromArgb(60, 200, 60), t);
        }
        if (percent <= 50f)
        {
            float t = (percent - 30f) / 20f;
            return LerpColor(Color.FromArgb(60, 200, 60), Color.FromArgb(220, 220, 40), t);
        }
        if (percent <= 70f)
        {
            float t = (percent - 50f) / 20f;
            return LerpColor(Color.FromArgb(220, 220, 40), Color.FromArgb(240, 150, 30), t);
        }
        if (percent <= 85f)
        {
            float t = (percent - 70f) / 15f;
            return LerpColor(Color.FromArgb(240, 150, 30), Color.FromArgb(220, 50, 40), t);
        }
        {
            float t = (percent - 85f) / 15f;
            return LerpColor(Color.FromArgb(220, 50, 40), Color.FromArgb(180, 20, 30), t);
        }
    }

    /// <summary>Color-code website response times.</summary>
    public static Color WebsiteResponseColor(int ms)
    {
        if (ms < 0) return Color.FromArgb(220, 50, 40); // DOWN
        if (ms < 200) return Color.FromArgb(50, 200, 60);
        if (ms < 500) return Color.FromArgb(220, 220, 40);
        if (ms < 1000) return Color.FromArgb(240, 150, 30);
        return Color.FromArgb(220, 50, 40);
    }

    public static Color LerpColor(Color a, Color b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return Color.FromArgb(
            (int)(a.R + (b.R - a.R) * t),
            (int)(a.G + (b.G - a.G) * t),
            (int)(a.B + (b.B - a.B) * t));
    }
}
