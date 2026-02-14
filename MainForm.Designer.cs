using ResourceMonitor.Controls;

namespace ResourceMonitor;

partial class MainForm
{
    private System.ComponentModel.IContainer components = null;

    private Panel _topPanel;
    private Label _titleLabel;
    private Label _physicalLabel;
    private Label _commitLabel;
    private Label _detailLabel;
    private MemoryBarControl _memoryBar;
    private MemoryGraphPanel _graphPanel;
    private ListView _processListView;
    private StatusStrip _statusStrip;
    private ToolStripStatusLabel _refreshLabel;
    private ToolStripStatusLabel _balloonLabel;
    private ToolStripStatusLabel _oomLabel;
    private Button _btn1s;
    private Button _btn5s;
    private Button _btn10s;
    private ContextMenuStrip _processContextMenu;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
            _memoryService?.Dispose();
            _timer?.Dispose();
        }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        SuspendLayout();

        // === Top Panel ===
        _topPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 130,
            Padding = new Padding(12, 8, 12, 4)
        };

        _titleLabel = new Label
        {
            Text = "Resource Monitor",
            Font = new Font("Segoe UI", 12f, FontStyle.Bold),
            Location = new Point(12, 8),
            AutoSize = true
        };

        // Interval buttons
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

        _topPanel.Controls.AddRange([_titleLabel, _btn1s, _btn5s, _btn10s,
            _physicalLabel, _memoryBar, _commitLabel, _detailLabel]);

        // === Process ListView ===
        _processListView = new ListView
        {
            Dock = DockStyle.Bottom,
            Height = 250,
            View = View.Details,
            FullRowSelect = true,
            VirtualMode = true,
            VirtualListSize = 0,
            Font = new Font("Consolas", 9f),
            BorderStyle = BorderStyle.FixedSingle
        };
        _processListView.Columns.Add("PID", 70, HorizontalAlignment.Right);
        _processListView.Columns.Add("Name", 220, HorizontalAlignment.Left);
        _processListView.Columns.Add("Private", 100, HorizontalAlignment.Right);
        _processListView.Columns.Add("WorkingSet", 100, HorizontalAlignment.Right);
        _processListView.RetrieveVirtualItem += OnRetrieveVirtualItem;
        _processListView.ColumnClick += OnColumnClick;

        _processContextMenu = new ContextMenuStrip(components);
        _processContextMenu.Items.Add("Kill Process", null, OnKillProcess);
        _processListView.ContextMenuStrip = _processContextMenu;

        // === Graph Panel ===
        _graphPanel = new MemoryGraphPanel
        {
            Dock = DockStyle.Fill
        };

        // === Status Strip ===
        _statusStrip = new StatusStrip();
        _refreshLabel = new ToolStripStatusLabel("Refresh: 1s") { Spring = false };
        _balloonLabel = new ToolStripStatusLabel("Balloon: detecting...") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        _oomLabel = new ToolStripStatusLabel("OOM (48h): ...") { Spring = false };
        _statusStrip.Items.AddRange([_refreshLabel, _balloonLabel, _oomLabel]);

        // === Form layout ===
        // Add order determines Z-order; highest Z (last added) is laid out first
        Controls.Add(_graphPanel);        // Z=0, Fill, laid out last
        Controls.Add(_processListView);   // Z=1, Bottom, laid out 3rd
        Controls.Add(_topPanel);          // Z=2, Top, laid out 2nd
        Controls.Add(_statusStrip);       // Z=3, Bottom, laid out 1st

        // === Form properties ===
        Text = "Resource Monitor";
        MinimumSize = new Size(600, 500);
        ClientSize = new Size(800, 700);
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
