using System.Diagnostics.Eventing.Reader;
using System.Text.RegularExpressions;
using ResourceMonitor.Models;

namespace ResourceMonitor.Services;

public sealed class EventLogService
{
    private static readonly Regex OffenderRegex = new(
        @"(\S+\.exe)\s+\((\d+)\)\s+consumed\s+(\d+)\s+bytes",
        RegexOptions.Compiled);

    private static readonly Regex FaultingAppRegex = new(
        @"Faulting application name:\s*(\S+?)[\s,]",
        RegexOptions.Compiled);

    public (int OomWarnings, int AppCrashes) GetCounts()
    {
        int oom = CountEvents("System",
            "*[System[Provider[@Name='Microsoft-Windows-Resource-Exhaustion-Detector'] and (EventID=2004) and TimeCreated[timediff(@SystemTime) <= 172800000]]]");

        int crashes = CountEvents("Application",
            "*[System[Provider[@Name='Application Error'] and TimeCreated[timediff(@SystemTime) <= 172800000]]]");

        return (oom, crashes);
    }

    public List<OomEvent> GetOomEvents()
    {
        var results = new List<OomEvent>();
        try
        {
            var query = new EventLogQuery("System", PathType.LogName,
                "*[System[Provider[@Name='Microsoft-Windows-Resource-Exhaustion-Detector'] and (EventID=2004) and TimeCreated[timediff(@SystemTime) <= 172800000]]]");

            using var reader = new EventLogReader(query);
            EventRecord? record;
            while ((record = reader.ReadEvent()) != null)
            {
                using (record)
                {
                    string message = record.FormatDescription() ?? "";
                    results.Add(new OomEvent(
                        record.TimeCreated ?? DateTime.Now,
                        "Resource Exhaustion",
                        message,
                        record.Id));
                }
            }
        }
        catch { }
        return results;
    }

    public List<OomEvent> GetCrashEvents()
    {
        var results = new List<OomEvent>();
        try
        {
            var query = new EventLogQuery("Application", PathType.LogName,
                "*[System[Provider[@Name='Application Error'] and TimeCreated[timediff(@SystemTime) <= 172800000]]]");

            using var reader = new EventLogReader(query);
            EventRecord? record;
            while ((record = reader.ReadEvent()) != null)
            {
                using (record)
                {
                    string message = record.FormatDescription() ?? "";
                    var match = FaultingAppRegex.Match(message);
                    string source = match.Success ? match.Groups[1].Value : "unknown";
                    results.Add(new OomEvent(
                        record.TimeCreated ?? DateTime.Now,
                        source,
                        message,
                        record.Id));
                }
            }
        }
        catch { }
        return results;
    }

    private static int CountEvents(string logName, string xpath)
    {
        int count = 0;
        try
        {
            var query = new EventLogQuery(logName, PathType.LogName, xpath);
            using var reader = new EventLogReader(query);
            while (reader.ReadEvent() is { } record)
            {
                record.Dispose();
                count++;
            }
        }
        catch { }
        return count;
    }
}
