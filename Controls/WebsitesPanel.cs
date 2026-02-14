using System.Drawing;
using System.Drawing.Drawing2D;
using ResourceMonitor.Models;

namespace ResourceMonitor.Controls;

public sealed class WebsitesPanel : Control
{
    private List<WebsiteCheck> _checks = [];
    private Dictionary<string, RingBuffer<WebsiteCheck>> _history = [];

    private static readonly Font TitleFont = new("Segoe UI", 10f, FontStyle.Bold);
    private static readonly Font LabelFont = new("Segoe UI", 9f);
    private static readonly Font ValueFont = new("Consolas", 9.5f, FontStyle.Bold);
    private static readonly Font SmallFont = new("Consolas", 7.5f);

    public WebsitesPanel()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.UserPaint
            | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.BgColor;
    }

    public void Update(List<WebsiteCheck> checks, Dictionary<string, RingBuffer<WebsiteCheck>> history)
    {
        _checks = checks;
        _history = history;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        int w = ClientSize.Width;
        int h = ClientSize.Height;
        g.Clear(Theme.BgColor);

        if (w < 100 || h < 50) return;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        int pad = 12;
        int y = pad;

        using var titleBrush = new SolidBrush(Theme.TextLabel);
        g.DrawString("Website Monitoring", TitleFont, titleBrush, pad, y);
        y += 28;

        if (_checks.Count == 0)
        {
            using var dimBrush = new SolidBrush(Theme.TextDim);
            g.DrawString("No sites configured. Edit config/websites.txt to add sites.", LabelFont, dimBrush, pad, y);
            return;
        }

        // Column headers
        int colName = pad;
        int colStatus = 180;
        int colTime = 260;
        int colLastCheck = 360;
        int colSparkline = 500;

        using var headerBrush = new SolidBrush(Theme.TextDim);
        g.DrawString("Site", SmallFont, headerBrush, colName, y);
        g.DrawString("Status", SmallFont, headerBrush, colStatus, y);
        g.DrawString("Response", SmallFont, headerBrush, colTime, y);
        g.DrawString("Last Check", SmallFont, headerBrush, colLastCheck, y);
        g.DrawString("History (last 60)", SmallFont, headerBrush, colSparkline, y);
        y += 18;

        // Separator
        using var sepPen = new Pen(Theme.Border);
        g.DrawLine(sepPen, pad, y, w - pad, y);
        y += 4;

        foreach (var site in _checks)
        {
            if (y + 30 > h) break;

            // Row background card
            var rowRect = new Rectangle(pad - 2, y - 2, w - pad * 2 + 4, 28);
            using var rowBrush = new SolidBrush(Theme.CardBg);
            g.FillRectangle(rowBrush, rowRect);

            // Site name
            using var nameBrush = new SolidBrush(Theme.TextBright);
            g.DrawString(site.Name, LabelFont, nameBrush, colName, y + 4);

            // Status
            string statusText = site.IsUp ? "UP" : "DOWN";
            var statusColor = site.IsUp ? Color.FromArgb(50, 200, 60) : Color.FromArgb(220, 50, 40);
            using var statusBrush = new SolidBrush(statusColor);
            g.DrawString(statusText, ValueFont, statusBrush, colStatus, y + 3);

            // Response time
            if (site.IsUp)
            {
                string timeText = site.ResponseTimeMs >= 1000
                    ? $"{site.ResponseTimeMs / 1000.0:F1}s"
                    : $"{site.ResponseTimeMs}ms";
                var timeColor = Theme.WebsiteResponseColor(site.ResponseTimeMs);
                using var timeBrush = new SolidBrush(timeColor);
                g.DrawString(timeText, ValueFont, timeBrush, colTime, y + 3);
            }
            else
            {
                using var failBrush = new SolidBrush(Color.FromArgb(220, 50, 40));
                g.DrawString("---", ValueFont, failBrush, colTime, y + 3);
            }

            // Last check time
            using var checkBrush = new SolidBrush(Theme.TextDim);
            g.DrawString(site.Timestamp.ToString("HH:mm:ss"), LabelFont, checkBrush, colLastCheck, y + 4);

            // Sparkline
            if (_history.TryGetValue(site.Name, out var buf) && buf.Count >= 2)
            {
                DrawSparkline(g, colSparkline, y + 4, Math.Min(w - colSparkline - pad, 200), 18, buf);
            }

            y += 32;
        }
    }

    private static void DrawSparkline(Graphics g, int x, int y, int w, int h, RingBuffer<WebsiteCheck> buf)
    {
        if (w < 10 || buf.Count < 2) return;

        // Find max for scaling
        int maxMs = 100;
        for (int i = 0; i < buf.Count; i++)
        {
            int ms = buf[i].ResponseTimeMs;
            if (ms > 0 && ms > maxMs) maxMs = ms;
        }
        maxMs = Math.Min(maxMs + 50, 5000); // cap at 5s

        g.SmoothingMode = SmoothingMode.AntiAlias;

        float step = (float)w / (buf.Count - 1);
        var points = new PointF[buf.Count];
        for (int i = 0; i < buf.Count; i++)
        {
            int ms = buf[i].ResponseTimeMs;
            float val = ms > 0 ? ms : maxMs; // failed = max height
            float py = y + h - (val / maxMs * h);
            points[i] = new PointF(x + i * step, Math.Clamp(py, y, y + h));
        }

        // Draw line with gradient coloring — use last point's color
        var lastCheck = buf[buf.Count - 1];
        var lineColor = Theme.WebsiteResponseColor(lastCheck.IsUp ? lastCheck.ResponseTimeMs : -1);
        using var pen = new Pen(lineColor, 1.5f);
        g.DrawLines(pen, points);

        // Draw dots for failures
        for (int i = 0; i < buf.Count; i++)
        {
            if (!buf[i].IsUp)
            {
                using var failBrush = new SolidBrush(Color.FromArgb(220, 50, 40));
                g.FillEllipse(failBrush, points[i].X - 2, points[i].Y - 2, 4, 4);
            }
        }

        g.SmoothingMode = SmoothingMode.None;
    }
}
