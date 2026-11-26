"""Unit tests of the Dukascopy downloader and converter (LAB-001, PLT-019).

Run from lab/data with `python3 -I -m unittest -v`. No test needs the network: the opener and the
sleep function are injected, and every file is built in the test.
"""
from __future__ import annotations

import gzip
import hashlib
import io
import lzma
import os
import struct
import tempfile
import unittest
from datetime import date, datetime

import dukascopy as dk


def record(sec, open_, close, low, high, volume):
    """One 24-byte record in Dukascopy's candle order: time, open, close, low, high, volume."""
    return struct.pack(">I4if", sec, open_, close, low, high, volume)


def bi5(*records):
    return lzma.compress(b"".join(records), format=lzma.FORMAT_ALONE)


def candle(sec, open_, high, low, close, volume=1.0):
    return dk.Candle(sec, open_, high, low, close, volume)


class FakeServer:
    """Answers the downloader's requests from a table; records every URL it is asked for."""

    def __init__(self, default=None):
        self.bodies = {}
        self.statuses = {}
        self.calls = []
        self.default = default

    def __call__(self, url, timeout):
        self.calls.append(url)
        if url in self.statuses:
            status = self.statuses[url].pop(0)
            if isinstance(status, Exception):
                raise status
            return status, self.bodies.get(url, b"")
        if url in self.bodies:
            return 200, self.bodies[url]
        if self.default is not None:
            return 200, self.default(url)
        return 404, b""


def weekday_noon(url):
    """Every weekday file holds one minute at 12:00 UTC with ticks; weekend files are empty."""
    parts = url.split("/")
    day = date(int(parts[-4]), int(parts[-3]) + 1, int(parts[-2]))
    if day.weekday() >= 5:
        return b""
    ask = parts[-1].startswith("ASK")
    base = 110010 if ask else 110000
    return bi5(record(12 * 3600, base, base + 5, base - 5, base + 10, 2.5))


class NoSleep:
    def __init__(self):
        self.delays = []

    def __call__(self, seconds):
        self.delays.append(seconds)


class UrlTests(unittest.TestCase):
    def test_url_january_is_month_zero(self):
        self.assertEqual(
            dk.url_for("EURUSD", "BID", date(2020, 1, 5)),
            "https://datafeed.dukascopy.com/datafeed/EURUSD/2020/00/05/BID_candles_min_1.bi5")

    def test_url_december_is_month_eleven(self):
        self.assertEqual(
            dk.url_for("XAUUSD", "ASK", date(2024, 12, 31)),
            "https://datafeed.dukascopy.com/datafeed/XAUUSD/2024/11/31/ASK_candles_min_1.bi5")


class DecodeTests(unittest.TestCase):
    def test_decode_eurusd_record_gives_prices_in_candle_order(self):
        spec = dk.SYMBOLS["EURUSD"]
        candles = dk.decode_candles("f", bi5(record(60, 110000, 110020, 109990, 110030, 1.5)), spec)
        self.assertEqual(len(candles), 1)
        c = candles[0]
        self.assertEqual(c.sec, 60)
        self.assertEqual(dk.minute_text(datetime(2020, 1, 6) + dk.timedelta(seconds=c.sec)), "2020-01-06T00:01Z")
        self.assertEqual(dk.price_text(c.open, spec), "1.10000")
        self.assertEqual(dk.price_text(c.close, spec), "1.10020")
        self.assertEqual(dk.price_text(c.low, spec), "1.09990")
        self.assertEqual(dk.price_text(c.high, spec), "1.10030")
        self.assertEqual(c.volume, 1.5)

    def test_decode_usdjpy_record_uses_point_of_one_thousandth(self):
        spec = dk.SYMBOLS["USDJPY"]
        c = dk.decode_candles("f", bi5(record(0, 110500, 110520, 110490, 110530, 2.0)), spec)[0]
        self.assertEqual([dk.price_text(p, spec) for p in (c.open, c.close, c.low, c.high)],
                         ["110.500", "110.520", "110.490", "110.530"])

    def test_decode_empty_body_is_day_without_data(self):
        self.assertEqual(dk.decode_candles("f", b"", dk.SYMBOLS["EURUSD"]), [])

    def test_decode_empty_lzma_content_is_day_without_data(self):
        self.assertEqual(dk.decode_candles("f", bi5(), dk.SYMBOLS["EURUSD"]), [])

    def assert_refused(self, data, symbol, *words):
        with self.assertRaises(dk.DataError) as ctx:
            dk.decode_candles("EURUSD/2020/00/06/BID_candles_min_1.bi5", data, dk.SYMBOLS[symbol])
        message = str(ctx.exception)
        self.assertIn("EURUSD/2020/00/06/BID_candles_min_1.bi5", message)
        for word in words:
            self.assertIn(word, message)

    def test_decode_length_not_multiple_of_24_is_refused(self):
        self.assert_refused(lzma.compress(b"x" * 23, format=lzma.FORMAT_ALONE), "EURUSD", "23 bytes")

    def test_decode_low_above_open_is_refused(self):
        good = record(0, 110000, 110020, 109990, 110030, 1.0)
        bad = record(60, 110000, 110020, 110001, 110030, 1.0)
        self.assert_refused(bi5(good, bad), "EURUSD", "record 2", "low")

    def test_decode_high_below_close_is_refused(self):
        self.assert_refused(bi5(record(0, 110000, 110020, 109990, 110019, 1.0)), "EURUSD", "record 1", "high")

    def test_decode_time_of_full_day_is_refused(self):
        self.assert_refused(bi5(record(86400, 110000, 110020, 109990, 110030, 1.0)), "EURUSD", "record 1", "time")

    def test_decode_time_not_whole_minute_is_refused(self):
        self.assert_refused(bi5(record(61, 110000, 110020, 109990, 110030, 1.0)), "EURUSD", "record 1", "time")

    def test_decode_times_not_increasing_are_refused(self):
        a = record(120, 110000, 110020, 109990, 110030, 1.0)
        b = record(120, 110000, 110020, 109990, 110030, 1.0)
        self.assert_refused(bi5(a, b), "EURUSD", "record 2", "increasing")

    def test_decode_eurusd_above_plausible_range_is_refused(self):
        self.assert_refused(bi5(record(0, 150000, 150000, 150000, 150000, 1.0)), "EURUSD", "record 1", "range")

    def test_decode_xauusd_below_plausible_range_is_refused(self):
        self.assert_refused(bi5(record(0, 900000, 900000, 900000, 900000, 1.0)), "XAUUSD", "record 1", "range")

    def test_decode_eurusd_at_range_edges_is_accepted(self):
        spec = dk.SYMBOLS["EURUSD"]
        self.assertEqual(len(dk.decode_candles("f", bi5(record(0, 80000, 140000, 80000, 140000, 1.0)), spec)), 1)

    def test_decode_corrupt_lzma_is_refused(self):
        self.assert_refused(b"not lzma at all", "EURUSD", "LZMA")


class FetchTests(unittest.TestCase):
    URL = "https://datafeed.dukascopy.com/datafeed/EURUSD/2020/00/06/BID_candles_min_1.bi5"

    def test_fetch_404_is_day_without_data(self):
        server, sleep = FakeServer(), NoSleep()
        self.assertIsNone(dk.fetch(self.URL, server, sleep))
        self.assertEqual(len(server.calls), 1)
        self.assertEqual(sleep.delays, [])

    def test_fetch_empty_body_is_returned_as_empty(self):
        server = FakeServer()
        server.bodies[self.URL] = b""
        self.assertEqual(dk.fetch(self.URL, server, NoSleep()), b"")

    def test_fetch_500_then_200_succeeds_after_one_retry(self):
        server, sleep = FakeServer(), NoSleep()
        server.statuses[self.URL] = [500, 200]
        server.bodies[self.URL] = b"body"
        self.assertEqual(dk.fetch(self.URL, server, sleep), b"body")
        self.assertEqual(len(server.calls), 2)
        self.assertEqual(sleep.delays, [2])

    def test_fetch_429_and_connection_error_are_retried(self):
        server, sleep = FakeServer(), NoSleep()
        server.statuses[self.URL] = [429, dk.TransientError("timed out"), 200]
        server.bodies[self.URL] = b"ok"
        self.assertEqual(dk.fetch(self.URL, server, sleep), b"ok")
        self.assertEqual(sleep.delays, [2, 4])

    def test_fetch_six_500s_fail_the_run(self):
        server, sleep = FakeServer(), NoSleep()
        server.statuses[self.URL] = [500] * 6
        with self.assertRaises(dk.DataError) as ctx:
            dk.fetch(self.URL, server, sleep)
        self.assertIn(self.URL, str(ctx.exception))
        self.assertEqual(len(server.calls), 6)
        self.assertEqual(sleep.delays, [2, 4, 8, 16, 32])

    def test_fetch_403_fails_at_once(self):
        server, sleep = FakeServer(), NoSleep()
        server.statuses[self.URL] = [403]
        with self.assertRaises(dk.DataError) as ctx:
            dk.fetch(self.URL, server, sleep)
        self.assertIn("403", str(ctx.exception))
        self.assertEqual(len(server.calls), 1)
        self.assertEqual(sleep.delays, [])


class JoinTests(unittest.TestCase):
    def join(self, day, bid, ask):
        counts = dk.new_counts()
        return dk.join_day(day, bid, ask, counts), counts

    def test_join_bid_volume_zero_is_dropped_and_counted(self):
        bid = [candle(600, 110000, 110000, 110000, 110000, 0.0), candle(660, 110000, 110010, 109990, 110005, 1.0)]
        ask = [candle(600, 110001, 110001, 110001, 110001, 0.0), candle(660, 110001, 110011, 109991, 110006, 1.0)]
        rows, counts = self.join(date(2021, 3, 3), bid, ask)
        self.assertEqual([r.time for r in rows], [datetime(2021, 3, 3, 0, 11)])
        self.assertEqual(counts["dropped_zero_volume"], 1)

    def test_join_minute_with_only_bid_is_dropped_and_counted(self):
        bid = [candle(600, 110000, 110010, 109990, 110005), candle(660, 110000, 110010, 109990, 110005)]
        ask = [candle(660, 110001, 110011, 109991, 110006)]
        rows, counts = self.join(date(2021, 3, 3), bid, ask)
        self.assertEqual(len(rows), 1)
        self.assertEqual(counts["dropped_one_side"], 1)

    def test_join_minute_with_only_ask_is_dropped_and_counted(self):
        rows, counts = self.join(date(2021, 3, 3), [], [candle(660, 110001, 110011, 109991, 110006)])
        self.assertEqual(rows, [])
        self.assertEqual(counts["dropped_one_side"], 1)

    def both(self, secs):
        bid = [candle(s, 110000, 110010, 109990, 110005) for s in secs]
        ask = [candle(s, 110001, 110011, 109991, 110006) for s in secs]
        return bid, ask

    def test_join_friday_17_00_new_york_winter_is_weekend(self):
        bid, ask = self.both([21 * 3600 + 59 * 60, 22 * 3600])
        rows, counts = self.join(date(2021, 3, 5), bid, ask)
        self.assertEqual([r.time for r in rows], [datetime(2021, 3, 5, 21, 59)])
        self.assertEqual(counts["dropped_weekend"], 1)

    def test_join_sunday_17_00_new_york_summer_is_kept(self):
        bid, ask = self.both([20 * 3600 + 59 * 60, 21 * 3600])
        rows, counts = self.join(date(2021, 3, 14), bid, ask)
        self.assertEqual([r.time for r in rows], [datetime(2021, 3, 14, 21, 0)])
        self.assertEqual(counts["dropped_weekend"], 1)

    def test_join_saturday_is_weekend(self):
        bid, ask = self.both([12 * 3600])
        rows, counts = self.join(date(2021, 3, 6), bid, ask)
        self.assertEqual(rows, [])
        self.assertEqual(counts["dropped_weekend"], 1)

    def test_join_ask_close_below_bid_close_is_counted_and_kept(self):
        bid = [candle(600, 110000, 110010, 109990, 110005)]
        ask = [candle(600, 110001, 110011, 109991, 110004)]
        rows, counts = self.join(date(2021, 3, 3), bid, ask)
        self.assertEqual(len(rows), 1)
        self.assertEqual(counts["ask_below_bid"], 1)

    def test_new_york_summer_switches_follow_us_rule(self):
        self.assertFalse(dk.is_summer_time(datetime(2021, 3, 14, 6, 59)))
        self.assertTrue(dk.is_summer_time(datetime(2021, 3, 14, 7, 0)))
        self.assertTrue(dk.is_summer_time(datetime(2021, 11, 7, 5, 59)))
        self.assertFalse(dk.is_summer_time(datetime(2021, 11, 7, 6, 0)))


class MonthFileTests(unittest.TestCase):
    def three_minutes(self):
        spec = dk.SYMBOLS["EURUSD"]
        rows = []
        for i in range(3):
            bid = candle(0, 110000 + i, 110010 + i, 109990 + i, 110005 + i, 1.5)
            ask = candle(0, 110002 + i, 110012 + i, 109992 + i, 110007 + i, 0.25)
            rows.append(dk.Minute(datetime(2021, 2, 1, 0, i), bid, ask))
        return spec, rows

    def test_month_file_written_twice_has_same_bytes_and_checksum(self):
        spec, rows = self.three_minutes()
        first = dk.month_csv_gz(spec, rows)
        second = dk.month_csv_gz(spec, rows)
        self.assertEqual(first, second)
        self.assertEqual(hashlib.sha256(first).hexdigest(), hashlib.sha256(second).hexdigest())

    def test_month_file_has_fixed_gzip_header_and_exact_text(self):
        spec, rows = self.three_minutes()
        data = dk.month_csv_gz(spec, rows)
        self.assertEqual(data[:3], b"\x1f\x8b\x08")
        self.assertEqual(data[3], 0, "no file name or other optional header field")
        self.assertEqual(data[4:8], b"\x00\x00\x00\x00", "mtime 0")
        text = gzip.decompress(data).decode("ascii")
        self.assertEqual(text, (
            "time_utc,bid_open,bid_high,bid_low,bid_close,ask_open,ask_high,ask_low,ask_close,volume\n"
            "2021-02-01T00:00Z,1.10000,1.10010,1.09990,1.10005,1.10002,1.10012,1.09992,1.10007,1.5\n"
            "2021-02-01T00:01Z,1.10001,1.10011,1.09991,1.10006,1.10003,1.10013,1.09993,1.10008,1.5\n"
            "2021-02-01T00:02Z,1.10002,1.10012,1.09992,1.10007,1.10004,1.10014,1.09994,1.10009,1.5\n"))

    def test_volume_text_is_shortest_float32_round_trip(self):
        self.assertEqual(dk.volume_text(1.5), "1.5")
        self.assertEqual(dk.volume_text(2.0), "2")
        f32 = struct.unpack(">f", struct.pack(">f", 0.1))[0]
        self.assertEqual(dk.volume_text(f32), "0.1")


class BuildTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.out = self.tmp.name

    def tearDown(self):
        self.tmp.cleanup()

    def build(self, server, first=date(2021, 2, 1), last=(2021, 2), symbol="EURUSD", workers=4):
        return dk.build_symbol(symbol, first, last, self.out, server, NoSleep(), workers=workers, log=lambda s: None)

    def empty_days(self, server, symbol, days):
        for d in days:
            for side in ("BID", "ASK"):
                server.bodies[dk.url_for(symbol, side, d)] = b""

    def test_build_manifest_lists_counts_and_checksum(self):
        server = FakeServer(default=weekday_noon)
        self.empty_days(server, "EURUSD", [date(2021, 2, 3), date(2021, 2, 10)])
        rows, raw = self.build(server)
        self.assertEqual(len(rows), 1)
        row = rows[0]
        path = os.path.join(self.out, "EURUSD", "2021-02.csv.gz")
        with open(path, "rb") as f:
            digest = hashlib.sha256(f.read()).hexdigest()
        self.assertEqual(row["symbol"], "EURUSD")
        self.assertEqual(row["month"], "2021-02")
        self.assertEqual(row["minutes_kept"], "18")
        self.assertEqual(row["weekdays_without_data"], "2021-02-03;2021-02-10")
        self.assertEqual(row["first_minute"], "2021-02-01T12:00Z")
        self.assertEqual(row["last_minute"], "2021-02-26T12:00Z")
        self.assertEqual(row["sha256"], digest)
        self.assertEqual(len(raw), 56)
        self.assertEqual(raw[0]["day"], "2021-02-01")
        self.assertEqual(raw[0]["side"], "ASK")
        self.assertEqual(raw[1]["side"], "BID")

    def test_build_month_with_three_weekdays_without_data_fails(self):
        server = FakeServer(default=weekday_noon)
        self.empty_days(server, "EURUSD", [date(2021, 2, 3), date(2021, 2, 10), date(2021, 2, 17)])
        with self.assertRaises(dk.DataError) as ctx:
            self.build(server)
        self.assertIn("EURUSD 2021-02", str(ctx.exception))
        self.assertIn("3 weekdays without data", str(ctx.exception))

    def test_build_christmas_and_new_year_do_not_count_as_missing(self):
        server = FakeServer(default=weekday_noon)
        self.empty_days(server, "EURUSD", [date(2020, 12, 25), date(2020, 12, 28), date(2020, 12, 29),
                                           date(2021, 1, 1)])
        rows, _ = self.build(server, first=date(2020, 12, 1), last=(2021, 1))
        self.assertEqual(rows[0]["weekdays_without_data"], "2020-12-28;2020-12-29")
        self.assertEqual(rows[1]["weekdays_without_data"], "")

    def test_build_month_in_cache_with_matching_checksum_is_not_requested(self):
        first_rows, first_raw = self.build(FakeServer(default=weekday_noon))
        server = FakeServer(default=weekday_noon)
        rows, raw = self.build(server)
        self.assertEqual(server.calls, [])
        self.assertEqual(rows, first_rows)
        self.assertEqual(raw, first_raw)

    def test_build_month_in_cache_with_wrong_checksum_is_requested_again(self):
        self.build(FakeServer(default=weekday_noon))
        with open(os.path.join(self.out, "EURUSD", "2021-02.csv.gz"), "ab") as f:
            f.write(b"x")
        server = FakeServer(default=weekday_noon)
        self.build(server)
        self.assertEqual(len(server.calls), 56)

    def test_build_is_the_same_whatever_the_number_of_workers(self):
        server = FakeServer(default=weekday_noon)
        rows4, raw4 = self.build(server, workers=4)
        other = tempfile.TemporaryDirectory()
        try:
            rows1, raw1 = dk.build_symbol("EURUSD", date(2021, 2, 1), (2021, 2), other.name,
                                          FakeServer(default=weekday_noon), NoSleep(), workers=1, log=lambda s: None)
        finally:
            other.cleanup()
        self.assertEqual(rows4, rows1)
        self.assertEqual(raw4, raw1)


class CommandLineTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.out = self.tmp.name

    def tearDown(self):
        self.tmp.cleanup()

    def run_main(self, argv, server=None, now=datetime(2021, 3, 26, 10, 0)):
        lines = []
        code = dk.main(argv, opener=server or FakeServer(default=weekday_noon), sleep=NoSleep(), now=now,
                       log=lines.append)
        return code, lines

    def test_main_prints_data_id_of_manifest_on_last_line(self):
        code, lines = self.run_main(["--symbols", "EURUSD,USDJPY", "--from", "2021-02-01",
                                     "--to-month", "2021-02", "--out", self.out])
        self.assertEqual(code, 0)
        with open(os.path.join(self.out, "manifest.csv"), "rb") as f:
            manifest = f.read()
        self.assertEqual(lines[-1], "data id " + hashlib.sha256(manifest).hexdigest())
        text = manifest.decode("ascii").splitlines()
        self.assertEqual(text[0], ",".join(dk.MANIFEST_FIELDS))
        self.assertEqual([t.split(",")[0] for t in text[1:]], ["EURUSD", "USDJPY"])
        self.assertTrue(os.path.exists(os.path.join(self.out, "raw-manifest.csv")))

    def test_main_to_month_defaults_to_month_before_current_utc_month(self):
        code, _ = self.run_main(["--symbols", "EURUSD", "--from", "2021-02-01", "--out", self.out])
        self.assertEqual(code, 0)
        self.assertTrue(os.path.exists(os.path.join(self.out, "EURUSD", "2021-02.csv.gz")))
        self.assertFalse(os.path.exists(os.path.join(self.out, "EURUSD", "2021-03.csv.gz")))

    def test_main_merge_only_makes_no_request(self):
        self.run_main(["--symbols", "EURUSD", "--from", "2021-02-01", "--to-month", "2021-02", "--out", self.out])
        server = FakeServer(default=weekday_noon)
        code, lines = self.run_main(["--symbols", "EURUSD", "--merge-only", "--out", self.out], server=server)
        self.assertEqual(code, 0)
        self.assertEqual(server.calls, [])
        self.assertTrue(lines[-1].startswith("data id "))

    def test_main_refuses_unknown_symbol(self):
        code, lines = self.run_main(["--symbols", "EURCHF", "--from", "2021-02-01", "--to-month", "2021-02",
                                     "--out", self.out])
        self.assertEqual(code, 1)
        self.assertIn("EURCHF", lines[-1])

    def test_main_refuses_start_not_on_first_day_of_month(self):
        code, lines = self.run_main(["--symbols", "EURUSD", "--from", "2021-02-02", "--to-month", "2021-02",
                                     "--out", self.out])
        self.assertEqual(code, 1)
        self.assertIn("first day of a month", lines[-1])

    def test_main_refuses_last_month_before_start(self):
        code, lines = self.run_main(["--symbols", "EURUSD", "--from", "2021-02-01", "--to-month", "2021-01",
                                     "--out", self.out])
        self.assertEqual(code, 1)
        self.assertIn("--to-month", lines[-1])

    def test_main_reports_data_error_with_exit_code_one(self):
        server = FakeServer(default=weekday_noon)
        server.statuses[dk.url_for("EURUSD", "BID", date(2021, 2, 1))] = [403]
        code, lines = self.run_main(["--symbols", "EURUSD", "--from", "2021-02-01", "--to-month", "2021-02",
                                     "--out", self.out], server=server)
        self.assertEqual(code, 1)
        self.assertIn("403", lines[-1])


if __name__ == "__main__":
    unittest.main()
