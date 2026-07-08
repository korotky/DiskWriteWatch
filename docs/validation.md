# Validation

Automated tests cover interval validation (`60/60`, `60/300`, `300/300`, and invalid combinations), aggregation, key overflow, PID reuse, anomaly thresholds, filters, forward/backward clock jumps, and persisted/in-memory timeline deduplication.

The live Windows acceptance procedure is:

1. Install and confirm `/api/health` reports `etwActive: true`, no lost events, and the intended data directory.
2. Run `tools/write-synthetic.ps1 -MiB 256 -Keep` and query the unique filename in `/api/top/paths`. Logical attribution must be within 5%.
3. Restart the service before the configured interval expires. Confirm the bucket becomes persisted and its timestamp/bytes are not duplicated.
4. Disconnect or temporarily rename the configured data drive in a disposable environment. Confirm queue growth is bounded and no database appears on `C:`.
5. Exercise writes on mounted `C:`, `D:`, `E:`, and optional RAM disk `R:`; inspect disk-number/volume mapping in health.
6. Suspend/resume Windows and confirm collection resumes without an unbounded gap allocation.

On the initial development machine, the 256 MiB synthetic test attributed 267,386,880 of 268,435,456 bytes (0.391% error). Cache-manager paging File I/O is excluded from logical totals to avoid counting the same cached data again; physical writes remain represented by Disk I/O.

For a 24-hour resource run:

```powershell
.\tools\soak.ps1 -DurationHours 24 -SampleSeconds 60
```

Run once with `BucketSeconds/FlushIntervalSeconds` set to `60/60`, then once with `60/300`. Compare mean/95th-percentile CPU, maximum private memory, database growth, dropped buckets, and lost events. Targets are CPU below 1.5%, private memory below 300 MiB, and database growth below 100 MiB/day under the representative workload.

The unavailable-drive scenario should be performed only in a disposable test environment; do not surprise-remove a production SSD.
