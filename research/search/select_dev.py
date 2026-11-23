"""Pool the development results of the symbols by variant and rank them. Usage: select_dev.py <dev|dev2> SYM SYM ..."""
import json, os, sys
HERE = os.path.dirname(os.path.abspath(__file__))
import numpy as np, pandas as pd
pre = sys.argv[1]; syms = sys.argv[2:]
pd.set_option("display.width", 260); pd.set_option("display.max_colwidth", 80)
d = {s: pd.read_csv(HERE + f"/results/{pre}_{s}.csv").set_index("vid") for s in syms}
base = d[syms[0]][["fam", "params"]].copy()
N = sum(d[s]["n"].fillna(0) for s in syms)
S = sum(d[s]["sum"].fillna(0) for s in syms)
# pooled variance from each symbol's n, mean and sd
ss = sum(((d[s]["n"] - 1) * d[s]["sd"] ** 2 + d[s]["n"] * d[s]["avg"] ** 2).fillna(0) for s in syms)
avg = S / N
var = (ss - N * avg ** 2) / (N - 1)
base["N"] = N.astype(int); base["avg"] = avg.round(3); base["t"] = (avg / np.sqrt(var / N)).round(2)
base["R_month"] = (S / d[syms[0]]["months"].max()).round(2)
base["tr_month"] = (N / d[syms[0]]["months"].max()).round(1)
base["avg1"] = (sum(d[s]["sum1"].fillna(0) for s in syms) / sum(d[s]["n1"].fillna(0) for s in syms)).round(3)
base["avg2"] = (sum(d[s]["sum2"].fillna(0) for s in syms) / sum(d[s]["n2"].fillna(0) for s in syms)).round(3)
for s in syms:
    base[s[:3]] = d[s]["avg"].round(2)
base["pos"] = sum((d[s]["avg"] > 0).astype(int) for s in syms)
base["p"] = base.params.str.replace('"', "").str.replace(" ", "")
base.to_csv(HERE + f"/results/{pre}_pooled.csv")
ok = base[base.N >= 150]
print("variants", len(base), "with 150+ pooled trades", len(ok), "| pooled avg > 0:", round((ok.avg > 0).mean(), 3),
      "| t>2:", int((ok.t > 2).sum()), "t>3:", int((ok.t > 3).sum()), "t>4:", int((ok.t > 4).sum()), "| t<-2:", int((ok.t < -2).sum()))
print(ok.groupby("fam").agg(variants=("avg", "size"), mean_avg=("avg", "mean"), best_t=("t", "max"), pos_share=("avg", lambda x: round((x > 0).mean(), 2)),
                            all_pos=("pos", lambda x: int((x == len(syms)).sum()))).round(3).to_string())
cols = ["fam", "p", "N", "avg", "t", "R_month", "tr_month", "avg1", "avg2"] + [s[:3] for s in syms]
print(ok.sort_values("t", ascending=False)[cols].head(22).to_string())
