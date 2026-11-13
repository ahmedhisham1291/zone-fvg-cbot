# Claude Code entry point

The project rules for every agent are in `AGENTS.md`. Read that file in full
before doing anything else, and follow it.

@AGENTS.md

## How work is started here

- To implement a work package, the human says for example:
  "Implement prompts/WP01-core-primitives.md". Read that file and follow it.
- To review, the human says: "Use the validator subagent on this branch".
  The validator's instructions are in `prompts/validator.md`.
- Never implement and validate the same change in one session.
