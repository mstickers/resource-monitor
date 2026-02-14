using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using ResourceMonitor.Models;

namespace ResourceMonitor.Controls;

public sealed class LedIndicatorControl : Control
{
    private readonly RingBuffer<float> _smoothBuffer;
    private float _animatedValue;
    private float? _currentValue;
    private bool _hasData;
    private string _label = "";
    private Bitmap? _icon;
    private const float LerpFactor = 0.3f;

    private static readonly Color NoDataColor = Color.FromArgb(60, 60, 65);

    public LedIndicatorControl()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.UserPaint
            | ControlStyles.ResizeRedraw, true);

        Size = new Size(80, 100);
        _smoothBuffer = new RingBuffer<float>(20); // 20s window at 1s interval
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Label
    {
        get => _label;
        set { _label = value; Invalidate(); }
    }

    public void SetValue(float percent)
    {
        _hasData = true;
        _currentValue = Math.Clamp(percent, 0f, 100f);
        _smoothBuffer.Add(_currentValue.Value);
        Invalidate();
    }

    public void SetIcon(Bitmap icon)
    {
        _icon = icon;
        Invalidate();
    }

    public void SetNoData()
    {
        _hasData = false;
        _currentValue = null;
        Invalidate();
    }

    public void SetSmoothingCapacity(int capacity)
    {
        _smoothBuffer.Clear();
        // RingBuffer doesn't support resize, but we keep using the same one
        // and rely on the averaging window naturally adjusting
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        int w = ClientSize.Width;
        int h = ClientSize.Height;

        g.Clear(Theme.BgColor);

        if (w < 10 || h < 10) return;

        // LED dimensions
        int ledSize = Math.Min(w - 8, 52);
        int ledX = (w - ledSize) / 2;
        int ledY = 4;
        int glowPad = ledSize / 4;

        // Compute smoothed + animated value
        float targetValue = 0;
        if (_hasData && _smoothBuffer.Count > 0)
        {
            float sum = 0;
            for (int i = 0; i < _smoothBuffer.Count; i++)
                sum += _smoothBuffer[i];
            targetValue = sum / _smoothBuffer.Count;
        }
        _animatedValue += (targetValue - _animatedValue) * LerpFactor;

        Color ledColor = _hasData ? Theme.PercentToColor(_animatedValue) : NoDataColor;

        g.SmoothingMode = SmoothingMode.AntiAlias;

        // Glow effect (larger semi-transparent ellipse behind)
        if (_hasData)
        {
            var glowRect = new Rectangle(
                ledX - glowPad, ledY - glowPad,
                ledSize + glowPad * 2, ledSize + glowPad * 2);
            using var glowPath = new GraphicsPath();
            glowPath.AddEllipse(glowRect);
            using var glowBrush = new PathGradientBrush(glowPath)
            {
                CenterColor = Color.FromArgb(60, ledColor),
                SurroundColors = [Color.FromArgb(0, ledColor)],
                CenterPoint = new PointF(
                    glowRect.X + glowRect.Width / 2f,
                    glowRect.Y + glowRect.Height / 2f)
            };
            g.FillEllipse(glowBrush, glowRect);
        }

        // Main LED body with radial gradient
        var ledRect = new Rectangle(ledX, ledY, ledSize, ledSize);
        using var ledPath = new GraphicsPath();
        ledPath.AddEllipse(ledRect);
        using var ledBrush = new PathGradientBrush(ledPath)
        {
            CenterColor = _hasData
                ? Theme.LerpColor(ledColor, Color.White, 0.35f)
                : Color.FromArgb(80, 80, 85),
            SurroundColors = [_hasData ? Theme.LerpColor(ledColor, Color.Black, 0.3f) : Color.FromArgb(40, 40, 45)],
            // Offset center up-left by 15%
            CenterPoint = new PointF(
                ledRect.X + ledRect.Width * 0.42f,
                ledRect.Y + ledRect.Height * 0.38f)
        };
        g.FillEllipse(ledBrush, ledRect);

        // Draw SVG icon centered in LED
        if (_icon != null)
        {
            int iconSize = ledSize * 2 / 5;
            int iconX = ledX + (ledSize - iconSize) / 2;
            int iconY = ledY + (ledSize - iconSize) / 2;
            using var imgAttr = new System.Drawing.Imaging.ImageAttributes();
            float alpha = _hasData ? 0.7f : 0.3f;
            float[][] matrixItems = [
                [1, 0, 0, 0, 0],
                [0, 1, 0, 0, 0],
                [0, 0, 1, 0, 0],
                [0, 0, 0, alpha, 0],
                [0, 0, 0, 0, 1]
            ];
            imgAttr.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix(matrixItems));
            g.DrawImage(_icon, new Rectangle(iconX, iconY, iconSize, iconSize),
                0, 0, _icon.Width, _icon.Height, GraphicsUnit.Pixel, imgAttr);
        }

        // Subtle highlight arc (top-left specular)
        var highlightRect = new Rectangle(
            ledX + ledSize / 5, ledY + ledSize / 7,
            ledSize / 2, ledSize / 3);
        using var highlightBrush = new SolidBrush(Color.FromArgb(_hasData ? 40 : 15, 255, 255, 255));
        g.FillEllipse(highlightBrush, highlightRect);

        g.SmoothingMode = SmoothingMode.None;

        // Label text below LED
        int textY = ledY + ledSize + 4;
        using var labelFont = new Font("Segoe UI", 7.5f);
        var labelSize = g.MeasureString(_label, labelFont);
        float labelX = (w - labelSize.Width) / 2f;
        using var labelBrush = new SolidBrush(Theme.TextLabel);
        g.DrawString(_label, labelFont, labelBrush, labelX, textY);

        // Value text
        int valueY = textY + 14;
        string valueText = _hasData ? $"{_animatedValue:F0}%" : "N/A";
        using var valueFont = new Font("Consolas", 8.5f, FontStyle.Bold);
        var valueSize = g.MeasureString(valueText, valueFont);
        float valueX = (w - valueSize.Width) / 2f;
        using var valueBrush = new SolidBrush(_hasData ? ledColor : Theme.TextDim);
        g.DrawString(valueText, valueFont, valueBrush, valueX, valueY);
    }
}
