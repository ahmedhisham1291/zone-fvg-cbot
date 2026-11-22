#!/usr/bin/env python3
"""Protected-path guard for pull requests.

Reads a JSON-lines file with one object per changed file
({"filename", "status", "previous_filename", "patch"}) as produced by:

    gh api repos/<owner>/<repo>/pulls/<n>/files --paginate \
        --jq '.[] | {filename, status, previous_filename, patch}'

Labels come from the PR_LABELS environment variable (comma separated).

Rules
  * Protected paths may change only when the pull request carries the label
    `human-approved-infra`. The orchestrating session applies it after
    reading every protected change (AGENTS.md, "Who merges", owner decision of
    2026-11-22); the owner may apply it too. Implementer and validator agents
    never do.
  * docs/requirements.yml may change without the label only if every changed
    line is a `status:` line going from `planned` to `implemented`.

This script is run by a workflow triggered on `pull_request_target`, so the
copy that runs always comes from the base branch and cannot be altered by the
pull request under test.
"""
from __future__ import annotations

import fnmatch
import json
import os
import re
import sys
from pathlib import Path

OVERRIDE_LABEL = "human-approved-infra"

PROTECTED = [
    ".github/*",
    ".claude/*",
    "tools/*",
    "prompts/*",
    "scripts/*",
    "docs/spec.md",
    "docs/architecture.md",
    "AGENTS.md",
    "CLAUDE.md",
    "Directory.Build.props",
    ".editorconfig",
    ".gitignore",
    "global.json",
]
# tools/sim is a normal source folder created by WP10, not a check script.
NOT_PROTECTED = ["tools/sim/*"]

REQUIREMENTS = "docs/requirements.yml"
STATUS_REMOVED = re.compile(r"^-\s+status:\s*planned\s*$")
STATUS_ADDED = re.compile(r"^\+\s+status:\s*implemented\s*$")


def is_protected(name: str) -> bool:
    if any(fnmatch.fnmatch(name, pattern) for pattern in NOT_PROTECTED):
        return False
    return any(fnmatch.fnmatch(name, pattern) for pattern in PROTECTED)


def requirements_patch_ok(patch: str | None) -> tuple[bool, str]:
    if not patch:
        return False, "no patch available for docs/requirements.yml (file too large or binary)"
    removed = added = 0
    for line in patch.splitlines():
        if line.startswith(("+++", "---", "@@")):
            continue
        if line.startswith("-"):
            if not STATUS_REMOVED.match(line):
                return False, f"line removed that is not 'status: planned': {line!r}"
            removed += 1
        elif line.startswith("+"):
            if not STATUS_ADDED.match(line):
                return False, f"line added that is not 'status: implemented': {line!r}"
            added += 1
    if removed != added:
        return False, f"{removed} status line(s) removed but {added} added"
    return True, f"{added} requirement(s) marked implemented"


def main() -> int:
    if len(sys.argv) != 2:
        print("usage: check_protected_paths.py <pr-files.jsonl>")
        return 2

    labels = {label.strip() for label in os.environ.get("PR_LABELS", "").split(",") if label.strip()}
    files = []
    for raw in Path(sys.argv[1]).read_text(encoding="utf-8").splitlines():
        raw = raw.strip()
        if raw:
            files.append(json.loads(raw))

    violations: list[str] = []
    notes: list[str] = []
    for item in files:
        names = [item.get("filename") or ""]
        if item.get("previous_filename"):
            names.append(item["previous_filename"])
        for name in names:
            if name == REQUIREMENTS:
                ok, message = requirements_patch_ok(item.get("patch"))
                if item.get("status") != "modified":
                    ok, message = False, f"file was {item.get('status')}, only status edits are allowed"
                (notes if ok else violations).append(f"{name}: {message}")
            elif is_protected(name):
                violations.append(f"{name}: protected path ({item.get('status')})")

    print(f"{len(files)} changed file(s); labels: {sorted(labels) or 'none'}")
    for note in notes:
        print(f"  ok   {note}")

    if violations and OVERRIDE_LABEL in labels:
        print(f"\nProtected changes ALLOWED by label '{OVERRIDE_LABEL}'. Each one is named in the merge report to the owner:")
        for violation in violations:
            print(f"  -    {violation}")
        return 0

    if violations:
        print(f"\nPROTECTED PATH CHECK FAILED ({len(violations)} problem(s)):")
        for violation in violations:
            print(f"  -    {violation}")
        print(f"\nThe orchestrating session (or the owner) approves these by adding the label '{OVERRIDE_LABEL}'.")
        return 1

    print("protected path check passed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
