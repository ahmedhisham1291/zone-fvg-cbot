#!/usr/bin/env python3
"""Turns the validator's JSON report into a pass/fail exit code.

usage: enforce_verdict.py validator-report.json

Exit 0 only when the report exists, has the required shape, its verdict is
PASS, it lists no blocking finding and every requirement verdict is `ok`.
Anything else, including a missing or malformed report, exits 1.
"""
from __future__ import annotations

import json
import sys
from pathlib import Path

REQUIRED_KEYS = {"verdict", "work_package", "requirements", "blocking_findings"}


def main() -> int:
    if len(sys.argv) != 2:
        print("usage: enforce_verdict.py <validator-report.json>")
        return 2

    path = Path(sys.argv[1])
    if not path.exists():
        print("VALIDATOR GATE FAILED: no report was produced")
        return 1

    try:
        report = json.loads(path.read_text(encoding="utf-8"))
    except json.JSONDecodeError as exc:
        print(f"VALIDATOR GATE FAILED: report is not valid JSON ({exc})")
        return 1

    if not isinstance(report, dict) or not REQUIRED_KEYS.issubset(report):
        missing = sorted(REQUIRED_KEYS - set(report)) if isinstance(report, dict) else sorted(REQUIRED_KEYS)
        print(f"VALIDATOR GATE FAILED: report is missing {missing}")
        return 1

    verdict = str(report.get("verdict", "")).strip().upper()
    blocking = report.get("blocking_findings") or []
    requirements = report.get("requirements") or []
    not_ok = [r for r in requirements if isinstance(r, dict) and str(r.get("verdict", "")).lower() != "ok"]

    print(f"work package: {report.get('work_package')}")
    print(f"verdict: {verdict}; blocking findings: {len(blocking)}; requirements not ok: {len(not_ok)}")
    for finding in blocking:
        if isinstance(finding, dict):
            print(f"  [{finding.get('rule')}] {finding.get('file')}:{finding.get('line')} {finding.get('finding')}")
    for req in not_ok:
        print(f"  {req.get('id')}: {req.get('verdict')}")

    if verdict != "PASS":
        print("VALIDATOR GATE FAILED: verdict is not PASS")
        return 1
    if blocking:
        print("VALIDATOR GATE FAILED: verdict says PASS but blocking findings are listed")
        return 1
    if not_ok:
        print("VALIDATOR GATE FAILED: verdict says PASS but not every requirement is ok")
        return 1

    print("validator gate passed (the owner still decides whether to merge)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
