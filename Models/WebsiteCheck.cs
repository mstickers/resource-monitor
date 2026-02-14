namespace ResourceMonitor.Models;

public readonly record struct WebsiteCheck(
    string Url,
    string Name,
    int ResponseTimeMs,   // -1 = failed
    int StatusCode,
    DateTime Timestamp,
    bool IsUp);
