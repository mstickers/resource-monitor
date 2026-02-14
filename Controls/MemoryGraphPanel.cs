using System.Drawing;
using System.Drawing.Drawing2D;
using ResourceMonitor.Models;

namespace ResourceMonitor.Controls;

public sealed class MemoryGraphPanel : Control
{
    private RingBuffer<SystemSnapshot>? _buffer;

    private static readonly Color BgColor = Color.FromArgb(25, 25, 30);
    private static readonly Color GridColor = Color.FromArgb(50, 50, 55);
    private static readonly Color UsedFill = Color.FromArgb(180, 60, 130, 210);
    private static readonly Color StandbyFill = Color.FromArgb(100, 50, 110, 180);
    private static readonly Color CommitLineColor = Color.FromArgb(220, 240, 120, 50);
    private static readonly Color CpuLineColor = Color.FromArgb(220, 80, 220, 80);
    private static readonly Color PagefileLineColor = Color.FromArgb(180, 220, 60, 180);
    private static readonly Color TextColor = Color.FromArgb(140, 140, 145);

    public MemoryGraphPanel()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.UserPaint
            | ControlStyles.ResizeRedraw, true);
    }

    public void SetBuffer(RingBuffer<SystemSnapshot> buffer)
    {
        _buffer = buffer;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        int w = ClientSize.Width;
        int h = ClientSize.Height;

        g.Clear(BgColor);

        if (w < 10 || h < 10) return;

        // Grid lines at 25%, 50%, 75%, 100%
        using var gridPen = new Pen(GridColor, 1);
        using var textBrush = new SolidBrush(TextColor);
        using var textFont = new Font("Segoe UI", 7.5f);

        for (int pct = 25; pct <= 100; pct += 25)
        {
            int y = h - (int)(pct / 100f * h);
            g.DrawLine(gridPen, 0, y, w, y);
            g.DrawString($"{pct}%", textFont, textBrush, 2, y + 1);
        }

        var buffer = _buffer;
        if (buffer == null || buffer.Count < 2) return;

        int count = buffer.Count;
        int capacity = buffer.Capacity;
        float xStep = (float)w / (capacity - 1);

        // Build point arrays
        var usedPts = new PointF[count];
        var standbyPts = new PointF[count];
        var commitPts = new PointF[count];
        var cpuPts = new PointF[count];
        var pagefilePts = new PointF[count];

        for (int i = 0; i < count; i++)
        {
            var snap = buffer[i];
            float x = (i + capacity - count) * xStep;

            usedPts[i] = new PointF(x, h * (1f - snap.UsedPercent / 100f));
            standbyPts[i] = new PointF(x, h * (1f - (snap.UsedPercent + snap.StandbyPercent) / 100f));
            commitPts[i] = new PointF(x, h * (1f - Math.Min(snap.CommitPercent, 100f) / 100f));
            cpuPts[i] = new PointF(x, h * (1f - Math.Min(snap.CpuPercent, 100f) / 100f));
            pagefilePts[i] = new PointF(x, h * (1f - Math.Min(snap.PagefileUsagePercent, 100f) / 100f));
        }

        // Memory areas
        DrawArea(g, usedPts, h, UsedFill);
        DrawAreaBetween(g, standbyPts, usedPts, StandbyFill);

        // Lines
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var commitPen = new Pen(CommitLineColor, 1.5f))
            g.DrawLines(commitPen, commitPts);
        using (var cpuPen = new Pen(CpuLineColor, 1.5f))
            g.DrawLines(cpuPen, cpuPts);
        using (var pfPen = new Pen(PagefileLineColor, 1f) { DashStyle = DashStyle.Dot })
            g.DrawLines(pfPen, pagefilePts);
        g.SmoothingMode = SmoothingMode.None;

        // Legend (two rows)
        int lx = w - 340, ly = 6;
        DrawLegendItem(g, textFont, lx, ly, "In Use + Modified", Color.FromArgb(60, 130, 210));
        DrawLegendItem(g, textFont, lx + 130, ly, "Standby", Color.FromArgb(50, 110, 180));
        DrawLegendItem(g, textFont, lx, ly + 14, "Commit %", Color.FromArgb(240, 120, 50));
        DrawLegendItem(g, textFont, lx + 130, ly + 14, "CPU %", Color.FromArgb(80, 220, 80));
        DrawLegendItem(g, textFont, lx + 230, ly + 14, "Pagefile", Color.FromArgb(220, 60, 180));

        // Current values (right edge)
        var latest = buffer[count - 1];
        int ry = h - 16;
        g.DrawString($"CPU {latest.CpuPercent:F0}%", textFont, new SolidBrush(CpuLineColor), w - 70, ry);
    }

    private static void DrawArea(Graphics g, PointF[] topEdge, int bottomY, Color fill)
    {
        if (topEdge.Length < 2) return;
        var poly = new PointF[topEdge.Length + 2];
        Array.Copy(topEdge, poly, topEdge.Length);
        poly[topEdge.Length] = new PointF(topEdge[^1].X, bottomY);
        poly[topEdge.Length + 1] = new PointF(topEdge[0].X, bottomY);
        using var brush = new SolidBrush(fill);
        g.FillPolygon(brush, poly);
    }

    private static void DrawAreaBetween(Graphics g, PointF[] topEdge, PointF[] bottomEdge, Color fill)
    {
        if (topEdge.Length < 2) return;
        var poly = new PointF[topEdge.Length + bottomEdge.Length];
        Array.Copy(topEdge, poly, topEdge.Length);
        for (int i = 0; i < bottomEdge.Length; i++)
            poly[topEdge.Length + i] = bottomEdge[bottomEdge.Length - 1 - i];
        using var brush = new SolidBrush(fill);
        g.FillPolygon(brush, poly);
    }

    private static void DrawLegendItem(Graphics g, Font font, int x, int y, string text, Color color)
    {
        using var brush = new SolidBrush(color);
        g.FillRectangle(brush, x, y + 2, 10, 10);
        using var textBrush = new SolidBrush(Color.FromArgb(160, 160, 165));
        g.DrawString(text, font, textBrush, x + 14, y);
    }
}
