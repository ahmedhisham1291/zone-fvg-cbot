"""Trading costs per symbol, in price units per unit traded.

spread: assumed typical spread in liquid hours (ASSUMPTION until FTMO's live spreads are confirmed).
commission: a fraction of the traded value, round trip. Forex 60e-6 = 30 USD per million USD per side,
which is what the owner's earlier EURUSD backtests charged (6.58 USD per lot round trip); the cTrader
listing for FTMO says 5 USD per FX lot. Metals 14e-6 = 0.0014% (cTrader listing; the bot's log shows
0.0007 per side).
slip: extra price lost on a market or stop fill. Swaps come from the symbol facts the export bot wrote
(pips per night, negative = cost). From 16:55 to 18:10 New York the spread is taken three times as wide.
Stress case (spec 12.1 step 6): spread and commission +50%, extra slippage on stops.
"""
from . import data, sim

BASE = {
    #          spread   comm rate  slip     stress extra slip
    "EURUSD": (0.00003, 60e-6,     0.00001, 0.00005),
    "GBPUSD": (0.00006, 60e-6,     0.00001, 0.00005),
    "AUDUSD": (0.00004, 60e-6,     0.00001, 0.00005),
    "USDJPY": (0.005,   60e-6,     0.001,   0.005),
    "XAUUSD": (0.30,    14e-6,     0.05,    0.15),
}


def market(m1, sym, stress=False, mode="ohlc", spread=None):
    sp, rate, sl, extra = BASE[sym]
    sp = sp if spread is None else spread
    if stress:
        sp, rate, sl = sp * 1.5, rate * 1.5, sl + extra
    inf = data.info(sym)
    pip = float(inf["pipSize"])
    return sim.Market(m1, sp, comm_rate=rate, swap_long=float(inf["swapLong"]) * pip, swap_short=float(inf["swapShort"]) * pip,
                      slip=sl, mode=mode, wide=(1015, 1090, 3.0))
