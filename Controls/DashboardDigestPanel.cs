using System.Drawing;
using System.Drawing.Drawing2D;
using ResourceMonitor.Models;

namespace ResourceMonitor.Controls;

public sealed class DashboardDigestPanel : Control
{
    // Cached data for painting
    private SystemSnapshot _snap;
    private List<ProcessInfo> _processes = [];
    private List<NetworkSnapshot> _netSnaps = [];
    private DiskSnapshot _diskSnap;
    private List<WebsiteCheck> _siteChecks = [];
    private TcpConnectionSnapshot _tcpSnap;
    private List<PingResult> _pingResults = [];
    private int _totalHandles;
    private bool _balloonInflated;
    private long _balloonReclaimed;
    private int _oomCount, _crashCount, _criticalCount;
    private bool _hasData;
    private RingBuffer<SystemSnapshot>? _ringBuffer;
    private ProcessIoSnapshot? _topIo;

    private static readonly Font TitleFont = new("Segoe UI", 9.5f, FontStyle.Bold);
    private static readonly Font LabelFont = new("Segoe UI", 8.5f);
    private static readonly Font ValueFont = new("Consolas", 9f, FontStyle.Bold);
    private static readonly Font SmallFont = new("Segoe UI", 7.5f);

    public DashboardDigestPanel()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.UserPaint
            | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.BgColor;
    }

    public void SetBuffer(RingBuffer<SystemSnapshot> buffer) => _ringBuffer = buffer;

    public void Update(SystemSnapshot snap, List<ProcessInfo> processes,
        List<NetworkSnapshot> netSnaps, DiskSnapshot diskSnap,
        List<WebsiteCheck> siteChecks, TcpConnectionSnapshot tcpSnap,
        List<PingResult> pingResults, int totalHandles,
        bool balloonInflated, long balloonReclaimed,
        int oomCount, int crashCount, int criticalCount,
        ProcessIoSnapshot? topIo = null)
    {
        _snap = snap;
        _processes = processes;
        _netSnaps = netSnaps;
        _diskSnap = diskSnap;
        _siteChecks = siteChecks;
        _tcpSnap = tcpSnap;
        _pingResults = pingResults;
        _totalHandles = totalHandles;
        _balloonInflated = balloonInflated;
        _balloonReclaimed = balloonReclaimed;
        _oomCount = oomCount;
        _crashCount = crashCount;
        _criticalCount = criticalCount;
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

        if (!_hasData || w < 100 || h < 100) return;

        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        // 3×2 card grid with padding
        int pad = 10;
        int gap = 8;
        int cols = w > 700 ? 3 : (w > 400 ? 2 : 1);
        int cardW = (w - pad * 2 - gap * (cols - 1)) / cols;
        int cardH = (h - pad * 2 - gap) / 2;

        // Draw 6 cards
        DrawMemoryCard(g, CardRect(0, pad, gap, cardW, cardH, cols));
        DrawCpuCard(g, CardRect(1, pad, gap, cardW, cardH, cols));
        DrawNetworkCard(g, CardRect(2, pad, gap, cardW, cardH, cols));
        DrawDiskCard(g, CardRect(3, pad, gap, cardW, cardH, cols));
        DrawWebsitesCard(g, CardRect(4, pad, gap, cardW, cardH, cols));
        DrawEventsCard(g, CardRect(5, pad, gap, cardW, cardH, cols));
    }

    private static Rectangle CardRect(int index, int pad, int gap, int cardW, int cardH, int cols)
    {
        int col = index % cols;
        int row = index / cols;
        return new Rectangle(
            pad + col * (cardW + gap),
            pad + row * (cardH + gap),
            cardW, cardH);
    }

    private void DrawCardBackground(Graphics g, Rectangle r, string title)
    {
        using var bgBrush = new SolidBrush(Theme.CardBg);
        using var borderPen = new Pen(Theme.Border);
        g.FillRectangle(bgBrush, r);
        g.DrawRectangle(borderPen, r);

        using var titleBrush = new SolidBrush(Theme.TextLabel);
        g.DrawString(title, TitleFont, titleBrush, r.X + 8, r.Y + 6);
    }

    private void DrawMemoryCard(Graphics g, Rectangle r)
    {
        DrawCardBackground(g, r, "Memory");

        // Draw sparkline at bottom of card
        DrawMiniSparkline(g, r, i => _ringBuffer?[i].UsedPercent ?? 0, Theme.PercentToColor(_snap.UsedPercent));

        int y = r.Y + 26;
        int x = r.X + 10;

        float usedGB = _snap.InUseGB + _snap.ModifiedMB / 1024f;
        var memColor = Theme.PercentToColor(_snap.UsedPercent);

        DrawLabelValue(g, x, y, "Physical:", $"{usedGB:F1} / {_snap.TotalGB:F1} GB ({_snap.UsedPercent:F0}%)", memColor);
        y += 18;
        DrawLabelValue(g, x, y, "Commit:", $"{_snap.CommitUsedGB:F1} / {_snap.CommitLimitGB:F1} GB ({_snap.CommitPercent:F0}%)",
            Theme.PercentToColor(_snap.CommitPercent));
        y += 18;
        string balloonText = _balloonInflated ? $"{FormatBytes(_balloonReclaimed)} reclaimed" : "idle";
        DrawLabelValue(g, x, y, "Balloon:", balloonText, _balloonInflated ? Color.FromArgb(220, 160, 40) : Theme.TextDim);
        y += 18;
        DrawLabelValue(g, x, y, "Pagefile:", $"{_snap.PagefileUsagePercent:F0}%",
            _snap.PagefileUsagePercent > 50 ? Theme.PercentToColor(_snap.PagefileUsagePercent) : Theme.TextDim);
        y += 18;

        // Memory offender
        var topMem = _processes.OrderByDescending(p => p.PrivateBytes).FirstOrDefault();
        if (topMem.Name != null)
        {
            var offColor = Theme.PercentToColor((float)Math.Min(topMem.PrivateBytes / (1024.0 * 1024 * 1024) * 30, 100));
            DrawLabelValue(g, x, y, "Top:", $"{topMem.Name} ({FormatBytes(topMem.PrivateBytes)})", offColor);
        }
        y += 18;
        DrawLabelValue(g, x, y, "Pools:", $"{FormatMB(_snap.PoolNonpagedMB)} NP / {FormatMB(_snap.PoolPagedMB)} P", Theme.TextDim);
    }

    private void DrawCpuCard(Graphics g, Rectangle r)
    {
        DrawCardBackground(g, r, "CPU & Processes");

        // Draw sparkline at bottom
        DrawMiniSparkline(g, r, i => _ringBuffer?[i].CpuPercent ?? 0, Theme.PercentToColor(_snap.CpuPercent));

        int y = r.Y + 26;
        int x = r.X + 10;

        var cpuColor = Theme.PercentToColor(_snap.CpuPercent);
        DrawLabelValue(g, x, y, "CPU:", $"{_snap.CpuPercent:F0}%", cpuColor);
        y += 18;
        DrawLabelValue(g, x, y, "Handles:", $"{_totalHandles:N0} total", Theme.TextDim);
        y += 20;

        // Top 3 by CPU
        using var dimBrush = new SolidBrush(Theme.TextDim);
        g.DrawString("Top by CPU:", SmallFont, dimBrush, x, y);
        y += 15;

        var topCpu = _processes.OrderByDescending(p => p.CpuPercent).Take(3);
        foreach (var p in topCpu)
        {
            if (p.CpuPercent < 0.1) continue;
            var pColor = Theme.PercentToColor((float)Math.Min(p.CpuPercent * 1.2, 100));
            DrawLabelValue(g, x + 4, y, $"{p.Name}:", $"{p.CpuPercent:F1}%", pColor);
            y += 16;
        }
    }

    private void DrawNetworkCard(Graphics g, Rectangle r)
    {
        DrawCardBackground(g, r, "Network");
        int y = r.Y + 26;
        int x = r.X + 10;

        // WAN
        var wan = _netSnaps.FirstOrDefault(n => n.Role == NetworkRole.WAN);
        float wanPct = NetworkMonitorService.RateToPercent(wan.TotalRateKBps, NetworkRole.WAN);
        DrawLabelValue(g, x, y, "WAN:",
            $"\u25b2{FormatRate(wan.SendRateKBps)}  \u25bc{FormatRate(wan.ReceiveRateKBps)}",
            Theme.PercentToColor(wanPct));
        y += 18;

        // DB
        var db = _netSnaps.FirstOrDefault(n => n.Role == NetworkRole.DB);
        float dbPct = NetworkMonitorService.RateToPercent(db.TotalRateKBps, NetworkRole.DB);
        DrawLabelValue(g, x, y, "DB:",
            $"\u25b2{FormatRate(db.SendRateKBps)}  \u25bc{FormatRate(db.ReceiveRateKBps)}",
            Theme.PercentToColor(dbPct));
        y += 18;

        // TCP
        DrawLabelValue(g, x, y, "TCP:",
            $"{_tcpSnap.TotalEstablished} est / {_tcpSnap.TotalTimeWait} tw / {_tcpSnap.TotalAll} total",
            Theme.TextDim);
        y += 18;

        // Ping
        foreach (var ping in _pingResults)
        {
            string val = ping.Success ? $"{ping.RoundtripMs}ms" : "FAIL";
            Color pc = !ping.Success ? Color.FromArgb(220, 50, 40) :
                ping.RoundtripMs < 10 ? Color.FromArgb(50, 200, 60) :
                ping.RoundtripMs < 50 ? Color.FromArgb(220, 220, 40) :
                Color.FromArgb(220, 50, 40);
            DrawLabelValue(g, x, y, $"{ping.Label}:", val, pc);
            x += 90;
        }
    }

    private void DrawDiskCard(Graphics g, Rectangle r)
    {
        DrawCardBackground(g, r, "Disk");

        // Sparkline for disk active %
        DrawMiniSparkline(g, r, i =>
        {
            // Disk % not in SystemSnapshot — use last known value for most recent
            return _diskSnap.DiskTimePct;
        }, Theme.PercentToColor(_diskSnap.DiskTimePct));

        int y = r.Y + 26;
        int x = r.X + 10;

        var diskColor = Theme.PercentToColor(_diskSnap.DiskTimePct);
        DrawLabelValue(g, x, y, "Read:", FormatRate(_diskSnap.ReadBytesPerSec / 1024), Theme.TextDim);
        y += 18;
        DrawLabelValue(g, x, y, "Write:", FormatRate(_diskSnap.WriteBytesPerSec / 1024), Theme.TextDim);
        y += 18;
        DrawLabelValue(g, x, y, "Active:", $"{_diskSnap.DiskTimePct:F0}%", diskColor);
        y += 18;

        // Disk offender
        if (_topIo is { } io && io.TotalRateKBps > 1)
        {
            DrawLabelValue(g, x, y, "Top I/O:", $"{io.Name} ({FormatRate(io.TotalRateKBps)})",
                Color.FromArgb(220, 180, 60));
        }
    }

    private void DrawWebsitesCard(Graphics g, Rectangle r)
    {
        DrawCardBackground(g, r, "Websites");
        int y = r.Y + 26;
        int x = r.X + 10;

        if (_siteChecks.Count == 0)
        {
            using var dimBrush = new SolidBrush(Theme.TextDim);
            g.DrawString("No sites configured", LabelFont, dimBrush, x, y);
            return;
        }

        int up = _siteChecks.Count(s => s.IsUp);
        int down = _siteChecks.Count - up;
        var summaryColor = down > 0 ? Color.FromArgb(220, 50, 40) : Color.FromArgb(50, 200, 60);
        DrawLabelValue(g, x, y, "Status:", $"{up}/{_siteChecks.Count} up" + (down > 0 ? $", {down} DOWN" : ""), summaryColor);
        y += 20;

        foreach (var site in _siteChecks)
        {
            if (y + 16 > r.Bottom - 4) break;
            string marker = site.IsUp ? "\u2713" : "\u2717";
            string time = !site.IsUp ? "DOWN" :
                site.ResponseTimeMs >= 1000 ? $"{site.ResponseTimeMs / 1000.0:F1}s" :
                $"{site.ResponseTimeMs}ms";
            var color = Theme.WebsiteResponseColor(site.IsUp ? site.ResponseTimeMs : -1);
            DrawLabelValue(g, x, y, $"{marker} {site.Name}:", time, color);
            y += 16;
        }
    }

    private void DrawEventsCard(Graphics g, Rectangle r)
    {
        DrawCardBackground(g, r, "Events (48h)");
        int y = r.Y + 26;
        int x = r.X + 10;

        DrawLabelValue(g, x, y, "OOM warnings:", $"{_oomCount}",
            _oomCount > 0 ? Color.FromArgb(220, 50, 40) : Color.FromArgb(50, 200, 60));
        y += 18;
        DrawLabelValue(g, x, y, "App crashes:", $"{_crashCount}",
            _crashCount > 0 ? Color.FromArgb(240, 150, 30) : Color.FromArgb(50, 200, 60));
        y += 18;
        DrawLabelValue(g, x, y, "Critical:", $"{_criticalCount}",
            _criticalCount > 0 ? Color.FromArgb(220, 50, 40) : Color.FromArgb(50, 200, 60));
    }

    private void DrawMiniSparkline(Graphics g, Rectangle card, Func<int, float> getValue, Color lineColor)
    {
        if (_ringBuffer == null || _ringBuffer.Count < 2) return;

        int samples = Math.Min(_ringBuffer.Count, 60);
        int startIdx = _ringBuffer.Count - samples;
        int sparkH = 60;
        int sparkY = card.Bottom - sparkH - 4;
        int sparkX = card.X + 4;
        int sparkW = card.Width - 8;

        if (sparkW < 10) return;

        float xStep = (float)sparkW / (samples - 1);
        var pts = new PointF[samples];
        for (int i = 0; i < samples; i++)
        {
            float val = Math.Clamp(getValue(startIdx + i), 0, 100);
            pts[i] = new PointF(sparkX + i * xStep, sparkY + sparkH * (1f - val / 100f));
        }

        // Fill area
        var poly = new PointF[samples + 2];
        Array.Copy(pts, poly, samples);
        poly[samples] = new PointF(pts[^1].X, sparkY + sparkH);
        poly[samples + 1] = new PointF(pts[0].X, sparkY + sparkH);
        using var fillBrush = new SolidBrush(Color.FromArgb(25, lineColor));
        g.FillPolygon(fillBrush, poly);

        // Line
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var pen = new Pen(Color.FromArgb(80, lineColor), 1.2f);
        g.DrawLines(pen, pts);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
    }

    private void DrawLabelValue(Graphics g, int x, int y, string label, string value, Color valueColor)
    {
        using var labelBrush = new SolidBrush(Theme.TextDim);
        g.DrawString(label, LabelFont, labelBrush, x, y);
        float labelWidth = g.MeasureString(label, LabelFont).Width;
        using var valueBrush = new SolidBrush(valueColor);
        g.DrawString(value, ValueFont, valueBrush, x + labelWidth + 2, y);
    }

    // Using Services namespace for RateToPercent — import the static method reference
    private static class NetworkMonitorService
    {
        public static float RateToPercent(double totalRateKBps, NetworkRole role) =>
            Services.NetworkMonitorService.RateToPercent(totalRateKBps, role);
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

    private static string FormatRate(double kbps)
    {
        if (kbps >= 1024) return $"{kbps / 1024:F1} MB/s";
        if (kbps >= 1) return $"{kbps:F0} KB/s";
        return "0";
    }
}
