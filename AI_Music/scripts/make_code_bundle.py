"""Build a single UTF-8 text bundle of the AI Music Maker source and docs."""

from __future__ import annotations

import argparse
from datetime import datetime
from pathlib import Path

BASE = Path(__file__).resolve().parents[1]
ROOT = BASE.parent
EXCLUDE_DIRS = {
    ".git", ".venv", "AI_Music.venv", "ACE-Step-1.5", "models", "wheels",
    "generated", "logs", "tools", "lib", "__pycache__", ".cache", ".hf-cache",
    ".uv-cache", ".xet-test", "node_modules",
}
INCLUDE_EXTENSIONS = {
    ".py", ".cs", ".ps1", ".bat", ".cmd", ".sh", ".json", ".md", ".txt",
    ".manifest", ".config", ".yaml", ".yml", ".toml",
}
ROOT_FILES = (
    ".gitignore",
    "migrate_venv_paths.ps1",
    "verify_migration.ps1",
    "retarget_workspace_paths.ps1",
)
SCAN_DIRS = (
    BASE / "app",
    BASE / "scripts",
    BASE / "tests",
)


def is_bundle(path: Path) -> bool:
    return path.name.startswith("AI_Music_code_bundle_")


def iter_candidates() -> list[Path]:
    files: list[Path] = []
    for name in ROOT_FILES:
        path = ROOT / name
        if path.is_file() and not is_bundle(path):
            files.append(path)
    for directory in SCAN_DIRS:
        if not directory.is_dir():
            continue
        for path in directory.rglob("*"):
            if not path.is_file() or is_bundle(path):
                continue
            if any(part in EXCLUDE_DIRS for part in path.relative_to(ROOT).parts):
                continue
            if path.suffix.lower() not in INCLUDE_EXTENSIONS:
                continue
            if path.stat().st_size > 5 * 1024 * 1024:
                continue
            files.append(path)
    for path in BASE.glob("*.md"):
        if path.is_file() and not is_bundle(path):
            files.append(path)
    for name in ("requirements_local.txt", "requirements.txt"):
        path = BASE / name
        if path.is_file():
            files.append(path)
    unique = {}
    for path in files:
        unique[path.resolve()] = None
    return sorted(unique, key=lambda item: str(item.relative_to(ROOT)).lower())


def build(output: Path) -> tuple[int, int]:
    files = iter_candidates()
    sections = []
    for index, path in enumerate(files, 1):
        try:
            text = path.read_text(encoding="utf-8-sig")
        except (UnicodeDecodeError, OSError):
            continue
        relative = path.relative_to(ROOT).as_posix()
        size = path.stat().st_size
        sections.append(
            f"===== [{index}/{len(files)}] {relative}  ({size} bytes) =====\n{text}\n")
    now = datetime.now().strftime("%Y-%m-%d %H:%M:%S")
    header = (
        "AI Music Maker / ACE-Step 1.5 - 全部程式碼與文件彙整\n"
        f"建立時間: {now}\n"
        f"專案根目錄: {ROOT}\n"
        "內容: 程式碼、設定、文件；排除 models/.venv/ACE-Step/generated/logs/tools/二進位/快取\n"
        f"檔案數: {len(sections)}\n"
        "================================================================================\n\n"
    )
    body = "\n".join(sections)
    output.write_text(header + body, encoding="utf-8")
    return len(sections), output.stat().st_size


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--out", type=Path)
    args = parser.parse_args()
    if args.out is None:
        stamp = datetime.now().strftime("%Y%m%d_%H%M%S")
        args.out = BASE / f"AI_Music_code_bundle_{stamp}.txt"
    count, size = build(args.out)
    print(f"[done] {args.out}")
    print(f"[files] {count}")
    print(f"[bytes] {size}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
