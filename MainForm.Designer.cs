using ResourceMonitor.Controls;

namespace ResourceMonitor;

/// <summary>Double-buffered ListView to eliminate flicker in virtual mode.</summary>
internal sealed class BufferedListView : ListView
{
    public BufferedListView()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.AllPaintingInWmPaint, true);
    }
}

partial class MainForm
{
    private System.ComponentModel.IContainer components = null;

    // Top panel
    private Panel _topPanel;
    private Label _titleLabel;
    private Label _physicalLabel;
    private Label _commitLabel;
    private Label _detailLabel;
    private Label _networkLabel;
    private Label _diskLabel;
    private Label _sitesLabel;
    private MemoryBarControl _memoryBar;
    private Button _btn1s, _btn5s, _btn10s;

    // Tabs
    private TabControl _tabControl;
    private TabPage _tabDashboard, _tabProcesses, _tabEvents;

    // Dashboard tab
    private MemoryGraphPanel _graphPanel;

    // Processes tab
    private BufferedListView _processListView;
    private ContextMenuStrip _processContextMenu;

    // Events tab
    private BufferedListView _eventListView;
    private Button _btnRefreshEvents;

    // Status
    private StatusStrip _statusStrip;
    private ToolStripStatusLabel _refreshLabel;
    private ToolStripStatusLabel _balloonLabel;
    private ToolStripStatusLabel _oomLabel;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
            _memoryService?.Dispose();
            _diskService?.Dispose();
            _websiteService?.Dispose();
            _timer?.Dispose();
        }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        SuspendLayout();

        // === Top Panel (always visible) ===
        _topPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 186,
            Padding = new Padding(12, 8, 12, 4)
        };

        _titleLabel = new Label
        {
            Text = "Resource Monitor",
            Font = new Font("Segoe UI", 12f, FontStyle.Bold),
            Location = new Point(12, 8),
            AutoSize = true
        };

        _btn1s = CreateIntervalButton("1s", 1000, new Point(370, 8));
        _btn5s = CreateIntervalButton("5s", 5000, new Point(414, 8));
        _btn10s = CreateIntervalButton("10s", 10000, new Point(458, 8));

        _physicalLabel = new Label
        {
            Text = "Physical: ... / ... GB",
            Font = new Font("Segoe UI", 9.5f),
            Location = new Point(12, 34),
            AutoSize = true
        };

        _memoryBar = new MemoryBarControl
        {
            Location = new Point(12, 56),
            Height = 22,
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top
        };

        _commitLabel = new Label
        {
            Text = "Commit: ... / ... GB  Balloon: ...",
            Font = new Font("Segoe UI", 9f),
            Location = new Point(12, 82),
            AutoSize = true
        };

        _detailLabel = new Label
        {
            Text = "Standby: ...  Modified: ...  Free: ...",
            Font = new Font("Segoe UI", 9f),
            Location = new Point(12, 102),
            AutoSize = true
        };

        _networkLabel = new Label
        {
            Text = "Network: ...",
            Font = new Font("Segoe UI", 9f),
            Location = new Point(12, 122),
            AutoSize = true
        };

        _diskLabel = new Label
        {
            Text = "Disk: ...",
            Font = new Font("Segoe UI", 9f),
            Location = new Point(12, 142),
            AutoSize = true
        };

        _sitesLabel = new Label
        {
            Text = "Sites: waiting...",
            Font = new Font("Segoe UI", 9f),
            Location = new Point(12, 162),
            AutoSize = true
        };

        _topPanel.Controls.AddRange([_titleLabel, _btn1s, _btn5s, _btn10s,
            _physicalLabel, _memoryBar, _commitLabel, _detailLabel,
            _networkLabel, _diskLabel, _sitesLabel]);

        // === Tab Control ===
        _tabControl = new TabControl { Dock = DockStyle.Fill };

        // --- Dashboard tab ---
        _tabDashboard = new TabPage("Dashboard");
        _graphPanel = new MemoryGraphPanel { Dock = DockStyle.Fill };
        _tabDashboard.Controls.Add(_graphPanel);

        // --- Processes tab ---
        _tabProcesses = new TabPage("Processes");
        _processListView = new BufferedListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            VirtualMode = true,
            VirtualListSize = 0,
            Font = new Font("Consolas", 9f),
            BorderStyle = BorderStyle.None
        };
        _processListView.Columns.Add("PID", 60, HorizontalAlignment.Right);
        _processListView.Columns.Add("Name", 180, HorizontalAlignment.Left);
        _processListView.Columns.Add("CPU %", 60, HorizontalAlignment.Right);
        _processListView.Columns.Add("Private", 90, HorizontalAlignment.Right);
        _processListView.Columns.Add("WorkingSet", 90, HorizontalAlignment.Right);
        _processListView.Columns.Add("Total CPU", 80, HorizontalAlignment.Right);
        _processListView.Columns.Add("Started", 100, HorizontalAlignment.Left);
        _processListView.RetrieveVirtualItem += OnRetrieveVirtualItem;
        _processListView.ColumnClick += OnColumnClick;

        _processContextMenu = new ContextMenuStrip(components);
        _processContextMenu.Items.Add("Kill Process", null, OnKillProcess);
        _processListView.ContextMenuStrip = _processContextMenu;
        _tabProcesses.Controls.Add(_processListView);

        // --- Events tab ---
        _tabEvents = new TabPage("Events");
        _eventListView = new BufferedListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            Font = new Font("Consolas", 9f),
            BorderStyle = BorderStyle.None
        };
        _eventListView.Columns.Add("Time", 130, HorizontalAlignment.Left);
        _eventListView.Columns.Add("Type", 100, HorizontalAlignment.Left);
        _eventListView.Columns.Add("Source", 150, HorizontalAlignment.Left);
        _eventListView.Columns.Add("Details", 400, HorizontalAlignment.Left);

        _btnRefreshEvents = new Button
        {
            Text = "Refresh",
            Dock = DockStyle.Bottom,
            Height = 28,
            FlatStyle = FlatStyle.Flat
        };
        _btnRefreshEvents.Click += OnRefreshEvents;
        _tabEvents.Controls.Add(_eventListView);
        _tabEvents.Controls.Add(_btnRefreshEvents);

        _tabControl.TabPages.AddRange([_tabDashboard, _tabProcesses, _tabEvents]);

        // === Status Strip ===
        _statusStrip = new StatusStrip();
        _refreshLabel = new ToolStripStatusLabel("Refresh: 1s") { Spring = false };
        _balloonLabel = new ToolStripStatusLabel("Balloon: detecting...") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        _oomLabel = new ToolStripStatusLabel("OOM (48h): ...") { Spring = false };
        _statusStrip.Items.AddRange([_refreshLabel, _balloonLabel, _oomLabel]);

        // === Form layout ===
        Controls.Add(_tabControl);     // Fill — laid out last
        Controls.Add(_topPanel);       // Top — laid out second
        Controls.Add(_statusStrip);    // Bottom — laid out first

        // === Form properties ===
        DoubleBuffered = true;
        Text = "Resource Monitor";
        MinimumSize = new Size(700, 550);
        ClientSize = new Size(900, 750);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9f);

        ResumeLayout(false);
        PerformLayout();
    }

    private Button CreateIntervalButton(string text, int intervalMs, Point location)
    {
        var btn = new Button
        {
            Text = text,
            Size = new Size(40, 24),
            Location = location,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 8f),
            Tag = intervalMs
        };
        btn.FlatAppearance.BorderSize = 1;
        btn.Click += (_, _) => SetInterval(intervalMs);
        return btn;
    }
}
