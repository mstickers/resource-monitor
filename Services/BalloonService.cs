using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ResourceMonitor.Services;

public sealed class BalloonService
{
    private readonly ulong _smbiosTotalBytes;
    private readonly bool _driverDetected;

    public bool DriverDetected => _driverDetected;
    public ulong SmbiosTotalBytes => _smbiosTotalBytes;

    public BalloonService()
    {
        // Detect VirtIO balloon driver
        _driverDetected = Process.GetProcessesByName("blnsvr").Length > 0
            || File.Exists(@"C:\Windows\System32\drivers\balloon.sys");

        // Get SMBIOS total (what the hypervisor originally assigned)
        if (GetPhysicallyInstalledSystemMemory(out ulong totalKB))
            _smbiosTotalBytes = totalKB * 1024;
    }

    public (bool IsInflated, long ReclaimedBytes) Check(ulong currentTotalPhysBytes)
    {
        if (!_driverDetected || _smbiosTotalBytes == 0)
            return (false, 0);

        long delta = (long)_smbiosTotalBytes - (long)currentTotalPhysBytes;
        const long threshold = 50L * 1024 * 1024; // 50 MB noise floor

        if (delta > threshold)
            return (true, delta);

        return (false, 0);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPhysicallyInstalledSystemMemory(out ulong totalMemoryInKilobytes);
}
