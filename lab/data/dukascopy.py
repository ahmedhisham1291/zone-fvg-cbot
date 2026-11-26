#!/usr/bin/env python3
"""Download Dukascopy's one-minute bid and ask candles and convert them to monthly files (LAB-001).

Standard library only. The output is deterministic: the same downloaded bytes give the same
monthly files (gzip with mtime 0 and no file name), the same manifests and the same data id,
whatever the number of worker threads or the machine's time zone and locale.

Layout of --out:
  <SYMBOL>/<yyyy-mm>.csv.gz       one file per symbol and month (LAB-001 columns)
  <SYMBOL>/manifest.csv           that symbol's rows of the manifest
  <SYMBOL>/raw-manifest.csv       that symbol's rows of the raw manifest
  manifest.csv, raw-manifest.csv  the rows of every symbol given in --symbols, sorted
The data id is the SHA-256 of manifest.csv and is printed on the last line.
"""
from __future__ import annotations

import argparse
import csv
import gzip
import hashlib
import http.client
import io
import lzma
import math
import os
import struct
import sys
import time
import urllib.error
import urllib.request
from collections import namedtuple
from concurrent.futures import ThreadPoolExecutor
from datetime import date, datetime, timedelta, timezone

BASE_URL = "https://datafeed.dukascopy.com/datafeed"
USER_AGENT = "zone-fvg-lab"
TIMEOUT_SECONDS = 30
RETRY_DELAYS = (2, 4, 8, 16, 32)
MAX_WORKERS = 4
MAX_WEEKDAYS_WITHOUT_DATA = 2
RECORD = struct.Struct(">I4if")
SIDES = ("ASK", "BID")

MANIFEST_FIELDS = ("symbol", "month", "minutes_kept", "dropped_weekend", "dropped_one_side",
                   "dropped_zero_volume", "ask_below_bid", "weekdays_without_data",
                   "first_minute", "last_minute", "sha256")
RAW_FIELDS = ("symbol", "day", "side", "status", "bytes", "sha256")
CSV_HEADER = ("time_utc,bid_open,bid_high,bid_low,bid_close,"
              "ask_open,ask_high,ask_low,ask_close,volume")

SymbolSpec = namedtuple("SymbolSpec", "name decimals low high")
Candle = namedtuple("Candle", "sec open high low close volume")
Minute = namedtuple("Minute", "time bid ask")


def _spec(name, decimals, low, high):
    scale = 10 ** decimals
    return SymbolSpec(name, decimals, int(round(low * scale)), int(round(high * scale)))


# Prices in the files are integers of the symbol's point (LAB-001); the plausible range checks
# every price of every record.
SYMBOLS = {
    "EURUSD": _spec("EURUSD", 5, 0.8, 1.4),
    "GBPUSD": _spec("GBPUSD", 5, 1.0, 1.8),
    "AUDUSD": _spec("AUDUSD", 5, 0.5, 0.9),
    "USDJPY": _spec("USDJPY", 3, 80, 200),
    "XAUUSD": _spec("XAUUSD", 3, 1000, 10000),
}


class DataError(Exception):
    """A problem that stops the run: bad data, a refused request or a month with too many gaps."""


class TransientError(Exception):
    """A timeout or connection error; the request is retried."""


# ------------------------------------------------------------------ URLs and requests

def url_for(symbol: str, side: str, day: date) -> str:
    """Daily candle file. The month in the path is zero-based (January is 00)."""
    return f"{BASE_URL}/{symbol}/{day.year:04d}/{day.month - 1:02d}/{day.day:02d}/{side}_candles_min_1.bi5"


def urllib_opener(url: str, timeout: float):
    """The real opener: (status, body); timeouts and connection errors raise TransientError."""
    request = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
    try:
        with urllib.request.urlopen(request, timeout=timeout) as response:
            return response.status, response.read()
    except urllib.error.HTTPError as error:
        return error.code, b""
    except (urllib.error.URLError, http.client.HTTPException, OSError) as error:
        raise TransientError(str(error)) from error


def fetch(url: str, opener, sleep):
    """Body of the file, b"" for an empty file, None for 404. Retries 429, 5xx and connection
    errors after 2, 4, 8, 16 and 32 s; any other status, or a sixth failure, raises DataError."""
    attempt = 0
    while True:
        try:
            status, body = opener(url, TIMEOUT_SECONDS)
        except TransientError as error:
            problem = f"connection error ({error})"
        else:
            if status == 200:
                return body
            if status == 404:
                return None
            if status != 429 and not 500 <= status <= 599:
                raise DataError(f"{url}: HTTP {status}")
            problem = f"HTTP {status}"
        if attempt >= len(RETRY_DELAYS):
            raise DataError(f"{url}: {problem} after {attempt + 1} attempts")
        sleep(RETRY_DELAYS[attempt])
        attempt += 1


# ------------------------------------------------------------------ decoding and checks

def decode_candles(name: str, data: bytes, spec: SymbolSpec):
    """Decode and check one daily file. An empty body or empty content is a day without data."""
    if not data:
        return []
    try:
        raw = lzma.decompress(data, format=lzma.FORMAT_ALONE)
    except lzma.LZMAError as error:
        raise DataError(f"{name}: not a valid LZMA file ({error})") from error
    if len(raw) % RECORD.size != 0:
        raise DataError(f"{name}: {len(raw)} bytes, not a multiple of {RECORD.size}")
    candles = []
    previous = -1
    for index, (sec, open_, close, low, high, volume) in enumerate(RECORD.iter_unpack(raw), start=1):
        problem = _record_problem(sec, previous, open_, high, low, close, volume, spec)
        if problem:
            raise DataError(f"{name} record {index}: {problem}")
        candles.append(Candle(sec, open_, high, low, close, volume))
        previous = sec
    return candles


def _record_problem(sec, previous, open_, high, low, close, volume, spec):
    if sec % 60 != 0 or sec >= 86400:
        return f"time {sec} s is not a whole minute below 86400"
    if sec <= previous:
        return f"time {sec} s is not increasing (previous {previous})"
    if low > min(open_, close):
        return f"low {low} above open {open_} or close {close}"
    if high < max(open_, close):
        return f"high {high} below open {open_} or close {close}"
    for price in (open_, high, low, close):
        if not spec.low <= price <= spec.high:
            return (f"price {price_text(price, spec)} outside the plausible range "
                    f"{price_text(spec.low, spec)} to {price_text(spec.high, spec)}")
    if not math.isfinite(volume) or volume < 0:
        return f"volume {volume} is not a finite number of at least 0"
    return None


# ------------------------------------------------------------------ New York time and the join

def _nth_sunday(year, month, n, hour):
    first = date(year, month, 1)
    day = 1 + (6 - first.weekday()) % 7 + 7 * (n - 1)
    return datetime(year, month, day, hour)


def is_summer_time(utc: datetime) -> bool:
    """New York summer time, as NewYorkClock: 07:00 UTC on the second Sunday of March to
    06:00 UTC on the first Sunday of November. Operating-system time-zone data is not used."""
    return _nth_sunday(utc.year, 3, 2, 7) <= utc < _nth_sunday(utc.year, 11, 1, 6)


def is_weekend(utc: datetime) -> bool:
    """True from Friday 17:00 to Sunday 17:00 New York."""
    ny = utc + timedelta(hours=-4 if is_summer_time(utc) else -5)
    weekday = ny.weekday()
    if weekday == 4:
        return ny.hour >= 17
    if weekday == 5:
        return True
    if weekday == 6:
        return ny.hour < 17
    return False


def new_counts():
    return {"minutes_kept": 0, "dropped_weekend": 0, "dropped_one_side": 0,
            "dropped_zero_volume": 0, "ask_below_bid": 0}


def join_day(day: date, bid, ask, counts):
    """Minutes of one UTC day that have both candles, bid ticks and lie outside the weekend.
    Rules are applied in this order: weekend, one side only, zero bid volume. A minute whose ask
    close is below its bid close is kept and counted."""
    bids = {c.sec: c for c in bid}
    asks = {c.sec: c for c in ask}
    start = datetime(day.year, day.month, day.day)
    kept = []
    for sec in sorted(set(bids) | set(asks)):
        moment = start + timedelta(seconds=sec)
        b, a = bids.get(sec), asks.get(sec)
        if is_weekend(moment):
            counts["dropped_weekend"] += 1
        elif b is None or a is None:
            counts["dropped_one_side"] += 1
        elif not b.volume > 0:
            counts["dropped_zero_volume"] += 1
        else:
            if a.close < b.close:
                counts["ask_below_bid"] += 1
            counts["minutes_kept"] += 1
            kept.append(Minute(moment, b, a))
    return kept


# ------------------------------------------------------------------ output formats

def price_text(points: int, spec: SymbolSpec) -> str:
    """Exact decimal text of an integer price in points; no floating-point formatting."""
    scale = 10 ** spec.decimals
    sign = "-" if points < 0 else ""
    whole, frac = divmod(abs(points), scale)
    return f"{sign}{whole}.{frac:0{spec.decimals}d}"


def volume_text(volume: float) -> str:
    """Shortest fixed-point text that reads back as the same 32-bit float."""
    target = struct.pack(">f", volume)
    for decimals in range(0, 13):
        text = f"{volume:.{decimals}f}"
        if struct.pack(">f", float(text)) == target:
            return text
    return repr(volume)


def minute_text(moment: datetime) -> str:
    return f"{moment.year:04d}-{moment.month:02d}-{moment.day:02d}T{moment.hour:02d}:{moment.minute:02d}Z"


def month_csv_gz(spec: SymbolSpec, minutes) -> bytes:
    lines = [CSV_HEADER]
    for m in minutes:
        prices = [price_text(p, spec) for p in (m.bid.open, m.bid.high, m.bid.low, m.bid.close,
                                                 m.ask.open, m.ask.high, m.ask.low, m.ask.close)]
        lines.append(",".join([minute_text(m.time)] + prices + [volume_text(m.bid.volume)]))
    text = "\n".join(lines) + "\n"
    buffer = io.BytesIO()
    with gzip.GzipFile(filename="", mode="wb", fileobj=buffer, mtime=0, compresslevel=9) as stream:
        stream.write(text.encode("ascii"))
    return buffer.getvalue()


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def csv_bytes(fields, rows) -> bytes:
    buffer = io.StringIO()
    writer = csv.writer(buffer, lineterminator="\n")
    writer.writerow(fields)
    for row in rows:
        writer.writerow([row[f] for f in fields])
    return buffer.getvalue().encode("ascii")


def read_csv(path):
    if not os.path.exists(path):
        return []
    with open(path, newline="", encoding="ascii") as f:
        return [dict(row) for row in csv.DictReader(f)]


def write_atomic(path, data: bytes):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    temp = path + ".part"
    with open(temp, "wb") as f:
        f.write(data)
    os.replace(temp, path)


def file_sha256(path):
    if not os.path.exists(path):
        return None
    with open(path, "rb") as f:
        return sha256(f.read())


# ------------------------------------------------------------------ building a symbol

def months(first_day: date, last_month):
    year, month = first_day.year, first_day.month
    while (year, month) <= tuple(last_month):
        yield year, month
        year, month = (year + 1, 1) if month == 12 else (year, month + 1)


def _days_of_month(year, month):
    day = date(year, month, 1)
    while day.month == month:
        yield day
        day += timedelta(days=1)


def _download_day(symbol, side, day, opener, sleep):
    url = url_for(symbol, side, day)
    return side, day, url, fetch(url, opener, sleep)


def _build_month(spec, year, month, opener, sleep, pool):
    days = list(_days_of_month(year, month))
    tasks = [(spec.name, side, day, opener, sleep) for day in days for side in SIDES]
    results = list(pool.map(lambda t: _download_day(*t), tasks))
    bodies = {(side, day): (url, body) for side, day, url, body in results}
    counts = new_counts()
    minutes, raw_rows, empty = [], [], []
    for day in days:
        decoded = {}
        for side in SIDES:
            url, body = bodies[(side, day)]
            name = url[len(BASE_URL) + 1:]
            decoded[side] = decode_candles(name, body or b"", spec)
            raw_rows.append({"symbol": spec.name, "day": day.isoformat(), "side": side,
                             "status": "missing" if body is None else ("empty" if not body else "ok"),
                             "bytes": "0" if body is None else str(len(body)),
                             "sha256": "" if body is None else sha256(body)})
        kept = join_day(day, decoded["BID"], decoded["ASK"], counts)
        minutes.extend(kept)
        if not kept and day.weekday() < 5 and (day.month, day.day) not in ((12, 25), (1, 1)):
            empty.append(day.isoformat())
    label = f"{year:04d}-{month:02d}"
    if len(empty) > MAX_WEEKDAYS_WITHOUT_DATA:
        raise DataError(f"{spec.name} {label}: {len(empty)} weekdays without data ({' '.join(empty)}); "
                        f"at most {MAX_WEEKDAYS_WITHOUT_DATA} are allowed")
    data = month_csv_gz(spec, minutes)
    row = {"symbol": spec.name, "month": label}
    row.update({k: str(v) for k, v in counts.items()})
    row.update({"weekdays_without_data": ";".join(empty),
                "first_minute": minute_text(minutes[0].time) if minutes else "",
                "last_minute": minute_text(minutes[-1].time) if minutes else "",
                "sha256": sha256(data)})
    return data, row, raw_rows


def build_symbol(symbol, first_day, last_month, out_dir, opener, sleep, workers=MAX_WORKERS, log=print):
    """Download and convert every month of one symbol; months already in out_dir whose file
    matches the recorded checksum are reused without a request. Returns the manifest rows and
    the raw manifest rows, in month order, and writes the symbol's own manifests."""
    spec = SYMBOLS[symbol]
    folder = os.path.join(out_dir, symbol)
    old_rows = {r["month"]: r for r in read_csv(os.path.join(folder, "manifest.csv"))}
    old_raw = {}
    for r in read_csv(os.path.join(folder, "raw-manifest.csv")):
        old_raw.setdefault(r["day"][:7], []).append(r)
    rows, raw_rows = [], []
    with ThreadPoolExecutor(max_workers=max(1, min(workers, MAX_WORKERS))) as pool:
        for year, month in months(first_day, last_month):
            label = f"{year:04d}-{month:02d}"
            path = os.path.join(folder, f"{label}.csv.gz")
            old = old_rows.get(label)
            if old and label in old_raw and file_sha256(path) == old["sha256"]:
                rows.append({f: old[f] for f in MANIFEST_FIELDS})
                raw_rows.extend({f: r[f] for f in RAW_FIELDS} for r in old_raw[label])
                log(f"{symbol} {label}: in cache, checksum matches")
                continue
            data, row, raw = _build_month(spec, year, month, opener, sleep, pool)
            write_atomic(path, data)
            rows.append(row)
            raw_rows.extend(raw)
            log(f"{symbol} {label}: {row['minutes_kept']} minutes kept, weekend {row['dropped_weekend']}, "
                f"one side {row['dropped_one_side']}, zero volume {row['dropped_zero_volume']}, "
                f"ask below bid {row['ask_below_bid']}, weekdays without data "
                f"[{row['weekdays_without_data']}]")
    write_atomic(os.path.join(folder, "manifest.csv"), csv_bytes(MANIFEST_FIELDS, rows))
    write_atomic(os.path.join(folder, "raw-manifest.csv"), csv_bytes(RAW_FIELDS, raw_rows))
    return rows, raw_rows


def write_manifests(out_dir, symbols):
    """Merge the symbols' own manifests into manifest.csv and raw-manifest.csv; returns the data id."""
    rows, raw_rows = [], []
    for symbol in sorted(symbols):
        folder = os.path.join(out_dir, symbol)
        own = read_csv(os.path.join(folder, "manifest.csv"))
        if not own:
            raise DataError(f"{symbol}: no manifest in {folder}")
        rows.extend(own)
        raw_rows.extend(read_csv(os.path.join(folder, "raw-manifest.csv")))
    rows.sort(key=lambda r: (r["symbol"], r["month"]))
    raw_rows.sort(key=lambda r: (r["symbol"], r["day"], r["side"]))
    manifest = csv_bytes(MANIFEST_FIELDS, rows)
    write_atomic(os.path.join(out_dir, "manifest.csv"), manifest)
    write_atomic(os.path.join(out_dir, "raw-manifest.csv"), csv_bytes(RAW_FIELDS, raw_rows))
    return sha256(manifest)


# ------------------------------------------------------------------ command line

def _parse_month(text):
    try:
        moment = datetime.strptime(text, "%Y-%m")
    except ValueError as error:
        raise DataError(f"--to-month {text!r} is not yyyy-mm") from error
    return moment.year, moment.month


def _parse_args(argv):
    parser = argparse.ArgumentParser(description="Download and convert Dukascopy one-minute candles (LAB-001).")
    parser.add_argument("--symbols", required=True, help="comma-separated, from " + ",".join(SYMBOLS))
    parser.add_argument("--from", dest="first", default="2019-03-01", help="first day, the first of a month")
    parser.add_argument("--to-month", default=None, help="last month yyyy-mm (default: the month before "
                                                         "the current UTC month)")
    parser.add_argument("--out", required=True, help="output folder")
    parser.add_argument("--merge-only", action="store_true",
                        help="only merge the symbols' manifests already in --out; no request")
    return parser.parse_args(argv)


def run(args, opener, sleep, now, log):
    symbols = [s.strip() for s in args.symbols.split(",") if s.strip()]
    unknown = [s for s in symbols if s not in SYMBOLS]
    if not symbols or unknown:
        raise DataError(f"unknown symbol(s) {','.join(unknown) or '(none given)'}; known: {','.join(SYMBOLS)}")
    if len(set(symbols)) != len(symbols):
        raise DataError("a symbol is named twice in --symbols")
    if not args.merge_only:
        try:
            first = datetime.strptime(args.first, "%Y-%m-%d").date()
        except ValueError as error:
            raise DataError(f"--from {args.first!r} is not yyyy-mm-dd") from error
        if first.day != 1:
            raise DataError(f"--from {args.first} must be the first day of a month")
        if args.to_month:
            last = _parse_month(args.to_month)
        else:
            moment = now()
            last = (moment.year - 1, 12) if moment.month == 1 else (moment.year, moment.month - 1)
        if last < (first.year, first.month):
            raise DataError(f"--to-month {last[0]:04d}-{last[1]:02d} is before --from {args.first}")
        for symbol in symbols:
            build_symbol(symbol, first, last, args.out, opener, sleep, log=log)
    data_id = write_manifests(args.out, symbols)
    log(f"data id {data_id}")


def main(argv=None, opener=urllib_opener, sleep=time.sleep, now=None, log=None):
    log = log or (lambda line: print(line, flush=True))
    if now is None:
        clock = lambda: datetime.now(timezone.utc).replace(tzinfo=None)  # only for the --to-month default
    else:
        clock = lambda: now
    args = _parse_args(sys.argv[1:] if argv is None else argv)
    started = time.monotonic()
    try:
        run(args, opener, sleep, clock, log)
    except DataError as error:
        log(f"error: {error}")
        return 1
    print(f"elapsed {time.monotonic() - started:.0f} s", file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main())
