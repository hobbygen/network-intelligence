# Implemented database schema — version 2

The migration is executed transactionally by `SqliteHistoryStore.InitializeAsync`, one version at a time (a database at version 1 gets only the version-2 migration applied, not a re-run of version 1). No destructive migration is automatic. Databases with a newer version than this build knows are rejected. SQLite uses WAL with a five-second busy timeout; a single asynchronous gate serializes this application's connections. Calls from the UI run on background tasks because SQLite's provider does not perform native asynchronous I/O.

| Table | Columns | Meaning | Since |
|---|---|---|---|
| SchemaMigrations | Version PK, AppliedUtc | Numbered schema and application time | 1 |
| Settings | Key PK, Json | Validated typed settings; key `app` | 1 |
| Adapters | Id PK, Name, Type, LastSeen | Adapter metadata, no addresses | 1 |
| TrafficMinutes | AdapterId + Minute PK, Download, Upload, CoveredSeconds, Samples | Sum of valid deltas on up interfaces; bytes and observed elapsed seconds | 1 |
| ConnectionEvents | Id PK, Time, AdapterId, AdapterName, Previous, State | Observed transitions | 1 |
| Diagnostics | Id PK, Time, Json | DNS/ICMP results; target redacted unless address collection is enabled | 1 |
| ApplicationTrafficMinutes | Pid + ProcessName + Minute PK, Received, Sent, Events, Windows | Per-process bytes from the optional elevated MonitoringService (docs/DECISIONS.md ADR-007/009), summed from its 5-second ETW windows into minute buckets | 2 |

Time columns are integer Unix UTC seconds. The minute bucket is the interval end timestamp rounded down to the minute. Intervals crossing bucket boundaries are not split. Timestamp indexes support retention/range queries. Traffic source is `NetworkInterface.GetIPStatistics` for `TrafficMinutes`; rates displayed historically are total delta bytes divided by actual covered seconds. Intervals with a missing direction, reset counter, invalid interval or gap longer than ten seconds are excluded. A missing row means absent coverage, never assumed zero traffic.

`ApplicationTrafficMinutes` is grouped by (Pid, ProcessName), **not a stable application identity** — a PID reused by a different process within the same minute lands in the same aggregated row, and a process that spans a minute boundary appears as separate rows per minute. Only ever written from a MonitoringService snapshot whose `Availability` is exactly `"Measured"`; an unreachable/unavailable service writes nothing, never a zero-byte row. Its numbers are validated only for a narrow controlled case (`docs/ETW_ACCURACY.md`) — treat stored history as indicative, not certified for accuracy.

History is capped by the configured 1–3650 day retention, applied identically to `ApplicationTrafficMinutes`. Cleanup removes at most 2000 rows per table per pass, then reschedules until caught up. Live chart history is separately bounded to 90 points. Application executable paths, addresses and WLAN names are not stored. Adapter rows themselves retain last-seen metadata; history deletion clears them, `ApplicationTrafficMinutes` included.

Stable application identities, learned baselines, anomalies, enhanced metrics and retained speed-test results remain planned and are not implied by schema 2. Back up the closed database before future migrations. `PRAGMA quick_check` is available from Diagnostics. Explicit history deletion is transactional and checkpoints WAL; it is not secure erasure.
