namespace ResourceMonitor.Models;

public sealed class MetricTracker
{
    private readonly (DateTime Time, float Value)[] _buffer;
    private int _head;
    private int _count;

    public static readonly TimeSpan[] Windows =
    [
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(10),
        TimeSpan.FromHours(1)
    ];

    public static readonly string[] WindowLabels = ["10s", "30s", "1m", "5m", "10m", "1h"];

    public MetricTracker(int capacity = 3600)
    {
        _buffer = new (DateTime, float)[capacity];
    }

    public void Record(float value)
    {
        _buffer[_head] = (DateTime.UtcNow, value);
        _head = (_head + 1) % _buffer.Length;
        if (_count < _buffer.Length) _count++;
    }

    public MetricStats GetStats(TimeSpan window)
    {
        if (_count == 0) return default;

        var cutoff = DateTime.UtcNow - window;
        float sum = 0, min = float.MaxValue, max = float.MinValue;
        int samples = 0;

        for (int i = 0; i < _count; i++)
        {
            int idx = (_head - 1 - i + _buffer.Length) % _buffer.Length;
            var (time, value) = _buffer[idx];
            if (time < cutoff) break;

            sum += value;
            if (value < min) min = value;
            if (value > max) max = value;
            samples++;
        }

        if (samples == 0) return default;
        return new MetricStats(sum / samples, min, max, samples);
    }

    public float LastValue => _count > 0
        ? _buffer[(_head - 1 + _buffer.Length) % _buffer.Length].Value
        : 0f;
}

public readonly record struct MetricStats(float Avg, float Min, float Max, int SampleCount);
