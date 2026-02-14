using System.Net.NetworkInformation;
using ResourceMonitor.Models;

namespace ResourceMonitor.Services;

public sealed class NetworkMonitorService
{
    private Dictionary<string, (long Sent, long Recv, DateTime When)> _prev = [];

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

            result.Add(new NetworkSnapshot(name, sent, recv, sendRate, recvRate));
        }

        _prev = newPrev;
        return result;
    }
}
