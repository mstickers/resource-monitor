namespace ResourceMonitor.Models;

public readonly record struct ProcessIoSnapshot(
    int Pid, string Name,
    double ReadRateKBps, double WriteRateKBps)
{
    public double TotalRateKBps => ReadRateKBps + WriteRateKBps;
}
