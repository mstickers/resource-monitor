using System.Diagnostics;
using ResourceMonitor.Models;

namespace ResourceMonitor.Services;

public sealed class ProcessMonitorService
{
    public List<ProcessInfo> GetProcesses()
    {
        var result = new List<ProcessInfo>(64);
        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                long priv = proc.PrivateMemorySize64;
                long ws = proc.WorkingSet64;
                if (priv > 1024 * 1024) // skip < 1 MB
                {
                    result.Add(new ProcessInfo(
                        proc.Id,
                        proc.ProcessName,
                        priv,
                        ws,
                        ProcessInfo.ComputeSeverity(priv)));
                }
            }
            catch { }
            finally { proc.Dispose(); }
        }
        result.Sort((a, b) => b.PrivateBytes.CompareTo(a.PrivateBytes));
        return result;
    }
}
