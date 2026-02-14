namespace ResourceMonitor.Models;

public readonly record struct PingResult(
    string Target,
    string Label,
    int RoundtripMs,    // -1 = failed
    bool Success,
    DateTime Timestamp);
