# WP22 test record: chart drawing clean-up (PLT-005)

Manual verification of the PLT-005 addition of 2026-11-17: the painter remembers
the names of the drawings it made in its previous paint and removes by name
every remembered name it does not draw again, because cTrader does not list
non-interactive drawings in the chart's object collection. A zone that ended,
or a gap that left the 24-hour snapshot, must disappear at the next paint.

The owner runs the steps on cTrader desktop and fills in the "Observed" and
"Pass / fail" lines. PLT-005 is marked `implemented` only after the owner has
signed this record (docs/test-records/README.md).

| Field | Value |
|---|---|
| Date | (fill in) |
| Run by | (fill in) |
| cTrader version | (Help > About) |
| Account | (fill in: demo or FTMO Free Trial) |
| Bot version | `ZoneFvgBot`, commit (fill in) |
| `.algo` | artifact `ZoneFvgBot-algo` of the CI run of that commit |

## Automatic evidence (no owner action)

| Item | Evidence |
|---|---|
| The existing PLT-005 tests (snapshots are read-only copies, gap age, restart equality, reading changes nothing) | `build-test` job, tests tagged `PLT-005` |
| `ChartPainter` removes remembered names that are not drawn again, after the existing removal over `Chart.Objects` | Diff of `src/ZoneFvg.Bot/ChartPainter.cs` in pull request #49 |

## Preparation

1. Download the artifact `ZoneFvgBot-algo` of the CI run and import
   `ZoneFvgBot.algo` into cTrader.
2. Open an EURUSD chart on **M15** and add an instance of ZoneFvgBot.
3. Parameters: `DrawObjects` = **true**; `RiskEpochDate` = the backtest start
   date; `InitialCapital` = the backtest starting capital (RSK-014 refuses to
   start otherwise); everything else as in `docs/backtest-howto.md`.
4. Backtest settings: a period of **12 months**, **Visual mode on** (drawings
   are made only in a visual backtest), any speed you can pause.

## Steps

### 1. Count the rectangles against the latest report

Start the visual backtest. **Pause** it after about month 11 (do not let it
end: cTrader removes non-interactive drawings when the test ends).

In the Log, find the latest line that contains `zones=` (the recovery `report`
line lists the active zones as `zones=[...]`; a later `report` line, if any,
replaces it). Count the zone rectangles on the chart: green for demand, red for
supply. Gap rectangles (blue, three M15 bars wide) are counted separately and
are not part of `zones=`.

Expected: the number of zone rectangles equals the number of zones in the
latest `zones=` list plus the zones created since that line, and no rectangle
belongs to a zone that has ended. Every rectangle's right edge is at the
current backtest time.

Observed: (fill in: number of rectangles, number of zones in the list)

Pass / fail: (fill in)

### 2. A zone that ends disappears

Pick one zone rectangle that is on the chart. Resume the backtest until that
zone ends (it is touched and confirmed, touched without a gap, closed through,
replaced by a newer zone or 120 H4 bars old; the daily `zones day=...` line
counts these endings). Pause again.

Expected: the rectangle of the ended zone is gone from the chart; the other
rectangles are still there.

Observed: (fill in)

Pass / fail: (fill in)

### 3. No drawing error

Expected: the Log has no line `ZFVG adapter error in chart drawing (drawing
switched off for this run)`.

Observed: (fill in)

Pass / fail: (fill in)

## Sign-off

| Field | Value |
|---|---|
| Result | (fill in: pass / fail) |
| Signed by the owner | (fill in) |
