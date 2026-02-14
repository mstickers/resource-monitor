namespace ResourceMonitor.Models;

public enum ProcessSeverity { Normal, Warning, Critical }

public readonly record struct ProcessInfo(
    int Pid,
    string Name,
    long PrivateBytes,
    long WorkingSet,
    ProcessSeverity Severity)
{
    public static ProcessSeverity ComputeSeverity(long privateBytes) =>
        privateBytes > 2L * 1024 * 1024 * 1024 ? ProcessSeverity.Critical :
        privateBytes > 500L * 1024 * 1024 ? ProcessSeverity.Warning :
        ProcessSeverity.Normal;
}
