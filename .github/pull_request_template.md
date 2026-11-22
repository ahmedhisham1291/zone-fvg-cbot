<!-- Title must be: WPNN: short description -->

## Work package

- Prompt file: `prompts/WPNN-....md`
- Branch: `wp/NN-...`

## Requirements

| Id | Status in this PR | Tests that prove it |
|---|---|---|
| ABC-000 | implemented / not done | `ClassName.Method_Condition_Expected` |

## What changed

<!-- Three to six sentences. No marketing. -->

## Command output

<!-- Paste the real, complete tail of each command. Do not summarise. -->

```
dotnet build tests/ZoneFvg.Core.Tests -c Release
dotnet test  tests/ZoneFvg.Core.Tests -c Release --collect:"XPlat Code Coverage" --results-directory TestResults
dotnet build src/ZoneFvg.Bot -c Release
dotnet format ... --verify-no-changes
python3 tools/check_traceability.py
python3 tools/check_architecture.py
python3 tools/check_secrets.py
python3 tools/check_coverage.py TestResults --package ZoneFvg.Core --min-line 90 --min-branch 80
```

## Decisions for the human

<!-- Anything the spec did not settle, with the options and the one you chose.
     Write "None" only if there really are none. -->

## Checklist

- [ ] Only files inside the prompt's allowed paths were changed.
- [ ] No test, assertion, threshold or CI step was removed, skipped or loosened.
- [ ] Tests were written before the implementation.
- [ ] Every requirement marked `implemented` has tagged tests for its boundaries and rejection paths.
- [ ] No secret, account number or credential is in the diff.
- [ ] No new dependency (or it is allowed by the prompt and listed above).

## Before merging

The orchestrating session merges, without the owner's review (`AGENTS.md`, section "Who merges", owner decision of 2026-11-22). Implementer and validator agents never merge.

- [ ] CI jobs `build-test`, `repo-checks`, `protected-paths` are green on the head commit.
- [ ] If a protected path is changed: the orchestrating session has read each protected change and added the label `human-approved-infra`.
- [ ] Work package that changes `src/` or `tests/`: the validator verdict for the head commit is PASS and its report is posted as a comment. If the validator could not be run, say so; it then runs on `main` after the merge.
- [ ] Every question of the implementer and of the validator, and every item under "Decisions for the human", has an answer from the orchestrating session, written in this pull request.
- [ ] After the merge the owner gets a plain-language report: what was merged, anything that touches orders, risk or money, every protected path changed, every decision made, every existing test or check changed.
