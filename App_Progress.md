# Network Intelligence — Progress Snapshot

**As of:** 2026-09-18 · **Branch:** master · **Last commit:** `fc7163e` "Add application icon, About page, and fix the theme toggle"
**Version:** 0.2.0 early access · **Tests:** 75/75 passing (`tests/NetworkIntelligence.Domain.Tests`) · **Build:** clean (`dotnet build NetworkIntelligence.slnx`)

This file is a fast-orientation snapshot for picking the work back up. It does not replace the detailed records —
when you need the *why* behind something, go to the source of truth:

- **`docs/DECISIONS.md`** — every architecture/feature decision as a numbered ADR (ADR-001 through ADR-013), each
  with rationale, what was validated, and known caveats. Always check here before assuming something is unbuilt.
- **`CHANGELOG.md`** — chronological, more implementation-detail-heavy than the ADRs.
- **`docs/TEST_REPORT.md`** — what's been tested/verified and, critically, its "Remaining acceptance work" section
  (a long semicolon list of everything still outstanding for release-readiness).
- **`docs/VALIDATION_PLAN.md`** — the original Phase-1 telemetry validation checklist and what's still open in it.
- **`NETWORK_INTELLIGENCE.md`** — the original full product spec/brief this build follows. Not a status doc; use it
  to check what a section *requires*, not what's done.

---

## What's done

### Architecture & telemetry (Phase 0–1, ADR-001–006)
Solution/CI/docs scaffolded. Tiered per-application telemetry strategy validated: Tier 1 (adapter counters) and
Tier 2 (IP Helper connection ownership) work unprivileged; Tier 3 (exact per-process byte attribution) requires
the optional elevated `MonitoringService`, validated against a known-traffic reference (0% delta, loopback,
`docs/ETW_ACCURACY.md`).

### Application shell (Phase 2)
WinUI 3 app (`NetworkIntelligence.App`), unpackaged, self-contained. Sidebar `NavigationView` with 9 pages
(Overview/Ethernet/Wi-Fi/Network performance/Bandwidth and data/Application usage/Connection history/Diagnostics)
plus **Settings** and the new **About** page. System tray with custom icon, native notifications, pause/resume.
Light/dark/system theme — **was broken, now fixed** (see "Recently fixed" below). Custom multi-resolution app icon
(exe, window/taskbar, tray) generated from `Application_Icon.png`; app metadata credits Naeem Ahmad.

### Core monitoring (Phase 3)
Real Ethernet/Wi-Fi/IPv4/IPv6 collectors, SQLite storage (schema version 3, see `docs/DATABASE_SCHEMA.md`), minute
aggregates, one-year default retention with configurable cleanup, connection history, integrity checks.

### Diagnostics & speed testing (Phase 4)
Cancellable DNS/ICMP diagnostics (`Diagnostics` class) against a user-configurable target — these numbers now
also feed the health score (see below). **Bounded HTTPS transfer test exists** (`TransferTest.cs`) but requires
the user to supply their own provider endpoint (a base URL implementing `/__down`/`/__up`) — there's no automatic
provider selection/discovery yet, so spec section 9.1's "automatically select a reliable supported provider" is
not fully met.

### Analytics & export (Phase 5)
Live charts, CSV/JSON export for adapter and per-application usage, session statistics. No dedicated
daily/weekly/monthly/yearly rollup reports beyond the raw exportable history.

### Application monitoring (Phase 6, ADR-007–010, ADR-012)
Elevated `MonitoringService` (Generic Host worker, named-pipe IPC, least-privilege) does real per-process TCP/UDP
byte attribution via kernel ETW. Process names are resolved from kernel `ProcessStart`/`ProcessDCStart` events
(not a post-exit lookup) — this was the fix in ADR-012, live-validated against 15 concurrent short-lived
processes. Per-app traffic persists as minute aggregates (schema v2) with its own export.
**Still not a fully stable identity**: grouping is by process name only; two different process instances sharing
a PID within the same 5-second window still merge (documented, accepted limitation).

### Anomaly detection (Phase 7, ADR-011 + 3 follow-ups)
Full detection pipeline per spec section 10.5: `BaselineCalculator` (Welford + outlier trim) and
`AnomalyEvaluator` in Domain (pure); `AnomalyTracker` (sustained-duration + cooldown + **snooze**, added later)
and `AnomalyDetectionService` in Application. Learning period (60 samples/7 days) before any alert; per-direction
evaluation; trusted-app exclusion (with full **trust AND untrust** UI, untrust added later); quiet hours; tray
notifications. Dashboard "Recent alerts" card shows persisted history with working **Snooze 1h** and **Dismiss**
buttons (both view-only — stored evidence is never altered). **Thresholds are a documented provisional first
pass, not validated against a real false-positive rate.**

### Network health score (Phase 12.4, ADR-013)
Built from scratch this cycle — was the one entirely-unbuilt Dashboard element. Pure `HealthScoreCalculator` in
Domain scores 8 factors (connectivity, latency, packet loss, jitter, DNS response, Wi-Fi signal, adapter errors,
recent disconnects), weighted-averaging only the currently-available ones — never fabricates a score. Dashboard
card shows the score, a qualitative band, and every factor's own detail. **Weights/curves are a provisional first
pass**, not validated against real user-perceived quality.

### Recently fixed (this session, not yet mentioned above)
- **Theme toggle bug**: previously only applied after clicking "Save settings," and that save's validation of
  unrelated fields could silently block it. Now applies and persists immediately on ComboBox selection. See
  `CHANGELOG.md` "Unreleased" and the commit `fc7163e` message for the full diagnosis.
- **Application icon**: real multi-res `.ico` now embedded in the exe and set on the window/taskbar/tray
  (previously a generic system icon everywhere).
- **About page**: new, in the nav footer above Settings — icon, live version, description, "Created by Naeem
  Ahmad."

---

## What's outstanding

Roughly in the order it'd make sense to pick things up, but none of it is blocking — pick whatever's relevant:

1. **A dedicated alert-history page.** The Dashboard "Recent alerts" card (capped at 50 rows, persisted, loaded
   on startup) already covers the spec's literal "alert history" requirement (section 12.6). A separate
   browsable/filterable page was never explicitly requested and hasn't been built. Worth asking whether it's
   actually wanted before building it.
2. **Real-world validation, not more coding**: anomaly-detection false-positive rate and health-score
   weights/curves both need days-to-weeks of live, varied usage data to validate against — tracked as explicitly
   outstanding in ADR-011/ADR-013 and `docs/VALIDATION_PLAN.md`. Nothing to build here yet; needs actual usage
   history first (or a plan for how to gather it).
3. **Stable application identity beyond process-name grouping.** ADR-012 fixed the specific short-lived-process
   name-loss bug but deliberately didn't take on full stable identity (executable path, PID-reuse
   disambiguation within a single window). Flagged as the natural next step if it turns out to matter in
   practice.
4. **Automatic speed-test provider selection** (spec 9.1) — currently requires a manually-configured endpoint.
5. **`docs/TEST_REPORT.md`'s "Remaining acceptance work" list** — the authoritative, longer catalog: controlled
   byte-accuracy edge cases (concurrent processes, UDP, real adapters), ETW CPU/memory overhead benchmark,
   MonitoringService code signing + dedicated service account, automated unauthorized-IPC-client tests, Windows
   10 + clean Windows 11 install testing, UI automation/accessibility coverage, long-duration/high-throughput
   benchmarks, and signed release packaging (MSIX or installer — currently an unsigned portable build only).
6. **Phase 8/9 polish**: accessibility pass, localization-ready strings audit, and a real automated
   integration/UI test harness (current coverage is 75 Domain/Application unit tests plus a lot of manual/live
   verification — solid for what it covers, but nothing exercises the full stack end-to-end automatically).

## Known environment quirk

The Bash/PowerShell tool sessions used to build/test this project run **non-elevated**. `NetworkIntelligence.MonitoringService`
needs Administrator to start its kernel ETW session, so live-testing it requires asking the user to run
`scripts/service-run-foreground.ps1` in their own elevated window — from there, `dotnet run --project
tools/NetworkIntelligence.TelemetryProbe -- --service-status` and reading `%ProgramData%\NetworkIntelligence\Service\service.log`
both work fine non-elevated. See `docs/DECISIONS.md` ADR-012 for exactly how this was used to live-validate a fix.

For WinUI `App`-layer UI changes (no automated test coverage there), the established verification pattern is
`--smoke-test --smoke-page <PageName>` (renders the visual tree to `smoke-preview.png` in the app's data
directory, viewable directly) combined with seeding real state through a throwaway console project referencing
`NetworkIntelligence.Infrastructure`'s `SqliteHistoryStore` (no `sqlite3` CLI is installed in this environment).
