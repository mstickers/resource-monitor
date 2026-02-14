# Resource Monitor

Real-time WinForms GUI memory monitor for QEMU/KVM VM with VirtIO balloon driver.

## Build & Run
```bash
dotnet build -c Release          # build
mmon                             # run (auto-builds if needed)
mmon --build                     # force rebuild + run
```

## Architecture
- **Models/**: `SystemSnapshot` (readonly record struct), `ProcessInfo`, `RingBuffer<T>` (600 slots, zero-alloc), `OomEvent`
- **Services/**: `SystemMemoryService` (P/Invoke + PerformanceCounter), `ProcessMonitorService`, `BalloonService` (VirtIO), `EventLogService`
- **Controls/**: `MemoryGraphPanel` (GDI+ double-buffered rolling graph), `MemoryBarControl` (segmented bar)
- **MainForm**: Timer loop on thread pool, `BeginInvoke` to UI thread

## Performance Constraints
- Target: < 30 MB working set
- `SystemSnapshot` is `readonly record struct` (stack-allocated)
- `RingBuffer<T>` pre-allocates once, zero GC pressure in hot path
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
