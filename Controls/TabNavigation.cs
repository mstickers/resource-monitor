using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace ResourceMonitor.Controls;

public sealed class TabNavigation : Control
{
    private readonly List<TabItem> _tabs = [];
    private int _activeIndex;
    private int _hoverIndex = -1;

    public event Action<int>? TabChanged;

    private static readonly Font TabFont = new("Segoe UI", 9.5f, FontStyle.Regular);
    private const int IconSize = 18;
    private const int IconTextGap = 6;
    private const int TabPadH = 16;
    private const int UnderlineHeight = 3;
    private const int UnderlineRadius = 1;

    public TabNavigation()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.UserPaint
            | ControlStyles.ResizeRedraw, true);

        Height = 42;
        Dock = DockStyle.Top;
        BackColor = Theme.TabBarBg;
    }

    public void AddTab(string label, Bitmap? icon)
    {
        _tabs.Add(new TabItem(label, icon));
        Invalidate();
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int ActiveIndex
    {
        get => _activeIndex;
        set
        {
            if (value == _activeIndex || value < 0 || value >= _tabs.Count) return;
            _activeIndex = value;
            Invalidate();
            TabChanged?.Invoke(value);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int newHover = HitTest(e.X);
        if (newHover != _hoverIndex)
        {
            _hoverIndex = newHover;
            Invalidate();
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        if (_hoverIndex != -1)
        {
            _hoverIndex = -1;
            Invalidate();
        }
        base.OnMouseLeave(e);
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        int idx = HitTest(e.X);
        if (idx >= 0 && idx != _activeIndex)
        {
            ActiveIndex = idx;
        }
        base.OnMouseClick(e);
    }

    private int HitTest(int x)
    {
        int tx = 8;
        using var g = CreateGraphics();
        for (int i = 0; i < _tabs.Count; i++)
        {
            int tabW = MeasureTabWidth(g, _tabs[i]);
            if (x >= tx && x < tx + tabW) return i;
            tx += tabW;
        }
        return -1;
    }

    private int MeasureTabWidth(Graphics g, TabItem tab)
    {
        int textW = (int)Math.Ceiling(g.MeasureString(tab.Label, TabFont).Width);
        int contentW = (tab.Icon != null ? IconSize + IconTextGap : 0) + textW;
        return contentW + TabPadH * 2;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        int w = ClientSize.Width;
        int h = ClientSize.Height;

        g.Clear(Theme.TabBarBg);
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        // Bottom border line
        using var borderPen = new Pen(Theme.Border);
        g.DrawLine(borderPen, 0, h - 1, w, h - 1);

        int tx = 8;
        for (int i = 0; i < _tabs.Count; i++)
        {
            var tab = _tabs[i];
            bool active = i == _activeIndex;
            bool hover = i == _hoverIndex && !active;

            int tabW = MeasureTabWidth(g, tab);

            // Hover background
            if (hover)
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using var hoverBrush = new SolidBrush(Theme.TabHover);
                var hoverRect = new Rectangle(tx + 2, 4, tabW - 4, h - 10);
                using var path = RoundedRect(hoverRect, 4);
                g.FillPath(hoverBrush, path);
                g.SmoothingMode = SmoothingMode.None;
            }

            // Icon
            int contentX = tx + TabPadH;
            int iconY = (h - IconSize) / 2 - 1;

            if (tab.Icon != null)
            {
                // Tint icon by drawing with color matrix
                if (active)
                {
                    g.DrawImage(tab.ActiveIcon ?? tab.Icon, contentX, iconY, IconSize, IconSize);
                }
                else
                {
                    g.DrawImage(tab.DimIcon ?? tab.Icon, contentX, iconY, IconSize, IconSize);
                }
                contentX += IconSize + IconTextGap;
            }

            // Text
            var textColor = active ? Theme.TextBright : Theme.TextDim;
            using var textBrush = new SolidBrush(textColor);
            int textY = (h - (int)TabFont.GetHeight(g)) / 2;
            g.DrawString(tab.Label, TabFont, textBrush, contentX, textY);

            // Active underline
            if (active)
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using var underBrush = new SolidBrush(Theme.TabActive);
                var underRect = new Rectangle(tx + 4, h - UnderlineHeight - 1, tabW - 8, UnderlineHeight);
                using var underPath = RoundedRect(underRect, UnderlineRadius);
                g.FillPath(underBrush, underPath);
                g.SmoothingMode = SmoothingMode.None;
            }

            tx += tabW;
        }
    }

    private static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        int d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private sealed class TabItem(string label, Bitmap? icon)
    {
        public string Label { get; } = label;
        public Bitmap? Icon { get; } = icon;
        public Bitmap? ActiveIcon { get; set; }
        public Bitmap? DimIcon { get; set; }
    }

    public void SetTabIcons(int index, Bitmap? activeIcon, Bitmap? dimIcon)
    {
        if (index < 0 || index >= _tabs.Count) return;
        _tabs[index].ActiveIcon = activeIcon;
        _tabs[index].DimIcon = dimIcon;
        Invalidate();
    }
}
