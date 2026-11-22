# Zone + FVG cBot

A cTrader cBot for an FTMO 2-Step (Swing) account, built by AI coding agents
under a strict process: numbered requirements, tests first, an independent
validator agent, CI gates, and an orchestrating session that merges when the
gates are green and reports every merge to the owner.

**Status:** the bot is built: the core logic, the cTrader adapter, the
simulation tools and the deployment files. It has two entry modes, chosen
per instance with the parameter `EntryMode` (spec v0.10): **Breakout**, the
default, and **ZoneFvg**. `docs/requirements.yml` holds 126 requirements;
116 are implemented (count of `python3 tools/check_traceability.py`). Of the
other ten, nine are checked by hand on cTrader and wait for the owner's
signed test records (`docs/test-records/`), and one (REC-016, symbol trading
sessions) is still planned. No backtest result is recorded yet, so nothing here
says whether either mode is profitable. How to run the backtests:
`docs/backtest-howto.md`. How to deploy: `deploy/README.md`.

## What is here

| Path | What it is |
|---|---|
| `docs/spec.md` | The specification (v0.10) |
| `docs/requirements.yml` | 126 numbered requirements, each tied to a work package and a way of proving it |
| `docs/architecture.md` | Layers and type contracts all work packages share |
| `AGENTS.md` | Rules every agent must follow |
| `prompts/WP00` … `WP11` | One detailed implementation prompt per work package |
| `prompts/00-orchestrator.md` | The order of work and the loop for each work package |
| `prompts/validator.md` | Instructions for the validator agent |
| `.claude/agents/validator.md` | The validator as a Claude Code subagent |
| `.github/workflows/ci.yml` | Build, format, tests, coverage gate, `.algo` build, traceability, architecture and secret checks |
| `.github/workflows/guard.yml` | Blocks changes to protected paths unless the pull request carries the label `human-approved-infra` |
| `.github/workflows/validator.yml` | Runs the validator agent inside GitHub. Switched off in this repository (owner decision of 2026-11-13) so that nothing uses API credit. The validator agent is run from the orchestrating session instead and posts its report as a comment |
| `tools/*.py` | The check scripts used by CI |
| `scripts/setup-github.sh` | One-time GitHub settings: labels, merge rules, required checks |

## How the checks fit together

```
implementer agent ──> pull request
                         │
        ┌────────────────┼──────────────────┬───────────────────┐
        ▼                ▼                  ▼                   ▼
   build-test       repo-checks       protected-paths       validator
   build, format,   requirement       rules, checks and     separate AI
   tests, 90/80     traceability,     spec cannot be        session reads the
   coverage,        core purity,      edited without        diff against each
   .algo build      secret scan       the label             requirement
        └────────────────┴──────────────────┴───────────────────┘
                         ▼
            merge, by the rule in AGENTS.md "Who merges"
```

A validator PASS only means it found no reason to block. Since the owner
decision of 2026-11-22 the owner does not review or merge pull requests: the
orchestrating session merges. The conditions, what the owner is told after
each merge and what stays with the owner are in `AGENTS.md`, section "Who
merges". In this repository the `validator` box is filled by a validator
agent run from the orchestrating session, not by the GitHub workflow.

## First-time setup

1. Create an empty **private** repository on GitHub and push this folder to
   `main`.
2. Optional, and not used in this repository: to run the validator inside
   GitHub, add the repository secret `ANTHROPIC_API_KEY` and switch the
   `validator` workflow on under Actions. Each run costs API usage.
3. Run `./scripts/setup-github.sh <owner>/<repo>` from a machine where the
   GitHub CLI is logged in. Read the script first.
4. **Plan note:** on a private repository GitHub only *enforces* required
   checks on a paid plan (GitHub Pro for a personal account). On the free
   plan the checks still run and show red or green, but GitHub will not stop
   a merge. Either upgrade, or simply never merge a red pull request.
5. Install the .NET 10 SDK and Claude Code (or another coding agent) on your
   development machine.
6. Start with `prompts/WP00-bootstrap.md` and add the label
   `human-approved-infra` to that one pull request.

## Running a work package

```text
git switch main && git pull
git switch -c wp/01-core-primitives

# in a fresh agent session:
Implement prompts/WP01-core-primitives.md

# when it says it is done, in a NEW session:
Use the validator subagent on this branch against main
```

Then the pull request is opened and the checks run. The orchestrating session
reads the validator's report, answers the "Decisions for the human" section
and merges by the rule in `AGENTS.md`, "Who merges". You get a plain-language
report after each merge and can ask for any of it to be reversed.

The prompts are plain Markdown and work with any coding agent. `AGENTS.md` is
read by most agent tools; `CLAUDE.md` points Claude Code to it.

## Order of work

`WP00` → `WP01` → (`WP02` → `WP03`), `WP04`, `WP05` → `WP06` → `WP07`, `WP09`
→ `WP08` → `WP11`. `WP10` any time after `WP01`.
See `prompts/00-orchestrator.md` for the gates only you can pass: checking
detection by eye, the backtests, the fault-injection tests and the decision to
start the FTMO Challenge.

## Things only you can provide

- Real samples of the two news feeds (`tests/ZoneFvg.Core.Tests/TestData/News/README.md`) before WP05.
- The money decisions: the account, the symbols, the risk per trade and the
  limits. Other questions of the agents are answered by the orchestrating
  session, which tells you what it decided.
- cTrader desktop checks for WP08 and the backtests.
- The FTMO account and all credentials. They never go into this repository.

## Risk note

This software is for trading on a simulated-capital evaluation account. It can
lose the evaluation fee and, on a funded account, the account itself. Nothing
here is financial advice, and passing every check in this repository does not
mean the strategy is profitable.
