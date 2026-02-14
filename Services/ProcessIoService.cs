using System.Diagnostics;
using System.Runtime.InteropServices;
using ResourceMonitor.Models;

namespace ResourceMonitor.Services;

public sealed class ProcessIoService
{
    private readonly Dictionary<int, (long ReadBytes, long WriteBytes, DateTime Time)> _prev = [];

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessIoCounters(IntPtr hProcess, out IO_COUNTERS counters);

    public ProcessIoSnapshot? GetTopIoProcess()
    {
        var now = DateTime.UtcNow;
        ProcessIoSnapshot? top = null;
        double topRate = 0;

        var current = new Dictionary<int, (long, long, DateTime)>();

        try
        {
            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    if (proc.Id == 0 || proc.Id == 4) continue; // skip System/Idle
                    if (!GetProcessIoCounters(proc.Handle, out var counters)) continue;

                    long readBytes = (long)counters.ReadTransferCount;
                    long writeBytes = (long)counters.WriteTransferCount;
                    current[proc.Id] = (readBytes, writeBytes, now);

                    if (_prev.TryGetValue(proc.Id, out var prev))
                    {
                        double elapsed = (now - prev.Time).TotalSeconds;
                        if (elapsed > 0.1)
                        {
                            double readRate = Math.Max(0, (readBytes - prev.ReadBytes) / elapsed / 1024.0);
                            double writeRate = Math.Max(0, (writeBytes - prev.WriteBytes) / elapsed / 1024.0);
                            double totalRate = readRate + writeRate;

                            if (totalRate > topRate)
                            {
                                topRate = totalRate;
                                top = new ProcessIoSnapshot(proc.Id, proc.ProcessName, readRate, writeRate);
                            }
                        }
                    }
                }
                catch { }
                finally { proc.Dispose(); }
            }
        }
        catch { }

        _prev.Clear();
        foreach (var kv in current)
            _prev[kv.Key] = kv.Value;

        return top;
    }
}
