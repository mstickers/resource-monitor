using System.Drawing;
using ResourceMonitor.Helpers;

namespace ResourceMonitor.Controls;

public sealed class LedBarPanel : Control
{
    public LedIndicatorControl LedCpu { get; }
    public LedIndicatorControl LedRam { get; }
    public LedIndicatorControl LedDisk { get; }
    public LedIndicatorControl LedNetWan { get; }
    public LedIndicatorControl LedNetDb { get; }
    public LedIndicatorControl LedWeb { get; }
    public LedIndicatorControl LedDbCpu { get; }
    public LedIndicatorControl LedDbRam { get; }

    private readonly LedIndicatorControl[] _leds;
    private Button _btn1s, _btn5s, _btn10s;

    public event Action<int>? IntervalChanged;

    public LedBarPanel()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.AllPaintingInWmPaint, true);

        Height = 100;
        Dock = DockStyle.Top;
        BackColor = Theme.BgColor;

        LedCpu = new LedIndicatorControl { Label = "CPU" };
        LedRam = new LedIndicatorControl { Label = "RAM" };
        LedDisk = new LedIndicatorControl { Label = "Disk" };
        LedNetWan = new LedIndicatorControl { Label = "WAN" };
        LedNetDb = new LedIndicatorControl { Label = "DB" };
        LedWeb = new LedIndicatorControl { Label = "Web" };
        LedDbCpu = new LedIndicatorControl { Label = "DB CPU" };
        LedDbRam = new LedIndicatorControl { Label = "DB RAM" };

        LedWeb.SetNoData();
        LedDbCpu.SetNoData();
        LedDbRam.SetNoData();

        _leds = [LedCpu, LedRam, LedDisk, LedNetWan, LedNetDb, LedWeb, LedDbCpu, LedDbRam];

        _btn1s = CreateButton("1s", 1000);
        _btn5s = CreateButton("5s", 5000);
        _btn10s = CreateButton("10s", 10000);

        Controls.AddRange(_leds);
        Controls.Add(_btn1s);
        Controls.Add(_btn5s);
        Controls.Add(_btn10s);

        LoadIcons();
        Resize += (_, _) => LayoutControls();
    }

    private void LoadIcons()
    {
        var white = Color.White;
        (LedIndicatorControl led, string name)[] mapping =
        [
            (LedCpu, "cpu"), (LedRam, "ram"), (LedDisk, "disk"),
            (LedNetWan, "wan"), (LedNetDb, "db"),
            (LedWeb, "web"), (LedDbCpu, "db-cpu"), (LedDbRam, "db-ram")
        ];
        foreach (var (led, name) in mapping)
        {
            var icon = SvgIconHelper.Load(name, 20, white);
            if (icon != null) led.SetIcon(icon);
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        LayoutControls();
        UpdateIntervalButtons(1000);
    }

    private void LayoutControls()
    {
        int w = ClientSize.Width;
        int h = ClientSize.Height;
        int ledW = 80;
        int totalLedWidth = _leds.Length * ledW;
        int startX = Math.Max(8, (w - totalLedWidth - 150) / 2); // leave room for buttons

        for (int i = 0; i < _leds.Length; i++)
        {
            _leds[i].SetBounds(startX + i * ledW, 0, ledW, h);
        }

        // Interval buttons right-aligned
        int btnY = (h - 24) / 2;
        int right = w - 8;
        _btn10s.Location = new Point(right - 40, btnY);
        _btn5s.Location = new Point(right - 84, btnY);
        _btn1s.Location = new Point(right - 128, btnY);
    }

    public void SetSmoothingWindow(int intervalMs)
    {
        // At 1s: 20 samples. At 5s: 4. At 10s: 2.
        int capacity = Math.Max(2, 20000 / intervalMs);
        foreach (var led in _leds)
            led.SetSmoothingCapacity(capacity);
        UpdateIntervalButtons(intervalMs);
    }

    private void UpdateIntervalButtons(int activeMs)
    {
        foreach (var btn in new[] { _btn1s, _btn5s, _btn10s })
        {
            bool active = (int)btn.Tag! == activeMs;
            btn.BackColor = active ? Color.FromArgb(60, 120, 200) : Color.FromArgb(50, 50, 55);
            btn.ForeColor = active ? Color.White : Theme.TextLabel;
        }
    }

    private Button CreateButton(string text, int intervalMs)
    {
        var btn = new Button
        {
            Text = text,
            Size = new Size(40, 24),
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 8f),
            Tag = intervalMs,
            BackColor = Color.FromArgb(50, 50, 55),
            ForeColor = Theme.TextLabel
        };
        btn.FlatAppearance.BorderColor = Theme.Border;
        btn.FlatAppearance.BorderSize = 1;
        btn.Click += (_, _) =>
        {
            UpdateIntervalButtons(intervalMs);
            IntervalChanged?.Invoke(intervalMs);
        };
        return btn;
    }
}
