using System.Diagnostics;
using ResourceMonitor.Controls;
using ResourceMonitor.Models;
using ResourceMonitor.Services;

namespace ResourceMonitor;

public partial class MainForm : Form
{
    private readonly SystemMemoryService _memoryService = new();
    private readonly ProcessMonitorService _processService = new();
    private readonly BalloonService _balloonService = new();
    private readonly EventLogService _eventLogService = new();
    private readonly NetworkMonitorService _networkService = new();
    private readonly DiskMonitorService _diskService = new();
    private readonly WebsiteMonitorService _websiteService = new();
    private readonly PingMonitorService _pingService = new();
    private readonly ProcessIoService _ioService = new();
    private readonly RingBuffer<SystemSnapshot> _ringBuffer = new(600);

    private readonly MetricTracker _cpuTracker = new();
    private readonly MetricTracker _ramTracker = new();
    private readonly MetricTracker _diskTracker = new();

    private System.Threading.Timer? _timer;
    private int _intervalMs = 1000;
    private int _tickCount;
    private const int EventLogEveryN = 60;
    private const int PingEveryN = 10; // ping every 10 ticks

    private List<ProcessInfo> _processes = [];
    private int _sortColumn = 3; // Private bytes
    private bool _sortAscending;
    private int _lastOomCount;
    private int _lastCrashCount;
    private int _lastCriticalCount;
    private List<PingResult> _lastPingResults = [];
    private ProcessIoSnapshot? _lastTopIo;

    public MainForm()
    {
        InitializeComponent();
        _graphPanel.SetBuffer(_ringBuffer);
        _memoryPanel.SetBuffer(_ringBuffer);
        _digestPanel.SetBuffer(_ringBuffer);

        // Try IIS auto-discovery first
        _websiteService.LoadFromIis();

        // Load website config (overrides/merges with IIS)
        string configDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "config");
        string configPath = Path.Combine(configDir, "websites.txt");
        if (!File.Exists(configPath))
            configPath = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath)!, "..", "..", "..", "..", "config", "websites.txt");
        _websiteService.LoadConfig(configPath);

        Load += (_, _) =>
        {
            EnableDarkTitleBar();

            _balloonLabel.Text = _balloonService.DriverDetected
                ? "Balloon: detected"
                : "Balloon: not detected";

            if (_websiteService.SiteCount > 0)
                _websiteService.Start(60);

            _timer = new System.Threading.Timer(OnTick, null, 0, _intervalMs);
        };
    }

    private void OnTick(object? state)
    {
        try
        {
            var snapshot = _memoryService.GetSnapshot();
            var processes = _processService.GetProcesses();
            int totalHandles = _processService.TotalHandleCount;
            var netSnaps = _networkService.GetSnapshots();
            var diskSnap = _diskService.GetSnapshot();
            var tcpSnap = NetworkMonitorService.GetTcpSnapshot();

            int oomCount = -1, crashCount = -1, criticalCount = -1;
            int tick = Interlocked.Increment(ref _tickCount);
            if (tick == 1 || tick % EventLogEveryN == 0)
            {
                (oomCount, crashCount, criticalCount) = _eventLogService.GetCounts();
            }

            List<PingResult>? pingResults = null;
            if (tick == 1 || tick % PingEveryN == 0)
            {
                pingResults = _pingService.Ping();
            }

            // I/O tracking (every tick, but lightweight)
            ProcessIoSnapshot? topIo = null;
            try { topIo = _ioService.GetTopIoProcess(); } catch { }

            var siteChecks = _websiteService.GetLatestChecks();

            if (IsHandleCreated && !IsDisposed)
            {
                BeginInvoke(() => UpdateUI(snapshot, processes, totalHandles, netSnaps,
                    diskSnap, tcpSnap, siteChecks, oomCount, crashCount, criticalCount, pingResults, topIo));
            }
        }
        catch { }
    }

    private void UpdateUI(SystemSnapshot snap, List<ProcessInfo> processes, int totalHandles,
        List<NetworkSnapshot> netSnaps, DiskSnapshot diskSnap, TcpConnectionSnapshot tcpSnap,
        List<WebsiteCheck> siteChecks, int oomCount, int crashCount, int criticalCount,
        List<PingResult>? pingResults, ProcessIoSnapshot? topIo)
    {
        _ringBuffer.Add(snap);

        // Record metrics for statistics
        _cpuTracker.Record(snap.CpuPercent);
        _ramTracker.Record(snap.UsedPercent);
        _diskTracker.Record(diskSnap.DiskTimePct);

        if (topIo != null)
            _lastTopIo = topIo;

        // === LED bar ===
        _ledBar.LedCpu.SetValue(snap.CpuPercent);
        _ledBar.LedRam.SetValue(snap.UsedPercent);
        _ledBar.LedDisk.SetValue(diskSnap.DiskTimePct);

        // Network LEDs — find WAN/DB role snapshots
        var wanSnap = netSnaps.FirstOrDefault(n => n.Role == NetworkRole.WAN);
        var dbSnap = netSnaps.FirstOrDefault(n => n.Role == NetworkRole.DB);

        if (wanSnap.Name != null)
            _ledBar.LedNetWan.SetValue(NetworkMonitorService.RateToPercent(wanSnap.TotalRateKBps, NetworkRole.WAN));
        else
            _ledBar.LedNetWan.SetNoData();

        if (dbSnap.Name != null)
            _ledBar.LedNetDb.SetValue(NetworkMonitorService.RateToPercent(dbSnap.TotalRateKBps, NetworkRole.DB));
        else
            _ledBar.LedNetDb.SetNoData();

        // DB CPU/RAM stay as N/A (placeholders for future DB server monitoring)

        // Website health LED — worst site drives the color
        if (siteChecks.Count > 0)
        {
            float worstScore = 0;
            foreach (var site in siteChecks)
            {
                float score = !site.IsUp ? 95f
                    : site.ResponseTimeMs >= 1000 ? 80f
                    : site.ResponseTimeMs >= 500 ? 65f
                    : site.ResponseTimeMs >= 200 ? 40f
                    : site.ResponseTimeMs >= 80 ? 25f
                    : 5f;
                if (score > worstScore) worstScore = score;
            }
            _ledBar.LedWeb.SetValue(worstScore);
        }
        else
        {
            _ledBar.LedWeb.SetNoData();
        }

        // === Ping ===
        if (pingResults != null)
            _lastPingResults = pingResults;

        // === Balloon ===
        var (inflated, reclaimedBytes) = _balloonService.Check(snap.TotalPhysicalBytes);
        _balloonLabel.Text = _balloonService.DriverDetected
            ? (inflated ? $"Balloon: {FormatBytes(reclaimedBytes)} reclaimed" : "Balloon: idle")
            : "Balloon: not detected";

        // === Event counts ===
        if (oomCount >= 0)
        {
            _lastOomCount = oomCount;
            _lastCrashCount = crashCount;
            _lastCriticalCount = criticalCount;
        }
        _oomLabel.Text = $"OOM: {_lastOomCount} | Crashes: {_lastCrashCount} | Critical: {_lastCriticalCount}";

        // === Dashboard digest ===
        _digestPanel.Update(snap, processes, netSnaps, diskSnap, siteChecks,
            tcpSnap, _lastPingResults, totalHandles,
            inflated, reclaimedBytes,
            _lastOomCount, _lastCrashCount, _lastCriticalCount, _lastTopIo);

        // === CPU detail panel ===
        _cpuPanel.Update(snap.CpuPercent, _cpuTracker, _diskTracker,
            processes, diskSnap, netSnaps, tcpSnap, _lastPingResults, _lastTopIo);

        // === Memory detail panel ===
        _memoryPanel.Update(snap, _ramTracker, processes, inflated, reclaimedBytes);

        // === Websites panel ===
        _websitesPanel.Update(siteChecks, _websiteService.GetHistory());

        // === Alert title bar ===
        bool memPressure = snap.AvailableMB < 500 || snap.UsedPercent > 90;
        bool cpuPressure = snap.CpuPercent > 85;
        bool pagePressure = snap.PagesOutputPerSec > 100;
        bool siteDown = siteChecks.Any(s => !s.IsUp);
        if (memPressure || cpuPressure || pagePressure || siteDown)
        {
            string alert = memPressure ? " [LOW MEM]" : "";
            alert += cpuPressure ? " [HIGH CPU]" : "";
            alert += pagePressure ? " [PAGING]" : "";
            alert += siteDown ? " [SITE DOWN]" : "";
            Text = $"Resource Monitor \u2014{alert}";
        }
        else
        {
            Text = "Resource Monitor";
        }

        // === Process list ===
        SortProcesses(processes);
        _processes = processes;
        int newCount = _processes.Count;
        if (_processListView.VirtualListSize != newCount)
            _processListView.VirtualListSize = newCount;
        _processListView.Invalidate();

        // === Graph ===
        _graphPanel.Invalidate();
    }

    private void SetInterval(int ms)
    {
        _intervalMs = ms;
        _timer?.Change(0, ms);
        _refreshLabel.Text = $"Refresh: {ms / 1000}s";
        _ledBar.SetSmoothingWindow(ms);
    }

    private void OnRetrieveVirtualItem(object? sender, RetrieveVirtualItemEventArgs e)
    {
        if (e.ItemIndex < _processes.Count)
        {
            var p = _processes[e.ItemIndex];
            var item = new ListViewItem(p.Pid.ToString());
            item.SubItems.Add(p.Name);
            item.SubItems.Add(p.CpuPercent > 0.1 ? $"{p.CpuPercent:F1}" : "");
            item.SubItems.Add(FormatBytes(p.PrivateBytes));
            item.SubItems.Add(FormatBytes(p.WorkingSet));
            item.SubItems.Add(FormatCpuTime(p.TotalCpu));
            item.SubItems.Add(p.HandleCount > 0 ? $"{p.HandleCount:N0}" : "");
            item.SubItems.Add(p.StartTime?.ToString("MM-dd HH:mm") ?? "?");
            item.ForeColor = p.Severity switch
            {
                ProcessSeverity.Critical => Color.FromArgb(220, 60, 60),
                ProcessSeverity.Warning => Color.FromArgb(220, 160, 40),
                _ => Theme.TextBright
            };
            item.BackColor = Theme.BgColor;
            e.Item = item;
        }
    }

    private void OnColumnClick(object? sender, ColumnClickEventArgs e)
    {
        if (_sortColumn == e.Column)
            _sortAscending = !_sortAscending;
        else
        {
            _sortColumn = e.Column;
            _sortAscending = e.Column <= 1;
        }
        SortProcesses(_processes);
        _processListView.Invalidate();
    }

    private void SortProcesses(List<ProcessInfo> list)
    {
        Comparison<ProcessInfo> cmp = _sortColumn switch
        {
            0 => (a, b) => a.Pid.CompareTo(b.Pid),
            1 => (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase),
            2 => (a, b) => a.CpuPercent.CompareTo(b.CpuPercent),
            4 => (a, b) => a.WorkingSet.CompareTo(b.WorkingSet),
            5 => (a, b) => a.TotalCpu.CompareTo(b.TotalCpu),
            6 => (a, b) => a.HandleCount.CompareTo(b.HandleCount),
            7 => (a, b) => Nullable.Compare(a.StartTime, b.StartTime),
            _ => (a, b) => a.PrivateBytes.CompareTo(b.PrivateBytes),
        };
        list.Sort(_sortAscending ? cmp : (a, b) => cmp(b, a));
    }

    private void OnKillProcess(object? sender, EventArgs e)
    {
        if (_processListView.SelectedIndices.Count == 0) return;
        var p = _processes[_processListView.SelectedIndices[0]];
        var result = MessageBox.Show(
            $"Kill process {p.Name} (PID {p.Pid})?\nPrivate: {FormatBytes(p.PrivateBytes)}",
            "Kill Process", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (result == DialogResult.Yes)
        {
            try { using var proc = Process.GetProcessById(p.Pid); proc.Kill(); }
            catch (Exception ex) { MessageBox.Show($"Failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
    }

    private void OnRefreshEvents(object? sender, EventArgs e)
    {
        _btnRefreshEvents.Enabled = false;
        _btnRefreshEvents.Text = "Loading...";
        Task.Run(() =>
        {
            var oomEvents = _eventLogService.GetOomEvents();
            var crashEvents = _eventLogService.GetCrashEvents();
            var criticalEvents = _eventLogService.GetCriticalEvents();
            if (IsHandleCreated && !IsDisposed)
            {
                BeginInvoke(() =>
                {
                    _eventListView.Items.Clear();
                    foreach (var ev in oomEvents.OrderByDescending(e => e.Timestamp))
                    {
                        var item = new ListViewItem(ev.Timestamp.ToString("MM-dd HH:mm:ss"));
                        item.SubItems.Add("OOM Warning");
                        item.SubItems.Add(ev.Source);
                        item.SubItems.Add(ev.Message.Length > 200 ? ev.Message[..200] : ev.Message);
                        item.ForeColor = Color.FromArgb(220, 60, 60);
                        item.BackColor = Theme.BgColor;
                        _eventListView.Items.Add(item);
                    }
                    foreach (var ev in crashEvents.OrderByDescending(e => e.Timestamp))
                    {
                        var item = new ListViewItem(ev.Timestamp.ToString("MM-dd HH:mm:ss"));
                        item.SubItems.Add("App Crash");
                        item.SubItems.Add(ev.Source);
                        item.SubItems.Add(ev.Message.Length > 200 ? ev.Message[..200] : ev.Message);
                        item.ForeColor = Color.FromArgb(220, 160, 40);
                        item.BackColor = Theme.BgColor;
                        _eventListView.Items.Add(item);
                    }
                    foreach (var ev in criticalEvents.OrderByDescending(e => e.Timestamp))
                    {
                        var item = new ListViewItem(ev.Timestamp.ToString("MM-dd HH:mm:ss"));
                        item.SubItems.Add("Critical");
                        item.SubItems.Add(ev.Source);
                        item.SubItems.Add(ev.Message.Length > 200 ? ev.Message[..200] : ev.Message);
                        item.ForeColor = Color.FromArgb(200, 30, 30);
                        item.BackColor = Theme.BgColor;
                        _eventListView.Items.Add(item);
                    }
                    _btnRefreshEvents.Text = $"Refresh ({_eventListView.Items.Count} events)";
                    _btnRefreshEvents.Enabled = true;
                });
            }
        });
    }

    private const string ArrowUp = "\u25b2";
    private const string ArrowDown = "\u25bc";

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        if (bytes >= 1024L * 1024) return $"{bytes / (1024.0 * 1024):F0} MB";
        return $"{bytes / 1024.0:F0} KB";
    }

    private static string FormatCpuTime(TimeSpan ts) =>
        ts.TotalHours >= 1 ? $"{ts.TotalHours:F1}h"
        : ts.TotalMinutes >= 1 ? $"{ts.TotalMinutes:F0}m"
        : $"{ts.TotalSeconds:F0}s";

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
