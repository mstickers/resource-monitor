using System.Diagnostics;
using System.Runtime.InteropServices;
using ResourceMonitor.Models;

namespace ResourceMonitor.Services;

public sealed class SystemMemoryService : IDisposable
{
    private readonly PerformanceCounter? _standbyReserve;
    private readonly PerformanceCounter? _standbyNormal;
    private readonly PerformanceCounter? _standbyCore;
    private readonly PerformanceCounter? _modified;
    private readonly PerformanceCounter? _free;
    private readonly PerformanceCounter? _poolPaged;
    private readonly PerformanceCounter? _poolNonpaged;
    private readonly bool _countersAvailable;

    public SystemMemoryService()
    {
        try
        {
            _standbyReserve = new PerformanceCounter("Memory", "Standby Cache Reserve Bytes", true);
            _standbyNormal = new PerformanceCounter("Memory", "Standby Cache Normal Priority Bytes", true);
            _standbyCore = new PerformanceCounter("Memory", "Standby Cache Core Bytes", true);
            _modified = new PerformanceCounter("Memory", "Modified Page List Bytes", true);
            _free = new PerformanceCounter("Memory", "Free & Zero Page List Bytes", true);
            _poolPaged = new PerformanceCounter("Memory", "Pool Paged Bytes", true);
            _poolNonpaged = new PerformanceCounter("Memory", "Pool Nonpaged Bytes", true);
            _countersAvailable = true;
        }
        catch
        {
            _countersAvailable = false;
        }
    }

    public SystemSnapshot GetSnapshot()
    {
        var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        GlobalMemoryStatusEx(ref mem);

        float standbyMB = 0, modifiedMB = 0, freeMB = 0;
        float poolPagedMB = 0, poolNonpagedMB = 0;

        if (_countersAvailable)
        {
            try
            {
                float reserve = _standbyReserve!.NextValue();
                float normal = _standbyNormal!.NextValue();
                float core = _standbyCore!.NextValue();
                standbyMB = (reserve + normal + core) / (1024f * 1024f);
                modifiedMB = _modified!.NextValue() / (1024f * 1024f);
                freeMB = _free!.NextValue() / (1024f * 1024f);
                poolPagedMB = _poolPaged!.NextValue() / (1024f * 1024f);
                poolNonpagedMB = _poolNonpaged!.NextValue() / (1024f * 1024f);
            }
            catch
            {
                // Fall back to basic calculation
                standbyMB = 0;
                modifiedMB = 0;
                freeMB = mem.ullAvailPhys / (1024f * 1024f);
            }
        }
        else
        {
            freeMB = mem.ullAvailPhys / (1024f * 1024f);
        }

        ulong commitUsed = mem.ullTotalPageFile - mem.ullAvailPageFile;

        return new SystemSnapshot(
            Timestamp: DateTime.Now,
            TotalPhysicalBytes: mem.ullTotalPhys,
            StandbyMB: standbyMB,
            ModifiedMB: modifiedMB,
            FreeMB: freeMB,
            CommitUsedBytes: commitUsed,
            CommitLimitBytes: mem.ullTotalPageFile,
            PoolPagedMB: poolPagedMB,
            PoolNonpagedMB: poolNonpagedMB);
    }

    public void Dispose()
    {
        _standbyReserve?.Dispose();
        _standbyNormal?.Dispose();
        _standbyCore?.Dispose();
        _modified?.Dispose();
        _free?.Dispose();
        _poolPaged?.Dispose();
        _poolNonpaged?.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);
}
