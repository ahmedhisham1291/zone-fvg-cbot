# WP45: FTMO Instant account (100,000 USD): trailing guard, consistency readout, defaults

## Read first

`AGENTS.md`; `docs/spec.md` 0 (rows BG to BM of v0.20), 4.2 (the table: daily budget, kill switch, emergency guard, monthly limit), **15.8**, 15.4, 7 (rows InitialCapital, FtmoDailyLossPercent / FtmoMaxLossPercent, FtmoMaxLossMode, FtmoGuardBufferPercent, ConsistencyMaxPercent, RewardPeriodStartDate, RiskEpochDate), 11 (journal); RSK-021, OPS-012 and PLT-022 in `docs/requirements.yml`, and for what they refer to RSK-006 to RSK-009, RSK-018, RSK-020, REC-014, OPS-001 onward (journal), PLT-017, PLT-020.

Code to reuse, not to copy: `Risk/RiskEngine.cs` (`KillSwitch` and the shared `PeakBalance` of WP41, the emergency guard of RSK-009), `Risk/RiskParameters.cs`, `Risk/CestClock.cs`, `Recovery/AccountGuard.cs` (where the guard runs), `App/BotCore.cs` (start line, daily duties, journal), `Ops/` (journal lines), `App/BacktestDefaults` (how `RiskEpochDate` text is read), `src/ZoneFvg.Bot/ZoneFvgBot.cs`; tests: the RSK-009 tests and scenarios, RSK-008 tests, the WP41 brake tests that share the peak, the WP44 monthly-limit scenarios (day and month boundaries in CE(S)T), PLT-017 and PLT-020 tests.

## Requirements

RSK-021, OPS-012, PLT-022. One pull request, branch `wp/45-ftmo-instant`.

## Allowed paths

- `src/ZoneFvg.Core/Risk/**`, `src/ZoneFvg.Core/Recovery/**` (the guard's call site), `src/ZoneFvg.Core/App/**` (parameters, start, daily duty for the readout), `src/ZoneFvg.Core/Ops/**` (the readout line).
- `src/ZoneFvg.Bot/ZoneFvgBot.cs`.
- `deploy/**`, `docs/runbook.md`, `docs/backtest-howto.md`, `deploy/README.md`, `README.md` (only a sentence naming the account).
- New test files under `tests/ZoneFvg.Core.Tests/`; helpers under `TestSupport/` only by adding; existing tests only as listed below.
- `docs/requirements.yml`: the status lines of RSK-021, OPS-012, PLT-022.

Not allowed: `tools/`, `docs/spec.md`, `.github/`, `prompts/`; any signal or execution rule; the daily budget, kill switch, monthly limit or brake rules themselves; any core record default other than the new fields (core defaults: `FtmoMaxLossMode` Static, `ConsistencyMaxPercent` 0, `RewardPeriodStartDate` empty, and the existing core defaults of `InitialCapital`, the FTMO percents and the buffer unchanged).

## Owner authorisation to change existing tests

- Tests that assert the cBot defaults `InitialCapital` 200000.0, `FtmoDailyLossPercent` 5.0, `FtmoGuardBufferPercent` 1.0, or those lines of the parameter files: the expectations become 100000.0, 3.0 and 0.5. Tests that pin the exact list or order of the parameter files' lines or the cBot's declarations may gain the new lines.
- Tests that go through the cBot's default values and assert amounts derived from 200,000 (for example a guard or budget amount of a "defaults run") may change to the amounts at 100,000, only where the amount follows from the changed default.
- Risk values are not in the parameter hash, so every hash pin stays.
- List each changed test with old and new expectation. No assertion removed or loosened. If any other existing test fails, stop and ask.

## Build

1. **Parameters (RSK-021, OPS-012).** `RiskParameters.FtmoMaxLossMode` (enum `FtmoMaxLossMode { Static, TrailingBalance }`, default Static), `ConsistencyMaxPercent` (default 0), `RewardPeriodStartDate` (a date or null; default null = `RiskEpochDate`); validation as RSK-021 and PLT-022 (`ConsistencyMaxPercent` from 0 to 100; buffer above 0 and below `FtmoDailyLossPercent`).
2. **Guard (RSK-021).** With `TrailingBalance`, the overall condition uses P from the same function the kill switch uses (`PeakBalance` or what WP41 named it), `max(InitialCapital, peak)`. The decision record carries P and the floor used; the existing journal line of a guard decision names them only in trailing mode (static mode: byte for byte as before). P not computable → static condition. The daily condition is untouched.
3. **Readout (OPS-012).** A pure function from the closed trades, the start instant and the limit to the readout values; the core writes the line at start and at the first event of each new CE(S)T day (find where daily duties run; no new timer). Days are CE(S)T dates of the close time. Profit is the trade's net profit as the broker reports it (commission and swap included). Exact line format: `consistency since=yyyy-MM-dd total=<R> bestDay=yyyy-MM-dd bestDayProfit=<R> ratio=<R or NA> limit=<R> met=<yes|no>` with money rounded to 2 decimals and the ratio as a percent rounded to 2 decimals; `status=unavailable` variant on a history failure. It reads history from the broker for the readout only once per day.
4. **cBot (PLT-022).** Defaults of PLT-022; `FtmoMaxLossMode` parsed in the core (ordinal, exact names) with a refusal naming it; `RewardPeriodStartDate` parsed like `RiskEpochDate` (invariant `yyyy-MM-dd`, empty allowed). Passed in `BuildRisk`.
5. **Files and guides (PLT-022).** Every file under `deploy/params/` (including `XAUUSD-overnight` if WP43 has added it by the time you branch; if not, say so) carries the lines of row BM. The runbook and `deploy/README.md` gain a short FTMO Instant section: the limits of 15.8, `RiskEpochDate` stays the account start, the withdrawal rule (keep the balance at least about 5% above peak x 0.92 after a withdrawal), and after each reward set `RewardPeriodStartDate` to that date (parameters are changed only when flat). `docs/backtest-howto.md`: starting capital 100,000.

## Tests required (tagged)

**RSK-021**

- Static mode: every RSK-009 case unchanged (existing tests pass untouched) and a pinned guard journal line unchanged.
- Trailing, InitialCapital 100,000, buffer 0.5, max 10: no trades → floor 90,500; equity 90,500 → CloseAllAndHalt, 90,500.01 → no action. After closed trades to a peak balance of 106,000 → floor 96,500; equity 96,500 → act, 96,500.01 → no. A peak only reached before `RiskEpochDate` does not count. A peak below 100,000 → floor from 100,000.
- A withdrawal (balance drops with no closed trade) keeps P: equity at the floor after a withdrawal → act.
- P not computable (history read fails or invalid) → the static floor 90,500 applies.
- Daily condition in trailing mode: M − 2,500 at 100,000 (buffer 0.5, daily 3): act at exactly M − 2,500, not at M − 2,499.99.
- Validation: mode text other than `Static`/`TrailingBalance` (including `trailingbalance`) refused naming `FtmoMaxLossMode`; buffer 0, −0.1, 3 (= daily) refused, 0.5 and 2.99 accepted.
- The kill switch and the brake decide exactly as before on the same histories (shared peak, no change).

**OPS-012**

- Readout values on hand-built histories: total 10,000 with best day 6,000 → ratio 60.00, met=no; best day 3,000 → 30.00, met=yes (exactly at the limit is met); 3,000.01 → met=no; total ≤ 0 → ratio=NA met=no; losing days count against the total; trades closed before 00:00 CE(S)T of the start date excluded, at 00:00 included; two trades closing on the same CE(S)T day sum into one day; a trade closing at 23:30 UTC in summer belongs to the next CE(S)T day.
- `RewardPeriodStartDate` empty → `RiskEpochDate` used; set → that date used.
- The line is written at start and once at the first event of each new CE(S)T day, not twice in a day; with `ConsistencyMaxPercent` 0 no line and no extra history read.
- History failure → `status=unavailable` line, no exception, no trading effect.
- Information only: a scenario where met=no and met=yes give identical orders.

**PLT-022**

- The cBot source declares the defaults of PLT-022 once each; `BuildRisk` assigns the new fields.
- A defaults run (XAUUSD, pip 1 and 0.01) accepted, with `FtmoMaxLossMode` TrailingBalance and `InitialCapital` 100,000; `RewardPeriodStartDate` "2026-13-01" and "27.11.2026" refused naming it; "" and "2026-12-15" accepted.
- Every parameter file carries each line of row BM once, in the cBot's order, and the files stay equal apart from the lines that differ by design (`HeartbeatUrl`, and for the overnight file `EntryMode` and `RiskPercent`).

## Owner decisions already taken (do not reopen them in code)

- Rows BG to BM of v0.20: FTMO Instant 100,000, daily 3, max 10 trailing on balance, guard buffer 0.5, P from the kill switch's peak since `RiskEpochDate` with the static fallback, consistency readout information only, the files and guides.
- Anything the spec does not settle: write it under "Decisions for the human" and leave the related id `planned`.

## Done

Tests first, CI green, changed lines covered, all commands of `AGENTS.md` run and pasted (dotnet from CI), the Python checks, status lines to `implemented` only if no decision is open, and a plain summary for the owner: the bot is now set for an FTMO Instant 100,000 USD account; its emergency guard closes everything 0.5% before FTMO's 3% daily floor and before FTMO's 10% floor that trails the highest closed balance; the daily budget (2%), kill switch (8%) and monthly limit (2%) are unchanged in percent; a daily journal line shows whether the 30% consistency condition for a reward is met.
