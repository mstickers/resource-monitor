using System.Xml.Linq;

namespace ResourceMonitor.Services;

public static class IisDiscoveryService
{
    public static List<(string Name, string Url)> GetIisSites()
    {
        var result = new List<(string, string)>();
        try
        {
            string configPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "inetsrv", "config", "applicationHost.config");

            if (!File.Exists(configPath)) return result;

            var doc = XDocument.Load(configPath);
            var sites = doc.Descendants("site");

            foreach (var site in sites)
            {
                string name = site.Attribute("name")?.Value ?? "";
                if (string.IsNullOrEmpty(name)) continue;

                // Skip known management panels and stopped sites
                var state = site.Element("application")?.Element("virtualDirectory")
                    ?.Attribute("physicalPath")?.Value ?? "";
                // Check state attribute on site element (IIS uses ftpServer/webServer state)
                bool isStarted = true;
                var stateAttr = site.Attribute("state");
                if (stateAttr != null && stateAttr.Value.Equals("Stopped", StringComparison.OrdinalIgnoreCase))
                    isStarted = false;

                if (!isStarted) continue;
                if (name.Equals("Default Web Site", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.Contains("SolidCP", StringComparison.OrdinalIgnoreCase)) continue;

                // Find HTTPS binding with hostname
                var bindings = site.Element("bindings")?.Elements("binding") ?? [];
                foreach (var binding in bindings)
                {
                    string protocol = binding.Attribute("protocol")?.Value ?? "";
                    string info = binding.Attribute("bindingInformation")?.Value ?? "";

                    if (!protocol.Equals("https", StringComparison.OrdinalIgnoreCase)) continue;

                    // Format: *:443:hostname or IP:443:hostname
                    var parts = info.Split(':');
                    if (parts.Length >= 3)
                    {
                        string hostname = parts[2];
                        if (!string.IsNullOrEmpty(hostname))
                        {
                            result.Add((name, $"https://{hostname}"));
                            break; // one URL per site
                        }
                    }
                }
            }
        }
        catch
        {
            // IIS not installed or config not accessible
        }
        return result;
    }
}
