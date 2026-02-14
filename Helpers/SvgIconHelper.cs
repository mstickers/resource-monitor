using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using Svg;

namespace ResourceMonitor.Helpers;

public static class SvgIconHelper
{
    public static Bitmap? Load(string name, int size, Color tint)
    {
        var assembly = Assembly.GetExecutingAssembly();
        string resourceName = $"ResourceMonitor.Assets.Icons.{name}.svg";

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null) return null;

        var doc = SvgDocument.Open<SvgDocument>(stream);
        doc.Width = size;
        doc.Height = size;

        // Tint: replace white strokes/fills with tint color
        var svgColor = new SvgColourServer(tint);
        ApplyTint(doc, svgColor);

        var bmp = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        doc.Draw(bmp);
        return bmp;
    }

    private static void ApplyTint(SvgElement element, SvgPaintServer color)
    {
        if (element.Stroke != SvgPaintServer.None && element.Stroke != null)
            element.Stroke = color;
        if (element.Fill != SvgPaintServer.None && element.Fill != null
            && element.Fill is SvgColourServer cs && cs.Colour.A > 0)
            element.Fill = color;

        foreach (var child in element.Children)
            ApplyTint(child, color);
    }
}
