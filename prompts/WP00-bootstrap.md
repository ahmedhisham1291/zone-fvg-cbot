# WP00 — Bootstrap: make the empty skeleton build and CI green

## Context

The repository skeleton (projects, CI workflows, check scripts) was written
without access to a .NET SDK and has never been compiled. Your job is to make
it build, test and pass every CI check, changing as little as possible.

Read `AGENTS.md` and `docs/architecture.md` first.

## Special permission for this work package only

You may edit build infrastructure: `*.csproj`, `Directory.Build.props`,
`global.json`, `.editorconfig`, `.gitignore`, `.github/workflows/*.yml`. The
owner will add the label `human-approved-infra` to this pull request. You may
not edit `tools/*.py`, `prompts/`, `docs/spec.md` or `docs/requirements.yml`.
If a Python check script has a bug, report it; do not patch it.

## Tasks

1. Install or locate the .NET 10 SDK. Run every command in the "Commands"
   section of `AGENTS.md` and record the output.
2. Fix whatever prevents a clean result. Typical causes: package versions that
   do not exist or conflict, formatting that `dotnet format` rejects, the
   out-of-support warning for `net6.0`, analyzer warnings in the placeholder
   code.
3. Create a solution file with `dotnet new sln` that contains the three
   projects, and switch the commands in `AGENTS.md`-equivalent CI steps to use
   it only if that makes them simpler. Keep the job names in the workflows
   unchanged: `build-test`, `repo-checks`, `protected-paths`, `validator`.
4. Enable NuGet lock files (`RestorePackagesWithLockFile`) and commit the lock
   files. Make CI restore in locked mode.
5. Confirm that `dotnet build src/ZoneFvg.Bot -c Release` produces a `.algo`
   file on Linux, and find out whether `ZoneFvg.Core.dll` is packed inside it.
   - If it is: record how you verified it.
   - If it is not: change `ZoneFvg.Bot.csproj` to compile the Core sources
     directly (`<Compile Include="..\ZoneFvg.Core\**\*.cs" Exclude="..\ZoneFvg.Core\obj\**;..\ZoneFvg.Core\bin\**" />`)
     instead of referencing the project, and record that decision in
     `docs/architecture.md` section 9 (the owner approves this edit).
6. Check that the cTrader-specific build settings are right: target `net6.0`,
   `LangVersion` 10. cTrader's own compiler builds cBots as C# 10 for .NET 6,
   so nothing newer may be used in `ZoneFvg.Core` or `ZoneFvg.Bot`.
7. Push the branch and iterate until all four CI jobs are green. The
   `validator` job needs the `ANTHROPIC_API_KEY` secret; if it is missing, say
   so in the pull request rather than disabling the job.
8. Create `docs/progress.md` with a table: work package, pull request, date
   merged, notes.

## Out of scope

Any trading logic. Do not add types from `docs/architecture.md` yet.

## Definition of done

- [ ] All commands in `AGENTS.md` run clean locally; outputs pasted in the pull request.
- [ ] CI jobs `build-test`, `repo-checks` and `protected-paths` are green.
- [ ] The `.algo` artifact is attached to the CI run.
- [ ] Lock files committed; CI restores in locked mode.
- [ ] The answer to task 5 is written down with evidence.
- [ ] No check was weakened. Every change to a threshold or rule is listed under "Decisions for the human".
