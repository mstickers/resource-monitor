using System.Diagnostics;
using ResourceMonitor.Models;

namespace ResourceMonitor.Services;

public sealed class WebsiteMonitorService : IDisposable
{
    private readonly HttpClient _client;
    private readonly List<(string Name, string Url)> _sites = [];
    private readonly Dictionary<string, WebsiteCheck> _lastChecks = [];
    private readonly Dictionary<string, RingBuffer<WebsiteCheck>> _history = [];
    private readonly object _lock = new();
    private System.Threading.Timer? _timer;
    private int _currentIndex = -1;
    private int _intervalSeconds = 60;

    public WebsiteMonitorService()
    {
        _client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15),
            DefaultRequestHeaders = { { "User-Agent", "ResourceMonitor/1.0" } }
        };
    }

    public void LoadConfig(string configPath)
    {
        if (!File.Exists(configPath)) return;

        foreach (var line in File.ReadAllLines(configPath))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;

            // Format: name|url
            var parts = trimmed.Split('|', 2);
            if (parts.Length == 2 && Uri.TryCreate(parts[1].Trim(), UriKind.Absolute, out _))
            {
                string name = parts[0].Trim();
                string url = parts[1].Trim();
                _sites.Add((name, url));
                _history[name] = new RingBuffer<WebsiteCheck>(60); // 1 hour at 1-min interval
            }
        }
    }

    public int SiteCount => _sites.Count;

    public void LoadFromIis()
    {
        var iisSites = IisDiscoveryService.GetIisSites();
        var existingUrls = new HashSet<string>(_sites.Select(s => s.Url), StringComparer.OrdinalIgnoreCase);

        foreach (var (name, url) in iisSites)
        {
            if (existingUrls.Contains(url)) continue;
            _sites.Add((name, url));
            _history[name] = new RingBuffer<WebsiteCheck>(60);
            existingUrls.Add(url);
        }
    }

    /// <summary>Start staggered checks. Spreads N sites evenly across the interval.</summary>
    public void Start(int intervalSeconds = 60)
    {
        _intervalSeconds = intervalSeconds;
        if (_sites.Count == 0) return;
        int staggerMs = intervalSeconds * 1000 / _sites.Count;
        // Start first check after a short delay
        _timer = new System.Threading.Timer(OnTick, null, 2000, staggerMs);
    }

    private void OnTick(object? state)
    {
        int idx = Interlocked.Increment(ref _currentIndex);
        if (_sites.Count == 0) return;
        idx = ((idx % _sites.Count) + _sites.Count) % _sites.Count;

        var (name, url) = _sites[idx];
        _ = CheckSiteAsync(name, url);
    }

    private async Task CheckSiteAsync(string name, string url, bool isRetry = false)
    {
        WebsiteCheck check;
        var sw = Stopwatch.StartNew();
        try
        {
            using var response = await _client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            sw.Stop();
            check = new WebsiteCheck(url, name, (int)sw.ElapsedMilliseconds,
                (int)response.StatusCode, DateTime.Now, response.IsSuccessStatusCode);
        }
        catch
        {
            sw.Stop();
            check = new WebsiteCheck(url, name, -1, 0, DateTime.Now, false);
        }

        lock (_lock)
        {
            _lastChecks[name] = check;
            if (_history.TryGetValue(name, out var buf))
                buf.Add(check);
        }

        // Schedule retry in 30s if down or slow (500ms+), but only once
        if (!isRetry && (!check.IsUp || check.ResponseTimeMs >= 500))
        {
            _ = Task.Delay(30_000).ContinueWith(_ => CheckSiteAsync(name, url, isRetry: true));
        }
    }

    public List<WebsiteCheck> GetLatestChecks()
    {
        lock (_lock)
        {
            return [.. _lastChecks.Values];
        }
    }

    public Dictionary<string, RingBuffer<WebsiteCheck>> GetHistory()
    {
        lock (_lock)
        {
            return new Dictionary<string, RingBuffer<WebsiteCheck>>(_history);
        }
    }

    public int GetSecondsUntilNextCheck(string name)
    {
        lock (_lock)
        {
            if (_lastChecks.TryGetValue(name, out var check))
            {
                var nextCheck = check.Timestamp.AddSeconds(_intervalSeconds);
                int remaining = (int)(nextCheck - DateTime.Now).TotalSeconds;
                return Math.Max(0, remaining);
            }
        }
        return -1; // not yet checked
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _client.Dispose();
    }
}
