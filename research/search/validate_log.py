"""Check the simulator against the bot's own cTrader backtest log (Breakout mode, open-price ticks).
Usage: validate_log.py <log file> <SYMBOL> <spread> <commission round trip per unit>"""
import os, re, sys
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import numpy as np, pandas as pd
from lib import data, sim

log, sym, spread, comm = sys.argv[1], sys.argv[2], float(sys.argv[3]), float(sys.argv[4])
m1 = data.load_m1(sym); t = m1["t"]; inf = data.info(sym)
pip = float(inf["pipSize"])
sig, trades = [], {}
for line in open(log, encoding="utf-8", errors="replace"):
    if " signal symbol=" in line and "result=taken" in line:
        f = dict(p.split("=", 1) for p in line.split(" signal ", 1)[1].split())
        ts = re.search(r"\| (\d{4}-\d\d-\d\dT\d\d:\d\d):\d\dZ signal", line).group(1)
        sig.append(dict(T=int(np.datetime64(ts, "m").astype(np.int64)), zone=f["zone"], side=1 if f["bias"] == "Long" else -1,
                        px=float(f["entry"]), sl=float(f["stop"]), tp=float(f["target"])))
    elif " tradelog " in line:
        row = line.split("row=", 1)[1].split()[0].split(",")
        trades[row[1]] = dict(et=row[2], xt=row[3], epx=float(row[6]), xpx=float(row[9]), why=row[10], r=float(row[12]))
od = pd.DataFrame(sig)
od["act"] = np.searchsorted(t, od["T"].values) + 1
od["exp"] = np.searchsorted(t, od["T"].values + 120)
wd = (od["T"].values // 1440 + 3) % 7                     # Mon = 0
fri = (od["T"].values // 1440 + (4 - wd) % 7) * 1440 + 20 * 60
fri = np.where(fri <= od["T"].values, fri + 7 * 1440, fri)
od["tex"] = np.searchsorted(t, fri)
od["kind"] = "S"; od["risk"] = (od["px"] - od["sl"]).abs()
mk = sim.Market(m1, spread, comm_rt=comm, swap_long=float(inf["swapLong"]) * pip, swap_short=float(inf["swapShort"]) * pip, mode="open")
tr = mk.run(od.sort_values("act"), one_at_a_time=True)
mine = {z: r for z, r in zip(tr["zone"], tr.itertuples(index=False))}
both = sorted(set(mine) & set(trades)); only_log = sorted(set(trades) - set(mine)); only_me = sorted(set(mine) - set(trades))
print(f"taken signals {len(od)}  trades in log {len(trades)}  trades simulated {len(tr)}  common {len(both)}  only log {len(only_log)}  only sim {len(only_me)}")
same_reason = sum({"StopLoss": "SL", "TakeProfit": "TP", "ClosedByBot": "TIME"}.get(trades[z]["why"]) == mine[z].why for z in both)
de = np.array([mine[z].epx - trades[z]["epx"] for z in both]); dx = np.array([mine[z].xpx - trades[z]["xpx"] for z in both])
dr = np.array([mine[z].r - trades[z]["r"] for z in both])
same_et = sum(str(mine[z].et)[:16].replace(" ", "T") == trades[z]["et"][:16] for z in both)
same_xt = sum(str(mine[z].xt)[:16].replace(" ", "T") == trades[z]["xt"][:16] for z in both)
print(f"same exit reason {same_reason}/{len(both)}  same entry minute {same_et}  same exit minute {same_xt}")
print(f"entry price: equal {np.mean(np.abs(de) < 0.006):.1%}  mean abs diff {np.abs(de).mean():.3f}   exit price: equal {np.mean(np.abs(dx) < 0.006):.1%}  mean abs diff {np.abs(dx).mean():.3f}")
print(f"R per trade: mean abs diff {np.abs(dr).mean():.4f}  max {np.abs(dr).max():.3f}   sum R log {sum(trades[z]['r'] for z in both):.2f}  sim {sum(mine[z].r for z in both):.2f}")
print("log total R (all trades):", round(sum(v["r"] for v in trades.values()), 2), " sim total R:", round(tr.r.sum(), 2))
worst = sorted(both, key=lambda z: -abs(mine[z].r - trades[z]["r"]))[:4]
for z in worst:
    print(" ", z, "log", trades[z], "| sim", str(mine[z].et)[:16], str(mine[z].xt)[:16], round(mine[z].epx, 2), round(mine[z].xpx, 2), mine[z].why, round(mine[z].r, 3))
print("only in log:", only_log[:5], " only in sim:", only_me[:5])
