#!/usr/bin/env python3
"""Small secret scan for the kinds of secret this project handles.

Not a replacement for a full scanner; it catches the obvious mistakes:
Telegram bot tokens, cTrader password files, private keys, API keys, and
parameter files with real values. Exit 1 on any finding.
"""
from __future__ import annotations

import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent

PATTERNS = [
    (re.compile(r"\b\d{8,10}:[A-Za-z0-9_-]{35}\b"), "Telegram bot token"),
    (re.compile(r"api\.telegram\.org/bot\d"), "Telegram URL containing a token"),
    (re.compile(r"-----BEGIN (?:RSA |EC |OPENSSH |DSA )?PRIVATE KEY-----"), "private key"),
    (re.compile(r"\bsk-ant-[A-Za-z0-9_-]{20,}"), "Anthropic API key"),
    (re.compile(r"\bgh[pousr]_[A-Za-z0-9]{30,}"), "GitHub token"),
    (re.compile(r"\bAKIA[0-9A-Z]{16}\b"), "AWS access key"),
    (re.compile(r"(?i)\b(password|passwd|pwd)\s*[:=]\s*[\"']?(?!\$|<|\{|your|example|changeme|dummy|\*)[^\s\"'<>{}$]{6,}"),
     "hard-coded password"),
]

FORBIDDEN_FILES = [
    (re.compile(r"\.pwd$"), "cTrader password file"),
    (re.compile(r"(^|/)\.env$"), "environment file with real values"),
    (re.compile(r"\.cbotset$"), "parameter file (commit only *.cbotset.example)"),
    (re.compile(r"\.(pem|pfx|p12|key)$"), "key material"),
]

SKIP_SUFFIXES = {".png", ".jpg", ".jpeg", ".gif", ".ico", ".algo", ".dll", ".exe", ".zip", ".nupkg", ".pdf"}
SELF = Path(__file__).resolve()


def tracked_files() -> list[Path]:
    try:
        out = subprocess.run(
            ["git", "ls-files", "--cached", "--others", "--exclude-standard"],
            cwd=ROOT, capture_output=True, text=True, check=True,
        ).stdout
        files = [ROOT / line for line in out.splitlines() if line]
        if files:
            return files
    except (subprocess.CalledProcessError, FileNotFoundError):
        pass
    return [p for p in ROOT.rglob("*") if p.is_file() and ".git" not in p.parts
            and "bin" not in p.parts and "obj" not in p.parts]


def main() -> int:
    findings: list[str] = []
    files = tracked_files()
    for path in files:
        if not path.is_file():
            continue
        rel = path.relative_to(ROOT).as_posix()
        for pattern, what in FORBIDDEN_FILES:
            if pattern.search(rel):
                findings.append(f"{rel}: {what} must not be committed")
        if path.suffix.lower() in SKIP_SUFFIXES or path.resolve() == SELF:
            continue
        try:
            text = path.read_text(encoding="utf-8")
        except (UnicodeDecodeError, OSError):
            continue
        for number, line in enumerate(text.splitlines(), start=1):
            for pattern, what in PATTERNS:
                if pattern.search(line):
                    findings.append(f"{rel}:{number}: possible {what}")

    print(f"scanned {len(files)} files")
    if findings:
        print(f"\nSECRET CHECK FAILED ({len(findings)} finding(s)):")
        for finding in findings:
            print(f"  - {finding}")
        print("\nIf a finding is a false positive, change the text so that it no longer "
              "looks like a secret. Do not edit this script.")
        return 1
    print("secret check passed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
