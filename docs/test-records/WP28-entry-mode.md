# WP28 test record: entry mode in the cBot

Manual verification of PLT-012 (the three entry-mode parameters of the cBot)
and PLT-013 (the parameter files), plus the open hour of the daily bars that
Breakout mode relies on. The owner runs the steps on cTrader desktop (Mac)
with an FTMO Free Trial or demo account, or in a backtest where a step says
so, and fills in the "Observed" and "Pass / fail" lines. PLT-012 and PLT-013
are marked `implemented` only after the owner has signed this record
(docs/test-records/README.md).

| Field | Value |
|---|---|
| Date | (fill in) |
| Run by | (fill in) |
| cTrader version | (Help > About) |
| Account | FTMO Free Trial or demo, account type (hedging / netting) |
| Bot version | `ZoneFvgBot 0.1.0`, commit (fill in) |
| `.algo` | artifact `ZoneFvgBot-algo` of the CI run of that commit |

Two lines of the WP08 record changed with this package: the first adapter
line now ends with `, mode Breakout` or `, mode ZoneFvg` (WP08 step 1), and
the parameter panel has a sixth group, "Entry mode" (WP08 step 5). Steps 1
and 2 below check both.

## Automatic evidence (no owner action)

| Item | Requirement | Evidence |
|---|---|---|
| The cBot with the three parameters builds and the `.algo` is uploaded as artifact `ZoneFvgBot-algo` | PLT-012 | `build-test` job, step "Build the cBot", of the pull request |
| In the core, a set `BotParameters.Breakout` is Breakout mode: the start line ends with `mode=Breakout`, the parameter hash includes the breakout values, and `Validate` refuses a breakout window that differs from the session times | REC-017 | `build-test` job, tests tagged REC-017 |

## Preparation

1. Download the artifact `ZoneFvgBot-algo` from the CI run, unzip it, and
   double-click `ZoneFvgBot.algo` (or drag it into cTrader). It appears under
   Algo > cBots.
2. Open an EURUSD chart on **M15**. Add an instance of ZoneFvgBot to it.
3. Parameters as in the WP08 record, "Preparation", step 3. Leave everything
   else at its default unless a step says otherwise.
4. Open the Log tab of the instance. Every adapter line starts with
   `ZFVG adapter`; core journal lines start with a time stamp and a category.

## Steps

### 1. Parameter panel (PLT-012)

Open the instance's parameter panel.

Expected: a group **Entry mode** with three parameters and these defaults:
`EntryMode` Breakout, `BreakoutEntryBufferAtr` 0.1, `BreakoutStopAtr` 1.0.
The groups Signal, Execution, Risk, News and Operations are unchanged.

Observed: (fill in)

Pass / fail: (fill in)

### 2. Start with the defaults: Breakout mode (PLT-012)

Start the instance with the defaults of step 1.

Expected:
- `ZFVG adapter version 0.1.0, symbol EURUSD, backtest no, mode Breakout`.
- The core `start` journal line ends with `mode=Breakout`.
- Write down the value of `hash=` in that line. It differs from the hash of
  step 3.

Observed (hash): (fill in)

Pass / fail: (fill in)

### 3. Start with `EntryMode` = ZoneFvg (PLT-012)

Stop the instance. Set `EntryMode` = `ZoneFvg`, change nothing else, and
start it.

Expected:
- `ZFVG adapter version 0.1.0, symbol EURUSD, backtest no, mode ZoneFvg`.
- The core `start` journal line has no `mode=` field.
- Its `hash=` equals the hash that the bot printed before this change with
  the same parameters. To get that hash, start the `.algo` of commit
  `c5b59ff` (the last commit of `main` before this package; artifact
  `ZoneFvgBot-algo` of its CI run on `main`, kept by GitHub until
  20 February 2027) once with the same parameters, or use the
  hash written in the WP08 record, step 1, if it was run with the same
  parameters.

Observed (both hashes): (fill in)

Pass / fail: (fill in)

### 4. Invalid values (PLT-012)

Stop the instance between the runs, and start it after each change. Put the
value back afterwards.

a. `EntryMode` = `Abc`. Expected: `ZFVG adapter refused to start: invalid
   parameter: EntryMode must be one of: ZoneFvg, Breakout.` (followed by the
   parameter name), then the instance stops. No order is placed.
b. `EntryMode` = Breakout, `BreakoutStopAtr` = 0. Expected: `ZFVG adapter
   refused to start: invalid parameter: StopAtrMult must be a finite number
   greater than zero but was ...`, then the instance stops. No order is
   placed. The message uses `StopAtrMult`, the core's name of the value set
   by `BreakoutStopAtr` (see the pull request, "Decisions for the human").

Observed: (fill in)

Pass / fail: (fill in)

### 5. A short backtest in each mode (PLT-012)

Backtest tab, EURUSD M15, settings as in `docs/backtest-howto.md` step 7
(start at least 10 months before the period you look at), `NewsEnabled` =
false. Run it once with `EntryMode` = Breakout and once with `EntryMode` =
ZoneFvg, over the same period.

Expected:
- Breakout run: `signal` lines with `kind=breakout` and a `level=` value;
  their `zone=` field holds a breakout id `BH-yyyyMMdd-HHmm` or
  `BL-yyyyMMdd-HHmm`.
- ZoneFvg run: `signal` lines without `kind=`; their `zone=` field holds a
  zone id `D-yyyyMMdd-HHmm` or `S-yyyyMMdd-HHmm`.
- Both runs complete without an exception.

Observed (one signal line of each run): (fill in)

Pass / fail: (fill in)

### 6. The parameter files load (PLT-013)

For each of the five files in `deploy/params/`, make a copy without
`.example` and replace the `CHANGE_ME...` values as in `deploy/README.md`
step 10.3 (on a trial account). Load it the way the deployment guide does
(`deploy/README.md`, fact 4: the CLI reads it as the second argument of
`run`, or `CBOT_SET`; on the laptop, the parameters of an instance) and start
the instance once with it.

Expected: no "Invalid .cbotset file format" message; the bot does not refuse
to start (a refusal would name the parameter); the first adapter line names
the symbol of the file and ends with `mode Breakout`. Because the bot refuses
to start while `RiskEpochDate` is empty or invalid, a start proves that the
file was read (`deploy/README.md`, fact 4).

Observed (one line per file: eurusd, gbpusd, usdjpy, audusd, xauusd): (fill in)

Pass / fail: (fill in)

### 7. Open time of the daily bars (architecture section 10, row "Daily and H4 bar boundaries on cTrader")

Switch the chart to D1 for a moment (or read the journal) and write down the
open time of the last three D1 bars in UTC. If the chart shows another time
zone, write down which one and convert. In Breakout mode the id of a breakout
signal (`BH-...` or `BL-...`, step 5) is the UTC open time of its reference
D1 bar and can be used as a second reading.

Expected: the D1 bars open at 21:00 UTC while New York is on daylight saving
time and at 22:00 UTC otherwise; there is no short daily bar of about two
hours for Sunday. Write the same observation into the WP08 record, step 3.
If the D1 bars open at 00:00 UTC or a short Sunday bar exists, write it
down: Breakout mode then does not go live before a follow-up.

Observed (three open times, and whether a short Sunday bar exists): (fill in)

Pass / fail: (fill in)

## Sign-off

| Requirement | Steps | Owner signature and date |
|---|---|---|
| PLT-012 | 1, 2, 3, 4, 5 | |
| PLT-013 | 6 | |
| Breakout go-live gate, D1 boundary (`deploy/README.md`, "Entry mode") | 7, and WP08 step 3 | |
