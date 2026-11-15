"""Download the official ACE-Step 1.5 model files from Hugging Face.

Offline / local setup helper (no WebUI, no API server).

* Repo  : ACE-Step/Ace-Step1.5  (official)
* Target: <workspace>\\AI_Music\\models\\Ace-Step1.5  (acts as ACESTEP_CHECKPOINTS_DIR)
* Engine: huggingface_hub + hf_xet (Xet backend, resumable)
* Safety: retries on network errors, skips files that are already complete.

Usage:
    <workspace>\\AI_Music\\tools\\hfvenv\\Scripts\\python.exe <workspace>\\AI_Music\\scripts\\download_ace_models.py

<workspace> = G:\\AIMUSIC.  All paths are resolved from this
file's location, so the project folder can be moved without editing the script.
"""

from __future__ import annotations

import os
import sys
import time
from pathlib import Path

REPO_ID = "ACE-Step/Ace-Step1.5"
# <workspace>\AI_Music\scripts\download_ace_models.py -> <workspace>\AI_Music
BASE_DIR = Path(__file__).resolve().parents[1]
TARGET_DIR = BASE_DIR / "models" / "Ace-Step1.5"
HF_HOME = BASE_DIR / ".hf-cache"

# Only the files that the local inference pipeline needs (turbo DiT + 1.7B LM +
# text encoder + VAE + configs/tokenizers). No XL / no 4B / no alternatives.
ALLOW_PATTERNS = [
    "*.json",
    "*.py",
    "*.jinja",
    "*.txt",
    ".gitattributes",
    "README.md",
    "vae/*.json",
    "vae/*.safetensors",
    "Qwen3-Embedding-0.6B/*",
    "acestep-v15-turbo/*",
    "acestep-5Hz-lm-1.7B/*",
]

EXPECTED_FILES = [
    "config.json",
    "vae/config.json",
    "vae/diffusion_pytorch_model.safetensors",
    "Qwen3-Embedding-0.6B/config.json",
    "Qwen3-Embedding-0.6B/model.safetensors",
    "acestep-v15-turbo/config.json",
    "acestep-v15-turbo/model.safetensors",
    "acestep-v15-turbo/silence_latent.pt",
    "acestep-5Hz-lm-1.7B/config.json",
    "acestep-5Hz-lm-1.7B/model.safetensors",
]

MAX_RETRIES = 40


def _human(num_bytes: float) -> str:
    for unit in ("B", "KB", "MB", "GB", "TB"):
        if abs(num_bytes) < 1024.0:
            return f"{num_bytes:3.1f}{unit}"
        num_bytes /= 1024.0
    return f"{num_bytes:.1f}PB"


def _current_size() -> float:
    total = 0
    for path in TARGET_DIR.rglob("*"):
        try:
            if path.is_file():
                total += path.stat().st_size
        except OSError:
            pass
    return float(total)


def _configure_env() -> None:
    # This repository is stored on the HF "Xet" backend.  The classic HTTP
    # downloader is capped at ~300 KB/s for anonymous clients and hf_transfer
    # cannot handle Xet at all, so the Xet engine (package: hf_xet) is used with
    # high-performance multi-connection chunking.
    os.environ.pop("HF_HUB_ENABLE_HF_TRANSFER", None)
    os.environ.pop("HF_HUB_DISABLE_XET", None)
    os.environ["HF_XET_HIGH_PERFORMANCE"] = "1"
    os.environ["HF_HOME"] = str(HF_HOME)
    os.environ["HF_HUB_DISABLE_TELEMETRY"] = "1"
    os.environ.pop("HF_HUB_DOWNLOAD_TIMEOUT", None)


def main() -> int:
    _configure_env()
    TARGET_DIR.mkdir(parents=True, exist_ok=True)

    from huggingface_hub import snapshot_download

    print(f"[download] repo        : {REPO_ID}", flush=True)
    print(f"[download] target dir  : {TARGET_DIR}", flush=True)
    print(f"[download] hf home     : {HF_HOME}", flush=True)
    try:
        import hf_xet  # noqa: F401
        engine = "hf_xet (Xet backend)"
    except ImportError:
        engine = "plain HTTP (hf_xet not installed)"
    print(f"[download] engine      : {engine}", flush=True)
    print(f"[download] already on disk: {_human(_current_size())}", flush=True)

    last_error: Exception | None = None
    for attempt in range(1, MAX_RETRIES + 1):
        started = time.time()
        before = _current_size()
        try:
            snapshot_download(
                repo_id=REPO_ID,
                local_dir=str(TARGET_DIR),
                allow_patterns=ALLOW_PATTERNS,
                max_workers=8,
                resume_download=True,
            )
            break
        except Exception as exc:  # noqa: BLE001 - network errors are expected here
            last_error = exc
            after = _current_size()
            elapsed = max(time.time() - started, 0.001)
            print(
                f"[download] attempt {attempt}/{MAX_RETRIES} failed after {elapsed:.0f}s "
                f"(+{_human(after - before)}, rate {(after - before) / elapsed / 1024:.0f} KB/s): "
                f"{type(exc).__name__}: {exc}",
                flush=True,
            )
            time.sleep(min(5 * attempt, 30))
    else:
        print(f"[download] FAILED after {MAX_RETRIES} attempts: {last_error}", flush=True)
        return 1

    print(f"[download] snapshot complete: {_human(_current_size())}", flush=True)
    missing = [rel for rel in EXPECTED_FILES if not (TARGET_DIR / rel).exists()]
    if missing:
        print("[download] MISSING FILES:", flush=True)
        for rel in missing:
            print(f"  - {rel}", flush=True)
        return 2

    print("[download] all required files present:", flush=True)
    for rel in EXPECTED_FILES:
        size = (TARGET_DIR / rel).stat().st_size
        print(f"  - {_human(size):>10}  {rel}", flush=True)
    print(f"[download] TOTAL: {_human(_current_size())}", flush=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
