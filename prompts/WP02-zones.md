# WP02 — Zones: detection and lifecycle

## Read first

`AGENTS.md`, `docs/architecture.md` (sections 2, 3), `docs/spec.md` sections
3.2 and 3.3. Read the text of each requirement below in
`docs/requirements.yml`; it is more precise than the spec prose and wins where
they differ.

## Requirements to implement

SIG-007, SIG-008, SIG-009, SIG-010, SIG-011, SIG-012, SIG-013, SIG-014,
SIG-015.

## Allowed paths

`src/ZoneFvg.Core/Domain/Zone.cs`, `src/ZoneFvg.Core/Signals/ZoneDetector.cs`,
`src/ZoneFvg.Core/Signals/ZoneBook.cs`, `src/ZoneFvg.Core/Signals/ImpulseLeg.cs`,
`tests/ZoneFvg.Core.Tests/**`, and the `status:` lines of the requirements
above.

## What to build

1. **`Zone`** record as in `docs/architecture.md`. For a demand zone
   `ExtremeSinceTouch` is the lowest low since the touch bar inclusive; for a
   supply zone the highest high.
2. **`ImpulseLeg`** finder (SIG-007). Input: H4 buffer ending at the
   just-closed bar. Output: the longest qualifying leg (1 to 3 bars) or none.
   The ATR used is the value as of the bar immediately before the leg's first
   bar. Keep that ATR value: SIG-009 and SIG-011 use the same number.
3. **`ZoneDetector.Detect(h4Buffer, swings, parameters)`** returns zero or one
   new zone per H4 close, applying SIG-008 to SIG-011 and SIG-013 in that
   order. It is a pure function. It does not know about existing zones.
4. **`ZoneBook`**:
   - `Add(zone)` applies the supersede rule SIG-012.
   - `OnH4Closed(bar)` expires zones (SIG-014, age counted in closed H4 bars
     since activation).
   - `OnM15Closed(bar)` advances Active to Touched and applies invalidation
     (SIG-014), considering only bars with `OpenTimeUtc >= ActivationTimeUtc`.
     It updates `ExtremeSinceTouch`. It exposes, for WP03, the zones currently
     in `Touched` together with the number of M15 bars closed since the touch
     bar, and a method `MarkSignalled(zoneId)` / `MarkUsed(zoneId)`.
   - The window-end transition Touched to Used (SIG-014) is applied here when
     `ConfirmWindowBars` bars have closed since the touch bar and the zone was
     not marked.
   - `Zones` returns non-terminal zones ordered by id (ordinal). Never expose
     a dictionary's iteration order.
5. Order of operations inside one M15 close, which WP03 will rely on:
   invalidation check first, then touch detection, then (WP03) FVG detection,
   then window-end.

## Decisions already made (do not reopen)

- A bar that touches and closes beyond distal in the same bar makes the zone
  Invalid, not Touched.
- The touch bar itself counts as bar 0 of the confirmation window.
- Zone ids can collide only if two zones share a first base bar and type; in
  that case the later detection is discarded and a diagnostic is recorded.
- Overlap for SIG-012 = length of the intersection of the two price ranges
  divided by the older zone's height; "more than 50%" is strict.

## Tests required

Build every scenario with `BarBuilder`; state the expected edges as literal
numbers computed by hand in a comment.

- SIG-007: one-bar leg exactly at the threshold (kept), one tick below
  (rejected); three-bar leg chosen over a qualifying one-bar leg; a doji
  breaks the run; four same-direction bars use only the last three; ATR taken
  from the bar before the leg.
- SIG-008: close above the swing (kept), equal to it (rejected), no swing in
  range (rejected), `RequireBos=false` (kept); the swing must be before the
  leg's first bar.
- SIG-009: one, two and three base bars; stops at a wide bar; the first bar
  back is included even when wide.
- SIG-010: demand and supply edges on a worked example; degenerate zone
  discarded.
- SIG-011: width exactly 0.2 and 1.5 ATR kept; just outside rejected.
- SIG-012: 51% overlap removes the older Active zone; 50% does not; a Touched
  older zone is never removed; different types never interact.
- SIG-013: activation time; id format; identical history gives identical ids.
- SIG-014: every transition in the requirement, including touch and
  invalidation in the same bar, bars before activation ignored, expiry at
  exactly 120 H4 bars, terminal states never change again.
- SIG-015: a supply zone is detected and tracked while bias is Long.

## Out of scope

FVG detection, signals, bias gating, anything about orders.

## Definition of done

- [ ] Tests first, all green; coverage thresholds met.
- [ ] `ZoneDetector` has no mutable state.
- [ ] All `AGENTS.md` commands clean; outputs in the pull request.
- [ ] The nine requirement statuses are `implemented`.
