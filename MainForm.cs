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
    private readonly RingBuffer<SystemSnapshot> _ringBuffer = new(600);

    private System.Threading.Timer? _timer;
    private int _intervalMs = 1000;
    private int _tickCount;
    private const int EventLogEveryN = 60;

    private List<ProcessInfo> _processes = [];
    private int _sortColumn = 3; // Private bytes
    private bool _sortAscending;
    private int _lastOomCount = -1;
    private int _lastCrashCount = -1;

    public MainForm()
    {
        InitializeComponent();
        _graphPanel.SetBuffer(_ringBuffer);
        UpdateIntervalButtons();

        // Load website config
        string configDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "config");
        string configPath = Path.Combine(configDir, "websites.txt");
        // Also try relative to project root
        if (!File.Exists(configPath))
            configPath = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath)!, "..", "..", "..", "..", "config", "websites.txt");
        _websiteService.LoadConfig(configPath);

        Load += (_, _) =>
        {
            _memoryBar.Width = _topPanel.ClientSize.Width - 24;
            PositionButtons();

            _balloonLabel.Text = _balloonService.DriverDetected
                ? "Balloon: detected"
                : "Balloon: not detected";

            if (_websiteService.SiteCount > 0)
            {
                _sitesLabel.Text = $"Sites: monitoring {_websiteService.SiteCount} site(s)...";
                _websiteService.Start(60);
            }
            else
            {
                _sitesLabel.Text = "Sites: no config (edit config/websites.txt)";
            }

            _timer = new System.Threading.Timer(OnTick, null, 0, _intervalMs);
        };

        Resize += (_, _) =>
        {
            _memoryBar.Width = _topPanel.ClientSize.Width - 24;
            PositionButtons();
        };
    }

    private void PositionButtons()
    {
        int right = _topPanel.ClientSize.Width - 12;
        _btn10s.Left = right - _btn10s.Width;
        _btn5s.Left = _btn10s.Left - _btn5s.Width - 4;
        _btn1s.Left = _btn5s.Left - _btn1s.Width - 4;
    }

    private void OnTick(object? state)
    {
        try
        {
            var snapshot = _memoryService.GetSnapshot();
            var processes = _processService.GetProcesses();
            var netSnaps = _networkService.GetSnapshots();
            var diskSnap = _diskService.GetSnapshot();

            int oomCount = -1, crashCount = -1;
            int tick = Interlocked.Increment(ref _tickCount);
            if (tick == 1 || tick % EventLogEveryN == 0)
            {
                (oomCount, crashCount) = _eventLogService.GetCounts();
            }

            var siteChecks = _websiteService.GetLatestChecks();

            if (IsHandleCreated && !IsDisposed)
            {
                BeginInvoke(() => UpdateUI(snapshot, processes, netSnaps, diskSnap, siteChecks, oomCount, crashCount));
            }
        }
        catch { }
    }

    private void UpdateUI(SystemSnapshot snap, List<ProcessInfo> processes,
        List<NetworkSnapshot> netSnaps, DiskSnapshot diskSnap,
        List<WebsiteCheck> siteChecks, int oomCount, int crashCount)
    {
        _ringBuffer.Add(snap);

        // Physical memory + CPU
        float usedGB = snap.InUseGB + snap.ModifiedMB / 1024f;
        _physicalLabel.Text = $"Physical: {usedGB:F1} / {snap.TotalGB:F1} GB ({snap.UsedPercent:F1}%)   CPU: {snap.CpuPercent:F0}%";

        // Memory bar
        float totalMB = snap.TotalMB;
        float inUsePct = totalMB > 0 ? snap.InUseMB / totalMB * 100f : 0;
        float modPct = totalMB > 0 ? snap.ModifiedMB / totalMB * 100f : 0;
        float standbyPct = totalMB > 0 ? snap.StandbyMB / totalMB * 100f : 0;
        _memoryBar.Update(inUsePct, modPct, standbyPct);

        // Commit + balloon + pagefile
        var (inflated, reclaimedBytes) = _balloonService.Check(snap.TotalPhysicalBytes);
        string balloonText = inflated
            ? $"Balloon: {FormatBytes(reclaimedBytes)} reclaimed"
            : "Balloon: idle";
        string pagefileText = snap.PagefileUsagePercent > 0.5f
            ? $"Pagefile: {snap.PagefileUsagePercent:F0}%"
            : "Pagefile: idle";
        string pagingText = snap.PagesOutputPerSec > 10
            ? $"  Paging: {snap.PagesOutputPerSec:F0}/s"
            : "";
        _commitLabel.Text = $"Commit: {snap.CommitUsedGB:F1} / {snap.CommitLimitGB:F1} GB  {balloonText}  {pagefileText}{pagingText}";
        _balloonLabel.Text = _balloonService.DriverDetected
            ? (inflated ? $"Balloon: {FormatBytes(reclaimedBytes)} reclaimed" : "Balloon: idle")
            : "Balloon: not detected";

        // Detail line
        _detailLabel.Text = $"Standby: {FormatMB(snap.StandbyMB)}  Modified: {FormatMB(snap.ModifiedMB)}  Free: {FormatMB(snap.FreeMB)}  Pools: {FormatMB(snap.PoolNonpagedMB)} NP / {FormatMB(snap.PoolPagedMB)} P";

        // Network
        if (netSnaps.Count > 0)
        {
            var parts = netSnaps.Select(n =>
                $"{n.Name}: {ArrowUp}{FormatRate(n.SendRateKBps)} {ArrowDown}{FormatRate(n.ReceiveRateKBps)}");
            _networkLabel.Text = $"Network: {string.Join("  |  ", parts)}";
        }

        // Disk
        _diskLabel.Text = $"Disk: Read {FormatRate(diskSnap.ReadBytesPerSec / 1024)}  Write {FormatRate(diskSnap.WriteBytesPerSec / 1024)}  Active: {diskSnap.DiskTimePct:F0}%";

        // Websites
        if (siteChecks.Count > 0)
        {
            var siteParts = siteChecks.Select(s =>
            {
                if (!s.IsUp)
                    return $"{s.Name}: DOWN";
                string time = s.ResponseTimeMs >= 1000
                    ? $"{s.ResponseTimeMs / 1000.0:F1}s"
                    : $"{s.ResponseTimeMs}ms";
                string color = s.ResponseTimeMs > 2000 ? "!" : s.ResponseTimeMs > 500 ? "~" : "";
                return $"{s.Name}: {time}{color}";
            });
            _sitesLabel.Text = $"Sites: {string.Join("  |  ", siteParts)}";
            _sitesLabel.ForeColor = siteChecks.Any(s => !s.IsUp) ? Color.FromArgb(220, 60, 60)
                : siteChecks.Any(s => s.ResponseTimeMs > 2000) ? Color.FromArgb(220, 160, 40)
                : SystemColors.ControlText;
        }

        // Alert: change title bar when system is under pressure
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
            _physicalLabel.ForeColor = Color.FromArgb(220, 60, 60);
        }
        else
        {
            Text = "Resource Monitor";
            _physicalLabel.ForeColor = SystemColors.ControlText;
        }

        // Process list
        SortProcesses(processes);
        _processes = processes;
        int newCount = _processes.Count;
        if (_processListView.VirtualListSize != newCount)
            _processListView.VirtualListSize = newCount;
        _processListView.Invalidate();

        // Graph
        _graphPanel.Invalidate();

        // Event counts
        if (oomCount >= 0)
        {
            _lastOomCount = oomCount;
            _lastCrashCount = crashCount;
        }
        if (_lastOomCount >= 0)
        {
            _oomLabel.Text = $"OOM (48h): {_lastOomCount} | Crashes: {_lastCrashCount}";
        }
    }

    private void SetInterval(int ms)
    {
        _intervalMs = ms;
        _timer?.Change(0, ms);
        _refreshLabel.Text = $"Refresh: {ms / 1000}s";
        UpdateIntervalButtons();
    }

    private void UpdateIntervalButtons()
    {
        foreach (var btn in new[] { _btn1s, _btn5s, _btn10s })
        {
            bool active = (int)btn.Tag! == _intervalMs;
            btn.BackColor = active ? Color.FromArgb(60, 120, 200) : SystemColors.Control;
            btn.ForeColor = active ? Color.White : SystemColors.ControlText;
        }
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
            item.SubItems.Add(p.StartTime?.ToString("MM-dd HH:mm") ?? "?");
            item.ForeColor = p.Severity switch
            {
                ProcessSeverity.Critical => Color.FromArgb(220, 60, 60),
                ProcessSeverity.Warning => Color.FromArgb(220, 160, 40),
                _ => SystemColors.WindowText
            };
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
            6 => (a, b) => Nullable.Compare(a.StartTime, b.StartTime),
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
                        _eventListView.Items.Add(item);
                    }
                    foreach (var ev in crashEvents.OrderByDescending(e => e.Timestamp))
                    {
                        var item = new ListViewItem(ev.Timestamp.ToString("MM-dd HH:mm:ss"));
                        item.SubItems.Add("App Crash");
                        item.SubItems.Add(ev.Source);
                        item.SubItems.Add(ev.Message.Length > 200 ? ev.Message[..200] : ev.Message);
                        item.ForeColor = Color.FromArgb(220, 160, 40);
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
