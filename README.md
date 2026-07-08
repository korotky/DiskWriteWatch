# DiskWriteWatch

DiskWriteWatch is a lightweight Windows 10/11 x64 service that attributes disk writes to processes and paths using kernel ETW. It needs no filter driver, stores aggregates in SQLite, and serves a private dashboard on `127.0.0.1`.

It is designed for questions such as “what keeps writing to my SSD?”, “is WSL or Docker producing this traffic?”, and “did a tuning change actually reduce writes?”. WSL and Docker writes appear at the Windows host VHDX level because that is where Windows observes them.

## Features

- Logical File I/O grouped by process instance, path, directory, and extension.
- Physical Disk I/O grouped by process instance and physical disk number.
- PID reuse-safe process identity (`PID + process start time`).
- Configurable 10–3600 second buckets and transaction intervals.
- Completed buckets remain visible from RAM before the next SQLite flush.
- SQLite WAL, batched transactions, hourly rollups, retention, and incremental vacuum.
- Bounded RAM buffering when the data drive is unavailable; no silent fallback to `C:`.
- Loopback-only dashboard, local assets, no CDN, accounts, or telemetry.
- Health, queue depth, last flush, dropped buckets, anomalies, and CSV export.

## Install

Download the self-contained `win-x64` release ZIP, verify its `.sha256`, extract it, then run an elevated PowerShell:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\install.ps1
```

To put the database on another drive:

```powershell
.\install.ps1 -DataDirectory 'E:\SystemMetrics\DiskWriteWatch' -Port 8765
```

Open <http://127.0.0.1:8765>. The service runs as `LocalSystem` with delayed automatic start. Configuration is stored at `%ProgramData%\DiskWriteWatch\config.json`; binaries are installed under `%ProgramFiles%\DiskWriteWatch`.

The installer parameters are `InstallDirectory`, `DataDirectory`, `Port`, `ServiceName`, and `StartService`. Re-run installation only after uninstalling the existing service.

```powershell
.\restart.ps1
.\uninstall.ps1             # preserves configuration and data
.\uninstall.ps1 -PurgeData  # removes ProgramData configuration/default data
```

## Configuration

See [`config.sample.json`](config.sample.json). `BucketSeconds` controls graph detail. `FlushIntervalSeconds` controls SQLite transaction frequency and must be a multiple of the bucket size and no smaller than it. Defaults are `60/300`: one-minute resolution and one transaction every five minutes. A crash can lose at most one flush interval; a normal service stop performs a final flush.

Common valid combinations are `60/60`, `60/300`, and `300/300`. The service validates configuration before startup:

```powershell
DiskWriteWatch.exe --validate-config --config C:\path\to\config.json
```

Full paths are stored by default. Set `Privacy.StoreFullPaths` to `false` to aggregate to directories. Command lines are not persisted.

## API

- `GET /api/health`
- `GET /api/timeline?from=<unix>&to=<unix>`
- `GET /api/top/processes?from=<unix>&to=<unix>&limit=20`
- `GET /api/top/paths?...`
- `GET /api/top/directories?...`
- `GET /api/top/extensions?...`
- `GET /api/export.csv?from=<unix>&to=<unix>`

The dashboard distinguishes persisted points from in-memory points. Monitor-generated writes are collected for accounting accuracy and hidden from tops by default; pass `includeMonitor=true` to show them.

## Build and package

Requirements: Windows x64, .NET 10 SDK, Administrator rights only for a live kernel ETW run or service installation.

```powershell
.\build.ps1
.\package.ps1 -Version 0.1.0
```

The release command creates a self-contained ZIP and SHA-256 file under `artifacts`. CI builds `master` and pull requests; `v*` tags publish a GitHub Release.

## Operational notes

- Kernel ETW is system-wide and requires elevation (`LocalSystem` supplies it for the service).
- High-cardinality buckets are bounded by `MaxKeysPerBucket`; excess logical writes are retained as `<other>` totals.
- If the configured data drive disappears, completed buckets remain in the bounded RAM queue. Oldest buckets are dropped when its time or memory limit is reached.
- Browser caches, page files, compressed files, and filesystem metadata may make physical bytes differ from logical bytes.
- Disk models are obtained from Windows inventory and disk I/O is recorded by physical disk number.

Performance targets for the initial release are CPU below 1.5%, private RAM below 300 MiB in normal operation, and database growth below 100 MiB/day. Measure these under your own workload; high path cardinality materially affects database size.

See [`docs/validation.md`](docs/validation.md) for the synthetic writer, shutdown test, drive scenarios, and the 24-hour `60/60` versus `60/300` soak procedure.

## Limitations

Version 1 targets Windows 10/11 x64. MSI, code signing, ARM64, automatic updates, and guest-level attribution inside WSL/Docker are out of scope.

## License

MIT. See [`LICENSE`](LICENSE).
