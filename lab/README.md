# The offline backtest lab

The lab (spec 12.5, rows AH to AR) runs the bot's own `ZoneFvg.Core` over
about six years of one-minute history in GitHub Actions, so that entry
settings and sizing plans can be explored without manual cTrader backtests.
It is a research tool: it is not part of the cBot, never places an order and
changes nothing in the bot, its rules, its defaults or its money settings.

## Stages

The workflow `.github/workflows/lab.yml` is started by hand (Actions > lab >
Run workflow) with a `stage`:

| Stage | Work package | What it does |
|---|---|---|
| `data` | WP40a | Downloads and converts the data of the chosen symbols into the Actions cache and uploads the manifests |
| `single` | WP40b | One symbol, one parameter file, the development period (not built yet) |
| `sweep` | WP40c | Every combination of a grid file on every symbol, development period only (not built yet) |
| `holdout` | WP40c | The committed selection, once, on the holdout (not built yet) |

A stage that is not built yet is refused with a message. On pull requests
that change `lab/**`, `src/ZoneFvg.Lab/**` or the workflow, the job
`lab-tests` runs the lab's own tests. The workflow is not a merge gate.

## Data and the cache

The data is Dukascopy's public one-minute bid and ask candles of EURUSD,
GBPUSD, USDJPY, AUDUSD and XAUUSD from 1 March 2019 (warm-up) to the last
complete month (`lab/data/README.md`). The repository is public and
Dukascopy's terms were not confirmed to allow redistribution, so the raw and
converted data is kept **only in the Actions cache** (key
`lab-m1-v1-<SYMBOL>-<last month>`). It is never committed and never uploaded
as an artifact. Artifacts hold the manifests (`lab-data-manifest`), and in
later stages trade logs, run records and reports. A run that finds no cache
downloads again; a cache of an earlier month is restored and only the new
months are downloaded.

The data id, the SHA-256 of `manifest.csv`, is printed at the end of the
`data` stage, between the lines `-----BEGIN LAB DATA MANIFEST-----` and
`-----END LAB DATA MANIFEST-----` of the job `data-manifest`, and in the run
summary. Every later report names it.

## Costs being modelled

Dukascopy's prices differ slightly from FTMO's feed, so FTMO's costs are
modelled (row AJ), not read from the data: commission 30 USD per million USD
of notional per side for the pairs and 0.0007% of notional per side for gold;
spread the larger of the data's spread and a floor (EURUSD 0.3 pip, GBPUSD
0.6, AUDUSD 0.4, USDJPY 0.5, gold 0.30 USD), three times as wide from 16:55
to 18:10 New York; slippage on stop entries and stop losses 0.1 pip (gold
0.05 USD); swap not modelled by default. The stress run widens spread and
commission by half and adds 0.5 pip of slippage (gold 0.15 USD). The values
live in `lab/costs.json` from WP40b.

## Differences from a cTrader backtest

Dukascopy is another liquidity provider than FTMO: prices differ by a
fraction of a pip, spreads differ, and quiet-hour candles differ more. Zones
and signals near a threshold can therefore come out differently from a
cTrader backtest on FTMO's data. The lab explores and chooses; going live
still follows spec 13.1 and the owner's go / no-go (row AR).
