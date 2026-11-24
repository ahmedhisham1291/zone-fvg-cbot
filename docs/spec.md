# Zone + FVG cBot: Technical Specification

Version 0.12 · 24 November 2026 (v0.11 of 23 November 2026, with the owner's decisions of 24 November 2026: a 200,000 USD account, a second sizing plan "Recovery", a 2% daily risk budget and a per-trade sanity cap that can be switched off, section 4)

## 0. What changed in v0.2

A review of v0.1 found these gaps. Each is fixed in the section shown.

| Gap in v0.1 | Fix in v0.2 | Section |
|---|---|---|
| Bot state lived in memory; restart behaviour was only sketched | Two-layer design: signals are recomputed from price history, trades are read back from the broker. Full recovery procedure | 3.3, 8 |
| "Retry once" on a failed order could create duplicates | Idempotent order placement: always check the broker first, never resend blindly | 5.3 |
| Kill switch needed a remembered equity peak | All risk limits are recalculated from account history at any time | 4.2 |
| No behaviour defined for internet or power loss | Outage scenarios, safe mode, reconnect handling | 8 |
| No news filter | Calendar-based news filter, including for backtests | 6 |
| cTrader cloud recommended for live | Cloud sends no HTTP requests and wipes stored files on restart, so it cannot run the news feed or alerts. A VPS running cTrader CLI is now the primary host | 10 |
| Nothing watches the bot when the server itself is down | External heartbeat monitor and phone alerts | 11 |
| Trading costs not checked per trade | Cost-to-risk cap and spread-aware FVG size | 3.6 |
| No protection against a runaway bug | Circuit breakers and sanity caps | 4.3 |
| Only strategy tests were planned | Code tests, fault-injection tests and a soak test | 12 |
| No plan for detecting a fading edge in live trading | Live edge monitoring with automatic risk reduction | 13.3 |

**Decisions recorded so far (v0.3 and v0.4)**

| Topic | Decision | Section |
|---|---|---|
| Symbols | EURUSD, GBPUSD, USDJPY, AUDUSD; XAUUSD (gold) added on 17 November 2026 | 2 |
| Timeframes | D1 trend, H4 zones, M15 entries | 2, 3 |
| Stop placement | Beyond the whole zone | 3.5 |
| Account limits | 1.5% daily loss stop (2% from v0.12), 8% drawdown kill switch | 4.2 |
| Account | FTMO 2-Step Challenge, Swing type, cTrader, 100,000 USD simulated capital. Already bought, no trade placed yet. From v0.12 (owner decision of 24 November 2026): 200,000 USD | 15 |
| Hosting | Run on your laptop for a few days first (demo account), then move to a rented VPS | 10.2, 10.5 |
| Alerts | Telegram | 11 |
| News calendar | Two free feeds combined (Forex Factory weekly export and ShowMyTrades); Forex Factory history for backtests | 6.2, 6.4, 6.5 |

**Changes in v0.9 (owner decisions of 17 November 2026, after the audits of the adapter and the core)**

| Topic | Decision | Section |
|---|---|---|
| A. Data gaps | A gap that is still in the broker's history after reloading it (for example the early close on 24 and 31 December) is accepted. Recovery fails only on too few bars or stale data | 8.3, 8.4 |
| B. Duties while recovering | Friday close and the FTMO emergency guard run in every connected state, Recovering included | 8.5 |
| C. Halts in backtests | In a backtest a halt (kill switch, emergency guard, error storm, request limit) and the daily budget are only written to the Log as "would halt" or "would block", and the bot keeps trading, so the trade list is complete. The simulation tools apply the limits and FTMO's rules afterwards. Automatic in backtests; live behaviour unchanged | 8.5, 12.1 |
| D. Gold | XAUUSD is a fifth instance with its own spread, stop and volume limits; only USD news blocks it; a gold buy counts as short USD; the gap check uses the symbol's trading sessions (gold's daily break); prices are rounded to the symbol's tick size | 2, 4.3, 5.1, 6.1, 7, 8.3, 13.1 |
| Fixes without a rule change | Old chart drawings removed by name; repeated Log lines limited; refuse to start when RiskEpochDate is after the server time or, in a backtest, the starting balance differs from InitialCapital; no Friday close request while the market is closed (the weekend close does it at the reopen); daily counts of why zones end | 5.5, 9.4, 11 |

**Changes in v0.10 (owner request of 22 November 2026: a breakout entry mode)**

| Topic | Decision | Section |
|---|---|---|
| E. Entry mode | The bot has two entry modes, chosen per instance with `EntryMode`. **Breakout** (new, the default): in the direction of the D1 trend, the first M15 bar that closes beyond the previous day's high or low inside the trading window arms a stop order beyond that bar. **ZoneFvg**: the zone and gap entry of sections 3.2 to 3.5, unchanged. Everything outside section 3 (sizing, limits, news, recovery, FTMO guards, alerts) is the same in both modes | 1, 2, 3, 3.7, 7 |
| F. Stop-entry orders | Breakout entries are stop orders with stop loss, take profit and expiry attached at placement. They are placed, verified, recovered and cancelled by the same rules as limit orders | 5.2, 5.3, 6.1, 8.3 |
| G. Which mode goes live | Both modes are backtested on the same data and costs. A mode may go live only if it passes section 13.1 by itself. The Breakout mode is the simple trend-following benchmark that the ZoneFvg mode has to beat; the Breakout mode itself has no benchmark yet | 12.1, 13.1 |
| H. Order guards (added on 22 November 2026, after the review of the stop-order code) | After an unclear outcome nothing is resent when the broker's history since the signal's confirmation holds a closed trade with the label, or when that history cannot be read. An order whose type does not fit its signal's mode is never sent | 5.2, 5.3 |
| I. Recovery in Breakout mode (added on 22 November 2026, after the review of the wiring) | A D1 series that lacks the bar that closed last does not pass recovery in Breakout mode, because that bar is the reference. The automated fault scenarios 5, 6, 8 and 14 also run in Breakout mode. "Flat" for a deployment or a parameter change means no open position and no pending order of the bot | 8.3, 10.2, 12.3 |
| J. A reference serves one day (added on 22 November 2026, after the review of the guards) | An M15 bar that opens 24 hours or more after the first M15 bar following the reference bar's close is not checked. So a daily series that is one bar behind cannot make the bot trade the range of the day before. A history list with a missing entry counts as no history | 3.7, 5.3, 8.3 |
| K. What goes live (added on 23 November 2026, after the review of the cBot parameters and guides) | The live parameter files carry the chosen mode and the accepted settings, and the hash at start must equal the hash of the accepted run. When ZoneFvg is the chosen mode, Breakout is run once on the holdout as the benchmark input only | 13.1 |
| L. A backtest runs with the defaults (added on 23 November 2026, owner request) | In a backtest an empty `RiskEpochDate` means the backtest's first day, and `NewsEnabled` has no effect while the embedded news history has no events. Live, both stay as before: `RiskEpochDate` must be set and the news filter blocks without a calendar | 7 |
| M. Breakout filters (added on 23 November 2026, owner request after the first backtests) | Breakout trades only when the D1 EMA(50) is on the trade's side of the D1 EMA(200), and only on a breakout confirmed by a bar that closes before 08:00 UTC. Both are cBot settings; 0 and 17:00 switch them off | 3.1, 3.7, 7 |
| N. SessionRange mode (added on 23 November 2026, owner request after the strategy search) | A third entry mode: a stop order at the edge of the Asian-session range (19:00 to 03:00 New York) on the side of the D1 trend, when the range is at least half a daily ATR wide; stop 1.0 x ATR(H4), target 2R, the order ends at 12:00 New York and an open position is closed at 16:30 New York. It was the only rule of the search that held on the years the search had not used, and only on gold. Breakout stays the default. Gold's smallest stop and widest spread are restated in the platform's pip of 1 USD: 8 and 0.4 | 3, 3.6, 3.8, 5.5, 7, 13.1, 14 |
| O. CHoCH entry model (v0.11, owner's answers of 23 November 2026) | In ZoneFvg mode the entry trigger at a touched H4 zone is chosen with `EntryModel`: **Fvg** (sections 3.4 and 3.5, unchanged, the default until the owner switches after a backtest) or **Choch** (new, 3.9): at an H4 demand zone an M15 bar closes above the most recent M15 swing high (the change of character), and a buy limit waits at that broken level for the retest; the stop goes just below the lowest low since the touch; the target is just before the next opposing H4 zone or swing level and the trade is skipped when that gives less than 1:2. Supply zones mirror it. The direction comes from the zone. All day: `SessionStartUtc` 00:00 and `SessionEndUtc` 24:00 are accepted, and the owner's choice for Choch is all day; the Friday and news rules stay. No trailing stop. Values marked "proposal" in 3.9 and 7 were accepted by the owner on 23 November 2026 | 3.3, 3.6, 3.9, 5.1, 5.2, 6.1, 7, 14 |
| Proposed, marked in the plan pull request | Breakout values: entry buffer 0.1 x ATR(M15), stop 1.0 x ATR(H4), target 2R, order life 8 bars. No trailing stop in the baseline (it is the first variant to test). Symbols unchanged; index CFDs are an open question | 3.7, 7, 12.4, 14 |
| FTMO wording | The gap-trading rule is restated as FTMO's page reads on 22 November 2026: two separate situations joined by "or" | 15.2 |

**Changes in v0.12 (owner decisions of 24 November 2026, confirmed in chat)**

| Topic | Decision | Section |
|---|---|---|
| P. Account size | The owner starts with a 200,000 USD FTMO account. `InitialCapital` defaults to 200,000; FTMO's limits are computed from it as before (daily floor: 00:00 CE(S)T balance minus 10,000; overall floor 180,000). Backtests start with 200,000 | 2, 4.1, 4.2, 7, 15 |
| Q. Sizing plan | `SizingPlan` = `Ladder` (default, the behaviour of v0.11 unchanged) or `Recovery` (new, 4.1.1). Recovery: the first trade after a win, or from `RiskEpochDate`, risks `RecoveryFirstRiskUsd` (100 USD); after losses each trade risks (L + RewardRisk x first) / RewardRisk, L being the money lost in the current losing run, so that one 2R win recovers the run plus 2 x first; at most `RecoveryMaxRiskPercent` (1.28%) of balance; a win resets. The owner was told and accepted that continuing at 1.28% fails much more often than restarting (simulation at 40% winners: pass 59%, fail or stop 32% in 500 trades), that a long enough losing run breaks FTMO's 10% limit, and that the 8% kill switch stops trading after about 13 losses in a row | 4.1, 4.1.1, 7, 12.1 |
| R. Daily risk budget | 2% of initial capital (was 1.5%), in both sizing plans. FTMO's 5% and the emergency guard unchanged | 4.2, 7, 15.4 |
| S. Per-trade sanity cap | The check "calculated risk above 0.55% of balance" can be switched with `SanityCapEnabled`, **default off** (the owner's explicit choice after being told that it is the last protection against a sizing bug or bad price data). Its percent stays a parameter, `SanityCapPercent` (0.55), for when it is on. The other sanity checks (volume above `MaxVolumeUnits`, stop or target on the wrong side) always apply | 4.3, 7 |
| T. Unchanged | Kill switch 8%, FTMO emergency guard, open-risk cap 1.5%, currency cap, `MaxVolumeUnits`, the circuit breakers and every other limit | 4.2, 4.3 |
| U. Simulator | The simulation tools offer `--sizing recovery` with the same rule, and their defaults follow the account: initial capital 200,000, daily budget 2% | 12.1 |

## 1. Purpose and scope

A cTrader cBot that trades four forex pairs and gold (XAUUSD) in the direction of the higher-timeframe trend, with one of three entry modes per instance (section 3). **Breakout** (the default from v0.10) enters with a stop order after an M15 bar closes beyond the previous day's range. **ZoneFvg** waits for price to return to a higher-timeframe supply or demand zone, then enters on a lower-timeframe fair value gap (FVG), or, with `EntryModel` = Choch (v0.11), on the retest of a lower-timeframe change of character (section 3.9).

**In scope (v1)**

- Fully mechanical detection of breakouts, zones and FVGs.
- One position per symbol; risk per trade from the sizing plan of section 4.1 (ladder, at most 0.5%, by default; Recovery, at most 1.28%, from v0.12).
- Broker-side stop loss, take profit and order expiry on every order.
- News filter for high-impact events.
- Unattended recovery after power loss, reboot, crash or network loss.
- Backtestable on tick data with no look-ahead.

**Out of scope (v1)**

- Partial closes, trailing stops, scaling in.
- Machine learning or adaptive parameters.
- Trading the news itself.

**Reliability goals (each is tested in section 12)**

1. No position ever exists without a broker-side stop loss.
2. No zone is ever traded twice, and no order is ever duplicated.
3. After any outage the bot recognises its own open trades and pending orders without manual help.
4. No trade is opened from a signal older than one bar.
5. When the bot is unsure (missing data, no calendar, repeated errors) it stops opening trades. Open trades stay protected by their broker-side stops.

**What a spec can and cannot do**

Reliability can be engineered and tested, and this version does that. Profitability cannot be specified; it can only be measured. The 2% monthly figure (a net +4R per month at 0.5% risk) remains an outcome to test. What the spec adds is everything that protects an edge if one exists: realistic costs, honest validation, strict risk limits and early detection when results fade. This is a technical design, not financial advice.

## 2. Settings and assumptions

| Item | Setting | Status and reason |
|---|---|---|
| Symbols | EURUSD, GBPUSD, USDJPY, AUDUSD, XAUUSD | Decided. Liquid majors, low spread. XAUUSD (gold) added by the owner on 17 November 2026; one instance per symbol |
| Entry mode | `Breakout` (default), `ZoneFvg` or `SessionRange` (added on 23 November 2026), one per instance | Decided on 22 November 2026 (owner request). Sections 3 and 3.7. Both modes use the bias, the M15 chart and everything from section 4 on |
| Bias timeframe | D1 | Decided. Trend filter |
| Zone timeframe | H4 | Decided. Higher-timeframe zones |
| Entry timeframe | M15 (the chart the bot runs on) | Decided. Lower-timeframe FVG |
| Trading window | 07:00 to 17:00 UTC; 00:00 to 24:00 (all day) for the Choch entry model | 07:00 to 17:00 assumed (London and New York sessions). All day for Choch: owner's answer of 23 November 2026 (3.9) |
| Entry model in ZoneFvg mode | `Fvg` (default) or `Choch` (v0.11) | Owner's answers of 23 November 2026. Section 3.9. Fvg stays the default until the owner switches after the backtest of both |
| Account | FTMO 2-Step Challenge, Swing type, cTrader, 200,000 USD simulated capital, used only by this bot | Decided (section 15); 200,000 USD from the owner's decision of 24 November 2026 (was 100,000). Manual trades on the same symbols would confuse recovery |
| Language | C# in cTrader Algo | Widest documentation and examples |
| Hosting | Stage 1: your laptop on a demo account for a few days. Stage 2: rented Linux VPS, cTrader CLI in Docker | Decided. See section 10 |
| News source | Forex Factory weekly export alone (owner decision 2026-11-14). ShowMyTrades is deferred: the design keeps room for a second feed, added later only with a real sample | Decided. See section 6 |
| Alerts | Telegram | Decided. See section 11 |

## 3. Strategy rules

All rules are evaluated on **closed bars only**. Long rules are shown; short rules are the mirror image. ATR means ATR(14) on the timeframe named.

**Two entry modes (v0.10).** An instance runs in one mode, set by `EntryMode` and fixed while it runs. Sections 3.1 (bias), 3.3 (two layers) and 3.6 (skips), and everything from section 4 on, apply to both modes. Sections 3.2, 3.4 and 3.5 describe the ZoneFvg mode. Section 3.7 describes the Breakout mode, in which no zones are detected.

**Entry model of the ZoneFvg mode (v0.11, added on 23 November 2026).** Inside ZoneFvg mode, `EntryModel` chooses the trigger at a touched zone: `Fvg` (3.4 and 3.5, the default) or `Choch` (3.9). Zone detection (3.2), the two layers (3.3) and everything from section 4 on are shared; 3.9 lists what differs.

**SessionRange mode (added on 23 November 2026).** A third mode, described in 3.8. What the paragraph above says about both modes holds for it too; sections 3.2, 3.4, 3.5 and 3.7 do not describe it.

**Indicator definitions (v0.8).** Every indicator depends only on a fixed window of recent bars, so a restarted bot reproduces the same values exactly.

- ATR(14) is the plain average of the true range of the last 14 closed bars.
- EMA(n) is calculated over the last 4 x n closed bars, seeded with the simple average of the first n bars of that window.
- When bars of several timeframes close at the same moment they are processed in the order D1, H4, M15.

### 3.1 Trend bias (D1)

- Long bias: last closed D1 close is above the D1 EMA(50).
- Short bias: last closed D1 close is below it.
- Only demand zones are traded in a long bias, only supply zones in a short bias.
- Both zone types are always detected and stored, because opposing zones are needed for the room check in section 3.6.
- In Breakout mode (3.7) a long bias allows only buys and a short bias only sells.
- Breakout trend filter (added on 23 November 2026, owner request): with `TrendSlowEmaPeriod` greater than zero, Breakout mode also needs the D1 EMA(`BiasEmaPeriod`) above the D1 EMA(`TrendSlowEmaPeriod`) for a buy and below it for a sell, both on the last closed D1 bar; otherwise no breakout signal is made that day. `TrendSlowEmaPeriod` 0 switches the filter off (the rule before 23 November). ZoneFvg mode does not use it.

### 3.2 Demand zone detection (H4)

Checked at each H4 bar close.

1. **Impulse leg.** The last 1 to 3 bars are all bullish and their net move (close of last minus open of first) is at least `ImpulseAtrMult` (2.0) x ATR(H4).
2. **Break of structure.** The leg's final close is above the most recent confirmed swing high that formed before the leg began. A swing high is a bar whose high is above the highs of `SwingStrength` (3) bars on each side. Only swings within the last 100 H4 bars are considered; with none, there is no break of structure and no zone.
3. **Base.** The 1 to 3 bars immediately before the leg, collected backward while each bar's range is at most 1.0 x ATR(H4). At least one bar is always taken.
4. **Zone edges.**
   - Distal (far) line = lowest low of the base bars and the first impulse bar.
   - Proximal (near) line = highest candle body top of the base bars.
5. **Width check.** Zone height must be between 0.2 and 1.5 x ATR(H4); otherwise discard.
6. **Overlap.** A new zone replaces any older, still-untouched demand zone that it overlaps by more than 50% of the older zone's height. (Changed in v0.8: "keep the older one" made the result depend on arbitrarily old history, which would break exact rebuilding after a restart.)
7. **Activation.** The zone becomes active at the open of the next H4 bar.
8. **Zone id.** Built from type and the base's first bar time, for example `D-20261112-1600`. The same history always produces the same id, which is what lets a restarted bot match its trades to zones.

### 3.3 Two layers: signals and execution

This split is the basis of restart safety.

**Signal layer.** Depends only on closed price bars. Given the same history it always produces the same result, so it can be rebuilt at any time by replaying bars.

| Zone state | Meaning | Transition |
|---|---|---|
| Active | Fresh, never touched | First M15 bar with low at or below proximal: Touched |
| Touched | Confirmation window open | Valid FVG appears: Signalled. Window ends: Used |
| Signalled | A signal was produced | Immediately Used |
| Used | No further signals | Removed after expiry |
| Invalid | Broken | Any M15 close below distal |

- A zone expires after `MaxZoneAgeBars` (120 H4 bars, about 20 trading days).
- Each zone produces at most one signal. Whether that signal became a trade does not change the zone's state.

**Execution layer.** Decides whether a signal becomes an order (filters, news, risk limits), and tracks orders and positions. Its source of truth is always the broker's list of orders, positions and history, never the bot's memory.

| Setup state | Meaning |
|---|---|
| Skipped | Signal blocked; reason logged |
| Placed | Limit order working at the broker |
| Filled | Position open |
| Closed | Position closed; result journaled |

### 3.4 Entry trigger: bullish FVG (M15)

Naming the three most recently closed M15 bars c3, c2, c1 (c1 is the newest):

- **FVG exists** when Low(c1) > High(c3). The gap runs from High(c3) up to Low(c1).
- c2 must be bullish (close above open).
- Gap height is at least `MinFvgAtr` (0.25) x ATR(M15). The gap must also be at least 3 x the current spread; because spread is not bar data, that check is applied by the execution layer (section 3.6).
- The FVG completes within `ConfirmWindowBars` (16 bars, 4 hours) of the touch.
- The gap's lower edge is no higher than proximal + 0.5 x ATR(H4), so the entry stays close to the zone.
- Only the first valid FVG after the touch is used.

### 3.5 Order levels

- **Entry:** buy limit at the gap midpoint (`EntryLevelPct` = 50).
- **Stop:** the lower of the zone's distal line and the lowest low since the touch, minus a buffer of 0.1 x ATR(M15). For sells, add the current spread, because sell stops trigger on the ask price.
- **Target:** entry + `RewardRisk` (2.0) x stop distance.
- **Order expiry:** `EntryExpiryBars` (8 bars, 2 hours), shortened if a news window starts sooner (section 6).

### 3.6 Signal is skipped when

- It is not live: its confirmation bar closed more than one bar ago (for example, it was found during a restart replay).
- Stop distance is under `MinStopPips` (12 pips for the forex pairs, 8 pips for gold: FTMO's cTrader reports a gold pip of 1 USD, and the 800 of earlier versions assumed a pip of 0.01 USD) or over 2.0 x ATR(H4).
- **Cost check fails:** spread plus round-trip commission, in pips, is more than `CostToRiskMax` (10%) of the stop distance.
- An active supply zone sits between entry and 1.5R above entry (ZoneFvg mode).
- The news filter blocks it (section 6).
- Any filter in section 5 or any limit in section 4 blocks it.

Every skip is logged with its reason.

In Breakout mode the two zone checks (the gap of at least 3 x the spread from 3.4, and the room to an opposing zone) do not apply, and section 3.7 adds two of its own. The stop bounds and the cost check apply as written.

With the Choch entry model (3.9) the gap check and the room check do not apply either; the reward check of 3.9 takes the place of the room check, and the smallest stop is `ChochMinStopPips`.

### 3.7 Breakout mode (v0.10)

Chosen with `EntryMode` = Breakout, the default. It follows the D1 trend into a break of the previous day's range. The reason for the mode is in the plan pull request of 22 November 2026: published evidence supports trend and momentum continuation better than gap-fill entries. For this bot it is unproven until section 12 says otherwise.

**Reference day.** Each time a D1 bar closes, that bar becomes the reference: its high, its low and its open time. It replaces the previous reference.

**Bars that are checked.** An M15 bar is checked against the reference only if

- it opened at or after the reference bar closed (so the M15 bar that closes together with the D1 bar is not checked), and
- it closes inside the trading window: the time of day of its close is at or after `SessionStartUtc` (07:00) and before `SessionEndUtc` (17:00), and
- its close is before `BreakoutLastConfirmUtc` (08:00 by default; a value equal to `SessionEndUtc` switches this off). A bar that closes later is checked for nothing and makes the reference finished for the day, so a later breakout is not traded (added on 23 November 2026, owner request: in the backtests of 2023–2026 breakouts confirmed in the first hour did better than later ones in both halves of the period), and
- it opened less than 24 hours after the first M15 bar that opened at or after the reference bar closed. A reference serves one day of M15 bars: when the D1 series is one bar behind, the bars of the following day are not checked against the range of the day before (added on 22 November 2026).

**Confirmation.** With a long bias (3.1), the first checked bar that closes above the reference high confirms a buy. With a short bias, the first checked bar that closes below the reference low confirms a sell. With a neutral bias nothing confirms. A close beyond the level on the side the bias does not trade changes nothing. A reference gives at most one signal; after its confirmation, or when the next D1 bar closes, it is finished. If price is already beyond the level when the window opens, the first checked bar that closes beyond it is the confirmation.

**Order levels (buy).** Naming the confirming bar c1:

- **Entry:** buy stop at High(c1) + `BreakoutEntryBufferAtr` (0.1) x ATR(M15). The order fills only if price goes on beyond the confirming bar.
- **Stop:** entry minus `BreakoutStopAtr` (1.0) x ATR(H4).
- **Target:** entry + `RewardRisk` (2.0) x stop distance.
- **Order expiry:** `EntryExpiryBars` (8 bars, 2 hours), shortened if a news window starts sooner (section 6), as for a limit order.
- **Spread.** Charts show bid prices. A buy stop triggers on the ask price, so the execution layer adds the current spread to a buy entry: the order then triggers when the bid reaches the level above. A sell stop triggers on the bid; for a sell the spread is added to the stop, as in 3.5. Stop and target of a buy, and entry and target of a sell, stay the signal's.

**Signal id.** `BH-yyyyMMdd-HHmm` for a break of the high (buy) and `BL-yyyyMMdd-HHmm` for a break of the low (sell), from the UTC open time of the reference D1 bar. It takes the place of the zone id in the label (5.2), in recovery (8.3), in the journal and in the trade log.

**Skipped when** (besides the checks of 3.6 that apply):

- The confirming bar overlaps a news window: a breakout made by news is not a signal (6.1).
- Price has already reached the entry when the order is about to be placed.

**What stays the same.** One position or pending order per symbol, sizing and the ladder, every account limit and circuit breaker, the session and Friday rules, the news windows, order expiry, idempotent placement, recovery, Friday close, alerts and the simulation tools.

**What the baseline does not have.** No trailing stop, no break-even move and no partial close: a trade ends at its stop, its target or the Friday close, as in ZoneFvg mode. Every exit therefore stays at the broker and works with the bot offline, and results come as clean wins and losses, which the ladder of 4.1 is built on. A trailing stop is the first variant to test (12.4).

### 3.8 SessionRange mode (added on 23 November 2026)

Chosen with `EntryMode` = SessionRange. It follows the D1 trend into a break of the range that price made in the Asian session. The reason for the mode is the strategy search of 23 November 2026 (`research/search/REPORT-2026-11-23.md`): of 1,358 rule variants this rule was the only one that still made money on the two years the search had not used, and it did so on gold only. For this bot it is unproven until section 12 says otherwise.

**New York time.** The times of this mode are New York wall-clock times, because the sessions they describe move with daylight saving time. The core computes New York time from UTC with the United States rule: UTC-4 from 07:00 UTC on the second Sunday of March until 06:00 UTC on the first Sunday of November, UTC-5 otherwise. No time zone database is used.

**Trading day.** A trading day starts at 17:00 New York and ends at 17:00 New York on the next calendar day. It is named by the date on which it ends. A trading day that ends on a Saturday or a Sunday gives no signal. The times of this mode are read in the order of the trading day: 19:00 comes two hours after its start, 03:00 ten hours, 12:00 nineteen hours and 16:30 twenty-three and a half hours after it.

**The range.** The range bars of a trading day are the closed M15 bars that opened at or after `RangeStartNy` (19:00) and before `RangeEndNy` (03:00). The range high is their highest high, the range low their lowest low.

**Evaluation.** The range is evaluated once per trading day: at the first closed M15 bar of that trading day whose close time is at or after `RangeEndNy`. A setup exists only if all of these hold; otherwise the trading day gives no signal:

- that close time is before `RangeLastEntryNy` (12:00);
- there are at least 16 range bars (half of the window);
- the bias of 3.1 is long or short (`TrendSlowEmaPeriod` is a Breakout setting and is not used here);
- range high minus range low is at least `RangeMinWidthAtr` (0.5) x ATR(D1) of the last closed D1 bar;
- ATR(H4) of the last closed H4 bar exists.

**Order levels.** With a long bias: a buy stop at the range high. With a short bias: a sell stop at the range low. Only the side of the bias is traded.

- **Stop:** `RangeStopAtr` (1.0) x ATR(H4) from the entry.
- **Target:** `RewardRisk` (2.0) x stop distance.
- **Spread:** as in 3.7.
- **Order expiry:** `RangeLastEntryNy` of the trading day. On a Friday it is not later than `FridayNoEntryUtc`. A news window shortens it as for every order (section 6).

**Void.** While the day's order is pending, a closed M15 bar that opened at or after `RangeEndNy` and whose low is at or below the range low (buy setup), or whose high is at or above the range high (sell setup), makes the setup void: price went the other way first. The order is cancelled and the trading day gives no further signal.

**One signal per trading day.** After a setup, whatever becomes of its order, the trading day gives no further signal.

**Timed exit.** At the first closed M15 bar whose close time is at or after `RangeExitNy` (16:30) on the date in the position's signal id, an open position of this mode is closed at market. If the market is closed at that moment, the position is closed when the market reopens, as for the Friday close (5.5). The duty runs in every connected state, as the Friday close does.

**Reported trading hours (added on 23 November 2026, after the owner's first backtest of this mode).** The bar that makes the exit due is itself proof that the market trades. At that bar the close is sent whatever trading hours the platform reports. On FTMO's cTrader the reported hours of gold do not move with winter time: they end at 20:50 UTC all year, while in winter gold trades until 21:49 UTC, so a bot that trusts them waits from 21:30 UTC until 22:05 UTC and carries the position into the daily break. The wait for a reopening applies only when the exit becomes due without a bar, or when the broker refuses the close.

**Signal id.** `RH-yyyyMMdd` for a buy at the range high and `RL-yyyyMMdd` for a sell at the range low, from the date that names the trading day. It takes the place of the zone id in the label (5.2), in recovery (8.3), in the journal and in the trade log.

**Skipped when** (besides the checks of 3.6 that apply): price has already reached the entry when the order is about to be placed.

**What stays the same.** One position or pending order per symbol, sizing and the ladder, every account limit and circuit breaker, the session and Friday rules, the news windows, idempotent placement, recovery, Friday close, alerts and the simulation tools.

**What the search measured.** Gold, 6 May 2020 to 22 November 2026, with the rules of this section, gold's smallest stop of 8 USD, an assumed spread of 0.30 USD and no news filter: 202 trades, 55% winners, +0.28R per trade, +56R, largest drawdown 9.4R; +12R in the development years (to October 2024) and +44R in the holdout. With cTrader's own way of filling orders in a backtest the total is +44R. The live bot will differ again: the news filter shortens orders on days with news, and spreads vary.

### 3.9 CHoCH entry model (v0.11, owner's answers of 23 November 2026)

Chosen with `EntryMode` = ZoneFvg and `EntryModel` = Choch. `EntryModel` = Choch with another `EntryMode` is refused at start, because the default `EntryMode` is Breakout and a backtest must not run another rule than the one chosen. The reason for the model: the backtests of the Fvg model showed no edge on EURUSD 2023–2026 (11 trades, +2.2R; with looser values 35 trades, +0.1R), and the owner traded supply and demand by hand with success. He sent 10 annotated TradingView charts and answered the developer's reading of them on 23 November 2026. This section is that method written as rules. For this bot it is unproven until section 12 says otherwise. Long rules are shown; short rules are the mirror image.

**What the owner confirmed.** He trades all pairs (the Dollar Index on one chart was an example, not a filter). Zones are H4 supply and demand; the trade direction comes from the zone: demand gives a buy, supply a sell. The trigger is the change of character ("CHoCH") seen on M15 at the zone; the entry is the retest of the broken level; the stop is beyond the extreme of the move; the target is "before the next supply/demand but at least taking 1:2"; trading all day, with the Friday and news rules kept; no trailing stop.

**Zones.** The zones of 3.2 with the same parameters (a base before an impulse that breaks structure), the same states, supersede rule, invalidation and age (3.3). Only the confirmation window differs: `ChochWindowBars` (32 M15 bars, 8 hours; **proposal**) from the touch bar, which is bar 0. Two more zone types from the owner's charts, a broken swing level that flipped (support becomes resistance and the reverse) and an H4 fair value gap, are **not** part of this version: they need new detection, new ids and their own supersede rules, and are planned as a second work package after the first backtest of this model shows whether the trigger works on the zones of 3.2.

**Bias.** With `ChochUseBias` = false (default; **proposal**, following "the direction comes from the zone") the D1 bias is not read. With true, a zone whose type does not match the bias of 3.1 gives no signal, as in the Fvg model.

**M15 swings.** The swing rule of 3.2 step 2 applied to M15 bars: a swing high is an M15 bar whose high is above the highs of `SwingStrength` (3) bars on each side, confirmed when the third bar to its right has closed, kept while it is among the last 100 M15 bars. Swing lows mirror it.

**Change of character (the trigger).** At each M15 close inside the window of a touched demand zone, take the most recent confirmed M15 swing high: in the owner's charts, the lower high where the last leg down into the zone started. The bar confirms a change of character when it **closes** above that swing high and no bar since the swing high has closed above it. If the most recent swing high was already closed above, the bar confirms nothing (an older swing is not tried). The first such bar of the window is used; a window without one ends the zone (Used). Each zone gives at most one signal.

**Sweep.** In the owner's charts price often first takes out a recent M15 swing low (marked X) before the change of character, but not always. A sweep is a bar, from the touch bar to the confirming bar, whose low goes below the most recent confirmed M15 swing low. With `RequireSweep` = false (default) a sweep is not needed and the journal says whether there was one, so one backtest shows both groups; with true a change of character without a sweep does not count.

**Order levels (buy).**

- **Entry:** buy limit at the broken swing high (the CHoCH level, marked BRK in the owner's charts). The order fills on the retest.
- **Stop:** the lowest low from the touch bar to the confirming bar (normally the sweep low), minus `StopBufferAtr` (0.1) x ATR(M15). With `ChochStopAtDistal` = true (default false; **proposal**) the lower of that low and the zone's distal line is used, as in the Fvg model. The owner's examples had stops of 7 to 13 pips. For sells the spread is added as in 3.5.
- **Target:** just before the next opposing level: the nearest of the proximal lines of the active and touched supply zones and the confirmed H4 swing highs (3.2 step 2) that no later H4 bar has closed above (a broken high is no longer resistance; 23 November 2026) and that lie above the entry, minus `ChochTargetBufferAtr` (0.1; **proposal**) x ATR(H4). When there is none, entry + `RewardRisk` (2.0) x stop distance (**proposal**).
- **Order expiry:** `EntryExpiryBars` (8 bars, 2 hours; **proposal**: the same value as the Fvg model), shortened if a news window starts sooner (section 6).

**Skipped when** (besides the checks of 3.6 that apply):

- The reward is under `MinRewardRisk` (2.0, the owner's minimum of 1:2): target distance divided by stop distance, on the signal's own levels.
- The stop is under `ChochMinStopPips` (5 pips; **proposal**) or over `MaxStopAtr` (2.0) x ATR(H4). The cost check (3.6) still applies and is in practice the stricter floor on small stops: at a spread plus commission of about 0.7 pips on EURUSD, a stop must be at least about 7 pips to pass `CostToRiskMax` (10%). The size cap is not changed either: at the ladder's usual 0.30% risk a EURUSD stop under about 6 pips needs more than `MaxVolumeUnits` (500,000 units) and is skipped with an alert (4.3); at 0.5% the limit is about 10 pips. Both caps are the owner's money decisions and stay as they are.
- The confirming bar overlaps a news window (section 6.1).
- Price is already back at the entry when the order is about to be placed.

**What stays the same.** One position or pending order per symbol, sizing and the ladder, every account limit, circuit breaker and sanity cap (including `MaxVolumeUnits`), the Friday rules, the news windows, idempotent placement, recovery, Friday close, alerts and the simulation tools. A trade ends at its stop, its target or the Friday close.

**Trading window.** The owner trades all day. `SessionStartUtc` = 00:00 and `SessionEndUtc` = 24:00 is accepted from v0.11 and is the setting for this model in the parameter files and the backtest guide. The cBot's default stays 07:00 to 17:00, because the default model is Fvg.

## 4. Risk and position sizing

### 4.1 Size per trade

**Sizing plan (owner decision of 24 November 2026).** The cBot parameter
`SizingPlan` chooses how the risk of each new trade is set: `Ladder` (the
default) or `Recovery` (4.1.1). With `Ladder` everything in this
paragraph and the next is exactly as in v0.11. Both plans feed the same
formula below, and every limit of 4.2 and 4.3 applies after them in both.

**Owner decision 2026-11-16: ladder sizing (the owner's 4-part plan).** The
risk percent of each new trade comes from the ladder rule of requirement
RSK-013 (deal risk 0.30%, split over 4 consecutive trades, never more than
0.5% per trade). Owner decision 2026-11-17: a win ends every open deal; the
growth step lives in four columns C, D, E, F for the 1st to 4th trade after
the last win, a win raises the column of its position (not past a column
that is still 0), steps stop rising once a trade would go over
0.5%, and all steps go back to 0 only when the owner sets a new
`RiskEpochDate` after a payout or for a new account (no monthly reset). The formula below then uses
that percent instead of a fixed 0.5%. With `LadderEnabled` = false the bot
uses the fixed `RiskPercent` as before.

```
riskAmount  = Account.Balance x riskPercent   (ladder, at most 0.5%)
riskAmount  = R of 4.1.1                      (Recovery, at most 1.28% of Account.Balance)
volumeUnits = riskAmount / (stopPips x Symbol.PipValue)
volume      = round DOWN to the symbol's volume step
```

- If the result is below the symbol's minimum volume, skip the trade. Never round up.
- If the result is above `MaxVolumeUnits`, skip the trade (4.3). The volume is never cut down to the cap.
- Example: balance 10,000 USD, stop 20 pips on EURUSD. Risk is 50 USD; pip value per unit is 0.0001 USD; volume = 50 / (20 x 0.0001) = 25,000 units (0.25 lot).
- cTrader's `Symbol.VolumeForProportionalRisk` is logged beside the formula for comparison, because forum users have reported it disagreeing with manual calculation.
- **Margin check:** skip if free margin after the trade would fall below 50% of equity.

#### 4.1.1 The Recovery plan (owner decision of 24 November 2026)

The owner's rule, in his words: the first trade risks 100 USD; after a
loss the next trade risks enough that one win at the target recovers
everything lost since the last win plus a normal win; from the 9th trade
of a losing run every trade risks 1.28% until a win; a win starts again at
100 USD.

```
F  = RecoveryFirstRiskUsd                 (default 100, account currency)
RR = RewardRisk                           (the instance's parameter, default 2.0)
L  = money lost in the current losing run (see below)
R  = min( (L + RR x F) / RR ,  Account.Balance x RecoveryMaxRiskPercent / 100 )
                                          (RecoveryMaxRiskPercent default 1.28)
```

- **The losing run.** L is the sum of the absolute net results (profit,
  commission and swap, as the broker booked them, so costs and slippage
  count) of the bot's closed trades that closed after the latest win, from
  broker history since `RiskEpochDate`. A win is a closed trade with a net
  result above zero; a break-even trade (net exactly 0) or a losing trade
  continues the run; a win ends it, and the next trade risks F again. With
  no win since `RiskEpochDate` the run starts at `RiskEpochDate`; with no
  closed trade at all L = 0 and the trade risks F.
- **The owner's sequence** at RR = 2 and F = 100, every trade losing exactly
  its risk: 100, 150, 225, 337.50, 506.25, 759.38, 1,139.06, 1,708.59, then
  (2,562.89 before the cap). On a 200,000 USD account the 9th trade is
  capped: after eight losses the balance is 195,074.22 and 1.28% of it is
  2,496.95, so from the 9th trade of the run every trade risks 1.28% of
  the current balance until a win. At a balance of exactly 200,000 the cap
  is 2,560.
- **Why it recovers.** A win at the target RR returns RR x R = L + RR x F:
  the whole run plus a normal win. When the cap applies it no longer
  recovers the whole run.
- **What counts as a trade, and in which order** (the ladder's conventions,
  RSK-013 and RSK-016): one trade is everything with the same label (a
  partial fill, its remainder and partial closes belong together); it is
  closed when nothing of it is open or pending, and its result is the sum
  of its closed parts. Trades placed before `RiskEpochDate` are not
  counted. Closed trades are taken in order of close time; at the same
  instant in order of placement time, then label. A trade that closed at
  the same instant as the new trade is placed counts as closed before it.
- **Several instances at once.** Only closed trades change L. Trades still
  open or pending, of this or another instance, do not: two trades placed
  while nothing closes in between risk the same amount, and both of their
  results then count in the run. The open-risk cap and the daily budget
  (4.2) still limit what can be open together.
- **Nothing is remembered.** R is recomputed from broker data at every
  placement, so a restart gives the same amount. If the history cannot be
  read or is invalid, no order is placed.
- **The owner was told and accepted** (24 November 2026): continuing at
  1.28% after a long losing run fails much more often than starting
  again (simulation at a 40% win rate: pass 59%, fail or stop 32% within
  500 trades); a long enough losing run breaks FTMO's 10% limit. With the
  cap taken from the current balance and each loss equal to its risk, the
  drawdown after 13 losses in a row is 8.55% of 200,000, so the 8% kill
  switch halts after the 13th; FTMO's 10% would be passed by the 15th (14
  with the cap taken as a flat 2,560). Gaps and slippage make each loss
  larger, and the daily budget (2%) lets only one loss of the capped size
  happen per day.
- `SizingPlan` = `Recovery` does not use `LadderEnabled`, `RiskPercent` or
  the ladder values for sizing (RSK-016).

**At 200,000 USD (FTMO simulated capital, from v0.12)**

| Item | Amount |
|---|---|
| Risk per trade, Ladder (at most 0.5%) | at most 1,000 USD |
| Risk per trade, Recovery | 100 USD first, at most 1.28% of balance (2,560 USD at 200,000) |
| Total open risk cap (1.5%) | 3,000 USD |
| Daily risk budget (2%) | 4,000 USD |
| Drawdown kill switch (8%) | 16,000 USD |
| 2% monthly target | 4,000 USD |
| FTMO daily floor (2-Step) | Balance at 00:00 CE(S)T minus 10,000 USD |
| FTMO overall floor (2-Step) | 180,000 USD |

| Stop distance on EURUSD | Size at 1,000 USD risk (0.5%) | Margin at 1:30 (Swing) |
|---|---|---|
| 12 pips (smallest allowed) | 833,000 units: above `MaxVolumeUnits` (500,000), skipped | n/a |
| 20 pips | 500,000 units (5 lots), exactly the cap | About 9% of the account |
| 50 pips | 200,000 units (2 lots) | About 4% |

- **The volume cap at 200,000.** `MaxVolumeUnits` (500,000 units, 5 lots;
  gold 200 ounces) is unchanged (owner decision T). A forex trade can
  therefore risk at most 500,000 x stop pips x 0.0001 USD: 1,000 USD at a
  20-pip stop, 2,560 USD only at a stop of 51.2 pips or more. Larger
  Recovery amounts on shorter stops are skipped as `VolumeAboveCap`, and
  the run continues with the next trade. Gold: 200 ounces x the stop in
  USD, so 2,560 USD needs a stop of 12.80 USD or more.
- **Leverage.** The Swing account type has 1:30 leverage. The smallest allowed stop was raised from 8 to 12 pips so that three simultaneous trades still fit inside the margin check. This also narrows the range of position sizes, which suits FTMO's rule on consistent sizing. The Recovery plan sizes trades very differently by design.
- **Fills.** These sizes are normal for major pairs, but partial fills on limit orders and slippage on stops become more likely than with small sizes. Both are logged per trade and stress-tested in section 12.1.
- **Backtests** use a 200,000 USD starting balance (100,000 before v0.12) so sizes and commissions are realistic.

**Minimum practical account.** With a 1,000-unit minimum and a 30-pip stop, the smallest possible risk is about 3 USD, which is 0.5% of 600 USD. Below roughly that balance many trades will be skipped. This does not apply at 200,000 USD.

**What sizing cannot prevent.** A price gap through the stop (weekend, major news) can lose more than the planned risk. The news filter and Friday rules reduce this; they do not remove it. In the Recovery plan such a larger loss also raises the next trade's risk, because L is the money actually lost.

### 4.2 Account-level limits (all recalculated from broker data, nothing remembered)

| Limit | Default | How it is calculated | Effect |
|---|---|---|---|
| Total open risk | 1.5% | Stop distance x volume of every bot position and pending order | At most three trades at risk at 0.5% each; with Recovery one trade of 1.28% leaves room for 0.22% |
| Same-direction exposure per currency | 2 | Buying EURUSD counts as long EUR and short USD. For each currency, at most two bot positions or orders on the same side | Limits correlated exposure, for example long EURUSD, GBPUSD and AUDUSD together |
| Daily risk budget | 2% of initial capital (4,000 USD at 200,000; 1.5% before v0.12) | Measured the way FTMO measures its daily limit: from the balance at 00:00 CE(S)T. A new trade is allowed only if equity would still be within 4,000 USD of that balance even if every open stop, including the new one, were hit (commission included) | No new entries until the next CE(S)T day; pending orders cancelled |
| Drawdown kill switch | 8% | Highest closing balance since `RiskEpochDate` (from history) minus current equity | Halt (section 8.5). Resumes only when you set a new `RiskEpochDate` |
| FTMO emergency guard | Equity within 1% of initial capital (2,000 USD) of either FTMO floor | Daily: equity at or below the 00:00 CE(S)T balance minus 8,000 USD. Overall: equity at or below 182,000 USD | Close all positions, cancel orders, Halt. The only case where the bot closes trades for risk reasons |

Because these are computed from the account's own records, a restart cannot reset them. The balance at 00:00 CE(S)T is rebuilt from the current balance and the trades closed since then.

With the daily risk budget, a normal worst day loses about 4,000 USD against FTMO's 10,000 USD allowance (amounts at 200,000 USD). The emergency guard exists for abnormal events such as a gap through several stops; breaching an FTMO floor ends the account, so closing early is the lesser loss.

### 4.3 Circuit breakers and sanity caps

Protection against code errors rather than market risk.

| Check | Default | Action |
|---|---|---|
| Entries per symbol per day | 2 | Skip further signals |
| Entries per account per day | 6 | Skip further signals |
| Order requests per instance per hour | 20 | Halt |
| Order requests per instance per day | 100 | Halt. Five instances stay far below FTMO's 2,000-a-day limit |
| Unhandled errors per hour | 5 | Halt |
| Calculated risk above `SanityCapPercent` (0.55%) of balance | Off (`SanityCapEnabled` = false, owner decision of 24 November 2026) | When on: skip and alert |
| Volume above `MaxVolumeUnits` | Per symbol: 500,000 units for the forex pairs, 200 for gold (ounces) | Skip and alert |
| Stop or target on the wrong side of entry | n/a | Skip and alert |

**With the per-trade sanity cap off (the default from v0.12).** The cap was
the last check tied to the current balance alone. What still bounds a
single trade, as the code stands (RSK-002, RSK-004, RSK-006, PreTradeFilters):

- **Open-risk cap (4.2), live and in backtests:** the new trade's risk at
  its stop plus that of every open bot position and pending order must not
  exceed 1.5% of initial capital (3,000 USD at 200,000). For one trade
  alone this is the tightest money limit, tighter than the daily budget.
- **Daily risk budget (4.2), live only:** the new trade's risk at its stop
  plus its commission, plus today's realised loss and the worst case of
  everything open, must not exceed 2% of initial capital (4,000 USD). In a
  backtest it only writes "would block" (8.5, decision C) and bounds
  nothing.
- **`MaxVolumeUnits` and the broker's largest volume, live and in
  backtests:** a trade above either is skipped. This is the only bound that
  does not use the symbol's pip size and pip value.
- **Margin check:** free margin after the trade at least 50% of equity.

The sanity cap, the open-risk cap and the daily budget all compute money
from the same stop price, pip size and pip value. Bad price data or a
wrong pip value from the broker would deceive all three alike, so the
sanity cap never protected against that by itself; what it added was a
limit of 0.55% of the current balance, below the 1.5% and 2% of initial
capital above. None of these limits covers a loss beyond the stop (a gap
or slippage); the kill switch and the emergency guard act afterwards.
With `SanityCapEnabled` = true and the Recovery plan, every Recovery amount
above `SanityCapPercent` of balance is skipped (1,100 USD at 200,000 with
0.55%), so the percent has to be at least `RecoveryMaxRiskPercent` for the
plan to work as the owner described.

## 5. Orders and execution

### 5.1 Filters checked before placing an order

- Time is inside `SessionStartUtc` to `SessionEndUtc` (07:00 to 17:00 UTC; 00:00 to 24:00 means all day, v0.11), and before `FridayNoEntryUtc` (12:00) on Fridays.
- Current spread is at most `MaxSpreadPips` (1.5 for the forex pairs, 40 for gold).
- The symbol's market is open.
- No existing bot position or pending order on the symbol.
- Not inside a news window; calendar data is valid (section 6).
- Bot state is Running (not Recovering or Halted).

### 5.2 Order format

- A pending order with stop loss, take profit and expiry attached at placement: a limit order in ZoneFvg mode (both entry models), a stop order in Breakout mode (v0.10). These live on the broker's server and keep working when the bot is offline. An order of the other type is never sent: a breakout signal is only ever placed as a stop order and a zone signal only ever as a limit order (fail closed).
- **Label:** `ZFVG|v1|<symbol>|<zoneId>`. Identifies the bot, its version, and the zone. In Breakout mode the breakout id of 3.7 takes the place of the zone id.
- **Comment:** planned entry, stop, target, risk amount and a short hash of the parameters. This is the trade's memory; it is stored at the broker and comes back in positions and history.

### 5.3 Idempotent placement

1. Immediately before sending, query the broker for any order or position with this zone's label. If one exists, do nothing.
2. Send the order with a synchronous call and read the result.
3. On success, verify the order now appears with the expected stop, target and expiry.
4. On failure with a clear rejection (bad volume, market closed, no money): log, mark Skipped, do not retry.
5. On an unclear outcome (timeout, disconnected, technical error): **do not resend.** At the next bar close, repeat step 1. Resend only if nothing exists at the broker, the signal is at most one bar old and price has not traded through the entry. For a stop order (v0.10) "traded through" means that a closed bar reached the signal's entry: its high for a buy, its low for a sell. "Nothing exists at the broker" includes the history (v0.10, added on 22 November 2026): if a trade with this label has closed since the signal's confirmation, the order was applied, filled and closed, and nothing is resent; if the history cannot be read (no list, or a list with a missing entry), nothing is resent either.

### 5.4 After a fill

- Confirm the position has a stop loss and take profit. If the stop is missing, set it from the comment data. If that fails twice, close the position and alert.
- Partial fills are kept as a normal position; the unfilled remainder dies at order expiry.

### 5.5 Open position management (v1 baseline)

- No break-even move and no trailing stop. The trade ends at stop, target or Friday close. In SessionRange mode it also ends at the timed exit of 3.8 (added on 23 November 2026).
- Friday 20:00 UTC: close positions and cancel pending orders. If the market is already closed then (an early close before a holiday), no request is sent; the positions are closed and the orders cancelled when the market reopens (v0.9).

## 6. News filter

### 6.1 Rule

For each symbol, the relevant currencies are its base and quote (EURUSD: EUR and USD). For gold (XAUUSD) they are XAU and USD; no calendar publishes XAU events, so gold is blocked by USD events only (and by "All" events and manual blackouts).

- **News window:** from `NewsBeforeMin` (30) minutes before to `NewsAfterMin` (30) minutes after every high-impact event for a relevant currency.
- **No new orders** inside a window.
- **FVG made by news is not a signal:** if any of the three FVG bars overlaps a window, the signal is skipped.
- **Breakout made by news is not a signal (v0.10):** in Breakout mode, if the confirming bar overlaps a window, the signal is skipped.
- **CHoCH made by news is not a signal (v0.11):** with the Choch entry model, if the confirming bar overlaps a window, the signal is skipped (**proposal**, the same rule as for a breakout).
- **Pending orders never sit through news:** when an order is placed, its expiry is set to the earlier of the normal expiry and the next window's start. If that leaves under 15 minutes, the signal is skipped. Because expiry is enforced by the broker, this works even if the bot is offline.
- **Open positions** are left alone in the baseline; their stops are at the broker. Closing before news is a variant to test (section 12.4).

### 6.2 Calendar data

cTrader has no confirmed built-in calendar feed for bots, so the calendar comes from outside through a small provider interface with three sources.

| Source | Used when | Notes |
|---|---|---|
| Two live feeds over HTTP | Live and demo | Event time, currency and impact from each feed, combined as described below |
| Embedded history | Backtests | High-impact events for the traded symbols' currencies over the test period (gold adds none: its currency is USD), compiled into the bot. cTrader's HTTP calls during a backtest return today's data, not historical data, so the live feed must be off in backtests |
| Manual list | Any time | `ExtraBlackouts` parameter for unscheduled events and holidays |

**Live feed handling**

- Fetch at start and every `NewsRefreshHours` (4). Never more than once an hour, including across restarts, so a restart loop cannot trigger the provider's rate limit.
- Validate every download: it must parse, contain events, and cover the current week. A failed or invalid download never replaces good data.
- Keep the last good copy in local storage.
- All times converted to UTC on load.
- HTTP calls must not delay bar processing; they run on a timer between bars.

**Live sources**

| | Forex Factory weekly export | ShowMyTrades calendar API |
|---|---|---|
| Cost and key | Free, no key | Free, no key |
| Looks ahead | Current week only | Up to 30 days |
| Impact rating | High, medium, low; the rating most retail traders follow | High, medium, low; assigned by the provider |
| Times | Need converting to UTC | Already UTC |
| Request limits | About two requests per five minutes; file refreshed hourly | 600 per minute; responses cached 15 minutes |
| Terms | No official API or agreement | States that automated and commercial use is allowed |
| Weakness | Unofficial; one week of data | Small provider; no promise of completeness or timing |

**Owner decision 2026-11-14:** run on Forex Factory alone. The ShowMyTrades column above is kept for reference; it is not built until the owner supplies a real sample. With one feed, "one feed down" means no feed, so the fail-safe (6.3) blocks entries.

**How the two are combined** (when a second feed is added)

- An event is treated as high impact if **either** feed rates it high.
- If one feed is down, the other is used alone and an alert is sent.
- Both downloads are cached. The cache is trusted for up to `NewsMaxCacheHours` (48) without a successful refresh.
- Each feed's events are logged, so their differences can be reviewed weekly and a weak feed replaced.

**Why two.** Neither free feed comes with a service guarantee, so each backs up the other. Forex Factory's ratings are also the ones in the backtest history (6.4); treating an event as high impact when either feed says so keeps the live filter at least as strict as the backtested one.

### 6.3 Fail-safe

If neither feed has refreshed within `NewsMaxCacheHours`, or the cached data does not cover the current time, `NewsFailMode` = Block applies: no new entries until a feed returns, and an alert is sent. Existing trades are unaffected.

### 6.4 Backtest parity

The same filter code runs in backtests using the embedded history. Results are reported with the filter on and off, so its effect is measured rather than assumed.

**Building the history file (phase 4)**

- **Main source:** a public archive of the Forex Factory calendar covering January 2007 to April 2025.
- **Top-up:** April 2025 to November 2026 from a second source (section 6.5), then kept current from the live feeds' own logs.
- **Keep only** high-impact events for USD, EUR, GBP, JPY and AUD.
- **Checks before use:** confirm the archive's time zone and convert to UTC; spot-check at least 30 known releases (payrolls, CPI, central bank decisions) against the official schedules; check daylight-saving weeks specifically.
- The archive was collected from a public website for research use. It is used here for personal backtesting only and is not redistributed.

### 6.5 Other options considered

| Option | Cost | Why not chosen |
|---|---|---|
| financecalendar.com | Free key: 100 responses a month. Paid from 9.99 USD a year | Free allowance is too small for refreshing several times a day; history is limited. A reasonable cheap third feed if one of the two proves weak |
| biquote.io | Free, no key | About 90 days of data each way; limits and terms not stated |
| JBlanked News API | Free key: one request per five minutes. Paid tier reported at 47 USD a month | Usable as a replacement feed; details could not be verified |
| Financial Modeling Prep | Free plan of 250 calls a day; paid plans from roughly 20 to 30 USD a month | Has an impact field and about ten years of history, so it is the first candidate for the history top-up. Whether the free plan includes the calendar needs a test call |
| EODHD | About 20 to 60 USD a month | Paid, with no clear advantage for this use |
| Trading Economics | Calendar access reported as enterprise-only, or from about 149 USD a month | Far more than this bot needs |
| Finnhub | Calendar only on a 3,500 USD a month plan | Not realistic |
| FXStreet, Benzinga | Licensed, price on request | Aimed at brokers and publishers |

If you later want a single paid provider with an account and support, Financial Modeling Prep is the one to test first.

## 7. Parameters

Only the four marked **Optimise** are searched. New in v0.2 are marked (new).

In Breakout mode (v0.10) the zone and gap parameters produce no signals (SwingStrength, ImpulseAtrMult, MaxImpulseBars, MaxBaseBars, RequireBos, MinZoneAtr, MaxZoneAtr, ConfirmWindowBars, MinFvgAtr, EntryLevelPct, StopBufferAtr, MinFvgSpreadMult, MinRoomR); they still set the amount of history the bot loads, which is the same in both modes. The searched parameters are three: BiasEmaPeriod, BreakoutStopAtr and RewardRisk.

| Parameter | Default | Role | Test range |
|---|---|---|---|
| EntryMode (new in 0.10) | Breakout | Entry mode of the instance: Breakout, ZoneFvg or SessionRange (section 3) | Both are backtested |
| EntryModel (new in 0.11) | Fvg | Trigger in ZoneFvg mode: Fvg or Choch (3.9). Choch with another EntryMode is refused | Both are backtested |
| RequireSweep (new in 0.11) | false | Choch: a sweep of an M15 swing must come before the change of character | Test false and true |
| ChochWindowBars (new in 0.11) | 32 (proposal) | Choch: M15 bars after the touch in which the change of character must come | Fixed |
| ChochUseBias (new in 0.11) | false (proposal) | Choch: also require the D1 bias of 3.1 | Test false and true |
| ChochStopAtDistal (new in 0.11) | false (proposal) | Choch: stop beyond the zone's distal line too, not only beyond the low since the touch | Test false and true |
| ChochTargetBufferAtr (new in 0.11) | 0.1 (proposal) | Choch: target this far (x ATR H4) before the next opposing level | Fixed |
| ChochMinStopPips (new in 0.11) | 5 (proposal); gold 3 (proposal) | Choch: smallest allowed stop, in place of MinStopPips | Per symbol |
| MinRewardRisk (new in 0.11) | 2.0 | Choch: smallest target distance in R (owner: at least 1:2) | Fixed |
| BreakoutEntryBufferAtr (new in 0.10) | 0.1 | Breakout entry beyond the confirming bar (x ATR M15) | Fixed |
| BreakoutStopAtr (new in 0.10) | 1.0 | Breakout stop distance (x ATR H4) | **Optimise** (Breakout mode) 0.75 to 1.5, step 0.25 |
| BiasTimeFrame | D1 | Trend filter timeframe | Fixed |
| ZoneTimeFrame | H4 | Zone timeframe | Fixed |
| BiasEmaPeriod | 50 | Trend EMA | **Optimise** 50, 100, 200 |
| AtrPeriod | 14 | All ATR values | Fixed |
| SwingStrength | 3 | Bars each side of a swing | Fixed |
| ImpulseAtrMult | 2.0 | Minimum impulse size | **Optimise** 1.5 to 3.0, step 0.5 |
| MaxImpulseBars | 3 | Bars in an impulse leg | Fixed |
| MaxBaseBars | 3 | Bars in a base | Fixed |
| RequireBos | true | Impulse must break structure | Test on and off |
| MinZoneAtr / MaxZoneAtr | 0.2 / 1.5 | Zone height limits | Fixed |
| MaxZoneAgeBars | 120 | Zone expiry | Fixed |
| ConfirmWindowBars | 16 | Bars allowed for the FVG | Fixed |
| MinFvgAtr | 0.25 | Minimum gap height | **Optimise** 0.15 to 0.45, step 0.10 |
| MinFvgSpreadMult (new) | 3 | Gap height versus spread | Fixed |
| EntryLevelPct | 50 | Entry depth into the gap | Test 0, 50, 100 |
| EntryExpiryBars | 8 | Limit order life | Fixed |
| StopBufferAtr | 0.1 | Stop buffer | Fixed |
| MinStopPips | 12 (was 8); gold 8 (a gold pip is 1 USD on FTMO's cTrader; written as 800 before, for a pip of 0.01 USD) | Smallest allowed stop | Per symbol |
| MaxStopAtr | 2.0 | Largest allowed stop (x ATR H4) | Fixed |
| RewardRisk | 2.0 | Target in R | **Optimise** 1.5 to 3.0, step 0.5 |
| MinRoomR | 1.5 | Clear space to opposing zone | Fixed |
| CostToRiskMax (new) | 0.10 | Costs as a share of stop distance | Fixed |
| RiskPercent | 0.5 | Risk per trade | Fixed |
| SizingPlan (new in 0.12) | Ladder | How the risk of a new trade is set: `Ladder` (RSK-013, unchanged) or `Recovery` (4.1.1) | Both are backtested |
| RecoveryFirstRiskUsd (new in 0.12) | 100 | Recovery: risk of the first trade after a win or from `RiskEpochDate`, in account currency | Fixed |
| RecoveryMaxRiskPercent (new in 0.12) | 1.28 | Recovery: largest risk of one trade, in percent of balance | Fixed |
| MaxOpenRiskPercent | 1.5 | Total open risk | Fixed |
| MaxPositionsPerCurrency | 2 | Correlation cap | Fixed |
| DailyLossLimitPercent | 2 (1.5 before v0.12) | Daily stop | Fixed |
| MaxDrawdownPercent | 8 | Kill switch | Fixed |
| InitialCapital (new in 0.6) | 200,000 (100,000 before v0.12) | Base for FTMO percentages; in a backtest it must equal the starting capital | Fixed |
| DayResetZone (new in 0.6) | Europe/Prague | Day boundary at 00:00 CE(S)T, as FTMO uses | Fixed |
| FtmoDailyLossPercent / FtmoMaxLossPercent (new in 0.6) | 5 / 10 | FTMO's own limits for the 2-Step | Per product |
| FtmoGuardBufferPercent (new in 0.6) | 1 | Distance from an FTMO floor at which the emergency guard acts | Fixed |
| MaxRequestsPerDay (new in 0.6) | 100 | Order requests per instance per day | Fixed |
| RiskEpochDate (new) | First live day; empty in a backtest means the backtest's first day (added 23 November 2026) | Start date for drawdown measurement | Set manually live |
| MaxEntriesPerSymbolPerDay (new) | 2 | Circuit breaker | Fixed |
| MaxEntriesPerDay (new) | 6 | Circuit breaker | Fixed |
| MaxVolumeUnits (new) | 500,000 (5 lots); gold 200 (ounces) | Size sanity cap; always on | Per symbol |
| SanityCapEnabled (new in 0.12) | false | The check "calculated risk above SanityCapPercent of balance" (4.3) on or off | Fixed |
| SanityCapPercent (new in 0.12) | 0.55 | Its limit when it is on | Fixed |
| SessionStartUtc / SessionEndUtc | 07:00 / 17:00; 00:00 / 24:00 for Choch (v0.11: 24:00 accepted as the end, not in Breakout mode) | Trading window | Fixed |
| FridayNoEntryUtc (new) | 12:00 | Last entry time on Friday | Fixed |
| FridayCloseUtc | 20:00 | Weekend flat time | Fixed |
| MaxSpreadPips | 1.5; gold 0.4 (a gold pip is 1 USD; written as 40 before) | Spread filter | Per symbol |
| NewsEnabled (new) | true; in a backtest whose embedded news history has no events the filter is off for that run and the start log says so (added 23 November 2026) | News filter on or off | Test on and off |
| TrendSlowEmaPeriod (new in v0.10) | 200 (0 = off) | Breakout trend filter (3.1) | Test 0 and 200 |
| BreakoutLastConfirmUtc (new in v0.10) | 08:00 (17:00 = off) | Latest close time of a confirming bar (3.7) | Test 08:00 and 17:00 |
| RangeStartNy / RangeEndNy (added 23 November 2026) | 19:00 / 03:00 | SessionRange: the range window, New York time (3.8) | Fixed |
| RangeLastEntryNy (added 23 November 2026) | 12:00 | SessionRange: the day's order ends, New York time | Fixed |
| RangeExitNy (added 23 November 2026) | 16:30 | SessionRange: an open position is closed, New York time | Fixed |
| RangeMinWidthAtr (added 23 November 2026) | 0.5 | SessionRange: smallest range, in ATR(D1) | Test 0 and 0.5 |
| RangeStopAtr (added 23 November 2026) | 1.0 | SessionRange: stop distance, in ATR(H4) | Fixed |
| NewsBeforeMin / NewsAfterMin (new) | 30 / 30 | Window around events | Fixed |
| NewsFailMode (new) | Block | Behaviour without calendar | Fixed |
| NewsSources (new in 0.3) | ForexFactory | ForexFactory only until a second feed is added (owner decision 2026-11-14) | n/a |
| NewsMaxCacheHours (new in 0.3) | 48 | Longest time without a successful refresh | Fixed |
| NewsRefreshHours (new) | 4 | Feed refresh interval | n/a |
| ExtraBlackouts (new) | empty | Manual blackout list | n/a |
| HeartbeatUrl (new) | empty | External monitor address | n/a |
| TelegramBotToken / TelegramChatId (new in 0.3) | empty | Telegram alerts. Kept in the parameter file, never in code | n/a |

## 8. Reliability and recovery

### 8.1 Principles

1. **The broker holds the truth.** Orders, positions and history at the broker are the only record of what the bot did. Local memory and local files are a convenience and are never required.
2. **Protection lives at the broker.** Stop loss, take profit and order expiry are attached when the order is created, so they work with the bot offline.
3. **Start, restart and reconnect are one procedure.** The bot always assumes it may have been away.
4. **Never act on the past.** Signals found while catching up are recorded and discarded.
5. **Fail closed.** Doubt means no new trades.

### 8.2 What happens during an outage

| Item | While the bot is offline |
|---|---|
| Open position | Stays open; closes at its stop or target at the broker |
| Pending limit order | Stays at the broker; may fill (with stop and target attached) or expire on schedule |
| News protection for pending orders | Still works, because expiry was set before the window |
| New signals | Missed. They are not taken late |
| Friday close | **Not performed.** A position can stay open over the weekend with its stop and target. This is the main residual risk; see 8.6 |
| Risk limits | Not enforced while offline, but no new trades can open either |

### 8.3 Recovery procedure

Runs at every start and after every reconnect. The bot is in state **Recovering** and cannot trade until it finishes.

1. **Wait for connection and data.** Confirm the server is connected and the latest closed M15 bar is recent. Load at least 300 D1 bars, 500 H4 bars and 3,000 M15 bars, loading more history if needed. If there are too few bars or the data is stale, stay in Recovering and alert. A gap that is still there after reloading the history is the broker's data (an early close, a broker outage): it is accepted and reported, not a reason to stay in Recovering (v0.9, decision A). Time outside the symbol's trading sessions, such as gold's daily break, is not a gap. In Breakout mode the D1 series is stale after one bar length of open-market time since the close of its last bar, not two, because the last closed D1 bar is the reference (v0.10, added on 22 November 2026). Closed time in between delays this check: a missing Friday bar is seen only from Monday 02:00 UTC on. It is the early alert; the guard against trading on such a reference is the one-day life of a reference (3.7).
2. **Rebuild the signal layer.** Replay the stored bars through the same detection code with trading switched off. This restores every zone and its state exactly as if the bot had never stopped.
3. **Read the broker.** List positions, pending orders and recent history carrying the bot's label for this symbol.
4. **Adopt positions.** For each position: match it to its zone by id, read its plan from the comment, and check it has a stop loss and take profit. Missing stop: set it from the comment. Cannot set: close the position and alert.
5. **Check pending orders.** Keep an order only if it has a stop, a target and an expiry and its zone is still valid. Otherwise cancel it.
6. **Handle strangers.** A labelled position whose zone cannot be found (for example after a parameter change) is kept with its existing stop and target, included in risk totals and Friday close, and reported.
7. **Backfill the journal** with trades that opened or closed while the bot was away.
8. **Recalculate limits** from history (daily loss, drawdown, open risk, daily entry counts).
9. **Catch up on duties.** If it is past Friday close, close positions now. If a limit is breached, cancel pending orders.
10. **Report and resume.** Write a recovery report (positions adopted, orders kept or cancelled, zones active, downtime length), send it as an alert, and switch to Running.

### 8.4 Connection handling

- Subscribe to the server's connected and disconnected events. On disconnect: mark the state, stop sending requests. On reconnect: run the recovery procedure.
- Under cTrader CLI a user reported that reconnection restarts the bot's start routine instead of raising the connected event. Running the same recovery from both places covers either behaviour.
- Do not rely on a connection flag before trading. Send the request, read the result, and follow section 5.3.
- After reconnect, check the bar series for gaps and reload history before resuming. A gap still there after the reload is accepted (8.3 step 1).
- Use server time only, never the machine's clock.

### 8.5 Bot states

| State | New orders | Open trades | Leaves the state when |
|---|---|---|---|
| Recovering | No | Protected at broker; Friday close and the emergency guard still run (v0.9) | Recovery completes |
| Running | Yes | Managed | n/a |
| Blocked | No | Managed | Cause clears (calendar back, daily limit resets, data restored) |
| Halted | No; pending orders cancelled | Managed; Friday close still runs | Kill switch or emergency guard: new `RiskEpochDate` after review. Error storm: next CE(S)T day or manual restart |

**Backtests (v0.9, decision C).** In a backtest the bot does not halt: a halt, and the daily budget's block, are only written to the Log as "would halt" or "would block", and the bot keeps trading. The simulation tools (12.1 steps 7 to 9) apply the account limits and FTMO's rules to the complete trade list. This is automatic in backtests and never applies live.

The bot never stops its own process when halted. It keeps running in a non-trading state, which avoids restart loops and keeps Friday close and alerts working.

Every event handler is wrapped so that one error is logged and counted but cannot crash the bot.

### 8.6 Known residual risks

| Risk | Mitigation | What remains |
|---|---|---|
| Offline across Friday close | No entries after Friday 12:00 UTC; heartbeat alert so you can close from the phone app | Weekend gap on an open trade |
| Price gaps through the stop | News filter, Friday rules | Loss above 0.5% on rare events |
| Unscheduled news | `ExtraBlackouts`; stops at broker | Cannot be predicted |
| Calendar provider wrong or late | Validation, cache, fail-closed | A missed or mistimed event |
| Broker-side outage | None possible from the bot | Depends on broker |
| Platform bugs | Defensive checks, pinned CLI version, demo testing before each upgrade | Small |

## 9. Architecture

### 9.1 Modules

| Module | Responsibility |
|---|---|
| MarketData | D1, H4 and M15 series, ATRs, EMA, gap checks |
| BiasFilter | Long, short or neutral from D1 |
| SwingDetector | Confirmed swing highs and lows on H4 |
| ZoneDetector | Section 3.2; creates zones with deterministic ids |
| ZoneBook | Signal-layer state (section 3.3) |
| FvgDetector | Section 3.4 |
| BreakoutTracker | Section 3.7: reference day, confirmation and levels (v0.10) |
| SignalEngine | Produces signals in the instance's entry mode; identical in live, replay and backtest |
| NewsProvider | Live feed, embedded history, manual list; validation and cache |
| RiskManager | Sizing, limits and circuit breakers, all from broker data |
| OrderManager | Idempotent placement, verification, cancellation |
| TradeManager | Post-fill checks, Friday close |
| Recovery | Section 8.3 |
| Health | Bot state, error counting, heartbeat, alerts |
| Journal | Logs, recovery reports, chart drawings on desktop |

The signal-side modules (detectors, ZoneBook, SignalEngine, sizing maths) are plain C# with no cTrader calls, so they can be unit-tested outside the platform.

### 9.2 Data structures

- **Zone:** id, type, proximal, distal, created time, ATR at creation, touch time, lowest low since touch, state.
- **Fvg:** type, upper, lower, time, height.
- **Signal:** zone id, FVG, entry, stop, target, stop pips, bar time. A breakout signal (v0.10) has the breakout id, no FVG, and the level that was broken.
- **Setup:** signal, volume, order id, state, skip reason.
- **NewsEvent:** time (UTC), currency, impact, title.

### 9.3 Event flow

| Event | Work done |
|---|---|
| Start | Validate parameters; run recovery (8.3) |
| Server disconnected | Mark state; stop requests |
| Server connected | Run recovery (8.3) |
| D1 bar closed | Recompute bias. Breakout mode: new reference day |
| H4 bar closed | Update swings; detect zones; expire zones (ZoneFvg mode) |
| M15 bar closed | ZoneFvg mode: update zone states; detect FVG. Breakout mode: check the confirmation. Both: produce signal; filters; place order; resolve unclear orders (5.3); check limits |
| Timer, every minute | Heartbeat; calendar refresh when due; Friday close; news-window state |
| Position or order event | Verify stops (5.4); update setup; journal |
| Error or exception | Log, count, alert; halt at threshold |

### 9.4 Rules the code must respect

- **No look-ahead.** For D1 and H4 series read from the M15 chart, the newest bar is still forming. Use only closed bars.
- **Deterministic.** Same data and parameters always give the same signals.
- **Replay equals live.** Rebuilding from history must give the same zone states as running continuously (tested in 12.2).
- **Stateless risk.** No limit depends on a stored value.
- **No reliance on the stop handler.** Users report it not firing in some environments.
- **Chart calls are optional.** Drawings run only on desktop; headless runs skip them.

## 10. Platform and hosting

### 10.1 Why not cTrader cloud

| Need | cTrader cloud | VPS with cTrader CLI |
|---|---|---|
| HTTP for news feed, heartbeat, alerts | No HTTP requests are sent | Available |
| Stored files and local storage | Deleted when the instance restarts | Kept on a mounted volume |
| Demo instances | 1, stopped after 7 days | No limit |
| Survives your home power or internet failing | Yes | Yes, if the VPS is in a data centre |
| Cost | Free | VPS fee |

The cloud remains a fallback. One of the news feeds offers a WebSocket route that works there, but Telegram alerts and the heartbeat would not.

### 10.2 Stage 2: VPS setup (for live trading)

- **Host:** a small Linux VPS in a commercial data centre, located outside the United States (FTMO does not accept access from a US location). Your home or office power and internet then have no effect on the bot. If you prefer your own machine, add a UPS, set the BIOS to power on when electricity returns, and consider a mobile-data backup link.
- **Runtime:** the official cTrader CLI Docker image. cTrader CLI runs cBots without the desktop application.
- **One container per symbol** (five containers: four forex pairs and gold), each started with `ctrader-cli run` for its symbol on M15, with a parameter file.
- **Automatic restart:** Docker's restart policy restarts each container after a crash or reboot, and Docker itself starts at boot. No login is needed.
- **Persistence:** a mounted volume for the bot's local storage and logs.
- **Credentials:** the cTrader ID password sits in a file readable only by the service user. Secure the VPS (key-only SSH, firewall, automatic security updates).
- **Version control:** pin the CLI image version. Upgrade only after the fault-injection tests pass on demo with the new version.
- **Deployments:** deploy a new bot version only when flat, or after confirming on demo that the new version adopts the old version's trades. Flat means no open position and no pending order of the bot. The same holds for a change of parameters: a position opened under the old values becomes a stranger, and a pending order placed under the old values is kept across the restart with its old levels and size (v0.10, added on 22 November 2026).

cTrader's documentation does not describe what the CLI does after a crash, reboot or disconnect, so the restart behaviour must be proven on a demo account (section 12.3).

### 10.3 Development

- cTrader desktop (Windows or Mac) for coding, visual checks and backtests.
- C#, access rights `None`, no external DLLs, robot time zone UTC.
- Compiled for the .NET version the CLI requires.
- Confirm in phase 3 that HTTP calls and local storage behave under the CLI image as they do on desktop.

### 10.4 Broker conditions to record

Commission per lot, typical spread per symbol, minimum volume and step, minimum stop distance, swap rates, depth of tick history, and whether the account is hedging or netting.

### 10.5 Stage 1: your laptop (demo account, a few days)

**Purpose.** A first end-to-end check of detection, orders, news feeds, Telegram alerts and recovery, before paying for a server.

**Setup**

- An FTMO Free Trial account, which has the same trading conditions as the Challenge and costs nothing.
- Run the five instances from cTrader desktop on Windows. Starting each as an external process keeps it running if the cTrader window is closed.
- Laptop settings: never sleep or hibernate, on battery or on power; closing the lid does nothing; keep it plugged in; pause automatic update restarts for the test period.

**What this stage can prove**

- Fault tests 1 and 3 to 14 from section 12.3, using Wi-Fi off, sleep and manual restarts. Test 10 needs the stage to include a Friday evening.
- Recovery after real outages. The battery carries the laptop through a power cut, but the router usually goes down, which exercises the reconnect path.

**What it cannot prove**

- Unattended restart after a full power loss or reboot (test 2), because a laptop needs someone to log in and start cTrader.
- Round-the-clock uptime over weeks.

**Move to the VPS when** every applicable fault test has passed and there have been no positions without a stop loss, no duplicate orders and no recovery that needed manual help. All 14 tests are then repeated on the VPS.

## 11. Logging, monitoring and alerts

**Per signal (taken or skipped):** time, symbol, bias, zone id and edges, FVG edges, entry, stop, target, stop pips, volume, spread, cost-to-risk, news status, skip reason.

**Per trade:** requested and filled price, slippage, exit reason, result in R and in money, commission, swap, duration, maximum adverse and favourable move in R.

**Daily summary:** trades, net R, balance, equity, drawdown, distance to FTMO's daily and overall floors, progress to the profit target, skips by reason, calendar status, uptime, restarts.

**Inactivity warning:** an alert after 21 days without a trade, including while halted, because FTMO is reported to close accounts after 30 days without one. Without any trade the count starts no earlier than `RiskEpochDate`, and a restart does not repeat a recent warning (v0.9).

**Zone counts (v0.9):** once a day, how many zones were created and why each zone ended (signalled, no FVG, bias, invalid, expired, superseded; with the Choch entry model of 3.9 "no CHoCH" in place of "no FVG"), so that the number of signals can be explained.

**Repeated lines (v0.9):** a condition that lasts (recovery waiting for data, the same error again and again, risk data unavailable) is written to the Log at most once per hour, or once per day for a waiting recovery, with a count of the repeats. The cTrader Log holds a limited number of lines, and the backtest trade list is read from it.

**Recovery report:** after every start or reconnect (section 8.3, step 10).

**Heartbeat (dead-man's switch).** Every five minutes each instance calls an external heartbeat-monitoring address. If the calls stop (power, internet, crash, frozen process), the monitoring service alerts your phone. This is the only way to learn about an outage from outside the server.

**Phone alerts by Telegram.** You create a Telegram bot once and give its token and your chat id to the cBot as parameters. The cBot sends each alert with one HTTP call.

- Alerts sent: bot started or recovered, state change, trade opened or closed, order rejected, stop loss missing or repaired, kill switch, daily limit, a news feed down, calendar unavailable, circuit breaker, error storm.
- A daily summary message.
- Alerts are best-effort. A failed send is retried later and never delays or blocks trading logic.
- The heartbeat monitor should also notify the same Telegram chat, so an outage of the bot itself still reaches you.

**Log files:** written to the mounted volume with rotation, so they survive restarts and cannot fill the disk.

## 12. Testing and validation

### 12.1 Strategy backtest

**Data and settings**

- Tick data from the broker's server. Bar-data backtests misjudge stop, target and limit fills.
- Longest history available, target 5 to 10 years, each symbol separately.
- Start each backtest at least 10 months before the period that is judged: cTrader cannot load older history during a backtest, and the bot needs about 9.5 months of D1 bars before it can trade.
- Halts are only logged in backtests (8.5); the simulation tools apply the limits afterwards.
- Real commission; starting balance equal to planned live capital.
- News filter on, using embedded history; repeat with it off.

**Data split**

| Segment | Period | Use |
|---|---|---|
| Development | Start of data to November 2024 | Baseline, rule changes, optimisation |
| Holdout | November 2024 to November 2026 | Run once, at the end, with final settings |

**Sequence**

1. Baseline run with default parameters on all five symbols, no optimisation (gold with its own spread, stop and volume values, section 7). From v0.10: one series per entry mode, with the same period, data and costs. Steps 2 to 9 are then done for each mode that is still in the running.
2. If the baseline is unprofitable across symbols, revisit the rules. Do not optimise a losing baseline into a winner.
3. Coarse optimisation of the four **Optimise** parameters. Prefer broad plateaus of good results.
4. Re-run chosen settings as a single backtest; optimisation passes and single runs sometimes disagree.
5. Sensitivity: move each key parameter 20% either way.
6. Stress costs: repeat with spread and commission increased by 50% and with 0.5 pip extra slippage on stops (gold: 15 pips, proposed in v0.9).
7. Combine the five symbols' trade lists by time for portfolio drawdown and exposure caps.
8. Monte Carlo: reshuffle the trade order 1,000 times to estimate the range of drawdowns and of 30-trade results (used in 13.3).
9. Challenge simulation: replay the backtest's trades through FTMO's exact rules several thousand times to estimate the chance of passing each phase and the time it takes, at 0.5%, 0.75% and 1.0% risk per trade (section 15.5). With a sizing plan (`--sizing ladder` or, from v0.12, `--sizing recovery`) the plan sets the risk of each trade and only the two phases are run. The tools' defaults follow the account: initial capital 200,000 and daily budget 2% (v0.12).
10. Benchmark: a simple D1 trend-following bot on the same data and costs. From v0.10 the Breakout mode is that bot for the ZoneFvg mode: the Breakout baseline trade logs are the benchmark input of the ZoneFvg acceptance report. The Breakout mode itself has no benchmark yet.
11. Holdout run, once, for the mode chosen in 13.1.

### 12.2 Code tests

| Test | Pass condition |
|---|---|
| Unit tests on zone, FVG, breakout and sizing logic | Hand-built bar sequences give the expected zones, breakout references, signals and volumes |
| Visual check | 50 or more drawn zones and FVGs match the written definitions |
| Determinism | Two identical backtests produce identical trade lists |
| Replay equals live | Backtests started on different dates show identical zone states from the point where their histories overlap |
| No look-ahead | Signals never use a bar that had not closed at signal time |
| News parity | Embedded calendar blocks the same events in backtest that the live feed would block |

### 12.3 Fault-injection tests (demo account, on the real VPS)

| # | Fault | Expected result |
|---|---|---|
| 1 | Kill the container while a position is open | Restarts alone; position adopted; no new order |
| 2 | Reboot the VPS while a limit order is pending | Order still at broker; kept if valid |
| 3 | Order fills while the bot is stopped | Position adopted; stop and target confirmed |
| 4 | Trade hits target while the bot is stopped | Found in history; journal backfilled; zone not traded again |
| 5 | Network cut for 1, 10 and 60 minutes | Recovery runs; signals from the gap discarded |
| 6 | Network cut at the moment an order is sent | No duplicate order after reconnect |
| 7 | Remove the stop loss by hand | Stop restored within one event; alert sent |
| 8 | Start a second instance on the same symbol | No second order for the same symbol |
| 9 | Calendar feed returns an error or garbage | Cache used; if stale, entries blocked and alert sent |
| 10 | Stop the bot across Friday close with a position open | On restart the position is closed at once; alert sent |
| 11 | Change a parameter while a trade is open | Trade kept as a stranger with its stop and target |
| 12 | Stop all heartbeats | Monitoring service alerts the phone |
| 13 | Force repeated exceptions | Bot halts; does not crash or loop |
| 14 | Restart ten times in ten minutes | Calendar fetched at most once; no orders duplicated |

In Breakout mode "limit order" in tests 2 and 3 reads "stop order" (v0.10). The tests are run in the mode that goes live. The automated scenarios of tests 5, 6, 8 and 14 also exist in Breakout mode (v0.10, added on 22 November 2026).

**Soak test.** Four weeks of continuous demo running on the VPS with at least ten forced restarts at random times.

### 12.4 Strategy variants

Ideas that may strengthen results are tested one at a time against the baseline, on development data only. A variant is adopted only if it improves the development result, holds up under the sensitivity and cost-stress tests, and adds no more than one parameter.

Candidates, in order:

1. Break-even at +1R.
2. Close open positions before high-impact news.
3. Liquidity sweep: the touch must first take out a prior M15 swing low.
4. Trend from H4 market structure instead of the D1 EMA.
5. Discount filter: demand zones only in the lower half of the recent D1 range.
6. Volatility filter: skip when H4 ATR is unusually low or high.
7. London-only or New York-only entries.
8. Time stop after 96 bars.

The CHoCH entry model (3.9) is not a variant of this list: it is a second trigger, backtested beside the Fvg model on the same data and costs. Candidate 3 (liquidity sweep) is built into it as `RequireSweep`.

Breakout mode candidates (v0.10), in order:

1. Trailing stop moved at H4 closes, in place of the fixed target.
2. The opening range of the London or New York session in place of the previous day's range.
3. Range filter: skip when the reference day's range is under 0.5 or over 2.0 x ATR(D1).
4. Extension cap: skip when the confirming bar closes more than 1.0 x ATR(H4) beyond the level.
5. Break-even at +1R.
6. Close open positions before high-impact news.

The number of variants tried is recorded, because every extra attempt raises the chance of a result that is only luck.

## 13. Acceptance criteria and build phases

### 13.1 Performance thresholds (proposed)

| Measure | Development | Holdout |
|---|---|---|
| Trades, all symbols | 300 or more | 60 or more |
| Profit factor after costs | 1.30 or higher | 1.15 or higher |
| Average result per trade | +0.15R or higher | At least half the development figure |
| Maximum drawdown at 0.5% risk | 6% or less (FTMO's floor is 10%) | 6% or less |
| Challenge simulation: chance of passing the Challenge phase | 70% or higher | n/a |
| Challenge simulation: chance of breaching an FTMO floor | 10% or lower | n/a |
| Profitable symbols | 4 of 5 (3 of 4 without gold; proposed in v0.9) | 4 of 5 (3 of 4 without gold) |
| Share of profit from one symbol | 60% or less | n/a |
| Profit factor under cost stress | Above 1.10 | n/a |
| Versus benchmark | Better return-to-drawdown ratio | Same |

If the holdout fails, the strategy is rejected or redesigned; the holdout is not reused for tuning.

**Two entry modes (v0.10).** The development column is applied to each mode separately. A mode may go live only if it passes by itself. For the ZoneFvg mode the benchmark is the Breakout mode (12.1 step 10). For the Breakout mode the row "Versus benchmark" is not applied until a simpler benchmark exists; every other row applies. If both pass, the mode with the better return-to-drawdown ratio on development data is chosen. The holdout is then run once, for the chosen mode only: running both modes on the holdout and keeping the better one would turn the holdout into a tuning step. If the chosen mode is ZoneFvg, its holdout row "Versus benchmark" needs Breakout trade logs of the holdout period: the Breakout mode is then run once on the holdout, after the choice, with the same data and costs and its accepted settings, as the benchmark input of that row only. Its result changes nothing about the choice and is not used for tuning (added on 23 November 2026).

**Two entry models (v0.11).** In ZoneFvg mode the Fvg and the Choch model are judged like two modes: each by itself on the development data, with the Breakout mode as the benchmark of both; the holdout is run once, for the chosen one only.

**A mode on one symbol (added on 23 November 2026).** SessionRange is meant for gold alone at first. The rows "Profitable symbols" and "Share of profit from one symbol" cannot be applied to one symbol, and the trade counts were written for five. Which marks such a mode must pass is the owner's decision before go / no-go 1 and is open (section 14, question 4). Until it is made, the mode is not accepted.

**What goes live (added on 23 November 2026).** The parameter files of the instances that go live carry the chosen mode in `EntryMode` and the accepted settings, that is the values of the run the acceptance report judged; every other value stays at its default. The laptop stage, the fault tests (12.3) and the soak test are run with these files. The parameter hash that the bot prints at start must equal, symbol by symbol, the hash of the accepted backtest run (the hash covers the signal, execution and breakout values; the risk and news values are not part of it and are set as sections 4 and 6 say). A mode that did not pass, or was not chosen, is never the `EntryMode` of an instance on the Challenge account.

### 13.2 Reliability thresholds

| Measure | Required |
|---|---|
| Code tests (12.2) | All pass |
| Fault-injection tests (12.3) | All 14 pass |
| Soak test: positions without a stop loss | 0 |
| Soak test: duplicate orders or zones traded twice | 0 |
| Soak test: restarts needing manual help | 0 |
| Soak test: unexplained differences between journal and broker history | 0 |

### 13.3 Live edge monitoring

Calculated from account history after every closed trade.

| Condition (last 30 trades versus the backtest's Monte Carlo range) | Action |
|---|---|
| Average result below the 5th percentile | Cut risk to 0.25%; alert |
| Average result below the 1st percentile | Block new entries; review |
| Average slippage above 0.5 pip over 20 trades (gold: 15 pips, proposed in v0.9) | Alert; review broker conditions |

Every quarter, re-run the backtest including the latest data and compare live results with it.

### 13.4 Phases

1. **Agree the spec.** Resolve section 14.
2. **Signal layer.** Detection code as a tested library, with chart drawings. Code tests in 12.2.
3. **Execution, recovery and safety.** Sections 4, 5, 8 and 11. Confirm platform behaviour under the CLI.
4. **News module** and the historical event file.
5. **Baseline backtest**, one series per entry mode (v0.10). Go / no-go 1.
6. **Robustness, benchmark and variants.**
7. **Holdout.** Go / no-go 2.
8. **Laptop stage on an FTMO Free Trial (10.5), then VPS deployment (10.2).** Fault-injection tests at each stage. Go / no-go 3.
9. **Soak and forward test on Free Trial accounts.** At least four weeks and 30 trades, with results and slippage close to the backtest.
10. **Start the purchased FTMO Challenge** only after go / no-go 1 to 3 have passed (see 15.7 for keeping it valid until then). Run the Challenge and Verification at the risk level chosen from the challenge simulation (0.5% unless the simulation supports another).
11. **FTMO Account.** Same bot and settings; live edge monitoring (13.3) active.

## 14. Open questions

1. On what date did you buy the Challenge? It decides which option in section 15.7 is open to you.
2. Will this account be used only by the bot?
3. Index CFDs (for example US500 and US100) for the Breakout mode (v0.10). They are not part of v0.10: the bot accepts only symbols that start with six letters, and an index needs its own news currency, exposure cap, stop and spread values. To add them, the developer needs the symbol line the cBot prints at start on your FTMO cTrader account for each index (name, digits, tick size, lot size, volume steps, commission, sessions).
4. Which acceptance marks does a mode pass that runs on one symbol (SessionRange on gold, 13.1)? The search measured it once on the holdout already, so the holdout of 12.1 is no longer unused for this rule: the remaining clean evidence is a forward test on the Free Trial or a demo account.
5. `MaxVolumeUnits` on the 200,000 USD account (v0.12). It stays at 500,000 units (gold 200 ounces), as decided on 24 November 2026. At that cap a forex trade risks at most 1,000 USD on a 20-pip stop, so many Ladder trades of 0.5% and most Recovery trades above about 1,000 USD on forex stops under 51 pips are skipped as `VolumeAboveCap` (4.1). Should the cap be raised for the new account (and, if so, what does the margin check of 4.1 then allow)?
6. Recovery: the divisor of 4.1.1 is the instance's `RewardRisk` parameter (2.0) in every entry mode. In the Choch model the target comes from chart levels and is at least `MinRewardRisk`, so a win can return more than RR x R. Accepted as written (proposal of the plan of 24 November 2026), or should the divisor be a separate parameter?


**Resolved:** the CHoCH entry model's proposal values of 3.9 and 7 (accepted by the owner on 23 November 2026), account (FTMO 2-Step, Swing, cTrader, 200,000 USD from 24 November 2026, before that 100,000 USD bought and not started), sizing plan, daily budget and per-trade sanity cap (owner decisions of 24 November 2026, v0.12), symbols (four majors, plus gold from v0.9), timeframes (D1 / H4 / M15), stop placement (beyond the whole zone), account limits (1.5% daily, 8% kill switch), news (two free feeds), hosting (laptop first, then a rented VPS), alerts (Telegram).

## 15. FTMO account

Rules below were checked on 13 November 2026 against FTMO's own pages and recent third-party summaries. FTMO changes its terms from time to time, so confirm each one in your FTMO client area before buying.

### 15.1 What the account is

- FTMO accounts use **simulated capital** at every stage. The 200,000 USD (the owner's account from 24 November 2026; 100,000 before) is not your money. The cash you can lose is the challenge fee.
- 2-Step path: FTMO Challenge, then Verification, then the FTMO Account, where you are paid a share of the simulated profit (reported as 80%, rising to 90%).
- The 2-Step fee is reported as refunded with the first reward.

### 15.2 Rules that matter for this bot

| Rule | 2-Step | 1-Step |
|---|---|---|
| Profit target | 10%, then 5% | 10% |
| Maximum daily loss | 5% of initial capital | 3% |
| Maximum loss | 10%, fixed | 10%, trailing at end of day |
| Minimum trading days | 4 per phase | None stated |
| Consistency rule | None | Best day at most 50% of profit |
| Swing account type | Available | Not available |
| Time limit | None | None |

**How FTMO measures the limits (2-Step, 200,000 USD)**

- **Daily:** equity, including open trades, commissions and swaps, must stay above the balance at 00:00 CE(S)T minus 10,000 USD (5%).
- **Overall:** equity must never fall below 180,000 USD (10%).

**Standard or Swing (the difference applies once funded)**

| | Standard | Swing |
|---|---|---|
| Forex leverage | 1:100 | 1:30 |
| Restricted news | No order may execute from 2 minutes before to 2 minutes after, on the affected symbols. This includes stop loss, take profit and pending orders | No restriction |
| Weekend or a market break over 2 hours | All positions must be closed beforehand | No restriction |
| Changing later | Cannot be changed to Swing | Can be changed to Standard on an account with no trades |

During the Challenge and Verification neither type has news or weekend restrictions.

**Other rules**

- Bots are allowed, with no approval step. The strategy must be your own; widely shared third-party bots risk refusal.
- Server limits: 200 orders at a time, 2,000 positions a day, and more than 2,000 server requests a day counts as forbidden hyperactivity.
- At most 50 lots per forex order.
- Gap trading is forbidden. FTMO's page, read again on 22 November 2026, names two situations joined by "or": opening trades when major news, macroeconomic events or earnings that can affect the market are scheduled, or opening trades two hours or less before a market closes for two hours or more. The page names no exception for the Swing type. The bot opens nothing inside its news windows, lets no pending order sit through one (6.1), and opens nothing after Friday 12:00 UTC. From Monday to Thursday the daily breaks of the five symbols are shorter than two hours (gold: about one hour).
- Position sizes and risk per trade should stay consistent.
- The account is for your personal use only. A VPS is allowed, but not from a United States location.
- Reported: an account with no trade for 30 days can be closed.

### 15.3 Configuration: 2-Step, Swing, cTrader (bought, not started)

This is the account you hold, and it is the right fit for this bot:

1. **2-Step** has the wider daily limit, a fixed overall floor and no best-day rule.
2. **Swing, because of how the bot protects trades.** Every trade has its stop and target at the broker. On a funded Standard account, a stop or target filling inside a restricted news window is a breach, and the bot cannot prevent a broker-side stop from filling. Avoiding it would mean closing every position before each restricted release, and an outage at the wrong moment could still cause a breach.
3. **Swing, because of weekends.** Standard requires being flat before the weekend. If the server is offline at Friday close, that is a rule breach on Standard and only a market risk on Swing.
4. **It must be chosen at purchase.** A Standard account cannot become Swing later.

**The cost of Swing** is 1:30 leverage. The bot is adjusted for it: smallest stop 12 pips and a 5-lot size cap (section 4.1).

The bot still closes positions on Friday evening and avoids entries around news. On Swing these are trading choices, not rules whose failure ends the account.

### 15.4 The bot's limits inside FTMO's

| | FTMO limit | Bot limit | Room left |
|---|---|---|---|
| Daily | Equity above the 00:00 CE(S)T balance minus 10,000 | Daily risk budget of 4,000 (2%), measured the same way | 6,000 |
| Overall | Equity above 180,000 | Kill switch 8% below the highest closing balance: 184,000 at the start | 4,000 at the start, more as the balance grows |
| Last resort | n/a | Emergency guard closes everything 2,000 (1%) before either FTMO floor | n/a |

Amounts for the 200,000 USD account (v0.12). Every amount is a percent of initial capital, so they follow `InitialCapital`.

### 15.5 What the targets mean

| Phase | Target | At 0.5% risk | At the hoped-for +4R a month |
|---|---|---|---|
| Challenge | +20,000 USD | +20R net | About 5 months |
| Verification | +10,000 USD | +10R net | About 2.5 months |

- There is no time limit, so a slow pass is allowed. The +4R a month is a hope, not a measurement.
- Raising risk shortens the wait and raises the chance of hitting a floor. The challenge simulation (12.1, step 9) puts numbers on that trade-off before any fee is paid.
- If risk per trade changes, the open-risk cap and daily risk budget scale with it and must still sit well inside FTMO's 5% (10,000 USD). In the Recovery plan the risk per trade changes with every loss by design; the targets in R above do not apply to it, and the challenge simulation with `--sizing recovery` (12.1, step 9) gives its pass and fail rates.

### 15.6 Staging with FTMO

1. **Backtests** on FTMO's own cTrader price history and commission (reported as about 5 USD per lot; confirm on the trial account).
2. **Laptop stage and soak test** on FTMO Free Trial accounts, which are free, repeatable and have the Challenge's trading conditions. Each trial is reported to last 14 days, to end early if no trade is placed in its first 7 days, and to be limited to one at a time, so the four-week soak test spans several trials in a row.
3. **Challenge and Verification** on the VPS.
4. **FTMO Account.**

**To confirm on the first Free Trial**

- That a Free Trial can be opened on cTrader. One source says trials are issued on MetaTrader 5. If so, the laptop stage needs another cTrader demo account, and FTMO-specific checks wait for the Challenge account.
- cBots run on an FTMO cTrader account from cTrader desktop and from cTrader CLI.
- Symbol names, minimum volume and step, minimum stop distance, typical spreads, commission.
- How much tick history FTMO's cTrader provides for backtests.
- Leverage shown on the account.

FTMO accounts are simulated accounts, so cTrader cloud's demo limits (one instance, seven days) may apply to them. This is one more reason to use the VPS.

### 15.7 Your purchased Challenge: do not place a trade yet

The bot is not built or tested, and the first trade on the Challenge account has consequences. From FTMO's Challenge terms (dated 12 October 2026, as quoted in search results; read the full terms in your client area and confirm with FTMO support):

| Event | What the terms say |
|---|---|
| First trade | Activates the Challenge phase |
| Right to withdraw from the purchase | 14 calendar days, and only before the first trade |
| No activation within 30 calendar days of the account being made available | Access is suspended |
| After suspension | Renewal can be requested within 6 months; after that the agreement ends with no refund |
| After activation | Prolonged inactivity can end the Challenge. Third-party summaries put this at 30 days without a trade, with a freeze available on request |

**Realistic timing.** Building the bot, backtesting, and a four-week soak test will take roughly two to three months before the Challenge should start.

**Options**

1. **Within 14 days of purchase and no trade placed:** withdraw from the purchase and buy again when the bot has passed its tests. This removes all deadlines.
2. **Past 14 days:** leave the account untouched, let access suspend at 30 days, and request renewal when the bot is ready. The latest date is 6 months after suspension; put it in your calendar with a margin.
3. **Not advised:** activating now, either by hand or with an untested bot. Manual trades would also break the assumption that the account is used only by the bot.

Before choosing option 2, ask FTMO support to confirm in writing that a never-activated Challenge can be renewed and whether renewal carries any cost or condition.

## References

- cTrader Algo: requirements for cBots in the cloud — https://help.ctrader.com/ctrader-algo/documentation/cbots/cloud-requirements/
- cTrader Algo: cloud features and limits — https://help.ctrader.com/ctrader-algo/documentation/cloud-features/
- cTrader CLI: cBot operations and Docker — https://help.ctrader.com/ctrader-cli/cbots/
- cTrader CLI: run a cBot around the clock — https://help.ctrader.com/ctrader-cli/use-cases/run-a-cbot-around-the-clock/
- cTrader Algo: network access — https://help.ctrader.com/ctrader-algo/guides/network-access/
- cTrader Algo: local storage — https://help.ctrader.com/ctrader-algo/guides/local-storage/
- cTrader Algo: server connection events — https://help.ctrader.com/ctrader-algo/references/Application/IServer/
- cTrader Algo: bar events — https://help.ctrader.com/ctrader-algo/how-tos/cbots/handle-bar-events/
- cTrader Algo: multi-timeframe strategies — https://help.ctrader.com/ctrader-algo/how-tos/cbots/code-multitimeframe-strategies/
- cTrader Algo: trading operations — https://help.ctrader.com/ctrader-algo/how-tos/cbots/cbot-trading-operations/
- cTrader Algo: backtesting and optimisation — https://help.ctrader.com/ctrader-algo/backtesting-and-optimizing-cbots/
- cTrader forum: CLI reconnect behaviour — https://community.ctrader.com/forum/ctrader-algo/41399/
- cTrader forum: pending orders after a disconnect — https://communityuat.ctrader.com/forum/cbot-support/3843
- cTrader forum: VolumeForProportionalRisk formula — https://community.ctrader.com/forum/ctrader-algo/40686
- cTrader forum: economic calendar access for cBots — https://community.ctrader.com/forum/ctrader-support/23225/
- Forex Factory forum: calendar export limits — https://www.forexfactory.com/thread/post/15166506
- ShowMyTrades: calendar API reference — https://showmytrades.com/docs/api/economic-calendar
- Forex Factory calendar archive 2007 to 2025 — https://huggingface.co/datasets/Tropstan/Forex_Factory_Calendar/blob/main/README.md
- financecalendar.com: API and plans — https://www.financecalendar.com/api/
- biquote.io: calendar API — https://biquote.io/guides/economic-calendar-api.html
- Financial Modeling Prep: economic calendar — https://site.financialmodelingprep.com/developer/docs/economic-calendar-api
- FXMacroData: calendar API price comparison (vendor-written) — https://fxmacrodata.com/articles/best-macroeconomic-data-apis-2026
- cTrader CLI: overview and external process — https://help.ctrader.com/ctrader-algo/documentation/ctrader-cli/
- FTMO: Swing account type — https://ftmo.com/faq/what-is-the-swing-account-type-and-how-does-it-work/
- FTMO: holding positions overnight and over the weekend — https://ftmo.oanda.com/faq/do-i-have-to-close-my-positions-overnight-or-before-weekend
- FTMO: instruments, strategies and server limits — https://ftmo.com/au/faq/which-instruments-can-i-trade-and-what-strategies-am-i-allowed-to-use/
- FTMO: forbidden trading practices — https://ftmo.oanda.com/forbidden-trading-practices/
- FTMO: forbidden trading practices, global site, read on 22 November 2026 — https://ftmo.com/en/forbidden-trading-practices/
- FTMO: trading objectives, read on 22 November 2026 — https://ftmo.com/en/trading-objectives
- Research behind the Breakout mode (v0.10): Hurst, Ooi and Pedersen, "A Century of Evidence on Trend-Following Investing" — https://www.aqr.com/Insights/Research/Journal-Article/A-Century-of-Evidence-on-Trend-Following-Investing ; Zarattini, Aziz and Barbon, "Beat the Market: An Effective Intraday Momentum Strategy for S&P500 ETF (SPY)" — https://papers.ssrn.com/sol3/papers.cfm?abstract_id=4824172 ; a fair value gap test on futures that found no edge after costs — https://mpmmarkets.com/research/does-the-fair-value-gap-strategy-work
- FTMO rules summary with sources (third party) — https://proea.app/en/blog/ftmo-rules
- FTMO news rule explained (third party) — https://proea.app/en/blog/ftmo-news-trading
