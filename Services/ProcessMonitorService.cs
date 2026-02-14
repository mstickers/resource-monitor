using System.Diagnostics;
using ResourceMonitor.Models;

namespace ResourceMonitor.Services;

public sealed class ProcessMonitorService
{
    // Track previous CPU times for delta calculation
    private Dictionary<int, (TimeSpan Cpu, DateTime When)> _prevCpu = [];
    private readonly int _processorCount = Environment.ProcessorCount;

    public List<ProcessInfo> GetProcesses()
    {
        var now = DateTime.UtcNow;
        var newCpu = new Dictionary<int, (TimeSpan Cpu, DateTime When)>();
        var result = new List<ProcessInfo>(64);

        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                long priv = proc.PrivateMemorySize64;
                long ws = proc.WorkingSet64;
                TimeSpan totalCpu = proc.TotalProcessorTime;
                DateTime? startTime = null;
                try { startTime = proc.StartTime; } catch { }

                // CPU % = delta CPU time / delta wall time / processor count
                double cpuPct = 0;
                if (_prevCpu.TryGetValue(proc.Id, out var prev))
                {
                    double elapsed = (now - prev.When).TotalMilliseconds;
                    if (elapsed > 0)
                    {
                        double cpuMs = (totalCpu - prev.Cpu).TotalMilliseconds;
                        cpuPct = cpuMs / elapsed / _processorCount * 100.0;
                        if (cpuPct < 0) cpuPct = 0;
                        if (cpuPct > 100) cpuPct = 100;
                    }
                }
                newCpu[proc.Id] = (totalCpu, now);

                if (priv > 1024 * 1024 || cpuPct > 1) // skip < 1 MB and < 1% CPU
                {
                    result.Add(new ProcessInfo(
                        proc.Id,
                        proc.ProcessName,
                        priv,
                        ws,
                        cpuPct,
                        totalCpu,
                        startTime,
                        ProcessInfo.ComputeSeverity(priv, cpuPct)));
                }
            }
            catch { }
            finally { proc.Dispose(); }
        }

        _prevCpu = newCpu;
        result.Sort((a, b) => b.PrivateBytes.CompareTo(a.PrivateBytes));
        return result;
    }
}
