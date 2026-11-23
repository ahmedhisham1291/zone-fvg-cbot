# WP35: SessionRange in the cBot, the parameter files and the guides

## Read first

`AGENTS.md`; `docs/spec.md` 3.8, 7, 13.1 ("A mode on one symbol", "What goes live"), 14 (question 4); PLT-015 in `docs/requirements.yml`; `docs/architecture.md` section 10 (rows "SessionRange mode" and "Gold's stop and spread limits in the platform's pip"); `prompts/WP28-entry-mode-cbot.md` and `docs/test-records/WP28-entry-mode.md` (the pattern); `src/ZoneFvg.Bot/ZoneFvgBot.cs`; `deploy/**`; `docs/backtest-howto.md`; `docs/runbook.md`; `README.md`; `research/search/REPORT-2026-11-23.md`.

## Requirements

PLT-015.

## Allowed paths

`src/ZoneFvg.Bot/**`; `deploy/**`; `docs/backtest-howto.md`; `docs/runbook.md`; `README.md`; a new record `docs/test-records/WP35-session-range.md`; new test files under `tests/ZoneFvg.Core.Tests/` if the adapter needs a small pure helper in the core (say which and why); `docs/requirements.yml` (the status line of PLT-015).

## Build

1. cBot: `EntryMode` accepts `SessionRange`; six parameters in the group "Entry mode" with the defaults of PLT-015, passed into `SessionRangeParameters` and checked at start in every mode; the names in a refusal are the names cTrader shows; the start line shows the mode. Breakout stays the default.
2. Parameter files: the six new lines after the existing Entry mode lines in all five example files. Gold file: `MinStopPips=8` and `MaxSpreadPips=0.4` (spec 7: the platform's gold pip is 1 USD).
3. Guides: how to backtest the mode on gold (which three values differ from the defaults: `EntryMode`, `MinStopPips`, `MaxSpreadPips`; the spread to enter in cTrader's backtest settings is in the platform's pip, so 0.3 means 0.30 USD on gold); the new default hash of the mode per symbol, computed with the core's rule (say how); what "What goes live" means for this mode.
4. Record `docs/test-records/WP35-session-range.md` for the owner, in the style of the WP28 record: start line and hash; one backtest on XAUUSD from 6 May 2020 with the three values of step 3; what to compare with the search (about 200 trades, the first trade in 2020, results by year in `research/search/REPORT-2026-11-23.md`, and that cTrader's own fills make the total lower than the report's); the timed exit and the void seen in the log; that the mode is not accepted until the owner has decided question 4 of spec 14.
5. Check and say in the pull request whether the cBot loads enough M15, H4 and D1 history at the start of a backtest for the first trading day to be evaluated.

## Tests (tagged)

PLT-015: what can be tested without cTrader lives in the core and is tested there (parameter texts to `SessionRangeParameters`, refusal messages with the cBot's names, the hash of the mode against a hand-computed value, the hashes of the other modes unchanged). The rest is the owner's record.

## Done

CI green, Python checks, the status to `implemented` only if no decision is open, a plain summary for the owner in the pull request.
