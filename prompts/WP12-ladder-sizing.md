# WP12: Ladder sizing (owner's 4-part money management)

## Read first

`AGENTS.md`, `docs/architecture.md` (sections 3 to 5 and 10), `docs/spec.md`
section 4 (4.1 starts with the owner decision), and requirement RSK-013.
The owner's spreadsheet logic, already reproduced exactly in Python, is:
part1 = C1 x 0.2962 x 1.5924^Step, part2 = part1/2, part3 = (part1+part2)/2,
part4 = (part1+part2+part3)/2; a new deal starts on every trade and each row
sums the parts of the last four deals.

## Requirements to implement

RSK-013.

## Allowed paths

`src/ZoneFvg.Core/Risk/**` (a new `LadderSizer` and the parameter entries in
`RiskParameters`), `src/ZoneFvg.Core/App/BotCore.cs` and
`src/ZoneFvg.Core/Execution/**` (only to pass the ladder percent to sizing),
`src/ZoneFvg.Core/Simulation/**` (an option to size simulated trades with
the same `LadderSizer`), `src/ZoneFvg.Bot/**` (the four new parameters),
`tests/ZoneFvg.Core.Tests/**`, and the `status:` line of RSK-013.

## What to build

1. `LadderSizer`: a pure function from the ordered list of entry records
   (placement time, closed or not, net result) to the risk percent of the
   next entry. No state of its own. Parameters: `LadderEnabled` (true),
   `LadderDealRiskPercent` (0.30), `LadderGrowth` (1.5924),
   `LadderFirstWeight` (0.2962), `MaxRiskPercentPerTrade` (0.5), validated.
2. Wire it so that the percent replaces the fixed `RiskPercent` in RSK-001.
   The open-risk cap, currency cap, daily budget, kill switch and FTMO guard
   stay exactly as they are and apply afterwards.
3. The entry list is read from the broker port each time (filled positions,
   closed history, pending entry orders with the bot label), never cached.
4. The simulator can size trades with the same function, so backtests and
   challenge simulations use the ladder too.

## Tests required

- The owner's sheet: with deal risk 2.0% and no cap, Step 0 gives parts
  592.40, 296.20, 444.30, 666.45 on 100,000; Step 2 gives 1,502.17, 751.09,
  1,126.63, 1,689.94; a row with four overlapping Step-0 deals sums to
  1,999.35. These values are from the owner's spreadsheet.
- Defaults: entries 1 to 4 at the start ramp up as only 1, 2, 3, 4 deals
  overlap; steady state after losses is 0.30% x 0.999675; after wins the
  cap 0.5% holds exactly (0.5% allowed, never more).
- Consecutive wins: a loss resets s to 0; a break-even trade (net 0) counts
  as not a win; open trades do not count; cancelled or expired orders do not
  take a number.
- Restart: the same broker data gives the same percent.
- `LadderEnabled` false gives exactly the old behaviour (all existing RSK-001
  tests still pass unchanged).

## Definition of done

- [ ] All tests pass in CI; coverage gates hold.
- [ ] RSK-013 is `implemented`.
