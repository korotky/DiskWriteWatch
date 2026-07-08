# Architecture

Kernel ETW events enter a single in-process aggregator. File writes are keyed by process instance and path; disk writes by process instance and physical disk number. A one-second clock service closes elapsed buckets, including empty buckets after sleep/resume or a clock jump.

Completed snapshots enter a bounded RAM queue. The persistence worker removes the queue every configured flush interval and commits all rows in one SQLite transaction. Failed writes are requeued; no alternative data directory is used. The dashboard merges persisted query results with queue/current-bucket snapshots and groups by bucket timestamp so a successful flush cannot duplicate a point.

SQLite uses WAL and `synchronous=NORMAL`. Minute/detail rows roll up to hourly rows after `MinuteRetentionDays`, then expire after `HourlyRetentionDays`; maintenance checkpoints WAL and performs incremental vacuum.

Process identity combines PID with observed start time to survive PID reuse. NT device paths are mapped to DOS volume letters with `QueryDosDevice`; physical disk metadata comes from Windows Management Instrumentation.
