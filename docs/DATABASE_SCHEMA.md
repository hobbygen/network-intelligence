# Implemented database schema — version 4

The migration is executed transactionally by `SqliteHistoryStore.InitializeAsync`, one version at a time (a database at version 1 gets only the version-2 through version-4 deltas applied, never a re-run of version 1). No destructive migration is automatic. Databases with a newer version than this build knows are rejected. SQLite uses WAL with a five-second busy timeout; a single asynchronous gate serializes this application's connections. Calls from the UI run on background tasks because SQLite's provider does not perform native asynchronous I/O.

| Table | Columns | Meaning | Since |
|---|---|---|---|
| SchemaMigrations | Version PK, AppliedUtc | Numbered schema and application time | 1 |
| Settings | Key PK, Json | Validated typed settings; key `app` | 1 |
| Adapters | Id PK, Name, Type, LastSeen | Adapter metadata, no addresses | 1 |
| TrafficMinutes | AdapterId + Minute PK, Download, Upload, CoveredSeconds, Samples | Sum of valid deltas on up interfaces; bytes and observed elapsed seconds | 1 |
| ConnectionEvents | Id PK, Time, AdapterId, AdapterName, Previous, State | Observed transitions | 1 |
| Diagnostics | Id PK, Time, Json | DNS/ICMP results; target redacted unless address collection is enabled | 1 |
| ApplicationTrafficMinutes | Pid + ProcessName + Minute PK, Received, Sent, Events, Windows | Per-process bytes from the optional elevated MonitoringService (docs/DECISIONS.md ADR-007/009), summed from its 5-second ETW windows into minute buckets | 2 |
| AnomalyEvents | Id PK, Time, ProcessName, Direction, Severity, CurrentRate, BaselineMean, Deviation, Explanation, Notified | Fired anomalies from the algorithmic detector (docs/DECISIONS.md ADR-011) | 3 |
| SpeedTests | Id PK, Time, Json | Completed speed-test runs (docs/DECISIONS.md ADR-016); cancelled/failed/timed-out runs are never written | 4 |

Time columns are integer Unix UTC seconds. The minute bucket is the interval end timestamp rounded down to the minute. Intervals crossing bucket boundaries are not split. Timestamp indexes support retention/range queries. Traffic source is `NetworkInterface.GetIPStatistics` for `TrafficMinutes`; rates displayed historically are total delta bytes divided by actual covered seconds. Intervals with a missing direction, reset counter, invalid interval or gap longer than ten seconds are excluded. A missing row means absent coverage, never assumed zero traffic.

`ApplicationTrafficMinutes` is grouped by (Pid, ProcessName), **not a stable application identity** — a PID reused by a different process within the same minute lands in the same aggregated row, and a process that spans a minute boundary appears as separate rows per minute. Only ever written from a MonitoringService snapshot whose `Availability` is exactly `"Measured"`; an unreachable/unavailable service writes nothing, never a zero-byte row. Its numbers are validated only for a narrow controlled case (`docs/ETW_ACCURACY.md`) — treat stored history as indicative, not certified for accuracy. Baseline/anomaly evaluation (`AnomalyEvents`) queries this table grouped by **process name alone**, summing bytes but taking `MAX(Windows)` (not `SUM`) for covered seconds — concurrent PIDs sharing a name overlap in wall-clock time, so summing their window counts would double-count coverage and understate the computed rate.

`AnomalyEvents.Notified` records whether a tray notification actually happened at write time (gated by the alerts-enabled setting and quiet hours) — quiet hours suppress the notification only, never this row's existence. `Direction` is `Download`/`Upload`, `Severity` is `Low`/`Medium`/`High`. Rows are never fabricated: an anomaly is only written once `NetworkIntelligence.Domain.AnomalyTracker` (sustained-duration + cooldown state machine) decides to fire, downstream of a stateless `AnomalyEvaluator` verdict that itself required the process to have cleared its learning period (default 60 samples / 7 days).

`SpeedTests` stores the same fields `TransferTest.RunAsync` returns to the UI (timestamp, endpoint, download/upload Mbps, HTTP latency/jitter, duration) as JSON, following the `Diagnostics` table's pattern rather than typed columns since neither is queried by field. Only a run that completes successfully is written — a cancelled, timed-out or failed run persists nothing, matching `TransferTest`'s own "incomplete results are discarded" contract.

History is capped by the configured 1–3650 day retention, applied identically to `ApplicationTrafficMinutes`, `AnomalyEvents` and `SpeedTests`. Cleanup removes at most 2000 rows per table per pass, then reschedules until caught up. Live chart history is separately bounded to 90 points. Application executable paths, addresses and WLAN names are not stored. Adapter rows themselves retain last-seen metadata; history deletion clears them, `ApplicationTrafficMinutes`, `AnomalyEvents` and `SpeedTests` included.

Stable application identities beyond process-name grouping and enhanced metrics remain planned and are not implied by schema 4. Back up the closed database before future migrations. `PRAGMA quick_check` is available from Diagnostics. Explicit history deletion is transactional and checkpoints WAL; it is not secure erasure.
