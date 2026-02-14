namespace ResourceMonitor.Models;

public readonly record struct NetworkSnapshot(
    string Name,
    long BytesSent,
    long BytesReceived,
    double SendRateKBps,
    double ReceiveRateKBps,
    NetworkRole Role = NetworkRole.Unknown)
{
    public double TotalRateKBps => SendRateKBps + ReceiveRateKBps;
}
