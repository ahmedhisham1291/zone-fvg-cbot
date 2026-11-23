"""Rule families for the search. Every function returns orders for lib.sim.Market.run.

No look-ahead: a signal on a bar uses that bar's close and earlier bars only, values of a higher time
frame come from its last bar that was already closed, and the order becomes live at `act`, the first
minute after the signal bar.
"""
import numpy as np
import pandas as pd
from . import data

COLS = ["act", "side", "kind", "px", "exp", "sl", "tp", "tex", "risk"]


class Ctx:
    def __init__(self, m1):
        self.m1, self.t, self.n = m1, m1["t"], len(m1["t"])
        self.b = {tf: data.bars(m1, tf) for tf in ("m15", "h1", "h4", "d1")}
        for tf, b in self.b.items():
            b["act"] = b["e"] + 1
            b["atr"] = data.atr(b, 14)
        d = self.b["d1"]
        pre = data.d1_before(m1["sym"], int(d["t"][0]))       # the platform's D1 closes before the m1 data starts
        c = np.r_[pre, d["c"]]
        for n in (20, 50, 200):
            d[f"ema{n}"] = data.ema(c, n)[len(pre):]

    def closed(self, tf, act):
        """Index of the last bar of tf that is closed at m1 index act (-1 if none)."""
        return np.searchsorted(self.b[tf]["act"], act, side="right") - 1

    def at(self, act, minute_utc):
        """First m1 index at or after act whose UTC minute of day is minute_utc or later that day,
        else the first bar of a later day (used for timed exits)."""
        t = self.t
        a = np.minimum(np.asarray(act), self.n - 1)
        day = t[a] // 1440
        target = day * 1440 + minute_utc
        target = np.where(target <= t[a], target + 1440, target)
        return np.searchsorted(t, target)

    def friday(self, act):
        """m1 index of Friday 20:00 UTC on or after act (the bot closes positions there)."""
        t = self.t
        a = np.minimum(np.asarray(act), self.n - 1)
        T = t[a]
        wd = (T // 1440 + 3) % 7
        fri = (T // 1440 + (4 - wd) % 7) * 1440 + 1200
        fri = np.where(fri <= T, fri + 7 * 1440, fri)
        return np.searchsorted(t, fri)

    def session_end(self, act):
        """m1 index of 16:40 New York of the trading day that act belongs to: before gold's close (16:50)
        and before spreads widen into the 17:00 rollover."""
        a = np.minimum(np.asarray(act), self.n - 1)
        nyt = self.m1["nyt"]
        day_start = ((nyt[a] + 420) // 1440) * 1440 - 420      # 17:00 NY, start of this trading day
        return np.searchsorted(nyt, day_start + 1420)

    def trend(self, kind, di):
        """+1 / -1 / 0 from the last closed D1 bar di."""
        d = self.b["d1"]
        ok = di >= 0
        i = np.maximum(di, 0)
        if kind == "none":
            return np.zeros(len(di), dtype=int)
        if kind == "ema50":
            s = np.sign(d["c"][i] - d["ema50"][i])
        elif kind == "ema20_50":
            s = np.sign(d["ema20"][i] - d["ema50"][i])
        elif kind == "ema50_200":
            s = np.where((d["c"][i] > d["ema50"][i]) & (d["ema50"][i] > d["ema200"][i]), 1,
                         np.where((d["c"][i] < d["ema50"][i]) & (d["ema50"][i] < d["ema200"][i]), -1, 0))
        else:
            raise ValueError(kind)
        return np.where(ok, s, 0).astype(int)


def _orders(act, side, kind, px, exp, sl, tp, tex, risk, **extra):
    df = pd.DataFrame(dict(act=act, side=side, kind=kind, px=px, exp=exp, sl=sl, tp=tp, tex=tex, risk=risk, **extra))
    df = df[np.isfinite(df["risk"]) & (df["risk"] > 0) & np.isfinite(df["sl"])]
    return df.sort_values("act", kind="stable").reset_index(drop=True)


def _exit_index(ctx, act, mode):
    if mode == "fri":
        return ctx.friday(act)
    if mode == "eod":
        return np.minimum(ctx.session_end(act), ctx.friday(act))
    if mode[-1] in "dD":                           # "2d": two calendar days later at the session end, never past
        days = int(mode[:-1])                      # Friday 20:00; "10D": the same without the Friday close
        nyt = ctx.m1["nyt"]
        a = np.minimum(np.asarray(act), ctx.n - 1)
        end = ((nyt[a] + 420) // 1440 + days) * 1440 - 420 + 1420
        x = np.searchsorted(nyt, end)
        return np.minimum(x, ctx.friday(act)) if mode[-1] == "d" else x
    raise ValueError(mode)


def _first_per_day(mask, day):
    """Keep only the first True of each day."""
    idx = np.flatnonzero(mask)
    if len(idx) == 0:
        return idx
    d = day[idx]
    return idx[np.r_[True, d[1:] != d[:-1]]]


def breakout(ctx, n=1, w0=420, w1=1020, trend="ema50", stop_k=1.0, rr=2.0, hold="fri", tf="m15"):
    """Close beyond the high/low of the previous n trading days, first one of the day inside the UTC
    window [w0, w1] (minutes, bar close time). Market entry on the next minute; stop stop_k x ATR(H4)."""
    b, d = ctx.b[tf], ctx.b["d1"]
    di = ctx.closed("d1", b["act"])
    hh = np.r_[np.nan, data.rolling_max(d["h"], n)][di + 1]     # high of the last n closed days
    ll = np.r_[np.nan, data.rolling_min(d["l"], n)][di + 1]
    close_min = (b["t"] % 1440) + {"m15": 15, "h1": 60}[tf]
    inwin = (close_min >= w0) & (close_min <= w1)
    tr = ctx.trend(trend, di)
    hi = ctx.closed("h4", b["act"])
    atr = np.where(hi >= 0, ctx.b["h4"]["atr"][np.maximum(hi, 0)], np.nan)
    out = []
    for side in (1, -1):
        cond = inwin & ((b["c"] > hh) if side > 0 else (b["c"] < ll))
        if trend != "none":
            cond &= tr == side
        j = _first_per_day(cond, b["td"])
        risk = stop_k * atr[j]
        px = b["c"][j]
        act = b["act"][j]
        out.append(_orders(act, side, "M", np.nan, -1, px - side * risk, px + side * rr * risk if rr else np.nan,
                           _exit_index(ctx, act, hold), risk))
    return pd.concat(out).sort_values("act", kind="stable").reset_index(drop=True)


def opening_range(ctx, r0=0, r1=420, cutoff=720, stop="range", rr=1.0, hold="eod", trend="none", wmin=0.0, wmax=9.0, buf=0.0, clock="utc"):
    """Range of [r0, r1) minutes; at r1 a buy stop above and a sell stop below it, live until cutoff.
    Whichever is touched first is taken (one per day). stop: "range" (other side) or a number x ATR(H4).
    clock "utc": minutes of the UTC day. clock "td": minutes since 17:00 New York (start of the trading
    day), so London (03:00 NY = 600) and New York (09:30 NY = 990) sessions keep their place through DST."""
    m1 = ctx.m1
    h, l = m1["h"], m1["l"]
    t = m1["t"] if clock == "utc" else m1["nyt"] + 420
    days = np.unique(t // 1440)
    a0 = np.searchsorted(t, days * 1440 + r0)
    a1 = np.searchsorted(t, days * 1440 + r1)
    a2 = np.searchsorted(t, days * 1440 + cutoff)
    d = ctx.b["d1"]
    rows = []
    for k in range(len(days)):
        s, e, c = a0[k], a1[k], a2[k]
        if e - s < (r1 - r0) * 0.5 or c - e < 5 or e >= ctx.n:
            continue
        if (days[k] + 3) % 7 > 4:
            continue
        if e - s < 3:
            continue
        hi, lo = h[s:e].max(), l[s:e].min()
        w = hi - lo
        di = ctx.closed("d1", e)
        if di < 14 or w <= 0:
            continue
        datr = d["atr"][di]
        if not (wmin * datr <= w <= wmax * datr):
            continue
        tr = ctx.trend(trend, np.array([di]))[0]
        up, dn = hi + buf * w, lo - buf * w
        ku = np.flatnonzero(h[e:c] >= up)
        kd = np.flatnonzero(l[e:c] <= dn)
        fu = ku[0] if len(ku) else 10 ** 9
        fd = kd[0] if len(kd) else 10 ** 9
        if fu == fd:
            continue
        side = 1 if fu < fd else -1
        if trend != "none" and tr != side:
            continue
        px = up if side > 0 else dn
        if stop == "range":
            risk = (up - dn)
        else:
            hi4 = ctx.closed("h4", e)
            risk = float(stop) * ctx.b["h4"]["atr"][hi4] if hi4 >= 14 else np.nan
        rows.append((e, side, "S", px, c, px - side * risk, px + side * rr * risk if rr else np.nan, risk))
    if not rows:
        return pd.DataFrame(columns=COLS)
    act, side, kind, px, exp, sl, tp, risk = map(np.array, zip(*rows))
    return _orders(act, side, kind, px, exp, sl, tp, _exit_index(ctx, act, hold), risk)


def rsi(c, n):
    d = np.diff(c, prepend=c[0])
    up, dn = np.maximum(d, 0), np.maximum(-d, 0)
    au, ad = np.empty(len(c)), np.empty(len(c))
    au[0], ad[0] = up[0], dn[0]
    a = 1.0 / n
    for i in range(1, len(c)):
        au[i] = au[i - 1] + a * (up[i] - au[i - 1])
        ad[i] = ad[i - 1] + a * (dn[i] - ad[i - 1])
    return 100 - 100 / (1 + au / np.maximum(ad, 1e-12))


def pullback(ctx, tf="h1", trend="ema50", n=3, lo=15, stop_k=1.5, rr=1.5, hold="2d"):
    """With the D1 trend: RSI(n) of tf drops below lo (longs) or rises above 100 - lo (shorts). Market entry,
    stop stop_k x ATR(tf), target rr x risk, timed exit."""
    b = ctx.b[tf]
    r = rsi(b["c"], n)
    di = ctx.closed("d1", b["act"])
    tr = ctx.trend(trend, di)
    cross_dn = (r < lo) & (np.r_[100, r[:-1]] >= lo)
    cross_up = (r > 100 - lo) & (np.r_[0, r[:-1]] <= 100 - lo)
    out = []
    for side, cond in ((1, cross_dn), (-1, cross_up)):
        j = np.flatnonzero(cond & (tr == side) & np.isfinite(b["atr"]))
        risk = stop_k * b["atr"][j]
        px, act = b["c"][j], b["act"][j]
        out.append(_orders(act, side, "M", np.nan, -1, px - side * risk, px + side * rr * risk if rr else np.nan,
                           _exit_index(ctx, act, hold), risk))
    return pd.concat(out).sort_values("act", kind="stable").reset_index(drop=True)


def _next_true(cond):
    """For every index i: the smallest j > i with cond[j], or len(cond) if none."""
    n = len(cond)
    nxt = np.full(n, n)
    last = n
    for i in range(n - 1, -1, -1):
        nxt[i] = last
        if cond[i]:
            last = i
    return nxt


def fade(ctx, tf="h4", n=20, k=2.0, stop_k=2.0, rr=1.0, hold="2d", trend="none", direction="fade", exit="time"):
    """A stretch: close outside SMA(n) +/- k standard deviations of tf (first bar outside).
    direction "fade": market entry against the stretch (buy below the lower band). "follow": with it.
    Stop stop_k x ATR(tf), target rr x risk (0 = none), timed exit `hold`. exit "mid": also leave at the
    first close of tf back across SMA(n). trend: "none", or a D1 trend the trade must agree with."""
    b = ctx.b[tf]
    c = b["c"]
    ma = data.sma(c, n)
    sd = pd.Series(c).rolling(n).std(ddof=0).values
    z = (c - ma) / sd
    zp = np.r_[0, z[:-1]]
    di = ctx.closed("d1", b["act"])
    tr = ctx.trend(trend, di)
    sgn = 1 if direction == "fade" else -1
    out = []
    for band, cond in ((1, (z < -k) & (zp >= -k)), (-1, (z > k) & (zp <= k))):
        side = band * sgn
        if trend != "none":
            cond = cond & (tr == side)
        j = np.flatnonzero(cond & np.isfinite(b["atr"]))
        risk = stop_k * b["atr"][j]
        px, act = c[j], b["act"][j]
        tex = _exit_index(ctx, act, hold)
        if exit == "mid":
            back = _next_true((c >= ma) if side > 0 else (c <= ma))[j]
            tex = np.minimum(tex, np.r_[b["act"], ctx.n][np.minimum(back, len(c))])
        out.append(_orders(act, side, "M", np.nan, -1, px - side * risk, px + side * rr * risk if rr else np.nan, tex, risk))
    return pd.concat(out).sort_values("act", kind="stable").reset_index(drop=True)


def donchian(ctx, tf="d1", n=20, stop_k=2.0, rr=3.0, hold="fri"):
    """Close above the high of the previous n bars of tf (below the low for shorts). Market entry,
    stop stop_k x ATR(tf), target rr x risk (0 = none), exit at the timed exit."""
    b = ctx.b[tf]
    hh = np.r_[np.nan, data.rolling_max(b["h"], n)[:-1]]
    ll = np.r_[np.nan, data.rolling_min(b["l"], n)[:-1]]
    out = []
    for side, cond in ((1, b["c"] > hh), (-1, b["c"] < ll)):
        prev = np.r_[False, cond[:-1]]
        j = np.flatnonzero(cond & ~prev & np.isfinite(b["atr"]))
        risk = stop_k * b["atr"][j]
        px, act = b["c"][j], b["act"][j]
        out.append(_orders(act, side, "M", np.nan, -1, px - side * risk, px + side * rr * risk if rr else np.nan,
                           _exit_index(ctx, act, hold), risk))
    return pd.concat(out).sort_values("act", kind="stable").reset_index(drop=True)


def time_of_day(ctx, start=0, hold=240, side=1, risk_k=0.5, trend="none"):
    """Every weekday: market entry at `start` (UTC minute of day), exit `hold` minutes later.
    Stop risk_k x ATR(D1) away, no target."""
    t = ctx.t
    days = np.unique(t // 1440)
    days = days[(days + 3) % 7 <= 4]
    act = np.searchsorted(t, days * 1440 + start)
    ok = (act < ctx.n - 1)
    act = act[ok]
    ok2 = (t[act] - (t[act] // 1440) * 1440 - start) < 30     # the market was open at that time
    act = act[ok2]
    di = ctx.closed("d1", act)
    keep = di >= 14
    act, di = act[keep], di[keep]
    risk = risk_k * ctx.b["d1"]["atr"][di]
    px = ctx.m1["o"][act]
    s = np.full(len(act), side)
    if trend != "none":
        tr = ctx.trend(trend, di)
        m = tr == side
        act, risk, px, s = act[m], risk[m], px[m], s[m]
    tex = np.searchsorted(t, t[act] + hold)
    return _orders(act, s, "M", np.nan, -1, px - s * risk, np.nan, tex, risk)


def intraday_move(ctx, check=780, k=0.5, mode="with", risk_k=0.5, rr=0.0, hold="eod"):
    """At `check` (UTC minute): if price has moved more than k x ATR(D1) since the start of the trading day,
    enter with the move ("with") or against it ("fade"). Stop risk_k x ATR(D1), exit at the session end."""
    t, m1 = ctx.t, ctx.m1
    days = np.unique(t // 1440)
    days = days[(days + 3) % 7 <= 4]
    act = np.searchsorted(t, days * 1440 + check)
    act = act[act < ctx.n - 1]
    act = act[(t[act] % 1440) - check < 30]
    d = ctx.b["d1"]
    cur = np.searchsorted(d["key"], m1["td"][act])            # the trading day in progress
    di = ctx.closed("d1", act)
    keep = (di >= 14) & (cur < len(d["o"]))
    act, cur, di = act[keep], cur[keep], di[keep]
    move = m1["o"][act] - d["o"][cur]
    atr = d["atr"][di]
    sig = np.where(move > k * atr, 1, np.where(move < -k * atr, -1, 0))
    if mode == "fade":
        sig = -sig
    m = sig != 0
    act, s, atr = act[m], sig[m], atr[m]
    px = m1["o"][act]
    risk = risk_k * atr
    return _orders(act, s, "M", np.nan, -1, px - s * risk, px + s * rr * risk if rr else np.nan, _exit_index(ctx, act, hold), risk)
