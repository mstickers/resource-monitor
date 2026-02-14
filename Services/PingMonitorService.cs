using System.Net.NetworkInformation;
using ResourceMonitor.Models;

namespace ResourceMonitor.Services;

public sealed class PingMonitorService
{
    private const int TimeoutMs = 2000;
    private const string DnsTarget = "1.1.1.1";
    private string? _gatewayAddress;

    public PingMonitorService()
    {
        _gatewayAddress = DetectGateway();
    }

    public List<PingResult> Ping()
    {
        var results = new List<PingResult>(2);

        // Gateway ping
        if (_gatewayAddress != null)
        {
            results.Add(DoPing(_gatewayAddress, "GW"));
        }
        else
        {
            // Retry gateway detection periodically
            _gatewayAddress = DetectGateway();
            results.Add(new PingResult("", "GW", -1, false, DateTime.Now));
        }

        // DNS ping (1.1.1.1)
        results.Add(DoPing(DnsTarget, "DNS"));

        return results;
    }

    private static PingResult DoPing(string target, string label)
    {
        try
        {
            using var pinger = new System.Net.NetworkInformation.Ping();
            var reply = pinger.Send(target, TimeoutMs);
            if (reply.Status == IPStatus.Success)
                return new PingResult(target, label, (int)reply.RoundtripTime, true, DateTime.Now);
            return new PingResult(target, label, -1, false, DateTime.Now);
        }
        catch
        {
            return new PingResult(target, label, -1, false, DateTime.Now);
        }
    }

    private static string? DetectGateway()
    {
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback
                    or NetworkInterfaceType.Tunnel) continue;

                foreach (var gw in nic.GetIPProperties().GatewayAddresses)
                {
                    if (gw.Address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
                        continue;
                    var bytes = gw.Address.GetAddressBytes();
                    if (bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 0 && bytes[3] == 0)
                        continue;
                    return gw.Address.ToString();
                }
            }
        }
        catch { }
        return null;
    }
}
