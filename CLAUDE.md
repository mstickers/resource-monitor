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
- **LED bar** (top, always visible): 7 glowing LED indicators (CPU, RAM, Disk, WAN, DB, DB CPU, DB RAM) + interval buttons
- **Tabs**: Dashboard (digest cards), Graphs (rolling chart), Processes (virtual ListView), Websites (detail + sparklines), Events (log viewer)
- **StatusStrip**: refresh interval, balloon status, event counts
- **Dark theme** throughout — `Theme.cs` has shared colors and `PercentToColor()` gradient

### Models/
- `SystemSnapshot` — readonly record struct, memory/CPU state
- `ProcessInfo` — per-process metrics including `HandleCount`
- `NetworkSnapshot` — per-NIC stats with `NetworkRole` (WAN/DB/Unknown)
- `TcpConnectionSnapshot` — system-wide TCP connection counts
- `PingResult` — gateway/DNS ping results
- `DiskSnapshot`, `WebsiteCheck`, `OomEvent` — other metric structs
- `RingBuffer<T>` — generic circular buffer (600 slots, zero-alloc)

### Services/
- `SystemMemoryService` — P/Invoke + PerformanceCounter (10 counters)
- `ProcessMonitorService` — process enumeration, CPU delta, handle count
- `BalloonService` — VirtIO balloon detection (SMBIOS)
- `EventLogService` — OOM (2004), app crashes, critical events (Level=1), 48h window
- `NetworkMonitorService` — NIC rate tracking, interface role classification (172.16.x = DB), TCP snapshot, rate-to-percent
- `DiskMonitorService` — physical disk PerformanceCounters
- `WebsiteMonitorService` — staggered HTTP checks, 60-slot history ring buffers
- `PingMonitorService` — pings gateway (auto-detected) + 1.1.1.1 DNS

### Controls/
- `LedIndicatorControl` — single LED voyant: GDI+ radial gradient, glow, ring buffer smoothing, animated lerp
- `LedBarPanel` — 7 LEDs in a row + interval buttons, propagates smoothing window
- `DashboardDigestPanel` — 6-card GDI+ digest (Memory, CPU & Processes, Network, Disk, Websites, Events) with color-coded values
- `WebsitesPanel` — per-site rows with status, response time, sparkline history
- `MemoryGraphPanel` — rolling multi-metric graph (used/standby/commit/CPU/pagefile)

### MainForm
- Timer loop on thread pool, `BeginInvoke` to UI thread
- Event log queries every 60 ticks, ping every 10 ticks
- Feeds all data to LED bar, digest panel, websites panel, graph, process list

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
- All digest/LED controls use GDI+ `OnPaint` (no child controls)
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
- Zero NuGet packages

## Notes
- ASR blocks running .exe directly — always launch via `dotnet ResourceMonitor.dll`
