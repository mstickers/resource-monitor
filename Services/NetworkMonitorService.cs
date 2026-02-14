using System.Net.NetworkInformation;
using ResourceMonitor.Models;

namespace ResourceMonitor.Services;

public sealed class NetworkMonitorService
{
    private Dictionary<string, (long Sent, long Recv, DateTime When)> _prev = [];

    public const double WanMaxKBps = 250_000;   // 2 Gbps
    public const double DbMaxKBps = 1_024_000;  // ~1 GB/s soft cap

    public List<NetworkSnapshot> GetSnapshots()
    {
        var now = DateTime.UtcNow;
        var result = new List<NetworkSnapshot>();
        var newPrev = new Dictionary<string, (long, long, DateTime)>();

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback
                or NetworkInterfaceType.Tunnel) continue;

            var stats = nic.GetIPv4Statistics();
            long sent = stats.BytesSent;
            long recv = stats.BytesReceived;
            string name = nic.Name;

            double sendRate = 0, recvRate = 0;
            if (_prev.TryGetValue(name, out var prev))
            {
                double elapsed = (now - prev.When).TotalSeconds;
                if (elapsed > 0)
                {
                    sendRate = (sent - prev.Sent) / 1024.0 / elapsed;
                    recvRate = (recv - prev.Recv) / 1024.0 / elapsed;
                    if (sendRate < 0) sendRate = 0;
                    if (recvRate < 0) recvRate = 0;
                }
            }
            newPrev[name] = (sent, recv, now);

            var role = ClassifyInterface(nic);
            result.Add(new NetworkSnapshot(name, sent, recv, sendRate, recvRate, role));
        }

        _prev = newPrev;
        return result;
    }

    public static TcpConnectionSnapshot GetTcpSnapshot()
    {
        try
        {
            var connections = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections();
            int established = 0, timeWait = 0;
            foreach (var conn in connections)
            {
                if (conn.State == TcpState.Established) established++;
                else if (conn.State == TcpState.TimeWait) timeWait++;
            }
            return new TcpConnectionSnapshot(established, timeWait, connections.Length);
        }
        catch
        {
            return default;
        }
    }

    public static float RateToPercent(double totalRateKBps, NetworkRole role)
    {
        double max = role == NetworkRole.DB ? DbMaxKBps : WanMaxKBps;
        return (float)Math.Clamp(totalRateKBps / max * 100.0, 0, 100);
    }

    private static NetworkRole ClassifyInterface(NetworkInterface nic)
    {
        try
        {
            foreach (var addr in nic.GetIPProperties().UnicastAddresses)
            {
                if (addr.Address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
                    continue;
                var bytes = addr.Address.GetAddressBytes();
                if (bytes[0] == 172 && bytes[1] == 16)
                    return NetworkRole.DB;
            }
            return NetworkRole.WAN;
        }
        catch
        {
            return NetworkRole.Unknown;
        }
    }
}
