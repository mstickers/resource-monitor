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

    // LED bar (always visible top)
    private LedBarPanel _ledBar;

    // Tabs
    private TabControl _tabControl;
    private TabPage _tabDashboard, _tabGraphs, _tabProcesses, _tabWebsites, _tabEvents;

    // Dashboard tab
    private DashboardDigestPanel _digestPanel;

    // Graphs tab
    private MemoryGraphPanel _graphPanel;

    // Processes tab
    private BufferedListView _processListView;
    private ContextMenuStrip _processContextMenu;

    // Websites tab
    private WebsitesPanel _websitesPanel;

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

        // === LED Bar (always visible top) ===
        _ledBar = new LedBarPanel();
        _ledBar.IntervalChanged += SetInterval;

        // === Tab Control ===
        _tabControl = new TabControl
        {
            Dock = DockStyle.Fill
        };

        // --- Dashboard tab ---
        _tabDashboard = new TabPage("Dashboard") { BackColor = Theme.BgColor };
        _digestPanel = new DashboardDigestPanel { Dock = DockStyle.Fill };
        _tabDashboard.Controls.Add(_digestPanel);

        // --- Graphs tab ---
        _tabGraphs = new TabPage("Graphs") { BackColor = Theme.BgColor };
        _graphPanel = new MemoryGraphPanel { Dock = DockStyle.Fill };
        _tabGraphs.Controls.Add(_graphPanel);

        // --- Processes tab ---
        _tabProcesses = new TabPage("Processes") { BackColor = Theme.BgColor };
        _processListView = new BufferedListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            VirtualMode = true,
            VirtualListSize = 0,
            Font = new Font("Consolas", 9f),
            BorderStyle = BorderStyle.None,
            BackColor = Theme.BgColor,
            ForeColor = Theme.TextBright
        };
        _processListView.Columns.Add("PID", 60, HorizontalAlignment.Right);
        _processListView.Columns.Add("Name", 180, HorizontalAlignment.Left);
        _processListView.Columns.Add("CPU %", 60, HorizontalAlignment.Right);
        _processListView.Columns.Add("Private", 90, HorizontalAlignment.Right);
        _processListView.Columns.Add("WorkingSet", 90, HorizontalAlignment.Right);
        _processListView.Columns.Add("Total CPU", 80, HorizontalAlignment.Right);
        _processListView.Columns.Add("Handles", 70, HorizontalAlignment.Right);
        _processListView.Columns.Add("Started", 100, HorizontalAlignment.Left);
        _processListView.RetrieveVirtualItem += OnRetrieveVirtualItem;
        _processListView.ColumnClick += OnColumnClick;

        _processContextMenu = new ContextMenuStrip(components);
        _processContextMenu.Items.Add("Kill Process", null, OnKillProcess);
        _processListView.ContextMenuStrip = _processContextMenu;
        _tabProcesses.Controls.Add(_processListView);

        // --- Websites tab ---
        _tabWebsites = new TabPage("Websites") { BackColor = Theme.BgColor };
        _websitesPanel = new WebsitesPanel { Dock = DockStyle.Fill };
        _tabWebsites.Controls.Add(_websitesPanel);

        // --- Events tab ---
        _tabEvents = new TabPage("Events") { BackColor = Theme.BgColor };
        _eventListView = new BufferedListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            Font = new Font("Consolas", 9f),
            BorderStyle = BorderStyle.None,
            BackColor = Theme.BgColor,
            ForeColor = Theme.TextBright
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
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(50, 50, 55),
            ForeColor = Theme.TextLabel
        };
        _btnRefreshEvents.FlatAppearance.BorderColor = Theme.Border;
        _btnRefreshEvents.Click += OnRefreshEvents;
        _tabEvents.Controls.Add(_eventListView);
        _tabEvents.Controls.Add(_btnRefreshEvents);

        _tabControl.TabPages.AddRange([_tabDashboard, _tabGraphs, _tabProcesses, _tabWebsites, _tabEvents]);

        // === Status Strip ===
        _statusStrip = new StatusStrip { BackColor = Color.FromArgb(30, 30, 35) };
        _statusStrip.ForeColor = Theme.TextDim;
        _refreshLabel = new ToolStripStatusLabel("Refresh: 1s") { Spring = false, ForeColor = Theme.TextDim };
        _balloonLabel = new ToolStripStatusLabel("Balloon: detecting...") { Spring = true, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.TextDim };
        _oomLabel = new ToolStripStatusLabel("OOM (48h): ...") { Spring = false, ForeColor = Theme.TextDim };
        _statusStrip.Items.AddRange([_refreshLabel, _balloonLabel, _oomLabel]);

        // === Form layout ===
        Controls.Add(_tabControl);     // Fill — laid out last
        Controls.Add(_ledBar);         // Top — laid out second
        Controls.Add(_statusStrip);    // Bottom — laid out first

        // === Form properties ===
        DoubleBuffered = true;
        Text = "Resource Monitor";
        MinimumSize = new Size(800, 600);
        ClientSize = new Size(1000, 800);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9f);
        BackColor = Theme.BgColor;

        ResumeLayout(false);
        PerformLayout();
    }
}
