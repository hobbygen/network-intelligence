# Network Intelligence — Progress Snapshot

**As of:** 2026-09-19 · **Branch:** master · **Last commit:** `be09b35` "Add App_Progress.md as a fast-orientation status snapshot"
**Working tree:** adds one-click speed testing (ADR-014), usage reports (ADR-015) and persisted speed-test history (ADR-016); not yet committed.
**Version:** 0.2.0 early access · **Tests:** 132/132 passing (`tests/NetworkIntelligence.Domain.Tests`) · **Build:** clean (`dotnet build NetworkIntelligence.slnx`)
**Package:** existing portable ZIP has not been rebuilt with the latest speed-test and usage-report changes.

This file is a fast-orientation snapshot for picking the work back up. It does not replace the detailed records —
when you need the *why* behind something, go to the source of truth:

- **`docs/DECISIONS.md`** — every architecture/feature decision as a numbered ADR (ADR-001 through ADR-016), each
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
WinUI 3 app (`NetworkIntelligence.App`), unpackaged, self-contained. Sidebar `NavigationView` with 8 main pages
(Overview/Ethernet/Wi-Fi/Network performance/Bandwidth and data/Application usage/Connection history/Diagnostics)
plus **Settings** and the new **About** page. System tray with custom icon, native notifications, pause/resume.
Light/dark/system theme — **was broken, now fixed** (see "Recently fixed" below). Custom multi-resolution app icon
(exe, window/taskbar, tray) generated from `Application_Icon.png`; app metadata credits Naeem Ahmad.

### Core monitoring (Phase 3)
Real Ethernet/Wi-Fi/IPv4/IPv6 collectors, SQLite storage (schema version 3, see `docs/DATABASE_SCHEMA.md`), minute
aggregates, one-year default retention with configurable cleanup, connection history, integrity checks.

### Diagnostics & speed testing (Phase 4)
Cancellable DNS/ICMP diagnostics (`Diagnostics` class) against a user-configurable target — these numbers now
also feed the health score (see below). **One-click bounded HTTPS speed testing is now implemented** (ADR-014): automatic Cloudflare selection with
network-routed serving edge, saved optional custom HTTPS endpoint, same-page progress/Cancel, download/upload
results, five-sample HTTP latency and jitter. 25 MB download + 10 MB random upload, 60-second deadline, manual
runs only. Failure/cancellation discards incomplete results; rate limits are not retried. Live endpoint transfer
and WinUI page renders verified. This is bounded throughput, not maximum line speed; packet loss is unavailable.
One built-in provider, not a ranked multi-provider catalog. **A completed run is now persisted** (ADR-016, database schema version 4): a "Recent speed tests" card on Network performance shows the last 10 runs, loaded on startup/refresh and right after a new run finishes. Only successful runs are stored — cancelled/failed/timed-out results are discarded, same as before. Wired into the existing retention and full-deletion paths. See `docs/TEST_REPORT.md` for evidence.

### Analytics & export (Phase 5)
Live charts, CSV/JSON export for adapter and per-application usage, session statistics. **Dedicated usage reports
are implemented** on **Bandwidth and data** (ADR-015): today, this week, this month, this year and custom dates
(up to 366 inclusive days). Weeks start Monday; local calendar boundaries account for daylight-saving changes
and the current period stops at the snapshot time. The yearly preset includes leap-year support.

Reports show separate adapter totals, daily charts, an expandable daily breakdown and approximate recorded
coverage. Missing measurements remain unavailable rather than becoming zero; measured idle intervals show
zero. Physical/VPN adapter totals are never added together, and overlapping coverage is flagged. Existing
minute aggregates supply the reports, with no schema migration; midnight-crossing intervals are not split.

The top ten application names are ranked across all interfaces, independently of the selected adapter.
CSV/JSON exports capture the displayed snapshot and selected adapter plus the complete application breakdown.
Application identity remains process-name based and service coverage is unknown. Refresh cancels superseded
queries, and deleting history refreshes the report.

**Validation:** 30 report test cases cover calendar boundaries, DST, leap years, missing versus zero data,
adapter separation, application grouping, coverage caps, cancellation, export and history deletion. The latest
solution build passed with zero warnings/errors and all 129 tests passed. The yearly WinUI report was visually
checked using isolated synthetic data; database integrity was `ok`. Local evidence:
`artifacts/report-ui-yearly-final/smoke-preview.png`. Native export-picker interaction, keyboard/screen-reader
behavior and long-duration/high-volume report performance remain unverified.

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

### Previously completed shell fixes (commit `fc7163e`)
- **Theme toggle bug**: previously only applied after clicking "Save settings," and that save's validation of
  unrelated fields could silently block it. Now applies and persists immediately on ComboBox selection. See
  `CHANGELOG.md` "Unreleased" and the commit `fc7163e` message for the full diagnosis.
- **Application icon**: real multi-res `.ico` now embedded in the exe and set on the window/taskbar/tray
  (previously a generic system icon everywhere).
- **About page**: new, in the nav footer above Settings — icon, live version, description, "Created by Naeem
  Ahmad."

---

## What's outstanding

The following work remains; release acceptance items still limit production readiness:

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
4. **Speed-test follow-ups (optional)** — additional supported providers or adaptive line-capacity estimation;
   automatic selection of the built-in provider (ADR-014) and persisted result history (ADR-016) are implemented.
5. **`docs/TEST_REPORT.md`'s "Remaining acceptance work" list** — the authoritative, longer catalog: controlled
   byte-accuracy edge cases (concurrent processes, UDP, real adapters), ETW CPU/memory overhead benchmark,
   MonitoringService code signing + dedicated service account, automated unauthorized-IPC-client tests, Windows
   10 + clean Windows 11 install testing, UI automation/accessibility coverage, long-duration/high-throughput
   benchmarks, and signed release packaging (MSIX or installer — currently an unsigned portable build only).
6. **Phase 8/9 polish**: accessibility pass, localization-ready strings audit, and a real automated
   integration/UI test harness (current coverage is 129 automated tests across Domain/Application/Infrastructure plus a lot of manual/live
   verification — solid for what it covers, but nothing exercises the full stack end-to-end automatically).
7. **Usage-report acceptance and packaging**: verify the native save picker and keyboard/screen-reader flows,
   benchmark large retained histories, then rebuild and verify the portable package when preparing a release.
   Daily/weekly/monthly/yearly/custom reporting itself is implemented; professional PDF reporting remains deferred.

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
