# Orchestration: how the work packages are run

This file is for the human owner and for any agent that coordinates others.

## Roles

| Role | Who | May do | May not do |
|---|---|---|---|
| Implementer | An AI coding agent, one per work package | Write code and tests inside the paths its prompt allows; open a pull request | Touch protected paths; merge; add a label; review its own work |
| Validator | A separate AI agent session with `prompts/validator.md` | Read the diff, run checks, write a verdict | Change any file other than its report; merge; add a label |
| CI | GitHub Actions | Build, test, coverage, traceability, architecture, secrets, protected paths | n/a |
| Orchestrator | The orchestrating session: the chat session the owner talks to | Write plans (spec, requirements, prompts); start implementer and validator sessions; answer their questions and record binding decisions in `docs/architecture.md` section 10; add the label `human-approved-infra` after reading every protected change; merge by the rule in `AGENTS.md`, "Who merges"; report every merge to the owner | Validate a change it wrote; merge on a red check; weaken a check; change one of the owner's money decisions; pass a gate from the table below |
| Owner | The human | Money decisions; the gates in the table below; secrets and credentials; reverse anything that was merged | n/a |

The owner does not review or merge pull requests (owner decision of
2026-11-22). `AGENTS.md`, section "Who merges", is the rule; this file says
how the loop runs under it.

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

1. The orchestrating session creates the branch `wp/NN-short-name` from an
   up-to-date `main`.
2. It starts a fresh implementer session and says:
   `Implement prompts/WPNN-<name>.md`.
3. Implementer works until every item of the prompt's "Definition of done"
   holds, then opens a pull request using the template. When the implementer
   stops and asks, the orchestrating session answers in the pull request (or
   in the hand-over to the next implementer session) and records a decision
   that binds later work in `docs/architecture.md` section 10.
4. CI runs. The implementer fixes failures without weakening any check.
5. The validator runs in a fresh session:
   `Use the validator subagent on this branch against main`. The GitHub
   workflow for it is switched off (owner decision of 2026-11-13). If the
   validator cannot be run at that time, see `AGENTS.md`, "Who merges".
6. On a FAIL verdict the orchestrating session passes the validator's
   findings, unchanged, to a fresh implementer session. Repeat from step 3.
   After three FAIL rounds on the same pull request, stop the rounds: the
   prompt or the spec is probably unclear. The orchestrating session reads
   the diff and the three reports, corrects the prompt or the spec in a plan
   pull request of its own, and names this in its report to the owner.
7. On PASS the orchestrating session answers every item under "Decisions for
   the human" and `questions_for_owner`, reads the diff of anything touching
   orders or risk, adds the label `human-approved-infra` if a protected path
   changed and it has read each such change, and merges when the conditions
   of `AGENTS.md`, "Who merges", hold.
8. The orchestrating session reports the merge to the owner in plain language
   (`AGENTS.md`, "What the owner is told").

## Gates that no agent can pass alone

These are not merges. The owner decision of 2026-11-22 does not change them:
each needs the owner's cTrader application or account, and the owner decides.

| Gate | After | Who decides |
|---|---|---|
| Detection matches the written rules on 50 real chart examples | WP03 | Owner, by eye, in cTrader |
| Baseline backtest (spec 13.1) | WP08 and WP10 | Owner |
| Holdout backtest | After baseline | Owner |
| Fault-injection tests on a real account (spec 12.3) | WP11 | Owner |
| Stop-order check on a demo or Free Trial account (PLT-011, `docs/test-records/WP26-stop-orders.md`), before any instance trades in Breakout mode | WP28 | Owner |
| Start of the FTMO Challenge | All of the above | Owner |

## Cost and context hygiene

- One work package per session. Start a new session for each fix round.
- Do not paste the whole spec into the chat. Agents read files.
- Keep pull requests under about 800 changed lines where possible; split a
  work package into `WPNNa`, `WPNNb` pull requests if needed, each with its own
  subset of requirement ids.
