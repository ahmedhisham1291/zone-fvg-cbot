"""Run every rule variant on the development period of one symbol and store one row per variant.
Usage: screen.py <SYMBOL> [end date, default 2024-11-01]   (the holdout starts at that date and is not loaded)"""
import json, os, sys, time
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import numpy as np, pandas as pd
from lib import data, sim, strats, costs

def grid():
    g = []
    add = lambda fam, **kw: g.append((fam, kw))
    for n in (1, 5, 20):
        for w0, w1 in ((420, 1020), (420, 600), (720, 960)):
            for tr in ("none", "ema50"):
                for sk in (1.0, 2.0):
                    for rr, hold in ((1.0, "fri"), (2.0, "fri"), (3.0, "fri"), (0.0, "eod")):
                        add("breakout", n=n, w0=w0, w1=w1, trend=tr, stop_k=sk, rr=rr, hold=hold)
    for r0, r1, cut in ((0, 420, 720), (0, 420, 960), (420, 480, 720), (420, 780, 1020), (810, 870, 1140)):
        for stop in ("range", 1.0):
            for rr in (1.0, 2.0, 0.0):
                for tr in ("none", "ema50"):
                    for wmin, wmax in ((0.0, 9.0), (0.0, 0.5), (0.5, 9.0)):
                        add("opening_range", r0=r0, r1=r1, cutoff=cut, stop=stop, rr=rr, hold="eod", trend=tr, wmin=wmin, wmax=wmax)
    for tf in ("h1", "h4"):
        for tr in ("ema50", "ema50_200"):
            for n in (2, 3):
                for lo in (10, 20):
                    for sk in (1.0, 2.0):
                        for rr in (1.0, 2.0):
                            for hold in ("1d", "3d"):
                                add("pullback", tf=tf, trend=tr, n=n, lo=lo, stop_k=sk, rr=rr, hold=hold)
    for tf in ("h1", "h4", "d1"):
        for k in (2.0, 2.5):
            for sk in (1.5, 3.0):
                for rr in (0.5, 1.0, 0.0):
                    for hold in ("1d", "3d"):
                        for tr in ("none", "ema50"):
                            add("fade", tf=tf, n=20, k=k, stop_k=sk, rr=rr, hold=hold, trend=tr)
    for tf in ("h4", "d1"):
        for n in (10, 20, 55):
            for sk in (1.5, 3.0):
                for rr in (0.0, 2.0, 4.0):
                    for hold in ("fri", "10D"):
                        add("donchian", tf=tf, n=n, stop_k=sk, rr=rr, hold=hold)
    for start in range(0, 1440, 120):
        for hold in (120, 240, 480):
            for side in (1, -1):
                for tr in ("none", "ema50"):
                    add("time_of_day", start=start, hold=hold, side=side, risk_k=0.5, trend=tr)
    for check in (420, 600, 780, 900, 1020):
        for k in (0.3, 0.6, 1.0):
            for mode in ("with", "fade"):
                add("intraday_move", check=check, k=k, mode=mode, risk_k=0.5, rr=0.0, hold="eod")
    return g

def grid2():
    """Second pass, on what the first pass pointed at: dips with the D1 trend, session ranges in New York
    time, and moves that follow a stretch. Variant ids start at 1000."""
    g = []
    add = lambda fam, **kw: g.append((fam, kw))
    for tf in ("h4", "h1"):
        for k in (1.5, 2.0, 2.5):
            for tr in ("ema50", "ema50_200"):
                for sk in (1.0, 1.5, 2.0):
                    for rr, hold, ex in ((0.0, "1d", "time"), (0.0, "3d", "time"), (0.0, "5d", "time"), (2.0, "5d", "time"), (3.0, "5d", "time"), (0.0, "5d", "mid")):
                        add("fade", tf=tf, n=20, k=k, stop_k=sk, rr=rr, hold=hold, trend=tr, direction="fade", exit=ex)
    for r0, r1, cut in ((120, 600, 840), (120, 600, 1140), (600, 660, 900), (600, 900, 1200), (870, 990, 1200), (990, 1020, 1260), (990, 1050, 1320)):
        for stop in ("range", 1.0):
            for rr in (1.0, 2.0, 0.0):
                for tr in ("none", "ema50"):
                    for wmin, wmax in ((0.0, 9.0), (0.0, 0.5), (0.5, 9.0)):
                        add("opening_range", r0=r0, r1=r1, cutoff=cut, stop=stop, rr=rr, hold="eod", trend=tr, wmin=wmin, wmax=wmax, clock="td")
    for tf in ("h1", "h4"):
        for k in (2.0, 2.5):
            for tr in ("none", "ema50"):
                for sk in (1.0, 2.0):
                    for rr, hold in ((1.0, "1d"), (2.0, "3d"), (0.0, "1d")):
                        add("fade", tf=tf, n=20, k=k, stop_k=sk, rr=rr, hold=hold, trend=tr, direction="follow", exit="time")
    return g


def run(sym, end="2024-11-01", stress=False, only=None, which=1):
    m1 = data.load_m1(sym)
    cut = np.searchsorted(m1["t"], int(np.datetime64(end, "m").astype(np.int64)))
    dev = {k: (v[:cut] if isinstance(v, np.ndarray) else v) for k, v in m1.items()}
    ctx = strats.Ctx(dev)
    mk = costs.market(dev, sym, stress=stress)
    t0, t1 = dev["t"][0], dev["t"][-1]
    months = (t1 - t0) / 1440 / 30.44
    mid = np.datetime64("2023-01-01")
    rows = []
    g = grid() if which == 1 else grid2()
    for i, (fam, kw) in enumerate(g, start=0 if which == 1 else 1000):
        if only is not None and i not in only:
            continue
        tr = mk.run(getattr(strats, fam)(ctx, **kw))
        s = sim.stats(tr, months=months)
        if s["n"]:
            a, b = tr[tr.et < mid], tr[tr.et >= mid]
            s.update(sd=round(float(tr.r.std(ddof=1)), 4) if len(tr) > 1 else np.nan, months=round(months, 2),
                     sum1=round(a.r.sum(), 2), sum2=round(b.r.sum(), 2),
                     n1=len(a), avg1=round(a.r.mean(), 3) if len(a) else np.nan, n2=len(b), avg2=round(b.r.mean(), 3) if len(b) else np.nan,
                     nl=int((tr.side > 0).sum()), suml=round(tr.r[tr.side > 0].sum(), 1), sums=round(tr.r[tr.side < 0].sum(), 1),
                     years=json.dumps({int(y): round(float(v), 1) for y, v in tr.groupby(tr.et.dt.year).r.sum().items()}))
        s.update(vid=i, fam=fam, params=json.dumps(kw), sym=sym)
        rows.append(s)
    return pd.DataFrame(rows)

if __name__ == "__main__":
    sym = sys.argv[1]; which = int(sys.argv[2]) if len(sys.argv) > 2 else 1; end = "2024-11-01"
    t = time.time(); df = run(sym, end, which=which)
    import os; os.makedirs(HERE + "/results", exist_ok=True)
    df.to_csv(HERE + f"/results/dev{'' if which == 1 else which}_{sym}.csv", index=False)
    ok = df[df.n >= 60]
    print(sym, "variants", len(df), "with 60+ trades", len(ok), "time", round(time.time() - t), "s")
    print("avg R per trade: share of variants above 0:", round((ok.avg > 0).mean(), 3), " t>2:", int((ok.t > 2).sum()), " t>3:", int((ok.t > 3).sum()), " t<-2:", int((ok.t < -2).sum()), " t<-3:", int((ok.t < -3).sum()))
    print(ok.groupby("fam").agg(variants=("avg", "size"), mean_avg=("avg", "mean"), best_t=("t", "max"), worst_t=("t", "min"), pos=("avg", lambda x: round((x > 0).mean(), 2))).round(3).to_string())
