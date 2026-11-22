#!/usr/bin/env python3
"""Coverage gate for Cobertura reports produced by coverlet.

usage: check_coverage.py <results-dir> --package ZoneFvg.Core --min-line 90 --min-branch 80

Finds the newest coverage.cobertura.xml under <results-dir>, reads the line
and branch rate of the named package, and exits 1 if either is below its
minimum or the package is missing.

After the gate result it prints the covered and total lines and branches of
every file of the package. That listing is information only: it never changes
the exit code. It exists so that "the new files are fully covered" can be
read in the CI log by a session that cannot download the CI artifact.
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


def condition_counts(line: ET.Element) -> tuple[int, int]:
    """Covered and total branches of one <line>, from 'NN% (covered/total)'."""
    if line.get("branch", "").lower() != "true":
        return 0, 0
    cond = line.get("condition-coverage", "")
    if "(" not in cond or "/" not in cond:
        return 0, 0
    covered, total = cond[cond.index("(") + 1:cond.index(")")].split("/")
    return int(covered), int(total)


def file_counts(report: Path, package: str) -> dict[str, list[int]]:
    """Per file of the package: [lines hit, lines total, branches hit, branches total].

    Information only; the gate uses package_rates. Each class is read once,
    from its own <lines> element (a Cobertura report repeats the same lines
    under <methods>). Classes that share a file are added up, so a file shows
    as fully covered only if every class in it is.
    """
    root = ET.parse(report).getroot()
    files: dict[str, list[int]] = {}
    for pkg in root.iter("package"):
        if pkg.get("name") != package:
            continue
        for cls in pkg.iter("class"):
            name = (cls.get("filename") or "?").replace("\\", "/")
            own = cls.find("lines")
            lines = list(own) if own is not None and len(own) else list(cls.iter("line"))
            counts = files.setdefault(name, [0, 0, 0, 0])
            for line in lines:
                counts[1] += 1
                if int(line.get("hits", "0")) > 0:
                    counts[0] += 1
                covered, total = condition_counts(line)
                counts[2] += covered
                counts[3] += total
    return files


def print_file_listing(report: Path, package: str) -> None:
    """Prints the per-file listing. Never raises and never changes the result."""
    try:
        files = file_counts(report, package)
    except Exception as error:  # the listing must not be able to fail the gate
        print(f"coverage by file: not available ({type(error).__name__}: {error})")
        return
    print(f"coverage by file ({package}, {len(files)} files; information only, the gate is the result above):")
    partial = 0
    sums = [0, 0, 0, 0]
    for name in sorted(files):
        lines_hit, lines_total, branch_hit, branch_total = files[name]
        full = lines_hit == lines_total and branch_hit == branch_total
        partial += 0 if full else 1
        for index, value in enumerate(files[name]):
            sums[index] += value
        mark = "full" if full else "PART"
        print(f"  {mark}  {name}: lines {lines_hit}/{lines_total}, branches {branch_hit}/{branch_total}")
    print(f"files not fully covered: {partial} of {len(files)}; "
          f"all files together: lines {sums[0]}/{sums[1]}, branches {sums[2]}/{sums[3]}")


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
    print_file_listing(report, args.package)
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
