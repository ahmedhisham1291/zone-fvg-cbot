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

## For the owner before merging

- [ ] CI jobs `build-test`, `repo-checks`, `protected-paths` are green.
- [ ] Validator verdict is PASS and I read its report.
- [ ] I read "Decisions for the human".
- [ ] I read the diff of anything that touches orders, risk or money.
