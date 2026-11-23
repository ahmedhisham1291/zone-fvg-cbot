# Research

Tools for the strategy search (owner's go of 2026-11-23, `docs/architecture.md` section 10).

Nothing in this folder is part of the trading bot. Nothing here places an order, changes an
order or reads the account. The trading bot is `src/`; its rules are `docs/spec.md`.

## Why this folder exists

Two entry rules were specified, built and then backtested by hand, and neither made money. From
now on an entry rule is tested on exported price history first, many rules at once and with
costs. It is chosen on older years and judged once on recent years that the choice never saw.
Only a rule that passes becomes a spec change and a work package. The owner's acceptance
backtests in cTrader (spec section 13) are unchanged and still decide go or no-go.

## ExportBars

`ExportBars/ExportBars/` is a cBot that writes the closed bars of the chart's symbol to files.
It has no trading call. The workflow `research-tools` builds `ExportBars.algo` (artifact
`ExportBars-algo`).

### How to run it

1. Open `ExportBars.algo`. cTrader adds it to the list of cBots.
2. Backtest it the same way as `ZoneFvgBot`, once per symbol: EURUSD, GBPUSD, USDJPY, AUDUSD,
   XAUUSD. Time frame `m1`. Start date as early as the date picker allows, end date today. The
   data and spread settings are the ones of the earlier backtests. Leave the one parameter
   unticked.
3. Let each run reach its end. The last log line starts with `XDONE` and names the folder.

### What it writes

In the folder cTrader gives every cBot (on the Mac under `~/cAlgo/Data`), per symbol:

| File | Content |
|---|---|
| `ExportBars_<SYMBOL>_Minute.csv.gz` | The chart's time frame (`m1`) |
| `ExportBars_<SYMBOL>_Minute15.csv.gz` | m15 bars as the platform reports them |
| `ExportBars_<SYMBOL>_Hour4.csv.gz` | h4 bars as the platform reports them |
| `ExportBars_<SYMBOL>_Daily.csv.gz` | D1 bars as the platform reports them |
| `ExportBars_<SYMBOL>_info.txt` | Pip size, tick size, lot size, commission and swap as reported |

Columns: `time_utc` (bar open, UTC), `open`, `high`, `low`, `close`, `tick_volume`. A bar that
is still forming is never written.

### If files cannot be written

The bot says so in the log (a line starting with `XE`) and prints the m15, h4 and D1 bars to
the log instead, twelve bars per line (lines starting with `XB`). Save the log and send it. The
parameter "Print bars to the log instead of files" forces this mode.

### Log lines

| Prefix | Meaning |
|---|---|
| `XI` | Symbol facts at start |
| `XF` | Files are being written, with the folder |
| `XL` | Bars are being printed to the log |
| `XB` | Bars (log mode): `XB <SYMBOL>_<TimeFrame> yyMMddHHmm,open,high,low,close,volume;...` |
| `XE` | Something could not be done; the text says what |
| `XS` | Summary per series at the end: rows, first and last bar |
| `XDONE` | The run is complete |
