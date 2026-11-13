#!/usr/bin/env python3
"""Architecture and hygiene rules that can be checked mechanically.

Core purity (AGENTS.md rule 6, requirement PLT-001), adapter declaration
(PLT-003), and test hygiene (AGENTS.md rule 4). Exit 1 on any violation.
"""
from __future__ import annotations

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
CORE = ROOT / "src" / "ZoneFvg.Core"
BOT = ROOT / "src" / "ZoneFvg.Bot"
TESTS = ROOT / "tests"

# (pattern, explanation). Applied to Core source with comments and strings removed.
CORE_FORBIDDEN = [
    (r"\bcAlgo\b", "Core must not reference cTrader (cAlgo)"),
    (r"\bDateTime\s*\.\s*(Now|UtcNow|Today)\b", "Core must not read the clock"),
    (r"\bDateTimeOffset\s*\.\s*(Now|UtcNow)\b", "Core must not read the clock"),
    (r"\bStopwatch\b", "Core must not measure wall-clock time"),
    (r"\bEnvironment\s*\.\s*(TickCount|TickCount64|MachineName|UserName|GetEnvironmentVariable)\b",
     "Core must not read the environment"),
    (r"\bThread\s*\.\s*Sleep\b", "Core must not sleep"),
    (r"\bTask\s*\.\s*(Delay|Run)\b", "Core must be single-threaded and synchronous"),
    (r"\bnew\s+Random\s*\(\s*\)", "Unseeded randomness is not allowed"),
    (r"\bRandom\s*\.\s*Shared\b", "Unseeded randomness is not allowed"),
    (r"\bGuid\s*\.\s*NewGuid\b", "Random identifiers break determinism"),
    (r"\bTimeZoneInfo\b", "Use CestClock; OS time-zone data is not allowed (RSK-012)"),
    (r"\bSystem\s*\.\s*IO\b", "Core must not perform file IO"),
    (r"\b(File|Directory|Path)\s*\.\s*[A-Z]\w*\s*\(", "Core must not perform file IO"),
    (r"\b(HttpClient|WebClient|Socket|TcpClient)\b", "Core must not perform network access"),
    (r"\bConsole\s*\.\s*\w+", "Core must not write to the console"),
    (r"\bCultureInfo\s*\.\s*(CurrentCulture|CurrentUICulture)\b", "Use InvariantCulture"),
    (r"\bNotImplementedException\b", "Unfinished code must not be merged"),
]

BOT_FORBIDDEN = [
    (r"AccessRights\s*\.\s*FullAccess", "The cBot must use AccessRights.None (PLT-003)"),
    (r"\bDateTime\s*\.\s*(Now|UtcNow|Today)\b", "Use server time, not the machine clock (REC-012)"),
]

TEST_FORBIDDEN = [
    (r"\bSkip\s*=", "Skipped tests are not allowed"),
    (r"\bDateTime\s*\.\s*(Now|UtcNow|Today)\b", "Tests must not depend on the clock"),
    (r"\bnew\s+Random\s*\(\s*\)", "Tests must use a seeded generator"),
    (r"\bHttpClient\b", "Tests must not use the network"),
]

BLOCK_COMMENT = re.compile(r"/\*.*?\*/", re.S)
LINE_COMMENT = re.compile(r"//[^\n]*")
VERBATIM_STRING = re.compile(r'@"(?:[^"]|"")*"')
STRING = re.compile(r'"(?:\\.|[^"\\\n])*"')
CHAR = re.compile(r"'(?:\\.|[^'\\\n])'")


def strip_comments_and_strings(text: str) -> str:
    """Blank out comments and literals while keeping line numbers."""
    def blank(match: re.Match[str]) -> str:
        return re.sub(r"[^\n]", " ", match.group(0))

    for pattern in (BLOCK_COMMENT, VERBATIM_STRING, STRING, CHAR, LINE_COMMENT):
        text = pattern.sub(blank, text)
    return text


def source_files(folder: Path):
    if not folder.exists():
        return
    for path in sorted(folder.rglob("*.cs")):
        if any(part in ("bin", "obj") for part in path.parts):
            continue
        yield path


def scan(folder: Path, rules, errors: list[str]) -> int:
    count = 0
    for path in source_files(folder):
        count += 1
        code = strip_comments_and_strings(path.read_text(encoding="utf-8", errors="replace"))
        for pattern, why in rules:
            for match in re.finditer(pattern, code):
                line = code.count("\n", 0, match.start()) + 1
                errors.append(f"{path.relative_to(ROOT)}:{line}: {why} [{match.group(0).strip()}]")
    return count


def check_project_references(errors: list[str]) -> None:
    for csproj in CORE.glob("*.csproj"):
        text = csproj.read_text(encoding="utf-8")
        if re.search(r"cTrader\.Automate|cAlgo", text):
            errors.append(f"{csproj.relative_to(ROOT)}: Core project must not reference cTrader packages")
        if re.search(r"<ProjectReference", text):
            errors.append(f"{csproj.relative_to(ROOT)}: Core project must not reference other projects")
        if not re.search(r"<TargetFramework>\s*net6\.0\s*</TargetFramework>", text):
            errors.append(f"{csproj.relative_to(ROOT)}: Core must target net6.0 (PLT-004)")
    for csproj in BOT.glob("*.csproj"):
        text = csproj.read_text(encoding="utf-8")
        if not re.search(r"<TargetFramework>\s*net6\.0\s*</TargetFramework>", text):
            errors.append(f"{csproj.relative_to(ROOT)}: Bot must target net6.0 (PLT-004)")


def check_bot_declaration(errors: list[str]) -> None:
    robots = []
    for path in source_files(BOT):
        text = path.read_text(encoding="utf-8", errors="replace")
        if re.search(r":\s*Robot\b", text):
            robots.append(path)
            if not re.search(r"\[Robot\([^\]]*AccessRights\s*=\s*AccessRights\s*\.\s*None", text, re.S):
                errors.append(f"{path.relative_to(ROOT)}: Robot attribute must set AccessRights.None")
            if not re.search(r"\[Robot\([^\]]*TimeZone\s*=\s*TimeZones\s*\.\s*UTC", text, re.S):
                errors.append(f"{path.relative_to(ROOT)}: Robot attribute must set TimeZone = TimeZones.UTC")
    if BOT.exists() and len(robots) != 1:
        errors.append(f"src/ZoneFvg.Bot: expected exactly one Robot class, found {len(robots)}")


def main() -> int:
    errors: list[str] = []
    core_count = scan(CORE, CORE_FORBIDDEN, errors)
    bot_count = scan(BOT, BOT_FORBIDDEN, errors)
    test_count = scan(TESTS, TEST_FORBIDDEN, errors)
    check_project_references(errors)
    check_bot_declaration(errors)

    print(f"scanned {core_count} Core, {bot_count} Bot and {test_count} test source files")
    if errors:
        print(f"\nARCHITECTURE CHECK FAILED ({len(errors)} problem(s)):")
        for err in errors:
            print(f"  - {err}")
        return 1
    print("architecture check passed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
