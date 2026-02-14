namespace ResourceMonitor.Models;

public readonly record struct OomEvent(
    DateTime Timestamp,
    string Source,
    string Message,
    int EventId);
