using System.Drawing;
using System.Drawing.Drawing2D;
using ResourceMonitor.Models;

namespace ResourceMonitor.Controls;

public sealed class CpuDetailPanel : Control
{
    private float _cpuPercent;
    private MetricTracker? _cpuTracker;
    private MetricTracker? _diskTracker;
    private List<ProcessInfo> _processes = [];
    private DiskSnapshot _diskSnap;
    private List<NetworkSnapshot> _netSnaps = [];
    private TcpConnectionSnapshot _tcpSnap;
    private List<PingResult> _pingResults = [];
    private ProcessIoSnapshot? _topIo;
    private bool _hasData;

    private static readonly Font TitleFont = new("Segoe UI", 10f, FontStyle.Bold);
    private static readonly Font LabelFont = new("Segoe UI", 9f);
    private static readonly Font ValueFont = new("Consolas", 9.5f, FontStyle.Bold);
    private static readonly Font BigValueFont = new("Consolas", 22f, FontStyle.Bold);
    private static readonly Font SmallFont = new("Segoe UI", 8f);
    private static readonly Font TableFont = new("Consolas", 8.5f);

    public CpuDetailPanel()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.UserPaint
            | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.BgColor;
    }

    public void Update(float cpuPercent, MetricTracker cpuTracker, MetricTracker diskTracker,
        List<ProcessInfo> processes, DiskSnapshot diskSnap,
        List<NetworkSnapshot> netSnaps, TcpConnectionSnapshot tcpSnap,
        List<PingResult> pingResults, ProcessIoSnapshot? topIo)
    {
        _cpuPercent = cpuPercent;
        _cpuTracker = cpuTracker;
        _diskTracker = diskTracker;
        _processes = processes;
        _diskSnap = diskSnap;
        _netSnaps = netSnaps;
        _tcpSnap = tcpSnap;
        _pingResults = pingResults;
        _topIo = topIo;
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

        // === CPU Load card (top-left) ===
        int cpuCardH = 60 + MetricTracker.Windows.Length * 22 + 20;
        var cpuRect = new Rectangle(pad, pad, leftW, cpuCardH);
        DrawCardBg(g, cpuRect, "CPU Load");

        int y = cpuRect.Y + 28;
        var cpuColor = Theme.PercentToColor(_cpuPercent);

        // Big current value
        string cpuText = $"{_cpuPercent:F0}%";
        g.DrawString(cpuText, BigValueFont, new SolidBrush(cpuColor), cpuRect.X + 12, y);

        // Progress bar
        int barX = cpuRect.X + 100;
        int barW = cpuRect.Width - 120;
        int barH = 16;
        int barY = y + 10;
        using (var bgBrush = new SolidBrush(Color.FromArgb(40, 40, 45)))
            g.FillRectangle(bgBrush, barX, barY, barW, barH);
        int fillW = (int)(barW * Math.Clamp(_cpuPercent / 100f, 0, 1));
        if (fillW > 0)
            using (var fillBrush = new SolidBrush(cpuColor))
                g.FillRectangle(fillBrush, barX, barY, fillW, barH);

        y += 46;

        // Stats table
        if (_cpuTracker != null)
        {
            DrawStatsTable(g, cpuRect.X + 10, y, cpuRect.Width - 20, _cpuTracker);
        }

        // === Top 5 CPU processes (bottom-left) ===
        int procY = cpuRect.Bottom + gap;
        int procH = Math.Max(140, h - procY - pad);
        var procRect = new Rectangle(pad, procY, leftW, procH);
        DrawCardBg(g, procRect, "Top Processes by CPU");

        y = procRect.Y + 28;
        var topCpu = _processes.OrderByDescending(p => p.CpuPercent).Take(5);
        int rank = 1;
        foreach (var p in topCpu)
        {
            if (p.CpuPercent < 0.1) break;
            if (y + 18 > procRect.Bottom - 4) break;

            var pColor = Theme.PercentToColor((float)Math.Min(p.CpuPercent * 1.5, 100));
            using var dimBrush = new SolidBrush(Theme.TextDim);
            g.DrawString($"{rank}.", SmallFont, dimBrush, procRect.X + 10, y);
            using var nameBrush = new SolidBrush(Theme.TextBright);
            g.DrawString(p.Name, LabelFont, nameBrush, procRect.X + 28, y);
            using var valBrush = new SolidBrush(pColor);
            g.DrawString($"{p.CpuPercent:F1}%", ValueFont, valBrush, procRect.X + 200, y);
            using var handleBrush = new SolidBrush(Theme.TextDim);
            g.DrawString($"{p.HandleCount:N0} handles", SmallFont, handleBrush, procRect.X + 270, y + 1);
            y += 22;
            rank++;
        }

        // === Network & Disk card (right) ===
        var netRect = new Rectangle(rightX, pad, rightW, h - pad * 2);
        DrawCardBg(g, netRect, "Network & Disk");

        y = netRect.Y + 28;
        int x = netRect.X + 10;

        // WAN
        var wan = _netSnaps.FirstOrDefault(n => n.Role == NetworkRole.WAN);
        DrawLabel(g, x, y, "WAN:");
        DrawValue(g, x + 50, y, $"\u25b2{FormatRate(wan.SendRateKBps)}  \u25bc{FormatRate(wan.ReceiveRateKBps)}",
            Theme.PercentToColor(Services.NetworkMonitorService.RateToPercent(wan.TotalRateKBps, NetworkRole.WAN)));
        y += 20;

        // DB
        var db = _netSnaps.FirstOrDefault(n => n.Role == NetworkRole.DB);
        DrawLabel(g, x, y, "DB:");
        DrawValue(g, x + 50, y, $"\u25b2{FormatRate(db.SendRateKBps)}  \u25bc{FormatRate(db.ReceiveRateKBps)}",
            Theme.PercentToColor(Services.NetworkMonitorService.RateToPercent(db.TotalRateKBps, NetworkRole.DB)));
        y += 20;

        // TCP
        DrawLabel(g, x, y, "TCP:");
        DrawValue(g, x + 50, y, $"{_tcpSnap.TotalEstablished} est / {_tcpSnap.TotalTimeWait} tw", Theme.TextDim);
        y += 20;

        // Ping
        foreach (var ping in _pingResults)
        {
            string val = ping.Success ? $"{ping.RoundtripMs}ms" : "FAIL";
            Color pc = !ping.Success ? Color.FromArgb(220, 50, 40) :
                ping.RoundtripMs < 10 ? Color.FromArgb(50, 200, 60) :
                ping.RoundtripMs < 50 ? Color.FromArgb(220, 220, 40) :
                Color.FromArgb(220, 50, 40);
            DrawLabel(g, x, y, $"{ping.Label}:");
            DrawValue(g, x + 50, y, val, pc);
            y += 20;
        }

        y += 10;

        // Disk separator
        using (var sepPen = new Pen(Theme.Border))
            g.DrawLine(sepPen, x, y, netRect.Right - 10, y);
        y += 8;

        // Disk
        var diskColor = Theme.PercentToColor(_diskSnap.DiskTimePct);
        DrawLabel(g, x, y, "Active:");
        DrawValue(g, x + 60, y, $"{_diskSnap.DiskTimePct:F0}%", diskColor);
        y += 20;
        DrawLabel(g, x, y, "Read:");
        DrawValue(g, x + 60, y, FormatRate(_diskSnap.ReadBytesPerSec / 1024), Theme.TextDim);
        y += 20;
        DrawLabel(g, x, y, "Write:");
        DrawValue(g, x + 60, y, FormatRate(_diskSnap.WriteBytesPerSec / 1024), Theme.TextDim);
        y += 20;

        // Top I/O
        if (_topIo is { } io && io.TotalRateKBps > 1)
        {
            DrawLabel(g, x, y, "Top I/O:");
            DrawValue(g, x + 60, y, $"{io.Name} {FormatRate(io.TotalRateKBps)}",
                Color.FromArgb(220, 180, 60));
        }
    }

    private void DrawStatsTable(Graphics g, int x, int y, int w, MetricTracker tracker)
    {
        // Header
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

    private void DrawLabel(Graphics g, int x, int y, string text)
    {
        using var brush = new SolidBrush(Theme.TextDim);
        g.DrawString(text, LabelFont, brush, x, y);
    }

    private void DrawValue(Graphics g, int x, int y, string text, Color color)
    {
        using var brush = new SolidBrush(color);
        g.DrawString(text, ValueFont, brush, x, y);
    }

    private static string FormatRate(double kbps)
    {
        if (kbps >= 1024) return $"{kbps / 1024:F1} MB/s";
        if (kbps >= 1) return $"{kbps:F0} KB/s";
        return "0";
    }
}
