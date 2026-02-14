# Resource Monitor

Real-time WinForms GUI dashboard for QEMU/KVM VM with VirtIO balloon driver.

## Build & Run
```bash
dotnet build -c Release          # build
mmon                             # run (auto-builds if needed)
mmon --build                     # force rebuild + run
```

## Architecture

### Layout
- **Dark title bar** — native via `DwmSetWindowAttribute(20, TRUE)` P/Invoke
- **LED bar** (top, always visible): 7 glowing LED indicators with SVG icons inside (CPU, RAM, Disk, WAN, DB, DB CPU, DB RAM) + interval buttons
- **Tab navigation** (custom-drawn `TabNavigation` control): Dashboard, CPU, Memory, Graphs, Processes, Websites, Events — web-like with SVG icons, accent underline, hover effect
- **Tab content**: Panel-based show/hide (no WinForms TabControl)
- **StatusStrip**: refresh interval, balloon status, event counts
- **Dark theme** throughout — `Theme.cs` has shared colors, `PercentToColor()` gradient, and tab bar colors

### Assets/Icons/
14 SVG icons (24×24 viewBox, white `#FFFFFF` strokes, 1.5px stroke width, Lucide/Feather style):
- LED icons: `cpu`, `ram`, `disk`, `wan`, `db`, `db-cpu`, `db-ram`
- Tab icons: `tab-dashboard`, `tab-cpu`, `tab-memory`, `tab-graph`, `tab-process`, `tab-website`, `tab-events`
- Embedded as resources, loaded via `SvgIconHelper.Load()`, tinted at runtime

### Helpers/
- `SvgIconHelper` — loads SVG from embedded resources via `Svg` NuGet, renders to Bitmap at specified size with color tinting

### Models/
- `SystemSnapshot` — readonly record struct, memory/CPU state
- `ProcessInfo` — per-process metrics including `HandleCount`
- `NetworkSnapshot` — per-NIC stats with `NetworkRole` (WAN/DB/Unknown)
- `TcpConnectionSnapshot` — system-wide TCP connection counts
- `PingResult` — gateway/DNS ping results
- `DiskSnapshot`, `WebsiteCheck`, `OomEvent` — other metric structs
- `RingBuffer<T>` — generic circular buffer (600 slots, zero-alloc)
- `MetricTracker` — rolling statistics (avg/min/max) across 6 time windows (10s, 30s, 1m, 5m, 10m, 1h), 3600-sample ring buffer
- `ProcessIoSnapshot` — per-process disk I/O rates

### Services/
- `SystemMemoryService` — P/Invoke + PerformanceCounter (10 counters)
- `ProcessMonitorService` — process enumeration, CPU delta, handle count
- `ProcessIoService` — per-process I/O tracking via `GetProcessIoCounters` P/Invoke, returns top I/O process ("disk offender")
- `BalloonService` — VirtIO balloon detection (SMBIOS)
- `EventLogService` — OOM (2004), app crashes, critical events (Level=1), 48h window
- `NetworkMonitorService` — NIC rate tracking, interface role classification (172.16.x = DB), TCP snapshot, rate-to-percent
- `DiskMonitorService` — physical disk PerformanceCounters
- `WebsiteMonitorService` — staggered HTTP checks, 60-slot history ring buffers, IIS auto-discovery integration
- `PingMonitorService` — pings gateway (auto-detected) + 1.1.1.1 DNS
- `IisDiscoveryService` — parses `applicationHost.config` XML, discovers HTTPS sites with hostnames, filters stopped/management sites

### Controls/
- `LedIndicatorControl` — single LED voyant: GDI+ radial gradient, glow, ring buffer smoothing, animated lerp, **SVG icon overlay**
- `LedBarPanel` — 7 LEDs in a row + interval buttons, loads SVG icons into LEDs
- `TabNavigation` — custom-drawn web-like tab bar: icons + text, accent underline, hover backgrounds, rounded corners
- `DashboardDigestPanel` — 6-card GDI+ digest (Memory, CPU & Processes, Network, Disk, Websites, Events) with color-coded values, **mini sparklines**, **memory/disk offender display**
- `CpuDetailPanel` — dedicated CPU tab: big CPU%, progress bar, avg/min/max stats table across 6 time windows, top 5 CPU processes, network/disk summary, top I/O process
- `MemoryDetailPanel` — dedicated Memory tab: big RAM%, progress bar, detailed memory state, top memory consumer, stats table, mini memory graph (last 5 min)
- `WebsitesPanel` — per-site rows with status, response time, sparkline history
- `MemoryGraphPanel` — rolling multi-metric graph (used/standby/commit/CPU/pagefile)

### MainForm
- Timer loop on thread pool, `BeginInvoke` to UI thread
- `MetricTracker` instances for CPU, RAM, Disk statistics
- `ProcessIoService` for disk offender tracking
- Event log queries every 60 ticks, ping every 10 ticks
- IIS auto-discovery on startup, merged with config file sites
- Feeds all data to all tabs (LED bar, digest, CPU detail, memory detail, websites, graph, process list)

## LED Color Gradient
`Theme.PercentToColor(0–100%)`: green `(30,180,50)` → yellow `(220,220,40)` → orange `(240,150,30)` → red `(220,50,40)` → deep red `(180,20,30)`. Used by LEDs and digest value text.

## Network Role Classification
- Interface with any 172.16.x.x IPv4 address → `NetworkRole.DB`
- Otherwise → `NetworkRole.WAN`
- WAN max: 250,000 KB/s (2 Gbps), DB max: 1,024,000 KB/s (~1 GB/s soft cap)

## Performance Constraints
- Target: < 30 MB working set
- `SystemSnapshot` is `readonly record struct` (stack-allocated)
- `RingBuffer<T>` pre-allocates once, zero GC pressure in hot path
- All digest/LED/tab controls use GDI+ `OnPaint` (no child controls)
- Event log queries run every 60 ticks (not every tick)
- Process enumeration filters < 1 MB (skip noise)

## VirtIO Balloon
- `GetPhysicallyInstalledSystemMemory` (SMBIOS) vs `GlobalMemoryStatusEx.ullTotalPhys`
- Delta > 50 MB → balloon inflated, shows reclaimed amount
- Detects `blnsvr.exe` process or `balloon.sys` driver

## Security
- `config/` is gitignored — never commit machine names, IPs, or credentials
- See parent `D:\CC\CLAUDE.md` for shared conventions

## Dependencies
- .NET 10 (`net10.0-windows`) — WinForms framework includes PerformanceCounter and EventLog
- `Svg` NuGet package (v3.4.7, svg-net) — renders SVG to System.Drawing objects for icon loading

## Notes
- ASR blocks running .exe directly — always launch via `dotnet ResourceMonitor.dll`
