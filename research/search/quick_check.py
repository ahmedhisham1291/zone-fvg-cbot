"""Short check of one symbol's export: clean m1 series, and harness bars equal the platform's."""
import os, sys
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import numpy as np, pandas as pd
from lib import data
sym = sys.argv[1]; R = data.RAW
m = data.load_m1(sym); t = m["t"]
gap = np.diff(t)
bad = int(((m["h"] < np.maximum(m["o"], m["c"]) - 1e-9) | (m["l"] > np.minimum(m["o"], m["c"]) + 1e-9)).sum())
print(sym, "m1 rows", len(t), "from", data.dt(t[0]), "to", data.dt(t[-1]), "| sorted", bool((gap > 0).all()), "bad bars", bad,
      "| gaps over 3 days", int((gap > 4320).sum()), "largest gap h", round(gap.max() / 60, 1), "| flat bars", f"{(m['h'] == m['l']).mean():.2%}")
for tf, f in (("m15", "Minute15"), ("h4", "Hour4"), ("d1", "Daily")):
    p = pd.read_csv(f"{R}/ExportBars_{sym}_{f}.csv.gz"); b = data.bars(m, tf)
    n = min(len(p), len(b["t"]) - 1) - 300
    eq = [round(float(np.isclose(b[k][-(n + 1):-1], p[c].values[-n:]).mean()), 5) for k, c in (("o", "open"), ("h", "high"), ("l", "low"), ("c", "close"))]
    print(" ", tf, "harness vs platform, last", n, "bars: equal o/h/l/c", eq)
print(" ", open(f"{R}/ExportBars_{sym}_info.txt").read().strip()[:260])
