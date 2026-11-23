"""Sanity checks on one symbol's ExportBars files. Usage: check_export.py <folder> <SYMBOL>"""
import sys
import numpy as np
import pandas as pd

folder, sym = sys.argv[1], sys.argv[2]
pd.set_option("display.width", 200)

def load(tf):
    df = pd.read_csv(f"{folder}/ExportBars_{sym}_{tf}.csv.gz")
    df["time_utc"] = pd.to_datetime(df["time_utc"], format="%Y-%m-%d %H:%M")
    return df

print(open(f"{folder}/ExportBars_{sym}_info.txt").read().strip())
S = {tf: load(tf) for tf in ("Minute", "Minute15", "Hour4", "Daily")}
for tf, df in S.items():
    t = df["time_utc"]
    bad = ((df.high < df[["open", "close"]].max(axis=1) - 1e-9) | (df.low > df[["open", "close"]].min(axis=1) + 1e-9)).sum()
    flat = (df.high == df.low).mean()
    print(f"{tf:9s} rows={len(df):8d} first={t.iloc[0]} last={t.iloc[-1]} dup={t.duplicated().sum()} "
          f"sorted={t.is_monotonic_increasing} nan={int(df.isna().sum().sum())} badOHLC={bad} flat(H==L)={flat:.3%} "
          f"vol0={(df.tick_volume == 0).mean():.2%} volMed={df.tick_volume.median():.0f}")

m1 = S["Minute"].set_index("time_utc")
# 1. gaps in the m1 series
gap = m1.index.to_series().diff().dt.total_seconds().div(60).dropna()
print("\nm1 gap minutes (top):", gap.value_counts().head(6).to_dict())
big = gap[gap > 5]
print("gaps > 5 min:", len(big), " of which 55-65 min:", int(((big >= 55) & (big <= 65)).sum()),
      " > 24h:", int((big > 1440).sum()), " other:", int(((big > 65) & (big <= 1440)).sum()) + int((big < 55).sum()))
per_day = m1.groupby(m1.index.normalize()).size()
print("m1 bars per calendar day: median", int(per_day.median()), "min", int(per_day.min()), "days", len(per_day))
print("m1 bars by year:", m1.groupby(m1.index.year).size().to_dict())
print("close by year end:", m1.groupby(m1.index.year)["close"].last().round(2).to_dict())

# 2. m1 -> m15 against the platform's m15
agg = m1.resample("15min", label="left", closed="left").agg(open=("open", "first"), high=("high", "max"), low=("low", "min"), close=("close", "last"), n=("open", "size")).dropna()
p15 = S["Minute15"].set_index("time_utc")
j = agg.join(p15, how="inner", rsuffix="_p")
print(f"\nm15: platform bars {len(p15)}, built from m1 {len(agg)}, common {len(j)}")
for c in ("open", "high", "low", "close"):
    d = (j[c] - j[c + "_p"]).abs()
    print(f"  {c:5s} equal {(d < 1e-9).mean():.4%}  max diff {d.max():.2f}  mean diff {d.mean():.4f}")
only_p = p15.index.difference(agg.index); only_a = agg.index.difference(p15.index)
print("  only in platform:", len(only_p), " only in m1 build:", len(only_a))

# 3. D1 and H4 as the platform reports them
for tf in ("Daily", "Hour4"):
    df = S[tf]; t = df["time_utc"]
    print(f"\n{tf} open hour (UTC) counts:", t.dt.hour.value_counts().sort_index().to_dict())
    print(f"{tf} open weekday counts (Mon=0):", t.dt.dayofweek.value_counts().sort_index().to_dict())
d1 = S["Daily"]
d1 = d1[d1.time_utc >= m1.index[0]].reset_index(drop=True)
edges = list(d1.time_utc) + [d1.time_utc.iloc[-1] + pd.Timedelta(days=1)]
rows = []
idx = m1.index
for a, b in zip(edges[:-1], edges[1:]):
    lo, hi = idx.searchsorted(a), idx.searchsorted(b)
    if hi > lo:
        seg = m1.iloc[lo:hi]
        rows.append((a, seg.open.iloc[0], seg.high.max(), seg.low.min(), seg.close.iloc[-1]))
b = pd.DataFrame(rows, columns=["time_utc", "o", "h", "l", "c"]).merge(d1, on="time_utc")
print(f"\nD1 rebuilt from m1 between the platform's D1 opens: {len(b)} days")
for c, p in (("o", "open"), ("h", "high"), ("l", "low"), ("c", "close")):
    d = (b[c] - b[p]).abs()
    print(f"  {p:5s} equal {(d < 1e-9).mean():.3%}  max diff {d.max():.2f}")
# summer/winter switch of the D1 open hour
sw = d1.assign(h=d1.time_utc.dt.hour); ch = sw[sw.h != sw.h.shift()].time_utc.dt.strftime("%Y-%m-%d %H:%M").tolist()
print("D1 open hour changes at:", ch[:16])
