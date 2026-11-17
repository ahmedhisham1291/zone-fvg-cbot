# WP18: Ladder step columns and win reset (RSK-013 change)

## Read first

`AGENTS.md`, `docs/architecture.md` (section 10, the ladder rows of
2026-11-16 and 2026-11-17), `docs/spec.md` 4.1, requirement RSK-013 (its
text was rewritten on 2026-11-17: read every numbered clause),
`prompts/WP12-ladder-sizing.md` (the first version) and the WP12 code:
`src/ZoneFvg.Core/Risk/LadderSizer.cs`, `LadderEntry.cs`, the wiring in
`Execution/PreTradeFilters.cs`, the CE(S)T clock in `Risk/`, and the
simulator's ladder mode.

## What changes

The owner corrected the ladder rule. WP12 grew every new deal by the number
of consecutive wins and let a deal's four parts always run to the end. Now:

1. **A win ends every open deal.**
2. **Steps live in four columns C, D, E, F**, one per position after the
   last win (1st to 4th trade); the 5th and later trades of a losing run
   use step 0. A new deal uses its position's column.
3. **A win raises the column of the winning entry's position** (F for 5 or
   more), but never past a column that is still 0: the raise moves back to
   the first column after the last non-zero one. C can always be raised.
4. **Month rule:** once an entry's raw (uncapped) percent is above
   `MaxRiskPercentPerTrade` (0.5), wins that close after that entry was
   placed raise nothing until the month ends. At 00:00 CE(S)T on the 1st
   all columns go back to 0. Open deals keep their sizes.

Unchanged from WP12: entry numbering and counting, a win is net > 0 (1e-9
tolerance), the tie rule (a close at the same instant as a placement counts
as before it), weights, deal risk default 0.30, cap 0.5, broker data only,
no state of its own, all other limits afterwards, `LadderEnabled` false gives
the fixed RiskPercent. Parameter `LadderGrowth` stays (1.5924).

The function stays pure: the column state is rebuilt from the full entry
list (placement time, close time, net, closed or not) every time, so a
restart gives the same percent.

## Requirements to implement

RSK-013 (rewritten text).

## Allowed paths

`src/ZoneFvg.Core/Risk/**`, `src/ZoneFvg.Core/Execution/**` (only if the
entry records need more broker fields), `src/ZoneFvg.Core/Simulation/**` (the
ladder mode must use the same function), `src/ZoneFvg.Bot/**` (only if a
parameter changes), `tests/ZoneFvg.Core.Tests/**`, and the `status:` line of
RSK-013.

## Owner authorisation to change existing tests

The owner changed this rule on 2026-11-17. Existing RSK-013 tests whose
expected values follow the WP12 rule (parts continuing after a win, or growth
by consecutive wins) may be updated to the new rule. Change only expected
values and setup that the rule change forces; keep every other assertion,
and list each changed test in the pull request with its old and new value.
The sheet-value tests (592.40 / 296.20 / 444.30 / 666.45, Step 2, the
four-deal row 1,999.35 after losses) stay as they are. No other test may be
weakened.

## Tests required

Percent of balance with the defaults (deal 0.30, cap 0.5), each trade closed
before the next is placed, all in one month unless stated. W = win, L = loss.
Compare with a tolerance of 1e-6.

- **The owner's example**, W W L L L W W L L W, then L L L L L L:
  0.08886, 0.141501, 0.225326, 0.201523, 0.302284, 0.453426, 0.225326,
  0.358809, 0.320905, 0.428717, then 0.358809, 0.320905, 0.481357, 0.5,
  0.398604, 0.359123. On 100,000 the first ten are the owner's dollars
  88.86, 141.50, 225.33, 201.52, 302.28, 453.43, 225.33, 358.81, 320.90,
  428.72. Also assert the columns after trades 2, 6, 7 and 10:
  (C, D, E, F) = (2,0,0,0), (2,1,0,0), (3,1,0,0), (3,1,1,0).
- **Freeze:** W W W W W W W: 0.08886, 0.141501, 0.225326, 0.358809, 0.5,
  0.5, 0.5, and C stays 4 after the 5th trade (raw 0.571 is above 0.5, so
  the later wins raise nothing).
- **Long losing run:** L x 6: 0.08886, 0.13329, 0.199935, 0.299903,
  0.299903, 0.299903 (5th and 6th at step 0).
- **Month reset:** three wins in one month, three in the next (first entry
  placed after 00:00 CE(S)T on the 1st): 0.08886, 0.141501, 0.225326, then
  0.08886, 0.141501, 0.225326. Include a deal still open across the month
  boundary keeping its size, the freeze lifting at the new month, and a
  month boundary in summer time and in winter time.
- **Break-even** is not a win: it does not end deals or raise a step, and
  the next trade's position goes up.
- **Another instance's win** ends the deals and raises a column; a win that
  closes after entry t is placed does not affect entry t; the tie rule.
- **Restart:** the same broker data gives the same percent and columns.
- `LadderEnabled` false: unchanged fixed sizing.
- **Simulator** ladder mode reproduces the owner's example.

## Definition of done

- [ ] All tests pass in CI; coverage gates hold.
- [ ] RSK-013 is `implemented` (only if "Decisions for the human" is empty).
