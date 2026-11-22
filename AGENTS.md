# Agent rules for this repository

You are working on a trading bot that will place real orders on a funded-trading
evaluation account. A defect can end the account. Correctness and traceability
matter more than speed or elegance.

Read this file fully before doing anything. Then read, in this order:

1. `docs/spec.md` — the specification. It is the law.
2. `docs/requirements.yml` — the numbered requirements you implement and test.
3. `docs/architecture.md` — layers, ports and type contracts.
4. The prompt for your work package in `prompts/`.

## What this project is

A cTrader cBot (C#) that trades four forex pairs and gold in the direction
of the D1 trend, one instance per symbol, with one of two entry modes per
instance (spec v0.10): a breakout of the previous day's range, entered with
a stop order (Breakout, the default), or higher-timeframe supply and demand
zones with a lower-timeframe fair value gap entry (ZoneFvg).

| Path | Content |
|---|---|
| `src/ZoneFvg.Core` | All logic. Pure C#, no cTrader reference, no IO, no clock |
| `src/ZoneFvg.Bot` | Thin cTrader adapter. Builds the `.algo` file |
| `tests/ZoneFvg.Core.Tests` | xUnit tests, FakeBroker scenarios |
| `tools/` | CI check scripts (Python). Protected |
| `docs/` | Spec, requirements, architecture, test records |
| `prompts/` | One prompt per work package, plus the validator prompt. Protected |

## Commands

```bash
dotnet restore tests/ZoneFvg.Core.Tests
dotnet build   tests/ZoneFvg.Core.Tests -c Release
dotnet test    tests/ZoneFvg.Core.Tests -c Release --collect:"XPlat Code Coverage" --results-directory TestResults
dotnet build   src/ZoneFvg.Bot -c Release
dotnet format  src/ZoneFvg.Core --verify-no-changes
dotnet format  src/ZoneFvg.Bot --verify-no-changes
dotnet format  tests/ZoneFvg.Core.Tests --verify-no-changes
python3 tools/check_traceability.py
python3 tools/check_architecture.py
python3 tools/check_secrets.py
python3 tools/check_coverage.py TestResults --package ZoneFvg.Core --min-line 90 --min-branch 80
```

Run all of them before you say the work is done. Paste the real output in the
pull request. Never claim a command passed without running it.

## Hard rules

1. **The spec decides.** If the spec, a requirement and your prompt disagree,
   or something is not specified, stop and ask. Do not guess, and do not
   "improve" a trading rule. Who answers is in "Who merges" below.
2. **One work package per pull request.** Implement only the requirement ids
   listed in your prompt. No drive-by refactoring.
3. **Tests first.** Write the tests for a requirement before its
   implementation. Every test that proves a requirement carries
   `[Trait("Req", "<ID>")]`.
4. **Never weaken a check.** Do not delete, skip, loosen or rewrite an existing
   test, assertion, analyzer rule, coverage threshold or CI step to make your
   change pass. If a test looks wrong, stop and ask.
5. **Protected paths.** Do not modify `.github/`, `.claude/`, `tools/`,
   `prompts/`, `scripts/`, `docs/spec.md`, `AGENTS.md`, `CLAUDE.md`,
   `Directory.Build.props`, `.editorconfig` or `global.json`. In
   `docs/requirements.yml` you may change only the `status:` line of your own
   requirements, from `planned` to `implemented`.
6. **Core stays pure.** Nothing in `ZoneFvg.Core` may reference `cAlgo`, read
   a clock, touch files or the network, use `TimeZoneInfo`, or create unseeded
   randomness. Time arrives as data: bar times and the broker port's server
   time.
7. **Determinism.** Same inputs, same outputs. No dependence on dictionary
   iteration order, culture, machine time zone or floating-point formatting.
   Use `CultureInfo.InvariantCulture` for every parse and format.
8. **Closed bars only.** No code path may read a forming bar.
9. **The broker is the truth.** Orders, positions and history are always read
   from the broker port. Never cache them across events as a source of truth.
10. **Fail closed.** On missing data, an unexpected value or an exception, the
    safe behaviour is to place no order. Never swallow an exception silently.
11. **No secrets.** No tokens, passwords, account numbers or URLs with keys in
    code, tests, logs, commits or pull requests.
12. **No new dependencies** without the human's approval in the work package
    prompt.

## Coding standards

- C# 10, `net6.0`, nullable enabled, warnings are errors in Core and tests.
- Prices are `double`. Compare with the helpers in `PriceMath` (to be created
  in WP01), never with raw `==` after arithmetic.
- Immutable records for data (`Bar`, `Zone` snapshots, `Signal`, `NewsEvent`).
- No static mutable state. No singletons. Dependencies are constructor
  parameters.
- Public members have XML documentation that names the requirement id.
- Methods stay small enough to test in isolation. No method longer than about
  60 lines without a reason stated in a comment.

## Testing standards

- xUnit. Test names read `Method_Condition_Expected`.
- Each requirement has tests for the normal case, each boundary (exactly at
  the threshold, one tick either side) and each rejection path.
- Bar data in tests is built with the `BarBuilder` helper (WP01), never loaded
  from a network or a clock.
- Scenario tests drive the full core through `FakeBroker` (WP06).
- Property-style tests use a seeded generator and print the seed on failure.
- Coverage of `ZoneFvg.Core`: at least 90% lines and 80% branches.

## Pull requests

- Branch name: `wp/NN-short-name`.
- Title: `WPNN: short description`.
- Fill in every section of the pull request template.
- Commit messages: imperative, with requirement ids, for example
  `Add impulse leg detection (SIG-007)`.
- If you had to make a decision the spec does not cover, list it under
  "Decisions for the human" and do not mark the related requirement
  `implemented`.

## When to stop and ask

Stop, write down the question, and wait for the answer (the orchestrating
session gives it; see "Who merges") when:

- a requirement is ambiguous or contradicts another;
- a requirement cannot be tested as written;
- an existing test fails for a reason you do not fully understand;
- the work needs a change in a protected path;
- you would need more than the files your prompt allows.

## Review

Every pull request is checked by CI and by the validator agent
(`prompts/validator.md`). The validator did not write your code and will not
take your word for anything: it reads the diff and re-runs the checks.

### Who merges (owner decision of 2026-11-22)

The owner does not review or merge pull requests. This replaces the rule of
2026-11-13, under which protected paths and open decisions waited for the
owner.

Implementer and validator agents never merge and never add a label.

The orchestrating session merges every pull request for the owner, also one
that changes a protected path, when all of these hold on the head commit:

- `build-test` and `repo-checks` are green;
- `protected-paths` is green. A pull request that changes a protected path
  needs the label `human-approved-infra` for that. The orchestrating session
  adds the label itself, after it has read every protected change in the
  diff;
- for a work-package pull request that changes `src/` or `tests/`: the
  validator verdict for that head commit is PASS. If the validator cannot be
  run at that time (for example because of a usage limit), the pull request
  is merged on green CI and the validator runs on `main` afterwards; what it
  finds is fixed in a new pull request (owner decision of 2026-11-14);
- no question of the implementer or of the validator is left without an
  answer.

**Questions and decisions.** When an agent stops and asks, or lists
something under "Decisions for the human" or `questions_for_owner`, the
orchestrating session answers. A decision that binds later work is recorded
in `docs/architecture.md` section 10 with its date. Where this file or a
prompt says "ask the human" or "ask the owner", it now means: ask the
orchestrating session.

**What the owner is told.** After every merge the orchestrating session
reports to the owner in plain language: what was merged; anything that
touches orders, risk or money; every protected path that changed; every
decision it made; and every existing test or check that was changed, and
why. The owner can reverse any of it. A reversal is a new pull request.

**What stays with the owner.** None of these is a merge, and no agent does
or decides them:

- the money decisions recorded in the spec: the account, the symbols, the
  risk per trade and the ladder, the account limits and the FTMO guard
  values. They change only when the owner asks for the change;
- the gates that need the owner's cTrader application or account: the
  backtests and their go or no-go, the manual test records (requirements
  with `verify: manual` or `backtest`), the fault-injection tests and the
  start of the FTMO Challenge (`prompts/00-orchestrator.md`);
- secrets and credentials.
