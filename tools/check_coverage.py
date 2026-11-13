#!/usr/bin/env python3
"""Coverage gate for Cobertura reports produced by coverlet.

usage: check_coverage.py <results-dir> --package ZoneFvg.Core --min-line 90 --min-branch 80

Finds the newest coverage.cobertura.xml under <results-dir>, reads the line
and branch rate of the named package, and exits 1 if either is below its
minimum or the package is missing.
"""
from __future__ import annotations

import argparse
import sys
import xml.etree.ElementTree as ET
from pathlib import Path


def newest_report(results_dir: Path) -> Path | None:
    reports = sorted(results_dir.rglob("coverage.cobertura.xml"), key=lambda p: p.stat().st_mtime)
    return reports[-1] if reports else None


def package_rates(report: Path, package: str) -> tuple[float, float, int, int] | None:
    root = ET.parse(report).getroot()
    lines_total = lines_hit = 0
    branch_total = branch_hit = 0
    found = False
    for pkg in root.iter("package"):
        if pkg.get("name") != package:
            continue
        found = True
        for line in pkg.iter("line"):
            lines_total += 1
            if int(line.get("hits", "0")) > 0:
                lines_hit += 1
            if line.get("branch", "").lower() == "true":
                cond = line.get("condition-coverage", "")
                # format: "50% (1/2)"
                if "(" in cond and "/" in cond:
                    covered, total = cond[cond.index("(") + 1:cond.index(")")].split("/")
                    branch_hit += int(covered)
                    branch_total += int(total)
    if not found:
        return None
    line_rate = 100.0 * lines_hit / lines_total if lines_total else 100.0
    branch_rate = 100.0 * branch_hit / branch_total if branch_total else 100.0
    return line_rate, branch_rate, lines_total, branch_total


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("results_dir", type=Path)
    parser.add_argument("--package", required=True)
    parser.add_argument("--min-line", type=float, required=True)
    parser.add_argument("--min-branch", type=float, required=True)
    args = parser.parse_args()

    report = newest_report(args.results_dir)
    if report is None:
        print(f"COVERAGE CHECK FAILED: no coverage.cobertura.xml under {args.results_dir}")
        return 1

    rates = package_rates(report, args.package)
    if rates is None:
        print(f"COVERAGE CHECK FAILED: package {args.package} not found in {report}")
        return 1

    line_rate, branch_rate, lines_total, branch_total = rates
    print(f"{args.package}: lines {line_rate:.1f}% of {lines_total}, branches {branch_rate:.1f}% of {branch_total}")
    ok = True
    if line_rate + 1e-9 < args.min_line:
        print(f"COVERAGE CHECK FAILED: line coverage {line_rate:.1f}% is below {args.min_line:.1f}%")
        ok = False
    if branch_rate + 1e-9 < args.min_branch:
        print(f"COVERAGE CHECK FAILED: branch coverage {branch_rate:.1f}% is below {args.min_branch:.1f}%")
        ok = False
    if ok:
        print("coverage check passed")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
