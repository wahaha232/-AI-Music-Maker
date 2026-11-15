"""Report the third-party dependency closure of the ACE-Step inference path.

Scans the official repo source (excluding UI / API server / training code) with
`ast`, then checks which of the imported modules are importable in the current
environment.  Run it with the project venv python:

    <workspace>\\AI_Music.venv\\Scripts\\python.exe <workspace>\\AI_Music\\scripts\\check_dependencies.py

<workspace> = G:\\AIMUSIC.  All paths are resolved from this
file's location, so the project folder can be moved without editing the script.
"""

from __future__ import annotations

import ast
import importlib.util
import sys
from pathlib import Path

# <workspace>\AI_Music\scripts\check_dependencies.py -> <workspace>\AI_Music
BASE_DIR = Path(__file__).resolve().parents[1]
REPO_DIR = BASE_DIR / "ACE-Step-1.5"
PACKAGE_DIR = REPO_DIR / "acestep"

# Directories that belong to the WebUI / REST server / training stacks, which this
# offline inference-only setup intentionally does not install.
SKIP_DIR_PARTS = {"ui", "api", "training", "training_v2", "third_parts", "mlx"}
SKIP_FILE_NAMES = {
    "acestep_v15_pipeline.py",  # Gradio web UI entry point (not used)
    "api_server.py",            # REST API server entry point (not used)
    "openrouter_models.py",
    "openrouter_adapter.py",
}
SKIP_NAME_PARTS = ("_test.py", "test_", "_support.py")


def _is_skipped(path: Path) -> bool:
    if any(part in SKIP_DIR_PARTS for part in path.relative_to(REPO_DIR).parts[:-1]):
        return True
    if path.name in SKIP_FILE_NAMES:
        return True
    return any(part in path.name for part in SKIP_NAME_PARTS)


def _local_modules() -> set[str]:
    modules = {"acestep"}
    for path in PACKAGE_DIR.rglob("*.py"):
        modules.add(path.stem)
    for path in PACKAGE_DIR.iterdir():
        if path.is_dir():
            modules.add(path.name)
    return modules


def _record(imports: dict, name: str, filename: str, guarded: bool) -> None:
    entry = imports.setdefault(name, {"files": set(), "guarded": guarded})
    entry["files"].add(filename)
    if not guarded:
        entry["guarded"] = False


def _scan_node(node: ast.AST, guarded: bool, imports: dict, filename: str) -> None:
    """Collect imports that run at module import time.

    Imports nested in functions/classes are lazy (vLLM backend, MLX, LoRA, ...)
    and are skipped.  Imports inside ``try:`` are marked "guarded" because the
    surrounding code handles their absence.
    """
    if isinstance(node, (ast.Import, ast.ImportFrom)):
        if isinstance(node, ast.Import):
            names = [alias.name.split(".")[0] for alias in node.names]
        elif node.module and not node.level:
            names = [node.module.split(".")[0]]
        else:
            names = []
        for name in names:
            _record(imports, name, filename, guarded)
    elif isinstance(node, ast.Try):
        for child in node.body:
            _scan_node(child, guarded, imports, filename)
        for handler in node.handlers:
            for child in handler.body:
                _scan_node(child, True, imports, filename)
        for child in list(node.orelse) + list(node.finalbody):
            _scan_node(child, guarded, imports, filename)
    elif isinstance(node, ast.If):
        test = node.test
        if (isinstance(test, ast.Name) and test.id == "TYPE_CHECKING") or (
            isinstance(test, ast.Attribute) and test.attr == "TYPE_CHECKING"
        ):
            return
        for child in list(node.body) + list(node.orelse):
            _scan_node(child, guarded, imports, filename)
    elif isinstance(node, (ast.With, ast.AsyncWith)):
        for child in node.body:
            _scan_node(child, guarded, imports, filename)


def collect_imports() -> dict:
    imports: dict = {}
    for path in sorted(PACKAGE_DIR.rglob("*.py")):
        if _is_skipped(path):
            continue
        try:
            tree = ast.parse(path.read_text(encoding="utf-8", errors="ignore"))
        except SyntaxError:
            continue
        for statement in tree.body:
            _scan_node(statement, False, imports, path.name)
    return imports


def main() -> int:
    if not PACKAGE_DIR.is_dir():
        print(f"[deps] repo source not found: {PACKAGE_DIR}")
        return 1

    stdlib = set(getattr(sys, "stdlib_module_names", set()))
    local = _local_modules()
    imports = collect_imports()

    third_party = {
        name: info
        for name, info in imports.items()
        if name not in stdlib and name not in local and not name.startswith("_")
    }

    def probe(name: str) -> bool:
        try:
            return importlib.util.find_spec(name) is not None
        except Exception:
            return False

    required_ok: list[str] = []
    required_missing: list[str] = []
    optional_ok: list[str] = []
    optional_missing: list[str] = []
    for name in sorted(third_party):
        if third_party[name]["guarded"]:
            bucket = (optional_ok, optional_missing)
        else:
            bucket = (required_ok, required_missing)
        bucket[0 if probe(name) else 1].append(name)

    total_required = len(required_ok) + len(required_missing)
    print(f"[deps] repo      : {REPO_DIR}")
    print(f"[deps] python    : {sys.executable}")
    print(f"[deps] required  : {len(required_ok)}/{total_required} importable")
    for name in required_ok:
        print(f"[deps]   OK      {name}")
    for name in required_missing:
        files = ", ".join(sorted(third_party[name]["files"]))[:80]
        print(f"[deps]   MISSING {name}  <- {files}")
    print(f"[deps] optional  : {len(optional_ok)} present, {len(optional_missing)} absent "
          f"(try/except or platform guarded)")
    for name in optional_ok:
        print(f"[deps]   present {name}")
    for name in optional_missing:
        print(f"[deps]   absent  {name}")
    if required_missing:
        print("[deps] result: INCOMPLETE - install the required modules listed above")
        return 2
    print("[deps] result: OK - inference dependency closure satisfied")
    return 0


if __name__ == "__main__":
    sys.exit(main())
