# WP18: Ladder win reset and deal risk 0.50 (RSK-013 change)

## Read first

`AGENTS.md`, `docs/architecture.md` (section 10, rows of 2026-11-16 and
2026-11-17 on ladder sizing), `docs/spec.md` 4.1, requirement RSK-013 (its
text changed on 2026-11-17), `prompts/WP12-ladder-sizing.md` (the first
version) and the WP12 code: `src/ZoneFvg.Core/Risk/LadderSizer.cs`,
`LadderEntry.cs`, the wiring in `Execution/PreTradeFilters.cs`, and the
simulator's ladder mode.

## What changes

The owner corrected the ladder rule. Before: a deal's four parts always ran
to the end. Now: **a win ends every open deal**. Deal j contributes nothing
to entry t when any bot trade with net result above zero closed after entry
j was placed and before entry t was placed (a close at the same instant as a
placement counts as before it, as in WP12). Losses and break-even trades do
not end deals. The default `LadderDealRiskPercent` changes from 0.30 to
0.50. Everything else in RSK-013 stays as WP12 built it: entry numbering,
s(t), weights, the 0.5% cap, broker data only, all other limits afterwards,
`LadderEnabled` false gives the fixed RiskPercent.

## Requirements to implement

RSK-013 (changed text).

## Allowed paths

`src/ZoneFvg.Core/Risk/**`, `src/ZoneFvg.Core/Execution/**` (only if the
entry records need the close time of other trades),
`src/ZoneFvg.Core/Simulation/**` (the ladder mode must use the same rule),
`src/ZoneFvg.Bot/**` (the parameter default 0.50),
`tests/ZoneFvg.Core.Tests/**`, and the `status:` line of RSK-013.

## Owner authorisation to change existing tests

The owner changed this rule on 2026-11-17. Existing RSK-013 tests whose
expected values follow the old rule (parts continuing after a win, or the old
default 0.30) may be updated to the new rule. Change only expected values and
setup that the rule change forces; keep every other assertion, and list each
changed test in the pull request with the old and new value. The sheet-value
tests (592.40 / 296.20 / 444.30 / 666.45, Step 2, the four-deal row 1,999.35
after losses) stay as they are. No other test may be weakened.

## Tests required

Percent of balance, sequence of results, each trade closed before the next
is placed (W = win, L = loss):

- Deal risk 0.30, no cap reached, W W L L L W W:
  0.08886, 0.141501, 0.225326, 0.201523, 0.302284, 0.453426, 0.141501
  (this is the owner's own table).
- Defaults (0.50, cap 0.5), same sequence:
  0.1481, 0.235834, 0.375543, 0.335871, 0.5, 0.5, 0.235834.
- Defaults, L L L L L L: 0.1481, 0.22215, 0.333225, 0.499838, 0.499838,
  0.499838.
- Defaults, W W W W W: 0.1481, 0.235834, 0.375543, 0.5, 0.5.
- A break-even trade does not end deals and resets s to 0.
- A win of any bot trade (another symbol's instance) ends the deals; a win
  that closes after entry t is placed does not affect entry t; a close at the
  same instant as a placement counts as before it.
- Restart: same broker data, same percent.
- `LadderEnabled` false: unchanged fixed sizing.
- Simulator ladder mode reproduces the default sequences above.

Values are to six decimals; compare with a tolerance of 1e-6.

## Definition of done

- [ ] All tests pass in CI; coverage gates hold.
- [ ] RSK-013 is `implemented` (only if "Decisions for the human" is empty).
