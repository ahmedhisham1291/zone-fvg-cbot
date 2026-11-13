# WP11 — Deployment and runbook

## Read first

`AGENTS.md`, `docs/spec.md` sections 10, 11, 12.3 and 15.6, and the requirement
text below. Depends on WP08.

Use the current cTrader CLI documentation at https://help.ctrader.com/ctrader-cli/
for every command and option. Do not write a command from memory; cite the
page next to each command.

## Requirement to implement

PLT-008.

## Allowed paths

`deploy/**`, `docs/runbook.md`, `docs/test-records/WP11-*.md`, and the
`status:` line of the requirement above.

## What to build

1. **`deploy/docker-compose.yml`**: one service per symbol (EURUSD, GBPUSD,
   USDJPY, AUDUSD) running the official cTrader CLI image at a pinned version
   tag, each with:
   - `restart: unless-stopped`;
   - the `.algo` file and a per-symbol parameter file mounted read-only;
   - a named volume for cTrader's data and local storage, and one for logs;
   - log rotation configured;
   - credentials supplied through an environment file that is not in the
     repository (`deploy/.env.example` documents the variable names with
     dummy values).
2. **`deploy/params/*.cbotset.example`**: one example parameter file per
   symbol with spec defaults and placeholder secrets.
3. **`deploy/README.md`**: step-by-step server setup for a fresh Ubuntu LTS
   VPS located outside the United States: user, SSH keys only, firewall,
   automatic security updates, Docker, time sync, directory layout, first
   start, how to update the bot version safely (only when flat, or after the
   adoption test), how to roll back.
4. **`docs/runbook.md`**: what each alert means and what to do; how to read
   the daily summary; how to halt and resume; how to change `RiskEpochDate`;
   weekly checks (calendar feed comparison, slippage, disk space); what to do
   before FTMO rule changes.
5. **`docs/test-records/WP11-fault-injection.md`**: a form with the 14 tests
   of spec 12.3, each with exact steps for the VPS (for example the Docker
   command that kills a container), the expected result, and blank fields for
   date, observed result and pass or fail. Include the laptop-stage variant
   (spec 10.5).

## Decisions already made

- No secret, account number or cTrader ID appears in any committed file.
- The compose file must not use the `latest` image tag.
- The bot is never updated while a position is open unless the adoption test
  for that version pair has passed on a trial account.

## Verification

The owner runs the fault-injection form on a trial account and signs it.
The requirement stays `planned` until then.

## Definition of done

- [ ] `docker compose config` validates the file (output in the pull request).
- [ ] `tools/check_secrets.py` clean.
- [ ] Every command in the READMEs has a documentation link.
- [ ] The fault-injection form is complete and unambiguous enough to follow
      without asking.
