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
    private readonly RingBuffer<SystemSnapshot> _ringBuffer = new(600);

    private System.Threading.Timer? _timer;
    private int _intervalMs = 1000;
    private int _tickCount;
    private const int EventLogEveryN = 60;

    private List<ProcessInfo> _processes = [];
    private int _sortColumn = 2; // Private bytes
    private bool _sortAscending;
    private int _lastOomCount = -1;
    private int _lastCrashCount = -1;

    public MainForm()
    {
        InitializeComponent();
        _graphPanel.SetBuffer(_ringBuffer);
        UpdateIntervalButtons();

        // Set memory bar width after layout
        Load += (_, _) =>
        {
            _memoryBar.Width = _topPanel.ClientSize.Width - 24;

            // Position interval buttons at right edge of top panel
            int right = _topPanel.ClientSize.Width - 12;
            _btn10s.Left = right - _btn10s.Width;
            _btn5s.Left = _btn10s.Left - _btn5s.Width - 4;
            _btn1s.Left = _btn5s.Left - _btn1s.Width - 4;

            // Initial balloon status
            _balloonLabel.Text = _balloonService.DriverDetected
                ? "Balloon: detected"
                : "Balloon: not detected";

            // Start collecting
            _timer = new System.Threading.Timer(OnTick, null, 0, _intervalMs);
        };

        Resize += (_, _) =>
        {
            _memoryBar.Width = _topPanel.ClientSize.Width - 24;
            int right = _topPanel.ClientSize.Width - 12;
            _btn10s.Left = right - _btn10s.Width;
            _btn5s.Left = _btn10s.Left - _btn5s.Width - 4;
            _btn1s.Left = _btn5s.Left - _btn1s.Width - 4;
        };
    }

    private void OnTick(object? state)
    {
        try
        {
            var snapshot = _memoryService.GetSnapshot();
            var processes = _processService.GetProcesses();

            int oomCount = -1, crashCount = -1;
            int tick = Interlocked.Increment(ref _tickCount);
            if (tick == 1 || tick % EventLogEveryN == 0)
            {
                (oomCount, crashCount) = _eventLogService.GetCounts();
            }

            if (IsHandleCreated && !IsDisposed)
            {
                BeginInvoke(() => UpdateUI(snapshot, processes, oomCount, crashCount));
            }
        }
        catch { }
    }

    private void UpdateUI(SystemSnapshot snap, List<ProcessInfo> processes, int oomCount, int crashCount)
    {
        _ringBuffer.Add(snap);

        // Physical memory label
        float usedGB = snap.InUseGB + snap.ModifiedMB / 1024f;
        _physicalLabel.Text = $"Physical: {usedGB:F1} / {snap.TotalGB:F1} GB ({snap.UsedPercent:F1}%)";

        // Memory bar
        float totalMB = snap.TotalMB;
        float inUsePct = totalMB > 0 ? snap.InUseMB / totalMB * 100f : 0;
        float modPct = totalMB > 0 ? snap.ModifiedMB / totalMB * 100f : 0;
        float standbyPct = totalMB > 0 ? snap.StandbyMB / totalMB * 100f : 0;
        _memoryBar.Update(inUsePct, modPct, standbyPct);

        // Commit + balloon
        var (inflated, reclaimedBytes) = _balloonService.Check(snap.TotalPhysicalBytes);
        string balloonText = inflated
            ? $"Balloon: {FormatBytes(reclaimedBytes)} reclaimed"
            : "Balloon: idle";
        _commitLabel.Text = $"Commit: {snap.CommitUsedGB:F1} / {snap.CommitLimitGB:F1} GB  {balloonText}";
        _balloonLabel.Text = _balloonService.DriverDetected
            ? (inflated ? $"Balloon: {FormatBytes(reclaimedBytes)} reclaimed" : "Balloon: idle")
            : "Balloon: not detected";

        // Detail line
        _detailLabel.Text = $"Standby: {FormatMB(snap.StandbyMB)}  Modified: {FormatMB(snap.ModifiedMB)}  Free: {FormatMB(snap.FreeMB)}";

        // Process list
        SortProcesses(processes);
        _processes = processes;
        _processListView.VirtualListSize = _processes.Count;
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
            item.SubItems.Add(FormatBytes(p.PrivateBytes));
            item.SubItems.Add(FormatBytes(p.WorkingSet));
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
            _sortAscending = e.Column <= 1; // ascending for PID/Name, descending for sizes
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
            3 => (a, b) => a.WorkingSet.CompareTo(b.WorkingSet),
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
            try
            {
                using var proc = Process.GetProcessById(p.Pid);
                proc.Kill();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to kill process: {ex.Message}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024)
            return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        if (bytes >= 1024L * 1024)
            return $"{bytes / (1024.0 * 1024):F0} MB";
        return $"{bytes / 1024.0:F0} KB";
    }

    private static string FormatMB(float mb)
    {
        if (mb >= 1024f)
            return $"{mb / 1024f:F1} GB";
        if (mb >= 10f)
            return $"{mb:F0} MB";
        return $"{mb:F1} MB";
    }
}
