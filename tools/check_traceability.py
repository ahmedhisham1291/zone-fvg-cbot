#!/usr/bin/env python3
"""Traceability check between docs/requirements.yml, prompts/ and tests/.

Fails (exit 1) when:
  * requirements.yml is malformed, has duplicate ids, unknown fields or values;
  * a requirement names a work package that has no prompt file;
  * a work package prompt does not mention a requirement assigned to it;
  * a prompt mentions a requirement id that does not exist;
  * a test carries [Trait("Req", "<ID>")] for an id that does not exist;
  * a requirement with status `implemented` and verify unit/scenario has no
    tagged test.

Uses only the Python standard library so that it can run anywhere. The YAML
subset used by requirements.yml is parsed by a small dedicated reader.
"""
from __future__ import annotations

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
REQ_FILE = ROOT / "docs" / "requirements.yml"
PROMPTS = ROOT / "prompts"
TESTS = ROOT / "tests"

ID_RE = re.compile(r"\b([A-Z]{3}-\d{3})\b")
TRAIT_RE = re.compile(r'Trait\(\s*"Req"\s*,\s*"([^"]+)"\s*\)')
ALLOWED_VERIFY = {"unit", "scenario", "build", "manual", "backtest"}
ALLOWED_STATUS = {"planned", "implemented"}
REQUIRED_FIELDS = ("id", "wp", "spec", "verify", "status", "text")
NEEDS_TEST = {"unit", "scenario"}


def parse_requirements(text: str) -> list[dict[str, str]]:
    """Parse the restricted YAML layout used by requirements.yml."""
    items: list[dict[str, str]] = []
    current: dict[str, str] | None = None
    folded_key: str | None = None
    folded_lines: list[str] = []

    def close_folded() -> None:
        nonlocal folded_key, folded_lines
        if current is not None and folded_key is not None:
            current[folded_key] = " ".join(s.strip() for s in folded_lines if s.strip())
        folded_key, folded_lines = None, []

    for raw in text.splitlines():
        line = raw.rstrip()
        stripped = line.strip()
        if not stripped or stripped.startswith("#"):
            if folded_key is not None and not stripped:
                continue
            continue
        indent = len(line) - len(line.lstrip(" "))

        if folded_key is not None:
            if indent >= 6:
                folded_lines.append(stripped)
                continue
            close_folded()

        if stripped.startswith("- id:"):
            if current is not None:
                items.append(current)
            current = {"id": stripped.split(":", 1)[1].strip().strip('"')}
            continue

        if current is None:
            continue  # header lines such as `version:` and `requirements:`

        if ":" not in stripped:
            raise ValueError(f"cannot parse line: {raw!r}")
        key, value = stripped.split(":", 1)
        key, value = key.strip(), value.strip()
        if value in (">-", ">", "|", "|-"):
            folded_key, folded_lines = key, []
        else:
            current[key] = value.strip('"')

    close_folded()
    if current is not None:
        items.append(current)
    return items


def main() -> int:
    errors: list[str] = []

    if not REQ_FILE.exists():
        print(f"ERROR: {REQ_FILE} not found")
        return 1

    try:
        reqs = parse_requirements(REQ_FILE.read_text(encoding="utf-8"))
    except ValueError as exc:
        print(f"ERROR: {exc}")
        return 1

    if not reqs:
        errors.append("requirements.yml contains no requirements")

    by_id: dict[str, dict[str, str]] = {}
    for req in reqs:
        rid = req.get("id", "?")
        for field in REQUIRED_FIELDS:
            if not req.get(field):
                errors.append(f"{rid}: missing field '{field}'")
        unknown = set(req) - set(REQUIRED_FIELDS)
        if unknown:
            errors.append(f"{rid}: unknown field(s) {sorted(unknown)}")
        if not re.fullmatch(r"[A-Z]{3}-\d{3}", rid):
            errors.append(f"{rid}: id must look like ABC-123")
        if rid in by_id:
            errors.append(f"{rid}: duplicate id")
        by_id[rid] = req
        if req.get("verify") not in ALLOWED_VERIFY:
            errors.append(f"{rid}: verify must be one of {sorted(ALLOWED_VERIFY)}")
        if req.get("status") not in ALLOWED_STATUS:
            errors.append(f"{rid}: status must be one of {sorted(ALLOWED_STATUS)}")
        if not re.fullmatch(r"WP\d{2}", req.get("wp", "")):
            errors.append(f"{rid}: wp must look like WP01")

    # Prompts: which ids each work package prompt mentions.
    prompt_ids: dict[str, set[str]] = {}
    for path in sorted(PROMPTS.glob("WP[0-9][0-9]-*.md")):
        wp = path.name[:4]
        prompt_ids.setdefault(wp, set()).update(ID_RE.findall(path.read_text(encoding="utf-8")))

    for wp, ids in prompt_ids.items():
        for rid in sorted(ids):
            if rid not in by_id:
                errors.append(f"prompt {wp}: mentions unknown requirement {rid}")

    for rid, req in by_id.items():
        wp = req.get("wp", "")
        if wp not in prompt_ids:
            errors.append(f"{rid}: no prompt file for {wp} in prompts/")
        elif rid not in prompt_ids[wp]:
            errors.append(f"{rid}: assigned to {wp} but not mentioned in its prompt")

    # Tests: which ids are covered by tagged tests.
    tested: dict[str, int] = {}
    if TESTS.exists():
        for path in TESTS.rglob("*.cs"):
            if any(part in ("bin", "obj") for part in path.parts):
                continue
            for rid in TRAIT_RE.findall(path.read_text(encoding="utf-8", errors="replace")):
                tested[rid] = tested.get(rid, 0) + 1
                if rid not in by_id:
                    errors.append(f"{path.relative_to(ROOT)}: Trait references unknown requirement {rid}")

    for rid, req in by_id.items():
        if req.get("status") == "implemented" and req.get("verify") in NEEDS_TEST and tested.get(rid, 0) == 0:
            errors.append(f"{rid}: status is 'implemented' but no test is tagged [Trait(\"Req\", \"{rid}\")]")

    total = len(by_id)
    implemented = sum(1 for r in by_id.values() if r.get("status") == "implemented")
    print(f"requirements: {total} total, {implemented} implemented, {len(tested)} ids with tagged tests")
    by_wp: dict[str, list[int]] = {}
    for req in by_id.values():
        counts = by_wp.setdefault(req.get("wp", "?"), [0, 0])
        counts[0] += 1
        counts[1] += req.get("status") == "implemented"
    for wp in sorted(by_wp):
        print(f"  {wp}: {by_wp[wp][1]}/{by_wp[wp][0]} implemented")

    if errors:
        print(f"\nTRACEABILITY CHECK FAILED ({len(errors)} problem(s)):")
        for err in errors:
            print(f"  - {err}")
        return 1

    print("traceability check passed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
