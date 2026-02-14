using System.Drawing;
using System.Drawing.Drawing2D;

namespace ResourceMonitor.Controls;

public sealed class MemoryBarControl : Control
{
    private float _inUsePct;
    private float _modifiedPct;
    private float _standbyPct;
    // Free = 100 - inUse - modified - standby

    private static readonly Color InUseColor = Color.FromArgb(100, 150, 220);
    private static readonly Color ModifiedColor = Color.FromArgb(220, 160, 60);
    private static readonly Color StandbyColor = Color.FromArgb(70, 130, 190);
    private static readonly Color FreeColor = Color.FromArgb(50, 50, 55);
    private static readonly Color BorderColor = Color.FromArgb(80, 80, 85);

    public MemoryBarControl()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.UserPaint
            | ControlStyles.ResizeRedraw, true);
        Height = 22;
    }

    public void Update(float inUsePct, float modifiedPct, float standbyPct)
    {
        _inUsePct = inUsePct;
        _modifiedPct = modifiedPct;
        _standbyPct = standbyPct;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.None;

        int w = ClientSize.Width - 2;
        int h = ClientSize.Height - 2;
        int x = 1, y = 1;

        // Border
        using var borderPen = new Pen(BorderColor);
        g.DrawRectangle(borderPen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);

        // Segments
        int inUseW = (int)(w * _inUsePct / 100f);
        int modW = (int)(w * _modifiedPct / 100f);
        int standbyW = (int)(w * _standbyPct / 100f);
        int freeW = w - inUseW - modW - standbyW;
        if (freeW < 0) freeW = 0;

        using var inUseBrush = new SolidBrush(InUseColor);
        using var modBrush = new SolidBrush(ModifiedColor);
        using var standbyBrush = new SolidBrush(StandbyColor);
        using var freeBrush = new SolidBrush(FreeColor);

        g.FillRectangle(inUseBrush, x, y, inUseW, h);
        g.FillRectangle(modBrush, x + inUseW, y, modW, h);
        g.FillRectangle(standbyBrush, x + inUseW + modW, y, standbyW, h);
        g.FillRectangle(freeBrush, x + inUseW + modW + standbyW, y, freeW, h);
    }
}
