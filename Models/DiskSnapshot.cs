namespace ResourceMonitor.Models;

public readonly record struct DiskSnapshot(
    float ReadBytesPerSec,
    float WriteBytesPerSec,
    float DiskTimePct);
