# WP40 test record: the offline backtest lab

First recorded run of each stage of `.github/workflows/lab.yml` (spec 12.5,
row AQ). PLT-019 and LAB-001 are `verify: manual` and are proven by this
record. The orchestrating session starts each stage on `main` after its part
is merged (Actions > lab > Run workflow, or
`POST /repos/<owner>/<repo>/actions/workflows/lab.yml/dispatches` with
`ref=main` and the inputs below) and fills in the section from the job log.
No data is attached here: the data lives only in the Actions cache.

## Stage `data` (WP40a: PLT-019 data part, LAB-001)

| Field | Value |
|---|---|
| Date | (fill in) |
| Run | (link to the workflow run) |
| Commit | (sha of `main`) |
| Inputs | stage `data`, symbols (fill in), from `2019-03-01`, to_month (fill in) |
| Data id | (last line between the manifest marker lines of job `data-manifest`) |
| Runtime per symbol | (minutes, from the `data` jobs) |
| Months per symbol | (fill in) |
| Minutes kept / dropped (weekend, one side, zero volume), ask below bid | (totals per symbol from `manifest.csv`) |
| Weekdays without data | (list per symbol) |
| URL layout and price order confirmed | (yes / no; if no, what differs and the `lab/data/README.md` fix) |
| Second run with the cache: no request, same data id | (yes / no, run link) |
| Nothing but manifests in the artifacts | (yes / no) |
| Problems | (fill in) |
| Result | (pass / fail) |

## Stage `single` (WP40b)

| Field | Value |
|---|---|
| Date | (fill in) |
| Run | (link) |
| Commit | (sha) |
| Inputs | (symbol, parameter file, overrides) |
| Data id | (fill in) |
| Counts | (trades, signals, skips, orders, fills, data-gap blocks, dropped minutes from `run.json`) |
| Runtime | (minutes for one symbol over the development range) |
| Problems | (fill in) |
| Result | (pass / fail) |

## Stage `sweep` (WP40c)

| Field | Value |
|---|---|
| Date | (fill in) |
| Run | (link) |
| Commit | (sha) |
| Grid | (file and its combination count) |
| Data id | (fill in) |
| Counts | (runs, shards, complete or INCOMPLETE) |
| Runtime | (longest shard, minutes) |
| Selection | (chosen combination or "No combination passes on the development data") |
| Problems | (fill in) |
| Result | (pass / fail) |

## Stage `holdout` (WP40c, WP40d report)

| Field | Value |
|---|---|
| Date | (fill in) |
| Run | (link) |
| Commit | (sha) |
| Selection file | (path under `lab/selection/`) |
| Data id | (fill in) |
| Counts | (holdout trades, boundary trades) |
| Runtime | (minutes) |
| Report | (artifact `lab-report-<run id>`, issue comment link if posted) |
| Problems | (fill in) |
| Result | (pass / fail) |
