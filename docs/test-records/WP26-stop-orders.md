# WP26 test record: stop-entry orders

Manual verification of PLT-011: the cTrader adapter places a breakout signal as
a pending stop order with stop loss, take profit and expiry, and the order
triggers, expires and is cancelled as expected. The owner runs the steps on
cTrader desktop with an FTMO Free Trial (or demo) account and fills in the
"Observed" and "Pass / fail" lines. PLT-011 is marked `implemented` only after
the owner has signed this record (docs/test-records/README.md).

The steps need an instance in Breakout mode, which is possible after WP28 (the
cBot parameter `EntryMode`). Until then this record cannot be run.

| Field | Value |
|---|---|
| Date | (fill in) |
| Run by | (fill in) |
| cTrader version | (Help > About) |
| Account | FTMO Free Trial or demo, account type (hedging / netting) |
| Bot version | `ZoneFvgBot`, commit (fill in) |
| `.algo` | artifact `ZoneFvgBot-algo` of the CI run of that commit |

## Automatic evidence (no owner action)

| Item | Requirement | Evidence |
|---|---|---|
| The adapter's `PlaceStopOrder` call (prices with `ProtectionType.Absolute`, expiration, comment) compiles against the pinned `cTrader.Automate` package | PLT-011 | `build-test` job, step "Build the cBot", of the pull request |
| Stop orders in the core: request, fill model, read-back, resend rule, journal | EXE-018, EXE-019, EXE-020, OPS-010 | `build-test` job, tests tagged with these ids |

## Preparation

1. Download the artifact `ZoneFvgBot-algo` from the CI run, unzip it, and
   double-click `ZoneFvgBot.algo` (or drag it into cTrader). It appears under
   Algo > cBots.
2. Open an EURUSD chart on **M15**. Add an instance of ZoneFvgBot to it with
   `EntryMode` = Breakout and the other parameters as in the WP08 record.
3. Open the Log tab of the instance and the Trade tab (Positions, Orders and
   History).
4. A breakout signal needs a long or short D1 bias and an M15 close beyond the
   previous day's high or low between 07:00 and 17:00 UTC. It may take some
   days. Write down the journal `signal` line of each signal (result=taken,
   `kind=breakout`, `level=`, entry, stop, target).

## Steps

### 1. Start line shows bid and ask (PLT-011)

Start the instance.

Expected: the adapter line `ZFVG adapter symbol EURUSD account ...` contains
`bid <price> ask <price>`, with the ask at or above the bid and both equal to
the prices in the symbol's quote at that moment.

Observed: (fill in)

Pass / fail: (fill in)

### 2. A breakout signal appears as a pending stop order (EXE-018, PLT-011)

When a `signal` line with `result=taken` and `kind=breakout` is written, open
the Orders tab.

Expected:
- One pending order of the symbol with the type **Stop** (Buy Stop for a buy,
  Sell Stop for a sell), not Limit.
- Its price is the journaled `entry`, its stop loss the journaled `stop`, its
  take profit the journaled `target` (prices, not distances).
- Its expiry is the journaled time plus 2 hours (8 M15 bars), or earlier when
  a news window starts sooner.
- Its label is `ZFVG|v1|EURUSD|BH-yyyyMMdd-HHmm` (buy) or `...|BL-...` (sell)
  and its comment `e=...;s=...;t=...;r=...;h=...`.
- For a buy the entry is above the ask at placement; for a sell it is below
  the bid.

Observed: (fill in)

Pass / fail: (fill in)

### 3. The order fills when price passes it (EXE-018, PLT-011)

Wait until the ask reaches a buy stop's price (the bid a sell stop's).

Expected: the order is gone from Orders; a position of the same volume is in
Positions with the stop loss and take profit of the order; the journal shows
no stop loss alert.

Observed: (fill in)

Pass / fail: (fill in)

### 4. An unfilled order disappears at its expiry (EXE-015, PLT-011)

For a stop order that price did not reach, wait until its expiry time.

Expected: the order is removed by cTrader at its expiry, while the bot may be
running or not; no position is opened from it; History shows no trade for it.

Observed: (fill in)

Pass / fail: (fill in)

### 5. The Friday close cancels a pending stop order (EXE-013, PLT-011)

On a Friday, with a pending breakout stop order of the bot that expires after
20:00 UTC (for example with `EntryExpiryBars` raised for this test only), keep
the instance running past 20:00 UTC.

Expected: at 20:00 UTC the bot cancels the stop order (Orders tab empty for
the symbol) and closes any bot position of the symbol; the journal records the
cancel.

Observed: (fill in)

Pass / fail: (fill in)

## Sign-off

| Field | Value |
|---|---|
| All steps pass | (yes / no) |
| Signed by the owner | (fill in) |
| Date | (fill in) |
