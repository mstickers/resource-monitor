namespace ResourceMonitor.Models;

public readonly record struct TcpConnectionSnapshot(
    int TotalEstablished,
    int TotalTimeWait,
    int TotalAll);
