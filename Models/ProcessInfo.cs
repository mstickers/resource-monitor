namespace ResourceMonitor.Models;

public enum ProcessSeverity { Normal, Warning, Critical }

public readonly record struct ProcessInfo(
    int Pid,
    string Name,
    long PrivateBytes,
    long WorkingSet,
    double CpuPercent,
    TimeSpan TotalCpu,
    DateTime? StartTime,
    ProcessSeverity Severity)
{
    public static ProcessSeverity ComputeSeverity(long privateBytes, double cpuPct) =>
        privateBytes > 2L * 1024 * 1024 * 1024 || cpuPct > 80 ? ProcessSeverity.Critical :
        privateBytes > 500L * 1024 * 1024 || cpuPct > 30 ? ProcessSeverity.Warning :
        ProcessSeverity.Normal;
}
