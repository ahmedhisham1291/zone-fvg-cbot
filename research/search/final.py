"""The rules chosen on the development years, and their report.
Usage: final.py dev            diagnostics on the development years only (data cut at 2024-11-01)
       final.py holdout        the one look at the holdout (2024-11-01 onward); run once
"""
import os, sys
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import numpy as np, pandas as pd
from lib import data, sim, strats, costs

SYMS = ("XAUUSD", "EURUSD", "GBPUSD")
HOLD = np.datetime64("2024-11-01")
# A: dip with the D1 trend on H4. Chosen as the centre of a plateau of the second pass, not as its best cell.
A = ("fade", dict(tf="h4", n=20, k=2.0, stop_k=1.5, rr=0.0, hold="5d", trend="ema50", direction="fade", exit="mid"))
# B: break of the Asian range with the D1 trend (secondary candidate, weaker in development).
B = ("opening_range", dict(r0=120, r1=600, cutoff=1140, stop=1.0, rr=2.0, hold="eod", trend="ema50", wmin=0.5, wmax=9.0, clock="td"))
RULES = {"A": A, "B": B}
_cache = {}


def ctx_for(sym, cut):
    key = (sym, cut)
    if key not in _cache:
        m1 = data.load_m1(sym)
        if cut:
            c = np.searchsorted(m1["t"], int(HOLD.astype("datetime64[m]").astype(np.int64)))
            m1 = {k: (v[:c] if isinstance(v, np.ndarray) else v) for k, v in m1.items()}
        _cache[key] = (m1, strats.Ctx(m1))
    return _cache[key]


def run(rules, cut=True, stress=False, ticks=False, override=None, syms=SYMS):
    """Trades of the given rules on every symbol, one position per symbol at a time."""
    out = []
    for sym in syms:
        m1, ctx = ctx_for(sym, cut)
        ods = []
        for name in rules:
            fam, kw = RULES[name]
            kw = dict(kw, **(override or {}))
            ods.append(getattr(strats, fam)(ctx, **kw).assign(rule=name))
        od = pd.concat(ods).sort_values("act", kind="stable").reset_index(drop=True)
        if ticks:                                   # cTrader's m1 backtest: four ticks per minute
            od = od.assign(act=od.act * 4 + (od.kind != "M"), exp=np.where(od.exp >= 0, od.exp * 4, -1), tex=np.where(od.tex >= 0, od.tex * 4, -1))
            mk = costs.market(sim.ticks4(m1), sym, stress=stress, mode="open")
        else:
            mk = costs.market(m1, sym, stress=stress)
        tr = mk.run(od)
        tr["sym"] = sym
        out.append(tr)
    return pd.concat(out).sort_values("xt").reset_index(drop=True)


def line(tr, label, months):
    s = sim.stats(tr, label, months=months)
    return (f"{label:34s} n={s['n']:4d} win={s['win']:5.1f}% avg={s['avg']:+.3f}R PF={s['pf']:.2f} t={s['t']:+.2f} "
            f"sum={s['sum']:+7.1f}R maxDD={s['dd']:5.1f}R R/month={s['per_month']:+.2f} trades/month={s['tr_month']:.1f}")


def report(tr, title, months):
    print(f"\n== {title} ({months:.1f} months) ==")
    print(line(tr, "all symbols", months))
    for sym in SYMS:
        print(line(tr[tr.sym == sym], "  " + sym, months))
    print(line(tr[tr.side > 0], "  long", months)); print(line(tr[tr.side < 0], "  short", months))
    print("  R by year:", {int(y): round(float(v), 1) for y, v in tr.groupby(tr.xt.dt.year).r.sum().items()},
          "| exits:", tr.why.value_counts().to_dict(), "| avg nights held:", round(tr.nights.mean(), 2))
    m = tr.groupby(tr.xt.dt.to_period("M")).r.sum()
    allm = pd.period_range(tr.xt.min(), tr.xt.max(), freq="M"); m = m.reindex(allm, fill_value=0.0)
    print(f"  months: {len(m)}, positive {int((m > 0).sum())}, negative {int((m < 0).sum())}; best {m.max():+.1f}R worst {m.min():+.1f}R; "
          f"at 0.5% risk: average {0.5 * m.mean():+.2f}% a month, worst month {0.5 * m.min():+.2f}%, months at or above +2%: {int((0.5 * m >= 2).sum())}")
    tot = tr.groupby("sym").r.sum(); pos = tot[tot > 0]
    print("  share of profit by symbol:", {k: f"{v / pos.sum():.0%}" for k, v in pos.items()} if len(pos) else "none")


if __name__ == "__main__":
    what = sys.argv[1]
    if what == "dev":
        t0 = min(data.load_m1(s)["t"][0] for s in SYMS)
        months = (int(HOLD.astype("datetime64[m]").astype(np.int64)) - t0) / 1440 / 30.44
        for rules, title in ((["A"], "A: H4 dip with the D1 trend"), (["B"], "B: Asian range break with the D1 trend"), (["A", "B"], "A + B together")):
            report(run(rules), "development, " + title, months)
        print("\n== A: checks on development ==")
        print(line(run(["A"], stress=True), "cost stress (+50%, more slippage)", months))
        print(line(run(["A"], ticks=True), "cTrader fill model (4 ticks a minute)", months))
        for ov in (dict(n=16), dict(n=24), dict(k=1.6), dict(k=2.4), dict(stop_k=1.2), dict(stop_k=1.8), dict(trend="ema50_200"), dict(trend="ema20_50"), dict(exit="time"), dict(hold="3d")):
            print(line(run(["A"], override=ov), "A with " + str(ov).replace("'", ""), months))
    elif what == "holdout":
        end = max(data.load_m1(s)["t"][-1] for s in SYMS)
        months = (end - int(HOLD.astype("datetime64[m]").astype(np.int64))) / 1440 / 30.44
        for rules, title in ((["A"], "A: H4 dip with the D1 trend"), (["B"], "B: Asian range break with the D1 trend"), (["A", "B"], "A + B together")):
            tr = run(rules, cut=False)
            report(tr[tr.et >= HOLD], "HOLDOUT, " + title, months)
        tr = run(["A"], cut=False, stress=True); print("\n" + line(tr[tr.et >= HOLD], "A holdout, cost stress", months))
        tr = run(["A"], cut=False, ticks=True); print(line(tr[tr.et >= HOLD], "A holdout, cTrader fill model", months))
        for ov in (dict(stop_k=1.0), dict(stop_k=2.0), dict(k=2.5), dict(k=2.5, stop_k=1.0), dict(k=2.5, stop_k=2.0)):
            tr = run(["A"], cut=False, override=ov); print(line(tr[tr.et >= HOLD], "A holdout, plateau " + str(ov).replace("'", ""), months))
