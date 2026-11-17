"""Compare installed package versions against requirements_lock.txt."""

from __future__ import annotations

import importlib.metadata
import re
import sys
from pathlib import Path

BASE = Path(__file__).resolve().parents[1]
LOCK = BASE / "requirements_lock.txt"


def main() -> int:
    if not LOCK.is_file():
        print(f"[error] missing {LOCK}")
        return 1
    mismatches = []
    checked = 0
    for line in LOCK.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if not line or line.startswith("#") or line.startswith("-"):
            continue
        match = re.fullmatch(r"([A-Za-z0-9_.-]+)==([^\s]+)", line)
        if not match:
            continue
        name, expected = match.groups()
        checked += 1
        try:
            actual = importlib.metadata.version(name)
        except importlib.metadata.PackageNotFoundError:
            mismatches.append(f"{name}: missing (expected {expected})")
            continue
        if actual != expected:
            mismatches.append(f"{name}: {actual} != {expected}")
    if mismatches:
        print("[error] requirements lock mismatch")
        for item in mismatches:
            print("  " + item)
        return 1
    print(f"[ok] requirements lock matched ({checked} packages)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
