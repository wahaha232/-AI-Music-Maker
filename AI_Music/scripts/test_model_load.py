"""Load the official ACE-Step 1.5 model locally on the RTX 3060 and report.

Offline only: no WebUI, no API server, no desktop GUI.
Run from VS Code / PowerShell:
    <workspace>\\AI_Music.venv\\Scripts\\python.exe <workspace>\\AI_Music\\scripts\\test_model_load.py

<workspace> = G:\\AIMUSIC.  All paths are resolved from this
file's location, so the project folder can be moved without editing the script.
"""

from __future__ import annotations

import gc
import json
import os
import sys
import time
import traceback
from pathlib import Path

from _common import bootstrap, human, safe_stdio



# <workspace>\AI_Music\scripts\test_model_load.py -> <workspace>\AI_Music
BASE_DIR = Path(__file__).resolve().parents[1]
REPO_DIR = BASE_DIR / "ACE-Step-1.5"
MODELS_DIR = BASE_DIR / "models" / "Ace-Step1.5"
LOG_DIR = BASE_DIR / "logs"

DIT_MODEL = "acestep-v15-turbo"
LM_MODEL = "acestep-5Hz-lm-1.7B"

REQUIRED_FILES = [
    "config.json",
    "vae/config.json",
    "vae/diffusion_pytorch_model.safetensors",
    "Qwen3-Embedding-0.6B/config.json",
    "Qwen3-Embedding-0.6B/model.safetensors",
    "Qwen3-Embedding-0.6B/tokenizer.json",
    "acestep-v15-turbo/config.json",
    "acestep-v15-turbo/model.safetensors",
    "acestep-v15-turbo/silence_latent.pt",
    "acestep-5Hz-lm-1.7B/config.json",
    "acestep-5Hz-lm-1.7B/model.safetensors",
    "acestep-5Hz-lm-1.7B/tokenizer.json",
]




def check_files() -> tuple[bool, list[str]]:
    missing = [rel for rel in REQUIRED_FILES if not (MODELS_DIR / rel).is_file()]
    total = sum(
        p.stat().st_size
        for p in MODELS_DIR.rglob("*")
        if p.is_file() and p.parent.name != ".cache"
    )
    print(f"[files] model dir      : {MODELS_DIR}")
    print(f"[files] on-disk size   : {human(float(total))}")
    for rel in REQUIRED_FILES:
        path = MODELS_DIR / rel
        state = human(float(path.stat().st_size)) if path.is_file() else "MISSING"
        print(f"[files]   {state:>10}  {rel}")
    return not missing, missing


def report_runtime() -> dict:
    import torch

    info: dict = {
        "python": sys.version.split()[0],
        "torch": torch.__version__,
        "torch_cuda": torch.version.cuda,
        "cuda_available": torch.cuda.is_available(),
    }
    print(f"[env] python           : {info['python']}")
    print(f"[env] torch            : {info['torch']} (cuda {info['torch_cuda']})")
    print(f"[env] cuda available   : {info['cuda_available']}")
    if not info["cuda_available"]:
        return info

    index = torch.cuda.current_device()
    props = torch.cuda.get_device_properties(index)
    free_bytes, total_bytes = torch.cuda.mem_get_info(index)
    info.update(
        {
            "gpu_name": torch.cuda.get_device_name(index),
            "compute_capability": f"{props.major}.{props.minor}",
            "total_vram_gb": round(total_bytes / 1024**3, 2),
            "free_vram_gb": round(free_bytes / 1024**3, 2),
            "bf16_supported": torch.cuda.is_bf16_supported(),
        }
    )
    print(f"[gpu] device           : {index} - {info['gpu_name']}")
    print(f"[gpu] compute cap.     : {info['compute_capability']}")
    print(f"[gpu] VRAM total/free  : {info['total_vram_gb']} GB / {info['free_vram_gb']} GB")
    print(f"[gpu] bf16 supported   : {info['bf16_supported']}")
    return info


def report_plan() -> dict:
    import torch

    from acestep import gpu_config

    cfg = gpu_config.get_gpu_config()
    plan = {
        "tier": cfg.tier,
        "recommended_lm": cfg.recommended_lm_model,
        "offload_to_cpu": cfg.offload_to_cpu_default,
        "offload_dit_to_cpu": cfg.offload_dit_to_cpu_default,
        "max_duration_with_lm": cfg.max_duration_with_lm,
        "max_batch_size_with_lm": cfg.max_batch_size_with_lm,
        "detected_vram_gb": cfg.gpu_memory_gb,
    }
    print(f"[plan] gpu tier        : {plan['tier']} ({plan['detected_vram_gb']} GB detected)")
    print(f"[plan] max duration/LM : {plan['max_duration_with_lm']} s")
    print(f"[plan] max batch w/ LM : {plan['max_batch_size_with_lm']}")
    print(
        f"[plan] offload defaults: to_cpu={plan['offload_to_cpu']} "
        f"dit_to_cpu={plan['offload_dit_to_cpu']}"
    )
    if torch.cuda.is_available():
        _, total = torch.cuda.mem_get_info()
        plan["detected_vram_gb"] = round(total / 1024**3, 2)
    return plan


def main() -> int:
    safe_stdio()
    LOG_DIR.mkdir(parents=True, exist_ok=True)
    bootstrap()

    status: dict = {"ok": False, "stage": "start", "errors": []}
    try:
        print("=" * 72)
        print("ACE-Step 1.5 - local model load test (offline, no WebUI / no API)")
        print("=" * 72)

        files_ok, missing = check_files()
        status["missing_files"] = missing
        if not files_ok:
            status["stage"] = "file check"
            print(f"[files] FAILED - missing: {missing}")
            return 1

        status["stage"] = "runtime"
        runtime = report_runtime()
        status["runtime"] = runtime
        if not runtime["cuda_available"]:
            print("[gpu] FAILED - CUDA is not available, aborting (GPU-only setup).")
            return 1

        status["stage"] = "plan"
        status["plan"] = report_plan()

        import torch

        from acestep.handler import AceStepHandler
        from acestep.llm_inference import LLMHandler

        torch.cuda.reset_peak_memory_stats()

        status["stage"] = "dit"
        print("-" * 72)
        print(f"[dit] loading {DIT_MODEL} ...")
        t0 = time.time()
        dit_handler = AceStepHandler()
        message, success = dit_handler.initialize_service(
            project_root=str(REPO_DIR),
            config_path=DIT_MODEL,
            device="auto",
            use_flash_attention=False,
            compile_model=False,
            offload_to_cpu=True,
            offload_dit_to_cpu=True,
            quantization=None,
        )
        status["dit_seconds"] = round(time.time() - t0, 1)
        status["dit_message"] = message
        if not success:
            print(f"[dit] FAILED: {message}")
            return 1
        params = sum(p.numel() for p in dit_handler.model.parameters())
        status["dit_params_b"] = round(params / 1e9, 3)
        status["sample_rate"] = dit_handler.sample_rate
        print(f"[dit] loaded in {status['dit_seconds']}s - dtype={dit_handler.dtype}")
        print(f"[dit] params           : {status['dit_params_b']} B")
        print(f"[dit] sample rate      : {dit_handler.sample_rate} Hz")
        print(f"[dit] vae device       : {next(dit_handler.vae.parameters()).device}")

        status["stage"] = "lm"
        print("-" * 72)
        print(f"[lm] loading {LM_MODEL} (backend=pt) ...")
        t0 = time.time()
        llm_handler = LLMHandler()
        message, success = llm_handler.initialize(
            checkpoint_dir=str(MODELS_DIR),
            lm_model_path=LM_MODEL,
            backend="pt",
            device="auto",
            offload_to_cpu=True,
            dtype=None,
        )
        status["lm_seconds"] = round(time.time() - t0, 1)
        status["lm_message"] = message
        if not success:
            print(f"[lm] FAILED: {message}")
            return 1
        status["lm_dtype"] = str(llm_handler.dtype)
        status["lm_backend"] = str(llm_handler.llm_backend)
        print(f"[lm] loaded in {status['lm_seconds']}s - backend={status['lm_backend']}")
        print(f"[lm] dtype/device      : {status['lm_dtype']} / {llm_handler.device}")

        peak = torch.cuda.max_memory_allocated() / 1024**3
        reserved = torch.cuda.max_memory_reserved() / 1024**3
        status["peak_vram_gb"] = round(peak, 2)
        status["reserved_vram_gb"] = round(reserved, 2)
        print("-" * 72)
        print(f"[vram] peak allocated  : {peak:.2f} GB")
        print(f"[vram] peak reserved   : {reserved:.2f} GB")

        status["stage"] = "teardown"
        print("[teardown] releasing models and clearing the CUDA cache ...")
        del dit_handler, llm_handler
        gc.collect()
        if torch.cuda.is_available():
            torch.cuda.empty_cache()
            torch.cuda.synchronize()
            free_bytes, total_bytes = torch.cuda.mem_get_info()
            status["free_vram_after_gb"] = round(free_bytes / 1024**3, 2)
            total_gb = round(total_bytes / 1024**3, 2)
            print(f"[teardown] VRAM free   : {status['free_vram_after_gb']} / {total_gb} GB")

        status["ok"] = True
        status["stage"] = "done"
        print("=" * 72)
        print("RESULT: model load OK (this script generates no audio)")
        print("=" * 72)
        return 0
    except Exception as exc:  # noqa: BLE001 - report the exact blocking step
        status["errors"].append(f"{type(exc).__name__}: {exc}")
        print(f"[error] stage='{status['stage']}' {type(exc).__name__}: {exc}")
        traceback.print_exc()
        return 1
    finally:
        (LOG_DIR / "model_load_report.json").write_text(
            json.dumps(status, indent=2, default=str), encoding="utf-8"
        )


if __name__ == "__main__":
    sys.exit(main())
