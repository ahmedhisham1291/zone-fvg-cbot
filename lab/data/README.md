# lab/data: Dukascopy downloader (LAB-001)

`dukascopy.py` downloads Dukascopy's daily one-minute candle files, checks
every record, joins bid and ask, drops the minutes the spec drops and writes
one gzip CSV per symbol and month, with manifests and a data id. Standard
library only.

```bash
python3 -I lab/data/dukascopy.py --symbols EURUSD,GBPUSD,USDJPY,AUDUSD,XAUUSD \
    --from 2019-03-01 --to-month 2026-10 --out lab-data
```

`--to-month` defaults to the month before the current UTC month (the only
place the program reads the clock). `--from` must be the first day of a
month. `--merge-only` makes no request: it merges the symbols' own manifests
already in `--out` into `manifest.csv` and prints the data id.

Tests (no network; run from this folder): `python3 -I -m unittest -v`.

## Source layout (proposal, to be confirmed on the first real files)

- URL: `https://datafeed.dukascopy.com/datafeed/{SYMBOL}/{YYYY}/{MM}/{DD}/{BID|ASK}_candles_min_1.bi5`,
  `MM` the month minus one (January is `00`), `DD` the day, both two digits.
- Body: LZMA (`FORMAT_ALONE`); an empty body, empty content or HTTP 404 is a
  day without data.
- Records: 24 bytes big-endian `>I4if`: seconds from 00:00 UTC, then open,
  close, low, high as integers of the point, then a 32-bit float volume.
  Point 0.00001 for EURUSD, GBPUSD and AUDUSD; 0.001 for USDJPY and XAUUSD.
  If the price order were wrong, the low and high checks would fail on most
  records, so a wrong order cannot pass silently.

The first recorded run of the `data` stage (`docs/test-records/WP40-lab.md`)
confirms or corrects this section.

## Checks and rules

- Record: time a whole minute below 86,400 s and strictly increasing; low at
  most min(open, close); high at least max(open, close); every price inside
  the plausible range (EURUSD 0.8-1.4, GBPUSD 1.0-1.8, AUDUSD 0.5-0.9,
  USDJPY 80-200, XAUUSD 1,000-10,000); volume finite and not negative. Any
  failure stops the run naming the file and the record.
- Requests: at most 4 at a time, User-Agent `zone-fvg-lab`, 30 s timeout,
  retries after 2, 4, 8, 16 and 32 s on a timeout, a connection error, 429 or
  5xx; any other status stops the run.
- Join, in this order: a minute from Friday 17:00 to Sunday 17:00 New York is
  dropped as `dropped_weekend`; a minute with only one side as
  `dropped_one_side`; a minute whose bid volume is 0 as `dropped_zero_volume`.
  A kept minute whose ask close is below its bid close is counted as
  `ask_below_bid` and kept (the C# reader of LAB-002 refuses it, so a count
  above 0 is a question before the engine stage). New York time uses the US
  rule as `NewYorkClock`, not the operating system's time-zone data.
- A weekday (UTC date, Monday to Friday, 25 December and 1 January excepted)
  without a kept minute is listed; more than 2 in a month stops the run.

## Output

- `<out>/<SYMBOL>/<yyyy-mm>.csv.gz`: header
  `time_utc,bid_open,bid_high,bid_low,bid_close,ask_open,ask_high,ask_low,ask_close,volume`,
  times `yyyy-MM-ddTHH:mmZ`, prices with the decimals of the point (5 for
  the pairs quoted in USD, 3 for USDJPY and XAUUSD), volume the bid candle's
  as the shortest text of its 32-bit value. Gzip with mtime 0 and no file name.
- `<out>/<SYMBOL>/manifest.csv` and `<out>/manifest.csv`: per symbol and month
  `symbol,month,minutes_kept,dropped_weekend,dropped_one_side,dropped_zero_volume,ask_below_bid,weekdays_without_data,first_minute,last_minute,sha256`.
- `<out>/<SYMBOL>/raw-manifest.csv` and `<out>/raw-manifest.csv`: per raw file
  `symbol,day,side,status,bytes,sha256` (status `ok`, `empty` or `missing`).
  The raw files themselves are not kept.
- Last line: `data id <sha256 of manifest.csv>`.

A month whose file in `<out>` matches the checksum in the symbol's manifest
is not requested again.
