using System.Drawing;
using System.Drawing.Drawing2D;
using ResourceMonitor.Models;

namespace ResourceMonitor.Controls;

public sealed class MemoryDetailPanel : Control
{
    private SystemSnapshot _snap;
    private MetricTracker? _ramTracker;
    private List<ProcessInfo> _processes = [];
    private bool _balloonInflated;
    private long _balloonReclaimed;
    private RingBuffer<SystemSnapshot>? _ringBuffer;
    private bool _hasData;

    private static readonly Font TitleFont = new("Segoe UI", 10f, FontStyle.Bold);
    private static readonly Font LabelFont = new("Segoe UI", 9f);
    private static readonly Font ValueFont = new("Consolas", 9.5f, FontStyle.Bold);
    private static readonly Font BigValueFont = new("Consolas", 22f, FontStyle.Bold);
    private static readonly Font SmallFont = new("Segoe UI", 8f);
    private static readonly Font TableFont = new("Consolas", 8.5f);

    public MemoryDetailPanel()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.UserPaint
            | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.BgColor;
    }

    public void SetBuffer(RingBuffer<SystemSnapshot> buffer) => _ringBuffer = buffer;

    public void Update(SystemSnapshot snap, MetricTracker ramTracker,
        List<ProcessInfo> processes, bool balloonInflated, long balloonReclaimed)
    {
        _snap = snap;
        _ramTracker = ramTracker;
        _processes = processes;
        _balloonInflated = balloonInflated;
        _balloonReclaimed = balloonReclaimed;
        _hasData = true;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        int w = ClientSize.Width;
        int h = ClientSize.Height;
        g.Clear(Theme.BgColor);
        if (!_hasData || w < 200 || h < 200) return;

        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        int pad = 12;
        int gap = 10;
        int leftW = (w - pad * 2 - gap) * 3 / 5;
        int rightW = w - pad * 2 - gap - leftW;
        int rightX = pad + leftW + gap;

        // === Memory State card (top-left) ===
        int memCardH = 200;
        var memRect = new Rectangle(pad, pad, leftW, memCardH);
        DrawCardBg(g, memRect, "Memory State");

        int y = memRect.Y + 28;
        int x = memRect.X + 10;

        // Big value
        float usedGB = _snap.InUseGB + _snap.ModifiedMB / 1024f;
        var memColor = Theme.PercentToColor(_snap.UsedPercent);
        string memText = $"{_snap.UsedPercent:F0}%";
        g.DrawString(memText, BigValueFont, new SolidBrush(memColor), x, y);

        // Progress bar
        int barX = x + 90;
        int barW = memRect.Width - 120;
        int barH = 16;
        int barY = y + 10;
        using (var bgBrush = new SolidBrush(Color.FromArgb(40, 40, 45)))
            g.FillRectangle(bgBrush, barX, barY, barW, barH);
        int fillW = (int)(barW * Math.Clamp(_snap.UsedPercent / 100f, 0, 1));
        if (fillW > 0)
            using (var fillBrush = new SolidBrush(memColor))
                g.FillRectangle(fillBrush, barX, barY, fillW, barH);

        y += 46;

        DrawLabelValue(g, x, y, "Physical:", $"{usedGB:F1} / {_snap.TotalGB:F1} GB", memColor);
        y += 18;
        DrawLabelValue(g, x, y, "Commit:", $"{_snap.CommitUsedGB:F1} / {_snap.CommitLimitGB:F1} GB ({_snap.CommitPercent:F0}%)",
            Theme.PercentToColor(_snap.CommitPercent));
        y += 18;
        DrawLabelValue(g, x, y, "Standby:", $"{FormatMB(_snap.StandbyMB)}", Theme.TextDim);
        DrawLabelValue(g, x + 150, y, "Modified:", $"{FormatMB(_snap.ModifiedMB)}", Theme.TextDim);
        DrawLabelValue(g, x + 310, y, "Free:", $"{FormatMB(_snap.FreeMB)}", Theme.TextDim);
        y += 18;
        DrawLabelValue(g, x, y, "Pools:", $"{FormatMB(_snap.PoolNonpagedMB)} NP / {FormatMB(_snap.PoolPagedMB)} P", Theme.TextDim);
        y += 18;
        string balloonText = _balloonInflated ? $"{FormatBytes(_balloonReclaimed)} reclaimed" : "idle";
        DrawLabelValue(g, x, y, "Balloon:", balloonText,
            _balloonInflated ? Color.FromArgb(220, 160, 40) : Theme.TextDim);
        y += 18;
        DrawLabelValue(g, x, y, "Pagefile:", $"{_snap.PagefileUsagePercent:F0}%",
            _snap.PagefileUsagePercent > 50 ? Theme.PercentToColor(_snap.PagefileUsagePercent) : Theme.TextDim);

        // === Memory Offender card (below Memory State) ===
        int offenderY = memRect.Bottom + gap;
        int offenderH = 50;
        var offenderRect = new Rectangle(pad, offenderY, leftW, offenderH);
        DrawCardBg(g, offenderRect, "Top Memory Consumer");

        var topMem = _processes.OrderByDescending(p => p.PrivateBytes).FirstOrDefault();
        if (topMem.Name != null)
        {
            var offColor = Theme.PercentToColor((float)Math.Min(topMem.PrivateBytes / (1024.0 * 1024 * 1024) * 30, 100));
            DrawLabelValue(g, offenderRect.X + 10, offenderRect.Y + 26,
                $"{topMem.Name}:", FormatBytes(topMem.PrivateBytes), offColor);
        }

        // === Stats table card (below offender) ===
        int statsY = offenderRect.Bottom + gap;
        int statsH = MetricTracker.Windows.Length * 22 + 50;
        var statsRect = new Rectangle(pad, statsY, leftW, statsH);
        DrawCardBg(g, statsRect, "RAM % Statistics");

        if (_ramTracker != null)
        {
            DrawStatsTable(g, statsRect.X + 10, statsRect.Y + 28, statsRect.Width - 20, _ramTracker);
        }

        // === Mini memory graph (right panel) ===
        var graphRect = new Rectangle(rightX, pad, rightW, h - pad * 2);
        DrawCardBg(g, graphRect, "Memory (last 5 min)");
        DrawMiniGraph(g, new Rectangle(graphRect.X + 8, graphRect.Y + 30, graphRect.Width - 16, graphRect.Height - 40));
    }

    private void DrawMiniGraph(Graphics g, Rectangle r)
    {
        if (_ringBuffer == null || _ringBuffer.Count < 2) return;

        // Show last 300 samples (5 min at 1s)
        int maxSamples = Math.Min(_ringBuffer.Count, 300);
        int startIdx = _ringBuffer.Count - maxSamples;

        float xStep = (float)r.Width / (maxSamples - 1);

        // Grid
        using var gridPen = new Pen(Theme.Border, 1);
        using var gridFont = new Font("Segoe UI", 7f);
        using var gridBrush = new SolidBrush(Theme.TextDim);
        for (int pct = 25; pct <= 100; pct += 25)
        {
            int gy = r.Y + r.Height - (int)(pct / 100f * r.Height);
            g.DrawLine(gridPen, r.X, gy, r.Right, gy);
            g.DrawString($"{pct}%", gridFont, gridBrush, r.X, gy + 1);
        }

        // Build points
        var usedPts = new PointF[maxSamples];
        var commitPts = new PointF[maxSamples];
        for (int i = 0; i < maxSamples; i++)
        {
            var snap = _ringBuffer[startIdx + i];
            float x = r.X + i * xStep;
            usedPts[i] = new PointF(x, r.Y + r.Height * (1f - snap.UsedPercent / 100f));
            commitPts[i] = new PointF(x, r.Y + r.Height * (1f - Math.Min(snap.CommitPercent, 100f) / 100f));
        }

        // Fill used area
        DrawArea(g, usedPts, r.Bottom, Color.FromArgb(120, 60, 130, 210));

        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var commitPen = new Pen(Color.FromArgb(200, 240, 120, 50), 1.5f))
            g.DrawLines(commitPen, commitPts);
        g.SmoothingMode = SmoothingMode.None;
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

    private void DrawStatsTable(Graphics g, int x, int y, int w, MetricTracker tracker)
    {
        using var dimBrush = new SolidBrush(Theme.TextDim);
        int colRange = x;
        int colAvg = x + 50;
        int colMin = x + 120;
        int colMax = x + 190;

        g.DrawString("Range", SmallFont, dimBrush, colRange, y);
        g.DrawString("Avg", SmallFont, dimBrush, colAvg, y);
        g.DrawString("Min", SmallFont, dimBrush, colMin, y);
        g.DrawString("Max", SmallFont, dimBrush, colMax, y);
        y += 18;

        using var sepPen = new Pen(Theme.Border);
        g.DrawLine(sepPen, x, y, x + w, y);
        y += 4;

        for (int i = 0; i < MetricTracker.Windows.Length; i++)
        {
            var stats = tracker.GetStats(MetricTracker.Windows[i]);
            if (stats.SampleCount == 0) continue;

            g.DrawString(MetricTracker.WindowLabels[i], TableFont, dimBrush, colRange, y);
            using var avgBrush = new SolidBrush(Theme.PercentToColor(stats.Avg));
            g.DrawString($"{stats.Avg:F1}%", TableFont, avgBrush, colAvg, y);
            using var minBrush = new SolidBrush(Color.FromArgb(50, 200, 60));
            g.DrawString($"{stats.Min:F1}%", TableFont, minBrush, colMin, y);
            using var maxBrush = new SolidBrush(Theme.PercentToColor(stats.Max));
            g.DrawString($"{stats.Max:F1}%", TableFont, maxBrush, colMax, y);
            y += 22;
        }
    }

    private void DrawCardBg(Graphics g, Rectangle r, string title)
    {
        using var bgBrush = new SolidBrush(Theme.CardBg);
        using var borderPen = new Pen(Theme.Border);
        g.FillRectangle(bgBrush, r);
        g.DrawRectangle(borderPen, r);
        using var titleBrush = new SolidBrush(Theme.TextLabel);
        g.DrawString(title, TitleFont, titleBrush, r.X + 8, r.Y + 6);
    }

    private void DrawLabelValue(Graphics g, int x, int y, string label, string value, Color color)
    {
        using var labelBrush = new SolidBrush(Theme.TextDim);
        g.DrawString(label, LabelFont, labelBrush, x, y);
        float lw = g.MeasureString(label, LabelFont).Width;
        using var valBrush = new SolidBrush(color);
        g.DrawString(value, ValueFont, valBrush, x + lw + 2, y);
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        if (bytes >= 1024L * 1024) return $"{bytes / (1024.0 * 1024):F0} MB";
        return $"{bytes / 1024.0:F0} KB";
    }

    private static string FormatMB(float mb)
    {
        if (mb >= 1024f) return $"{mb / 1024f:F1} GB";
        if (mb >= 10f) return $"{mb:F0} MB";
        return $"{mb:F1} MB";
    }
}
