using System.Diagnostics;
using ResourceMonitor.Models;

namespace ResourceMonitor.Services;

public sealed class DiskMonitorService : IDisposable
{
    private readonly PerformanceCounter? _readBytes;
    private readonly PerformanceCounter? _writeBytes;
    private readonly PerformanceCounter? _diskTime;
    private readonly bool _available;

    public DiskMonitorService()
    {
        try
        {
            _readBytes = new PerformanceCounter("PhysicalDisk", "Disk Read Bytes/sec", "_Total", true);
            _writeBytes = new PerformanceCounter("PhysicalDisk", "Disk Write Bytes/sec", "_Total", true);
            _diskTime = new PerformanceCounter("PhysicalDisk", "% Disk Time", "_Total", true);
            // Prime rate-based counters
            _readBytes.NextValue();
            _writeBytes.NextValue();
            _diskTime.NextValue();
            _available = true;
        }
        catch
        {
            _available = false;
        }
    }

    public DiskSnapshot GetSnapshot()
    {
        if (!_available) return default;
        try
        {
            return new DiskSnapshot(
                _readBytes!.NextValue(),
                _writeBytes!.NextValue(),
                _diskTime!.NextValue());
        }
        catch
        {
            return default;
        }
    }

    public void Dispose()
    {
        _readBytes?.Dispose();
        _writeBytes?.Dispose();
        _diskTime?.Dispose();
    }
}
