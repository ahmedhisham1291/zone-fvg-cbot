"""Trade simulator on m1 bars.

Prices in the data are bid. A buy fills at ask = bid + spread and leaves at bid; a sell fills at bid and
leaves at ask. Two tick models:
  ohlc  a level is hit when the m1 high or low reaches it (used for the search)
  open  only the m1 open counts as a tick, like cTrader's "m1 bars (open prices)" backtest (used to check
        the simulator against the bot's own backtest logs)
One position at a time per symbol, like the bot. If stop and target fall in the same minute, the stop wins.
"""
import numpy as np
import pandas as pd

WEEK_W = np.array([1, 1, 3, 1, 1, 0, 0])  # swap nights charged at the end of Mon..Sun trading days (Wed = 3)


def _first(fn, a, b):
    n, i = 256, a
    while i < b:
        j = min(b, i + n)
        m = fn(i, j)
        k = int(np.argmax(m))
        if m[k]:
            return i + k
        i, n = j, min(n * 2, 1 << 16)
    return -1


class Market:
    def __init__(self, m1, spread, comm_rt=0.0, swap_long=0.0, swap_short=0.0, slip=0.0, mode="ohlc", wide=None, comm_rate=0.0):
        """spread, comm_rt (round trip), swaps (per night, negative = cost) and slip are in price units per
        unit traded. wide: optional (from_ny_minute, to_ny_minute, factor) to widen the spread around rollover."""
        self.m1, self.mode = m1, mode
        o = m1["o"]
        sp = np.full(len(o), float(spread))
        if wide is not None:
            mod = m1["nyt"] % 1440
            sp[(mod >= wide[0]) & (mod < wide[1])] *= wide[2]
        self.sp = sp
        self.o = o
        self.ao = o + sp
        if mode == "open":
            self.h = self.l = o
            self.ah = self.al = self.ao
        else:
            self.h, self.l = m1["h"], m1["l"]
            self.ah, self.al = self.h + sp, self.l + sp
        self.comm, self.swl, self.sws, self.slip = comm_rt, swap_long, swap_short, slip
        self.comm_rate = comm_rate               # round-trip commission as a fraction of the entry price
        td = m1["td"]
        d0, d1 = int(td.min()), int(td.max()) + 2
        days = np.arange(d0, d1)
        w = WEEK_W[(days + 3) % 7]            # 1970-01-01 was a Thursday -> (d + 3) % 7 gives Mon = 0
        self.cw, self.d0 = np.r_[0, np.cumsum(w)], d0
        self.n = len(o)

    def nights(self, k, x):
        td = self.m1["td"]
        return int(self.cw[td[x] - self.d0] - self.cw[td[k] - self.d0])

    def run(self, orders, one_at_a_time=True, excursions=False):
        """orders: DataFrame sorted by act with columns act, side, kind (M/S/L), px, exp, sl, tp, tex, risk
        and any others (carried through). Returns a DataFrame of trades."""
        o, ao, h, l, ah, al = self.o, self.ao, self.h, self.l, self.ah, self.al
        N, slip, ohlc = self.n, self.slip, self.mode == "ohlc"
        t = self.m1["t"]
        out, busy = [], 0
        extra = [c for c in orders.columns if c not in ("act", "side", "kind", "px", "exp", "sl", "tp", "tex", "risk")]
        for od in orders.itertuples(index=False):
            a = int(od.act)
            if a >= N or (one_at_a_time and a < busy):
                continue
            side, kind, px, sl, tp = int(od.side), od.kind, od.px, od.sl, od.tp
            exp = min(int(od.exp), N) if od.exp >= 0 else N
            tex = min(int(od.tex), N) if od.tex >= 0 else N
            # ---- entry
            if kind == "M":
                k, epx = a, (ao[a] if side > 0 else o[a])
            elif kind == "S":
                if side > 0:
                    k = _first(lambda i, j: ah[i:j] >= px, a, exp)
                    epx = max(px, ao[k]) + slip if k >= 0 else np.nan
                else:
                    k = _first(lambda i, j: l[i:j] <= px, a, exp)
                    epx = min(px, o[k]) - slip if k >= 0 else np.nan
            else:
                if side > 0:
                    k = _first(lambda i, j: al[i:j] <= px, a, exp)
                    epx = min(px, ao[k]) if k >= 0 else np.nan
                else:
                    k = _first(lambda i, j: h[i:j] >= px, a, exp)
                    epx = max(px, o[k]) if k >= 0 else np.nan
            if k < 0:
                busy = max(busy, exp)        # the pending order held the slot until it expired
                continue
            # ---- exit
            x, why, xpx = -1, "", np.nan
            start = k if (kind == "M" and ohlc) else k + 1
            if ohlc and kind != "M":         # entry minute: assume the worst
                if side > 0 and l[k] <= sl:
                    x, why, xpx = k, "SL", sl - slip
                elif side < 0 and ah[k] >= sl:
                    x, why, xpx = k, "SL", sl + slip
            if x < 0 and start < tex:
                if side > 0:
                    x = _first(lambda i, j: (l[i:j] <= sl) | (h[i:j] >= tp), start, tex)
                    if x >= 0:
                        if l[x] <= sl:
                            why, xpx = "SL", min(sl, o[x]) - slip
                        else:
                            why, xpx = "TP", (tp if ohlc else o[x])
                else:
                    x = _first(lambda i, j: (ah[i:j] >= sl) | (al[i:j] <= tp), start, tex)
                    if x >= 0:
                        if ah[x] >= sl:
                            why, xpx = "SL", max(sl, ao[x]) + slip
                        else:
                            why, xpx = "TP", (tp if ohlc else ao[x])
            if x < 0:
                x = min(tex, N - 1)
                why, xpx = ("TIME" if tex < N else "END"), (o[x] if side > 0 else ao[x])
            nights = self.nights(k, x)
            gross = side * (xpx - epx)
            cost = self.comm + self.comm_rate * epx - nights * (self.swl if side > 0 else self.sws)
            row = [a, k, x, t[k], t[x], side, epx, xpx, why, nights, (gross - cost) / od.risk, gross / od.risk]
            if excursions:
                hi, lo = (h[k:x + 1].max(), l[k:x + 1].min()) if side > 0 else (ah[k:x + 1].max(), al[k:x + 1].min())
                row += [(hi - epx) / od.risk if side > 0 else (epx - lo) / od.risk,
                        (epx - lo) / od.risk if side > 0 else (hi - epx) / od.risk]
            out.append(row + [getattr(od, c) for c in extra])
            busy = x + 1
        cols = ["act", "ei", "xi", "et", "xt", "side", "epx", "xpx", "why", "nights", "r", "r_gross"]
        cols += ["mfe", "mae"] if excursions else []
        tr = pd.DataFrame(out, columns=cols + extra)
        tr["et"] = pd.to_datetime(tr["et"], unit="m")
        tr["xt"] = pd.to_datetime(tr["xt"], unit="m")
        return tr


def stats(tr, label="", months=None):
    """One line of results for a trade list (r in units of the planned risk)."""
    n = len(tr)
    if n == 0:
        return dict(label=label, n=0)
    r = tr["r"].values
    eq = np.r_[0.0, np.cumsum(r)]
    dd = float((np.maximum.accumulate(eq) - eq).max())
    sd = r.std(ddof=1) if n > 1 else np.nan
    loss = -r[r < 0].sum()
    if months is None:
        months = max((tr["xt"].max() - tr["et"].min()).days / 30.44, 1e-9)
    return dict(label=label, n=n, win=round(100 * (r > 0).mean(), 1), avg=round(r.mean(), 3), sum=round(r.sum(), 1),
                pf=round(r[r > 0].sum() / loss, 2) if loss > 0 else np.inf,
                t=round(r.mean() / sd * np.sqrt(n), 2) if sd and sd > 0 else np.nan,
                dd=round(dd, 1), per_month=round(r.sum() / months, 2), tr_month=round(n / months, 1))


def ticks4(m1):
    """cTrader's "m1 bars" backtest replays every minute as four ticks: open, high, low, close. Running a
    Market in mode "open" on this tick series reproduced the bot's gold backtest trade for trade (163 of
    163, same prices). A tick index is 4 x the m1 index (+1, +2, +3 inside the minute)."""
    px = np.column_stack([m1["o"], m1["h"], m1["l"], m1["c"]]).ravel()
    rep = lambda x: np.repeat(x, 4)
    return dict(t=rep(m1["t"]), nyt=rep(m1["nyt"]), td=rep(m1["td"]), o=px, h=px, l=px, c=px, v=rep(m1["v"]), sym=m1["sym"])
