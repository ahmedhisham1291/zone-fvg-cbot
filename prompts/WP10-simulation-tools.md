# WP10 — Simulation tools: trade logs, portfolio, Monte Carlo, FTMO challenge

## Read first

`AGENTS.md`, `docs/spec.md` sections 12.1, 13.1, 13.3, 15.2, 15.4 and 15.5, and
the requirement texts below. Depends on WP01 (and WP04 for `CestClock`).

## Requirements to implement

SIM-001, SIM-002, SIM-003, SIM-004, SIM-005.

## Allowed paths

`src/ZoneFvg.Core/Simulation/**`, `tools/sim/**` (a small console project,
`net10.0`, that references Core; this is the one place a new project may be
added), `tests/ZoneFvg.Core.Tests/**`, and the `status:` lines of the
requirements above.

## What to build

All logic lives in `ZoneFvg.Core/Simulation` as pure, seeded functions. The
console project only parses arguments, reads and writes files.

1. **`TradeRecord`** and CSV reader and writer (SIM-001). Invariant culture,
   header row, strict validation with row numbers in error messages.
2. **`PortfolioCombiner`** (SIM-002): merges logs by close time; replays the
   account-level caps of RSK-004 to RSK-006 to count how many trades would
   have been skipped; outputs the combined trade list and equity curve.
3. **`MonteCarlo`** (SIM-003): given a list of results in R and a seed,
   produces N reshuffled sequences (default 1,000) and reports the
   distribution of maximum drawdown (in R and in percent at a given risk) and
   of rolling 30-trade average R, with the 1st, 5th, 50th and 95th
   percentiles.
4. **`FtmoChallengeSimulator`** (SIM-004): parameters are initial capital,
   risk percent per trade, profit target percent, daily loss percent, maximum
   loss percent, minimum trading days, the bot's own daily risk budget and
   kill switch. Input is trade records with times, so that days and
   simultaneous trades are respected: sample contiguous blocks of the combined
   trade list starting at random trading days (block bootstrap), scale results
   to the chosen risk, and walk day by day applying:
   - the bot's own limits first (they stop trading for the day or halt);
   - FTMO daily floor from the 00:00 CE(S)T balance, using intraday worst
     case = all trades open that day at their stops;
   - FTMO static floor;
   - pass when balance reaches the target with at least the minimum trading
     days and no open trade.
   Output: probability of pass, of breach, of neither within the data; median
   and 90th percentile days to pass; for Challenge (10%) and Verification
   (5%); for risk 0.5%, 0.75% and 1.0%.
5. **`AcceptanceReport`** (SIM-005): computes every row of spec 13.1 and prints
   pass or fail per threshold, as Markdown.
6. **Console commands**: `sim combine`, `sim montecarlo`, `sim ftmo`,
   `sim accept`, each with `--help`, deterministic for a given `--seed`.

## Decisions already made

- Randomness: a small seeded generator implemented in Core (for example
  xoshiro256**), never `System.Random` without a seed, so results are
  identical across platforms.
- Percentiles use the nearest-rank method.
- The simulator states its assumptions at the top of every report, including
  that past trade distributions may not repeat.

## Tests required

- SIM-001: round-trip; each malformed row rejected with its row number.
- SIM-002: two logs with overlapping trades; caps counted on a worked example.
- SIM-003: same seed gives identical output; a constant series has zero
  drawdown; a hand-checked five-trade example.
- SIM-004: a sequence that passes on day 4 exactly; one that reaches the
  target on day 3 and must wait for day 4; one that breaches the daily floor
  through floating loss; one that breaches the static floor; the bot's own
  budget preventing a breach; same seed, same probabilities.
- SIM-005: a synthetic log with known profit factor, average R and drawdown.

## Out of scope

Running cTrader backtests (the owner does that and supplies the logs).

## Definition of done

- [ ] Tests first, all green; coverage thresholds met.
- [ ] `tools/sim` builds in CI and each command runs on the sample log in
      `tests/ZoneFvg.Core.Tests/TestData/Sim/`.
- [ ] All `AGENTS.md` commands clean; outputs in the pull request.
- [ ] The five requirement statuses are `implemented`.
