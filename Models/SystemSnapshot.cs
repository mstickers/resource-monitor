namespace ResourceMonitor.Models;

public readonly record struct SystemSnapshot(
    DateTime Timestamp,
    ulong TotalPhysicalBytes,
    float StandbyMB,
    float ModifiedMB,
    float FreeMB,
    ulong CommitUsedBytes,
    ulong CommitLimitBytes,
    float PoolPagedMB,
    float PoolNonpagedMB)
{
    public float TotalMB => TotalPhysicalBytes / (1024f * 1024f);
    public float TotalGB => TotalPhysicalBytes / (1024f * 1024f * 1024f);
    public float InUseMB => TotalMB - StandbyMB - ModifiedMB - FreeMB;
    public float InUseGB => InUseMB / 1024f;
    public float AvailableMB => StandbyMB + FreeMB;
    public float CommitUsedGB => CommitUsedBytes / (1024f * 1024f * 1024f);
    public float CommitLimitGB => CommitLimitBytes / (1024f * 1024f * 1024f);

    public float UsedPercent => TotalMB > 0 ? (InUseMB + ModifiedMB) / TotalMB * 100f : 0f;
    public float StandbyPercent => TotalMB > 0 ? StandbyMB / TotalMB * 100f : 0f;
    public float CommitPercent => CommitLimitBytes > 0 ? (float)CommitUsedBytes / CommitLimitBytes * 100f : 0f;
}
