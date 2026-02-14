using System.Runtime.InteropServices;
using ResourceMonitor.Controls;
using ResourceMonitor.Helpers;

namespace ResourceMonitor;

/// <summary>Double-buffered dark-themed ListView with owner-drawn column headers.</summary>
internal sealed class BufferedListView : ListView
{
    private DarkHeaderNativeWindow? _headerWindow;

    public BufferedListView()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.AllPaintingInWmPaint, true);
        OwnerDraw = true;
        DrawColumnHeader += OnDrawColumnHeader;
        DrawItem += OnDrawItem;
        DrawSubItem += OnDrawSubItem;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // Dark scrollbars via Explorer dark mode theme
        SetWindowTheme(Handle, "DarkMode_Explorer", null);
        // Subclass the header control to paint its background dark
        var headerHandle = SendMessage(Handle, 0x101F /* LVM_GETHEADER */, 0, 0);
        if (headerHandle != IntPtr.Zero)
            _headerWindow = new DarkHeaderNativeWindow(headerHandle);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hwnd, string? pszSubAppName, string? pszSubIdList);

    private static void OnDrawColumnHeader(object? sender, DrawListViewColumnHeaderEventArgs e)
    {
        using var bgBrush = new SolidBrush(Color.FromArgb(35, 35, 40));
        e.Graphics.FillRectangle(bgBrush, e.Bounds);

        using var borderPen = new Pen(Theme.Border);
        e.Graphics.DrawLine(borderPen, e.Bounds.Right - 1, e.Bounds.Y, e.Bounds.Right - 1, e.Bounds.Bottom);
        e.Graphics.DrawLine(borderPen, e.Bounds.X, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);

        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
        flags |= e.Header!.TextAlign switch
        {
            HorizontalAlignment.Right => TextFormatFlags.Right,
            HorizontalAlignment.Center => TextFormatFlags.HorizontalCenter,
            _ => TextFormatFlags.Left
        };
        var textRect = new Rectangle(e.Bounds.X + 4, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height);
        TextRenderer.DrawText(e.Graphics, e.Header.Text, e.Font, textRect, Theme.TextLabel, flags);
    }

    private static void OnDrawItem(object? sender, DrawListViewItemEventArgs e) => e.DrawDefault = true;
    private static void OnDrawSubItem(object? sender, DrawListViewSubItemEventArgs e) => e.DrawDefault = true;

    /// <summary>Subclasses the ListView header control to paint background dark.</summary>
    private sealed class DarkHeaderNativeWindow : NativeWindow
    {
        private const int WM_ERASEBKGND = 0x0014;

        public DarkHeaderNativeWindow(IntPtr handle) => AssignHandle(handle);

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_ERASEBKGND)
            {
                using var g = Graphics.FromHdc(m.WParam);
                g.Clear(Color.FromArgb(35, 35, 40));
                m.Result = (IntPtr)1;
                return;
            }
            base.WndProc(ref m);
        }
    }
}

partial class MainForm
{
    private System.ComponentModel.IContainer components = null;

    // LED bar (always visible top)
    private LedBarPanel _ledBar;

    // Custom tab navigation
    private TabNavigation _tabNav;

    // Tab content panels
    private Panel _contentPanel;
    private DashboardDigestPanel _digestPanel;
    private CpuDetailPanel _cpuPanel;
    private MemoryDetailPanel _memoryPanel;
    private MemoryGraphPanel _graphPanel;
    private Panel _processesPanel;
    private WebsitesPanel _websitesPanel;
    private Panel _eventsPanel;

    // Processes tab controls
    private BufferedListView _processListView;
    private ContextMenuStrip _processContextMenu;

    // Events tab controls
    private BufferedListView _eventListView;
    private Button _btnRefreshEvents;

    // Status
    private StatusStrip _statusStrip;
    private ToolStripStatusLabel _refreshLabel;
    private ToolStripStatusLabel _balloonLabel;
    private ToolStripStatusLabel _oomLabel;

    // All tab content panels for show/hide
    private Control[] _tabPanels = [];

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

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private void EnableDarkTitleBar()
    {
        int value = 1;
        DwmSetWindowAttribute(Handle, 20, ref value, sizeof(int));
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        SuspendLayout();

        // === LED Bar (always visible top) ===
        _ledBar = new LedBarPanel();
        _ledBar.IntervalChanged += SetInterval;

        // === Tab Navigation (custom-drawn) ===
        _tabNav = new TabNavigation();

        // Load tab icons
        var tabDefs = new (string label, string icon)[]
        {
            ("Dashboard", "tab-dashboard"),
            ("CPU", "tab-cpu"),
            ("Memory", "tab-memory"),
            ("Graphs", "tab-graph"),
            ("Processes", "tab-process"),
            ("Websites", "tab-website"),
            ("Events", "tab-events")
        };
        for (int i = 0; i < tabDefs.Length; i++)
        {
            var dimIcon = SvgIconHelper.Load(tabDefs[i].icon, 18, Theme.TextDim);
            var activeIcon = SvgIconHelper.Load(tabDefs[i].icon, 18, Theme.TabActive);
            _tabNav.AddTab(tabDefs[i].label, dimIcon);
            _tabNav.SetTabIcons(i, activeIcon, dimIcon);
        }

        _tabNav.TabChanged += OnTabChanged;

        // === Content Panel ===
        _contentPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.BgColor
        };

        // --- Dashboard ---
        _digestPanel = new DashboardDigestPanel { Dock = DockStyle.Fill, Visible = true };

        // --- CPU ---
        _cpuPanel = new CpuDetailPanel { Dock = DockStyle.Fill, Visible = false };

        // --- Memory ---
        _memoryPanel = new MemoryDetailPanel { Dock = DockStyle.Fill, Visible = false };

        // --- Graphs ---
        _graphPanel = new MemoryGraphPanel { Dock = DockStyle.Fill, Visible = false };

        // --- Processes ---
        _processesPanel = new Panel { Dock = DockStyle.Fill, Visible = false, BackColor = Theme.BgColor };
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
        _processesPanel.Controls.Add(_processListView);

        // --- Websites ---
        _websitesPanel = new WebsitesPanel { Dock = DockStyle.Fill, Visible = false };

        // --- Events ---
        _eventsPanel = new Panel { Dock = DockStyle.Fill, Visible = false, BackColor = Theme.BgColor };
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
        _eventsPanel.Controls.Add(_eventListView);
        _eventsPanel.Controls.Add(_btnRefreshEvents);

        // Add all tab panels to content panel
        _tabPanels = [_digestPanel, _cpuPanel, _memoryPanel, _graphPanel, _processesPanel, _websitesPanel, _eventsPanel];
        // Add in reverse order so first panel is on top
        for (int i = _tabPanels.Length - 1; i >= 0; i--)
            _contentPanel.Controls.Add(_tabPanels[i]);

        // === Status Strip ===
        _statusStrip = new StatusStrip { BackColor = Color.FromArgb(30, 30, 35) };
        _statusStrip.ForeColor = Theme.TextDim;
        _refreshLabel = new ToolStripStatusLabel("Refresh: 1s") { Spring = false, ForeColor = Theme.TextDim };
        _balloonLabel = new ToolStripStatusLabel("Balloon: detecting...") { Spring = true, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.TextDim };
        _oomLabel = new ToolStripStatusLabel("OOM (48h): ...") { Spring = false, ForeColor = Theme.TextDim };
        _statusStrip.Items.AddRange([_refreshLabel, _balloonLabel, _oomLabel]);

        // === Form layout (Dock order matters: bottom first, top second, fill last) ===
        Controls.Add(_contentPanel);   // Fill — laid out last
        Controls.Add(_tabNav);         // Top — laid out third (below LED bar)
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

    private void OnTabChanged(int index)
    {
        for (int i = 0; i < _tabPanels.Length; i++)
            _tabPanels[i].Visible = i == index;
    }
}
