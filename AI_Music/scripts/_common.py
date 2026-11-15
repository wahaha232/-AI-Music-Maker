"""Shared helpers for the AI_Music maintenance scripts.

Kept dependency-free so every script can import it before torch/ACE-Step.
"""

from __future__ import annotations

import os
import sys
from pathlib import Path

BASE_DIR = Path(__file__).resolve().parents[1]
REPO_DIR = BASE_DIR / "ACE-Step-1.5"
MODELS_DIR = BASE_DIR / "models" / "Ace-Step1.5"


def safe_stdio() -> None:
    for stream in (sys.stdin, sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, ValueError):
            pass


def human(num: float) -> str:
    for unit in ("B", "KB", "MB", "GB", "TB"):
        if abs(num) < 1024.0:
            return f"{num:.2f} {unit}"
        num /= 1024.0
    return f"{num:.2f} PB"


def bootstrap(base_dir: Path | None = None) -> None:
    """Point ACE-Step at the local models and force offline execution."""
    base = Path(base_dir) if base_dir is not None else BASE_DIR
    repo = base / "ACE-Step-1.5"
    models = base / "models" / "Ace-Step1.5"
    os.environ["ACESTEP_CHECKPOINTS_DIR"] = str(models)
    os.environ["HF_HOME"] = str(base / ".hf-cache")
    os.environ["HF_HUB_OFFLINE"] = "1"
    os.environ["TRANSFORMERS_OFFLINE"] = "1"
    os.environ["TOKENIZERS_PARALLELISM"] = "false"
    os.environ["TORCHAUDIO_USE_BACKEND"] = "ffmpeg"
    for var in ("http_proxy", "https_proxy", "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY"):
        os.environ.pop(var, None)
    if str(repo) not in sys.path:
        sys.path.insert(0, str(repo))
