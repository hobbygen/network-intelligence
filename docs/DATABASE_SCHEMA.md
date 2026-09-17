# Implemented database schema — version 1

The migration is executed transactionally by `SqliteHistoryStore.InitializeAsync`. No destructive migration is automatic. Databases with a newer version are rejected. SQLite uses WAL with a five-second busy timeout; a single asynchronous gate serializes this application's connections. Calls from the UI run on background tasks because SQLite's provider does not perform native asynchronous I/O.

| Table | Columns | Meaning |
|---|---|---|
| SchemaMigrations | Version PK, AppliedUtc | Numbered schema and application time |
| Settings | Key PK, Json | Validated typed settings; key `app` |
| Adapters | Id PK, Name, Type, LastSeen | Adapter metadata, no addresses |
| TrafficMinutes | AdapterId + Minute PK, Download, Upload, CoveredSeconds, Samples | Sum of valid deltas on up interfaces; bytes and observed elapsed seconds |
| ConnectionEvents | Id PK, Time, AdapterId, AdapterName, Previous, State | Observed transitions |
| Diagnostics | Id PK, Time, Json | DNS/ICMP results; target redacted unless address collection is enabled |

Time columns are integer Unix UTC seconds. The minute bucket is the interval end timestamp rounded down to the minute. Intervals crossing bucket boundaries are not split. Three timestamp indexes support retention/range queries. Traffic source is `NetworkInterface.GetIPStatistics` under schema version 1; rates displayed historically are total delta bytes divided by actual covered seconds. Intervals with a missing direction, reset counter, invalid interval or gap longer than ten seconds are excluded. A missing row means absent coverage, never assumed zero traffic.

History is capped by the configured 1–3650 day retention. Cleanup removes at most 2000 rows per table per pass, then reschedules until caught up. Live chart history is separately bounded to 90 points. Application process names/paths, addresses and WLAN names are not stored. Adapter rows themselves retain last-seen metadata; history deletion clears them.

Application identities/baselines/anomalies, enhanced metrics and retained speed-test results remain planned and are not implied by schema 1. Back up the closed database before future migrations. `PRAGMA quick_check` is available from Diagnostics. Explicit history deletion is transactional and checkpoints WAL; it is not secure erasure.
