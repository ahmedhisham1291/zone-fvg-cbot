# WP05 — News: parsers, calendar, windows, fail-safe

## Read first

`AGENTS.md`, `docs/architecture.md` (sections 3, 4), `docs/spec.md` section 6,
and the requirement texts below.

## Requirements to implement

NWS-001, NWS-002, NWS-003, NWS-004, NWS-005, NWS-006, NWS-007, NWS-008,
NWS-009, NWS-010, NWS-011, NWS-012.

## Allowed paths

`src/ZoneFvg.Core/News/**`,
`src/ZoneFvg.Core/Execution/Ports/IKeyValueStore.cs`,
`src/ZoneFvg.Core/Execution/Ports/IHttpTransport.cs`,
`tests/ZoneFvg.Core.Tests/**` (including `TestData/News/`), and the `status:`
lines of the requirements above.

## What to build

1. **`NewsEvent`** record: `DateTime? TimeUtc`, `string Currency`,
   `NewsImpact Impact` (High, Medium, Low, Holiday, Unknown), `string Title`,
   `string Source`.
2. **Ports** `IKeyValueStore` and `IHttpTransport` exactly as in
   `docs/architecture.md`. `HttpResult` has `bool Ok`, `int Status`,
   `string Body`. The transport never throws.
3. **`ForexFactoryParser`** (NWS-003). There is no `ShowMyTradesParser`: the
   owner decided on 2026-11-14 to run on Forex Factory alone. NWS-004 is the
   `NewsSources` check instead. Keep the calendar open for a second source
   behind the same parse-result interface, and test the merge rule (NWS-002)
   with a second list of `NewsEvent`s given directly in the test.
   Use `System.Text.Json` only. No third-party packages. Each returns a parse
   result: events plus a validation outcome with a reason.
   - The owner's real Forex Factory sample is
     `tests/ZoneFvg.Core.Tests/TestData/News/forexfactory-thisweek.json`. Do
     not invent any feed format from memory.
4. **`EmbeddedHistoryProvider`** (NWS-010): reads the one-event-per-line
   format from a string supplied by the caller (the adapter embeds the file).
5. **`ExtraBlackoutsParser`** (NWS-011).
6. **`NewsCalendar`**: merges sources (NWS-002), computes windows for a symbol
   (NWS-001, NWS-012), answers `IsInWindow(symbol, utc)`,
   `OverlapsWindow(symbol, fromUtc, toUtc)` and
   `NextWindowStart(symbol, afterUtc)`.
7. **`NewsService`**: owns refresh timing (NWS-006), validation and
   replacement (NWS-005), cache through `IKeyValueStore` (NWS-007), status
   `Available` or `Unavailable` (NWS-008), and the per-outage alert flag. It
   takes server time as an argument on every call. In backtest mode it uses
   only the embedded provider and never calls `IHttpTransport` (NWS-010).
8. **`NewsParameters`** with spec defaults and `Validate()`.

## Decisions already made

- Currency of a symbol: first three and last three letters of the symbol name
  after removing any broker suffix following the sixth character.
- An event belongs to the "current week" check using Monday 00:00 UTC to the
  next Monday.
- Merging uses the earlier of the two event times.
- Keys in the store are prefixed `news.` and values are JSON.

## Tests required

- NWS-003, NWS-004: the real sample files parse to the expected count and to
  three hand-checked events each; an HTML "Request Denied" page fails;
  truncated JSON fails; an empty list fails; times with offsets convert to UTC
  correctly on a daylight-saving week.
- NWS-001: window edges exactly at minus 30 and plus 30 minutes (inside), one
  second outside; overlapping windows merge; irrelevant currency ignored.
- NWS-002: high in one feed and medium in the other gives high; near-duplicate
  events within 5 minutes become one; 6 minutes apart stay two.
- NWS-005: a failed download after a good one keeps the good data.
- NWS-006: no second fetch within 60 minutes, including after a simulated
  restart with the same store; fetch due after `NewsRefreshHours`.
- NWS-007: cache round-trip; data 48 hours old trusted, older not.
- NWS-008: both sources down and cache stale gives Unavailable and exactly one
  alert flag until recovery.
- NWS-009: now inside a window; an FVG bar overlapping a window; neither.
- NWS-010: backtest mode makes zero transport calls (a counting fake proves it).
- NWS-011: valid list; malformed entry rejected with the entry quoted.
- NWS-012: an all-day event creates no window and is reported.

## Out of scope

Deciding expiry times or skipping signals (WP06 calls this module). The real
HTTP implementation (WP08).

## Definition of done

- [ ] Tests first, all green; coverage thresholds met.
- [ ] No network access in any test.
- [ ] All `AGENTS.md` commands clean; outputs in the pull request.
- [ ] The twelve requirement statuses are `implemented`.
