# WP09 — Operations: journal, alerts, heartbeat, daily summary

## Read first

`AGENTS.md`, `docs/architecture.md` (sections 3, 6), `docs/spec.md` section 11
and 15.4, and the requirement texts below. Depends on WP06; can run in
parallel with WP07 once the hook interfaces exist.

## Requirements to implement

OPS-001, OPS-002, OPS-003, OPS-004, OPS-005, OPS-006, OPS-007.

## Allowed paths

`src/ZoneFvg.Core/Ops/**`, `src/ZoneFvg.Core/Execution/Ports/IJournalSink.cs`,
`tests/ZoneFvg.Core.Tests/**`, and the `status:` lines of the requirements
above.

## What to build

1. **`Journal`**: typed methods `Signal(...)`, `Trade(...)`, `Recovery(...)`,
   `State(...)`, `Error(...)` that format one line each and write through
   `IJournalSink`. Format: UTC ISO time, category, then `key=value` pairs in a
   fixed order, invariant culture, values escaped. Fields per OPS-001 and
   OPS-002. Result in R = money result divided by the planned risk parsed
   from the order comment.
2. **`SecretMasker`** (OPS-007): constructed with the secret strings; every
   journal line, alert text and exception message passes through it; any
   occurrence is replaced with `***`. Empty secrets are ignored.
3. **`AlertService`** (OPS-004): `Raise(kind, text)` enqueues; `Pump(serverTimeUtc)`
   sends at most a few messages per call through `IHttpTransport` to the
   Telegram Bot API `sendMessage` endpoint; failures stay queued with
   exponential back-off; the queue is bounded (drop oldest, count drops);
   nothing here can throw into the caller. Alert kinds follow the list in spec
   section 11. Identical alerts within a short interval are collapsed.
4. **`Heartbeat`** (OPS-005): `Pump(serverTimeUtc)` calls `HeartbeatUrl` when
   five minutes have passed since the last call, in every bot state.
5. **`DailySummary`** (OPS-003): builds the summary from a `RiskSnapshot`, the
   journal counters and the calendar status once per CE(S)T day, including
   distance to both FTMO floors and progress to the phase target.
6. **`InactivityMonitor`** (OPS-006): from broker history and open positions,
   days since the last opened trade; raises one alert at 21 days and again
   every 3 days after.
7. **`OpsParameters`** with `Validate()`; secrets are optional, and with an
   empty token the alert service becomes a no-op that still journals.

## Decisions already made

- Telegram message text is plain text, no markup, at most 1,000 characters.
- The Telegram URL contains the token; it must never be journaled. Journal the
  alert kind and a hash of the text instead.
- `Pump` methods are called from `BotCore.OnTimer` only.

## Tests required

- OPS-001, OPS-002: golden-line tests for each journal method; R computed from
  a comment; missing comment gives `r=NA`, never a crash.
- OPS-003: summary on a worked snapshot; produced once per CE(S)T day across a
  daylight-saving change.
- OPS-004: transport failure keeps the message queued and never throws; back-off
  timing; bounded queue drops oldest; duplicates collapsed; empty token means
  zero transport calls.
- OPS-005: one call per five minutes of server time; failure logged; no effect
  on callers; runs in Halted.
- OPS-006: 20 days no alert; 21 alert; repeated every 3 days; reset by a new trade.
- OPS-007: a line containing the token is masked; an exception whose message
  contains the heartbeat URL is masked; a test greps every string written to
  the fake sink and the fake transport log during a full scenario run and
  finds no secret.

## Out of scope

The cTrader implementations of the sink and transport (WP08).

## Definition of done

- [ ] Tests first, all green; coverage thresholds met.
- [ ] All `AGENTS.md` commands clean; outputs in the pull request.
- [ ] The seven requirement statuses are `implemented`.
