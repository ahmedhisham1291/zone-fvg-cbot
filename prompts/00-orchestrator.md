# Orchestration: how the work packages are run

This file is for the human owner and for any agent that coordinates others.

## Roles

| Role | Who | May do | May not do |
|---|---|---|---|
| Implementer | An AI coding agent, one per work package | Write code and tests inside the paths its prompt allows; open a pull request | Touch protected paths; merge; review its own work |
| Validator | A separate AI agent session with `prompts/validator.md` | Read the diff, run checks, write a verdict | Change any file other than its report |
| CI | GitHub Actions | Build, test, coverage, traceability, architecture, secrets, protected paths | n/a |
| Owner | The human | Answer questions, approve decisions, merge | n/a |

The implementer and the validator must be different sessions. The validator
must not be shown the implementer's reasoning, only the repository.

## Order

```
WP00 bootstrap
  └─ WP01 core primitives
       ├─ WP02 zones ── WP03 FVG and signal engine ─┐
       ├─ WP04 risk engine ─────────────────────────┤
       └─ WP05 news ────────────────────────────────┤
                                                    └─ WP06 execution
                                                         ├─ WP07 recovery ── WP08 cTrader adapter ── WP11 deployment
                                                         └─ WP09 operations
WP10 simulation tools (any time after WP01)
```

WP02, WP04 and WP05 can run in parallel in separate branches. Everything else
is sequential.

## Loop for one work package

1. Owner creates the branch `wp/NN-short-name` from an up-to-date `main`.
2. Owner starts a fresh implementer session and says:
   `Implement prompts/WPNN-<name>.md`.
3. Implementer works until every item of the prompt's "Definition of done"
   holds, then opens a pull request using the template.
4. CI runs. The implementer fixes failures without weakening any check.
5. The validator runs (automatically in CI, or locally in a fresh session:
   `Use the validator subagent on this branch against main`).
6. On a FAIL verdict the owner passes the validator's findings, unchanged, to
   a fresh implementer session. Repeat from step 3. After three FAIL rounds on
   the same pull request, stop and review by hand: the prompt or the spec is
   probably unclear.
7. On PASS the owner reads the "Decisions for the human" section, reads the
   diff of anything touching orders or risk, and merges.
8. Owner updates `docs/progress.md` (one line per merged work package).

## Gates that no agent can pass alone

| Gate | After | Who decides |
|---|---|---|
| Detection matches the written rules on 50 real chart examples | WP03 | Owner, by eye, in cTrader |
| Baseline backtest (spec 13.1) | WP08 and WP10 | Owner |
| Holdout backtest | After baseline | Owner |
| Fault-injection tests on a real account (spec 12.3) | WP11 | Owner |
| Start of the FTMO Challenge | All of the above | Owner |

## Cost and context hygiene

- One work package per session. Start a new session for each fix round.
- Do not paste the whole spec into the chat. Agents read files.
- Keep pull requests under about 800 changed lines where possible; split a
  work package into `WPNNa`, `WPNNb` pull requests if needed, each with its own
  subset of requirement ids.
