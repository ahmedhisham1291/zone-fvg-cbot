"""Price data for the strategy search: m1 arrays from the ExportBars files, and bars built from them."""
import os
import numpy as np
import pandas as pd

_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
RAW = os.environ.get("EXPORTBARS_DIR", os.path.join(_ROOT, "data"))      # folder with the ExportBars_* files
CACHE = os.path.join(_ROOT, "cache")
SYMBOLS = ("EURUSD", "GBPUSD", "USDJPY", "AUDUSD", "XAUUSD")


def load_m1(sym):
    """t: minutes since epoch (UTC), nyt: the same in New York wall time, o h l c: bid prices, v: tick volume."""
    os.makedirs(CACHE, exist_ok=True)
    path = f"{CACHE}/{sym}_m1.npz"
    if not os.path.exists(path):
        df = pd.read_csv(f"{RAW}/ExportBars_{sym}_Minute.csv.gz")
        ts = pd.to_datetime(df["time_utc"], format="%Y-%m-%d %H:%M")
        t = ts.values.astype("datetime64[m]").astype(np.int64)
        ny = pd.DatetimeIndex(ts).tz_localize("UTC").tz_convert("America/New_York").tz_localize(None)
        nyt = ny.values.astype("datetime64[m]").astype(np.int64)
        np.savez(path, t=t, nyt=nyt, o=df["open"].values, h=df["high"].values, l=df["low"].values,
                 c=df["close"].values, v=df["tick_volume"].values.astype(np.int32))
    z = np.load(path)
    m1 = {k: z[k] for k in z.files}
    m1["td"] = (m1["nyt"] + 420) // 1440          # trading day (days since epoch); a day starts at 17:00 New York
    m1["sym"] = sym
    return m1


def info(sym):
    out = {}
    for part in open(f"{RAW}/ExportBars_{sym}_info.txt").read().split():
        if "=" in part:
            k, v = part.split("=", 1)
            out[k] = v
    return out


def key(m1, tf):
    """Group key per m1 bar. D1 and H4 follow the platform: they are anchored to 17:00 New York."""
    t, nyt = m1["t"], m1["nyt"]
    if tf == "d1":
        return m1["td"]
    if tf == "h4":
        return (nyt + 420) // 240
    return t // {"m5": 5, "m15": 15, "m30": 30, "h1": 60}[tf]


def bars(m1, tf):
    """Bars of a time frame built from m1. s/e: first and last m1 index of the bar; a bar is known
    to be closed at m1 index e + 1 (the first minute after it)."""
    k = key(m1, tf)
    s = np.flatnonzero(np.r_[True, k[1:] != k[:-1]])
    e = np.r_[s[1:] - 1, len(k) - 1]
    return dict(key=k[s], s=s, e=e, t=m1["t"][s], nyt=m1["nyt"][s], td=m1["td"][s],
                o=m1["o"][s], c=m1["c"][e],
                h=np.maximum.reduceat(m1["h"], s), l=np.minimum.reduceat(m1["l"], s),
                v=np.add.reduceat(m1["v"].astype(np.int64), s))


def ema(x, n):
    """Plain EMA, alpha = 2 / (n + 1), seeded with the first value. NaN-free input."""
    a = 2.0 / (n + 1.0)
    out = np.empty(len(x))
    out[0] = x[0]
    for i in range(1, len(x)):
        out[i] = a * x[i] + (1 - a) * out[i - 1]
    return out


def sma(x, n):
    c = np.cumsum(np.r_[0.0, x])
    out = np.full(len(x), np.nan)
    out[n - 1:] = (c[n:] - c[:-n]) / n
    return out


def atr(b, n=14):
    """Average true range, simple mean of the last n true ranges (NaN until n bars exist)."""
    h, l, c = b["h"], b["l"], b["c"]
    pc = np.r_[c[0], c[:-1]]
    tr = np.maximum(h - l, np.maximum(np.abs(h - pc), np.abs(l - pc)))
    return sma(tr, n)


def rolling_max(x, n):
    return pd.Series(x).rolling(n).max().values


def rolling_min(x, n):
    return pd.Series(x).rolling(n).min().values


def dt(minutes):
    return pd.to_datetime(np.asarray(minutes, dtype=np.int64), unit="m")


def d1_before(sym, first_minute):
    """D1 closes the platform reported before the first m1 bar (used only to warm up the D1 averages, as
    the bot itself has that history). Weekend stubs (bars that open on a Saturday) are left out."""
    df = pd.read_csv(f"{RAW}/ExportBars_{sym}_Daily.csv.gz")
    ts = pd.to_datetime(df["time_utc"], format="%Y-%m-%d %H:%M")
    keep = (ts.values.astype("datetime64[m]").astype(np.int64) < first_minute - 1440) & (ts.dt.dayofweek != 5).values
    return df["close"].values[keep]
