# WP43 test record: the Overnight cost gate on the Free Trial (spec v0.18, row BD)

The Overnight mode (spec 3.10) buys gold at 18:15 New York with a stop order
just above the price and holds to 02:45 New York. Its research result
depends on the cost: with a spread of 0.30 USD +23R over 6.5 years, with
0.50 USD +14R, with 0.80 USD +1R, with 1.20 USD -16R (spec section 0,
v0.18). Row BD: before the Overnight instance trades on the Challenge
account, your Free Trial weeks must show gold's spread at the Overnight
entries.

You run both gold instances on the FTMO Free Trial with
`deploy/params/xauusd.cbotset.example` (SessionRange) and
`deploy/params/xauusd-overnight.cbotset.example` (Overnight), and write one
row per Overnight evaluation (Monday to Thursday evenings) below. This record
is the owner's gate; no agent fills it in.

| Field | Value |
|---|---|
| Period | (fill in: first and last date) |
| Run by | (fill in) |
| Account | FTMO Free Trial |
| Bot version | `ZoneFvgBot 0.1.0`, commit (fill in) |
| Overnight `start` line | `mode=Overnight`, `hash=826c617f`, `brake=off` (fill in what you see) |
| Pip size of XAUUSD (adapter start line `pipSize`) | (fill in: 1 or 0.01) |

## Automatic evidence (no owner action)

| Item | Requirement | Evidence |
|---|---|---|
| The Overnight signal, its levels, days and id | SIG-043 | `build-test` job, tests tagged SIG-043 |
| The buy stop, the spread check, the journal lines with the spread, the timed exit, the two instances on one account | EXE-033 | `build-test` job, tests tagged EXE-033 |
| The seven cBot parameters, the brake off by default, the six parameter files | PLT-021 | `build-test` job, tests tagged PLT-021 |

## Where to read the spread

In the Log tab of the Overnight instance, every Overnight evaluation writes
one `signal` line with `zone=ON-yyyyMMdd`. It shows `result=taken` or
`result=skipped` and `spreadPips=` the spread at that moment, in the
platform's pip. **With a pip of 1 USD the value is in USD; with a pip of
0.01 USD divide it by 100** (for example `spreadPips=30.0` is 0.30 USD). A
skipped line also shows `skip=` and the reason; `SpreadTooWide` means the
spread was above `MaxSpreadPips` (automatic: 0.40 USD). There is no
`signal` line on a day without a setup (for example a missing bar between
18:15 and 19:00 New York).

## Evaluations (at least ten)

| # | Trading day (`ON-` date) | Time of the line (UTC) | `spreadPips` | Spread in USD | Taken or skipped | Skip reason |
|---|---|---|---|---|---|---|
| 1 | | | | | | |
| 2 | | | | | | |
| 3 | | | | | | |
| 4 | | | | | | |
| 5 | | | | | | |
| 6 | | | | | | |
| 7 | | | | | | |
| 8 | | | | | | |
| 9 | | | | | | |
| 10 | | | | | | |

## Result

| Measure | Value |
|---|---|
| Evaluations recorded | (fill in, at least 10) |
| Taken | (fill in) |
| Skipped as `SpreadTooWide` | (fill in) |
| Skipped for another reason (name it) | (fill in) |
| Average spread of the taken entries, USD | (fill in) |

**Pass** when most evaluations were taken (more than half) and the taken
entries' spreads average at most 0.40 USD. **Fail** when most entries were
skipped for spread, or the taken ones paid more than about 0.40 USD on
average: then the Overnight instance is not started on the Challenge
account (row BD), and you decide what follows.

| Pass / fail | Signed (owner) | Date |
|---|---|---|
| (fill in) | | |
