# WP28: Entry mode in the cBot, the deployment files and the guides

## Read first

`AGENTS.md`; `docs/spec.md` section 0 (changes in v0.10) and sections 3.7,
7, 10.2, 12.1 and 13.1; `docs/architecture.md` section 8 and section 10
(the row "Entry mode" of 2026-11-22); the requirement texts below;
`src/ZoneFvg.Bot/ZoneFvgBot.cs` and `ParameterText.cs`; `deploy/README.md`,
`deploy/params/*.cbotset.example`, `docs/backtest-howto.md` and
`README.md`. Depends on WP25, WP26 and WP27.

## Requirements to implement

PLT-012, PLT-013.

Owner request of 2026-11-22. Both are verified by hand (`verify: manual`),
so their status stays `planned` until the owner has signed the test record.
The adapter still holds no trading rule: it translates three parameters
into `BotParameters.Breakout` or leaves it null.

## Allowed paths

- `src/ZoneFvg.Bot/ZoneFvgBot.cs` and `ParameterText.cs`
- `deploy/**`
- `docs/backtest-howto.md`, `README.md`
- a new file `docs/test-records/WP28-entry-mode.md`
- `tests/ZoneFvg.Core.Tests/**` (only if you add a core helper that needs a
  test; none is expected)

No file under `src/ZoneFvg.Core/` changes. If you need one, stop and ask.

## Owner authorisation to change existing tests

None.

## What to build, in this order

1. **PLT-012, parameters.** Three cBot parameters in a new group
   "Entry mode", in the style of the existing ones:
   - `EntryMode` (text, default `Breakout`), parsed with
     `ParameterText.EnumName<EntryMode>`: `Breakout` or `ZoneFvg`, any other
     value refuses to start through the existing "invalid parameter" path;
   - `BreakoutEntryBufferAtr` (default 0.1);
   - `BreakoutStopAtr` (default 1.0).
   `BuildParameters` sets `Breakout` to
   `new BreakoutParameters { EntryBufferAtr = BreakoutEntryBufferAtr,
   StopAtrMult = BreakoutStopAtr, WindowStartUtc = <parsed SessionStartUtc>,
   WindowEndUtc = <parsed SessionEndUtc> }` in Breakout mode and leaves it
   null in ZoneFvg mode. The adapter's first log line names the mode
   (`... backtest yes, mode Breakout`). Nothing else in the adapter changes.
2. **PLT-013, deployment files.** Every file in `deploy/params/` gets
   `EntryMode=Breakout`, `BreakoutEntryBufferAtr=0.1` and
   `BreakoutStopAtr=1.0`, placed first. No other value changes: gold keeps
   its three own values. `deploy/README.md` names the mode, says that one
   instance runs one mode, and that a change of mode is a parameter change:
   only when flat (spec 10.2, "Deployments"), because open trades of the
   other mode become strangers.
3. **PLT-013, backtest guide.** In `docs/backtest-howto.md`:
   - step 6: `EntryMode` is `Breakout` or `ZoneFvg`; run the whole of Part 2
     once per mode with the same period, data, commission and spread, and
     keep the files apart (`eurusd-breakout-dev.csv`,
     `eurusd-zonefvg-dev.csv`);
   - Part 3: each command once per mode; in the `accept` run of the
     ZoneFvg mode, the Breakout development files are the `--benchmark`
     input (spec 12.1 step 10). The Breakout mode has no benchmark yet: its
     report shows the benchmark rows as missing (INCOMPLETE), and spec 13.1
     says that row is not applied to it. Say this plainly, so the owner
     does not read INCOMPLETE as a failure of the other rows;
   - Part 4: the table is applied to each mode separately; a mode goes live
     only if it passes by itself; if both pass, the better
     return-to-drawdown ratio on development data decides, and the holdout
     is run once for that mode only (spec 13.1);
   - the zone drawings exist in ZoneFvg mode only.
   Keep the existing facts and sources of the guide as they are.
4. **PLT-013, `README.md`.** The status paragraph is out of date ("skeleton
   only", spec v0.8, 94 requirements). Replace it with the present state:
   the bot is built, two entry modes, spec v0.10, the count printed by
   `python3 tools/check_traceability.py`, and that no backtest result is
   recorded yet. Fix the title line only if the owner says so in the pull
   request; the repository name stays.
5. **Test record** `docs/test-records/WP28-entry-mode.md` in the style of
   `WP08-adapter.md`, for the owner on cTrader desktop (Mac) with a demo or
   Free Trial account or in a backtest:
   - the parameter panel shows the group "Entry mode" with the three
     defaults;
   - start with the defaults: the adapter line says `mode Breakout`, the
     core start line ends with `mode=Breakout`, and the hash differs from
     the one of a ZoneFvg start;
   - start with `EntryMode=ZoneFvg`: no `mode=` in the core start line, and
     the hash equals the one the bot printed before this change with the
     same parameters;
   - `EntryMode=Abc`: the bot refuses to start and names the parameter;
     `BreakoutStopAtr=0`: refuses and names it;
   - a short backtest in each mode on EURUSD produces `signal` lines with
     `kind=breakout` in one and zone ids in the other;
   - each parameter file in `deploy/params/` loads without an error.
   Each step has "Observed" and "Pass / fail" lines.

## Tests required

None in CI beyond the build of the cBot and the unchanged test suite: both
requirements are manual. Do not add a test that only repeats the parameter
file's text.

## Out of scope

Any trading rule; the embedded news history; index CFDs; chart drawing for
breakouts; the VPS setup.

## Definition of done

- [ ] The cBot builds in CI and the `.algo` artifact is uploaded.
- [ ] No file under `src/ZoneFvg.Core/` and no existing test changed.
- [ ] Every command of `AGENTS.md` run and its output pasted (without a
      .NET SDK in the session: say so and paste the CI output).
- [ ] PLT-012 and PLT-013 stay `planned`; the pull request says which steps
      of the record the owner still has to run.
- [ ] "Decisions for the human" lists anything the guides state that you
      could not check against a source.
