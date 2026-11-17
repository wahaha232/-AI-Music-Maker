"""Headless music-generation backend for AIMusicGenerator.exe.

Modes:
    --config <json>   generate one track and exit
    --daemon          load models once, then read one JSON job per stdin line
    --preflight       check repo/models/packages only

In daemon mode the process prints "[ready]" after the models are loaded, then
for every job prints progress lines followed by "[idle] code=<n>".
"""

from __future__ import annotations

import argparse
import gc
import json
import os
import shutil
import re
import sys
import tempfile
import time
import traceback
from datetime import datetime
from pathlib import Path

from gpu_lock import GpuBusy, acquire

APP_DIR = Path(__file__).resolve().parent
BASE_DIR = APP_DIR.parent                      # ...\AI_Music
WORKSPACE = BASE_DIR.parent                    # ...\AIMUSIC
REPO_DIR = BASE_DIR / "ACE-Step-1.5"
MODELS_DIR = BASE_DIR / "models" / "Ace-Step1.5"
DEFAULT_OUT = BASE_DIR / "generated"

DEFAULTS = {
    "caption": "Warm lo-fi chill instrumental with mellow Rhodes piano, soft hip-hop drums, "
               "warm bass and vinyl crackle. Instrumental only, no vocals.",
    "lyrics": "[Instrumental]",
    "instrumental": True,
    "thinking": True,
    "bpm": None,
    "keyscale": "",
    "timesignature": "",
    "vocal_language": "unknown",
    "duration": 60,
    "inference_steps": 8,
    "guidance_scale": 1.0,
    "seed": -1,
    "lm_negative_prompt": "",
    "fade_in_duration": 0.0,
    "fade_out_duration": 0.0,
    "mp3_bitrate": "128k",
    "mp3_sample_rate": 48000,
    "shift": 1.0,
    "output_dir": str(DEFAULT_OUT),
    "filename_prefix": "ace",
    "audio_format": "wav",
    "offload_mode": "auto",
}


def _stdio() -> None:
    for stream in (sys.stdin, sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except Exception:
            pass


def _emit(tag: str, message: str) -> None:
    print(f"[{tag}] {message}", flush=True)


def acquire_gpu(source: str, wait_seconds: float):
    try:
        return acquire(source, wait_seconds)
    except GpuBusy as exc:
        _emit("busy", str(exc))
        _emit("error", "GPU 使用中")
        return None


def bootstrap() -> None:
    os.environ["ACESTEP_CHECKPOINTS_DIR"] = str(MODELS_DIR)
    os.environ["HF_HOME"] = str(BASE_DIR / ".hf-cache")
    os.environ["HF_HUB_OFFLINE"] = "1"
    os.environ["TRANSFORMERS_OFFLINE"] = "1"
    os.environ["TOKENIZERS_PARALLELISM"] = "false"
    os.environ["TORCHAUDIO_USE_BACKEND"] = "ffmpeg"
    os.environ["PYTHONIOENCODING"] = "utf-8"
    for var in ("http_proxy", "https_proxy", "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY"):
        os.environ.pop(var, None)
    ffmpeg_dir = BASE_DIR / "tools" / "ffmpeg" / "bin"
    if ffmpeg_dir.is_dir():
        os.environ["PATH"] = str(ffmpeg_dir) + os.pathsep + os.environ.get("PATH", "")
    if str(REPO_DIR) not in sys.path:
        sys.path.insert(0, str(REPO_DIR))


def normalize_config(data: dict) -> dict:
    cfg = dict(DEFAULTS)
    cfg.update({k: v for k, v in data.items() if v is not None})

    caption = str(cfg["caption"]).strip()
    if not caption:
        raise ValueError("caption 不可為空")
    duration = float(cfg["duration"])
    if not 10.0 <= duration <= 7200.0:
        raise ValueError("duration 必須介於 10 到 7200 秒")
    raw_bpm = cfg.get("bpm")
    if raw_bpm in (None, "", "auto", "Auto"):
        bpm = None
    else:
        bpm = int(raw_bpm)
        if not 30 <= bpm <= 300:
            raise ValueError("bpm 必須介於 30 到 300，或留空自動")
    steps = int(cfg["inference_steps"])
    if not 1 <= steps <= 100:
        raise ValueError("inference_steps 必須介於 1 到 100")
    guidance = float(cfg["guidance_scale"])
    if not 0.1 <= guidance <= 20.0:
        raise ValueError("guidance_scale 必須介於 0.1 到 20")
    keyscale = str(cfg.get("keyscale") or "").strip()
    timesignature = str(cfg.get("timesignature") or "").strip()
    if timesignature and timesignature not in ("2", "3", "4", "6"):
        raise ValueError("timesignature 必須是 2、3、4、6，或留空自動")
    audio_format = str(cfg.get("audio_format") or "wav").lower()
    if audio_format not in ("wav", "flac", "mp3", "wav32", "opus", "aac"):
        raise ValueError("不支援的 audio_format")
    negative_prompt = str(cfg.get("lm_negative_prompt") or "").strip()
    fade_in = float(cfg.get("fade_in_duration") or 0.0)
    fade_out = float(cfg.get("fade_out_duration") or 0.0)
    if not 0.0 <= fade_in <= 30.0 or not 0.0 <= fade_out <= 30.0:
        raise ValueError("fade 秒數必須介於 0 到 30")
    mp3_bitrate = str(cfg.get("mp3_bitrate") or "128k").lower()
    if mp3_bitrate not in ("128k", "192k", "256k", "320k"):
        raise ValueError("不支援的 mp3_bitrate")
    mp3_sample_rate = int(cfg.get("mp3_sample_rate") or 48000)
    if mp3_sample_rate not in (44100, 48000):
        raise ValueError("mp3_sample_rate 必須是 44100 或 48000")
    shift = float(cfg.get("shift") or 1.0)
    if not 0.5 <= shift <= 10.0:
        raise ValueError("shift 必須介於 0.5 到 10")
    offload_mode = str(cfg.get("offload_mode") or "auto").lower()
    if offload_mode not in ("auto", "save_vram"):
        raise ValueError("offload_mode 必須是 auto 或 save_vram")

    cfg["caption"] = caption
    cfg["duration"] = duration
    cfg["bpm"] = bpm
    cfg["keyscale"] = keyscale
    cfg["timesignature"] = timesignature
    cfg["inference_steps"] = steps
    cfg["guidance_scale"] = guidance
    cfg["audio_format"] = audio_format
    cfg["offload_mode"] = offload_mode
    cfg["lm_negative_prompt"] = negative_prompt
    cfg["fade_in_duration"] = fade_in
    cfg["fade_out_duration"] = fade_out
    cfg["mp3_bitrate"] = mp3_bitrate
    cfg["mp3_sample_rate"] = mp3_sample_rate
    cfg["shift"] = shift
    cfg["seed"] = int(cfg.get("seed", -1))
    out = Path(str(cfg["output_dir"])).expanduser()
    if not out.is_absolute():
        out = (BASE_DIR / out).resolve()
    out.mkdir(parents=True, exist_ok=True)
    cfg["output_dir"] = str(out)
    return cfg


def load_config(path: Path) -> dict:
    return normalize_config(json.loads(path.read_text(encoding="utf-8-sig")))


def preflight() -> int:
    problems = []
    if not REPO_DIR.is_dir():
        problems.append(f"缺少 ACE-Step 原始碼: {REPO_DIR}")
    if not MODELS_DIR.is_dir():
        problems.append(f"缺少模型目錄: {MODELS_DIR}")
    for name, expected in (
        ("acestep-v15-turbo", "model.safetensors"),
        ("acestep-5Hz-lm-1.7B", "model.safetensors"),
        ("Qwen3-Embedding-0.6B", "model.safetensors"),
        ("vae", "diffusion_pytorch_model.safetensors"),
    ):
        target = MODELS_DIR / name / expected
        if not target.is_file():
            problems.append(f"缺少權重: {target}")
    try:
        import torch  # noqa: F401
        import soundfile  # noqa: F401
        import transformers  # noqa: F401
    except Exception as exc:  # noqa: BLE001
        problems.append(f"套件匯入失敗: {type(exc).__name__}: {exc}")
    if problems:
        for item in problems:
            _emit("error", item)
        _emit("preflight", "FAILED")
        return 1
    emit_limits()
    _emit("preflight", "OK - repo/models/packages present")
    return 0


def emit_limits() -> None:
    try:
        from acestep.gpu_config import get_gpu_config
        cfg = get_gpu_config()
        _emit("limits", f"tier={cfg.tier} max_duration_with_lm={cfg.max_duration_with_lm} "
                        f"offload_to_cpu_default={cfg.offload_to_cpu_default} "
                        f"offload_dit_to_cpu_default={cfg.offload_dit_to_cpu_default}")
    except Exception as exc:  # noqa: BLE001
        _emit("limits", f"unavailable: {type(exc).__name__}: {exc}")


def load_handlers(offload_mode: str = "auto"):
    import torch
    from acestep.handler import AceStepHandler
    from acestep.llm_inference import LLMHandler

    offload_cpu, offload_dit = True, True
    if offload_mode == "auto":
        try:
            from acestep.gpu_config import get_gpu_config
            gpu_cfg = get_gpu_config()
            offload_cpu = bool(gpu_cfg.offload_to_cpu_default)
            offload_dit = bool(gpu_cfg.offload_dit_to_cpu_default)
        except Exception as exc:  # noqa: BLE001
            _emit("info", f"gpu_config 讀取失敗，沿用 offload=True: {type(exc).__name__}: {exc}")
    _emit("info", f"offload_to_cpu={offload_cpu} offload_dit_to_cpu={offload_dit} "
                  f"(mode={offload_mode})")

    if torch.cuda.is_available():
        free_b, total_b = torch.cuda.mem_get_info()
        _emit("gpu", f"{torch.cuda.get_device_name(0)} - VRAM free "
                     f"{free_b / 1024**3:.2f}/{total_b / 1024**3:.2f} GB")
    else:
        _emit("gpu", "CUDA 不可用，將使用 CPU（速度極慢）")

    _emit("stage", "載入 DiT 主模型 (acestep-v15-turbo)...")
    t0 = time.time()
    dit = AceStepHandler()
    message, ok = dit.initialize_service(
        project_root=str(REPO_DIR), config_path="acestep-v15-turbo", device="auto",
        use_flash_attention=False, compile_model=False,
        offload_to_cpu=offload_cpu, offload_dit_to_cpu=offload_dit, quantization=None,
    )
    _emit("info", f"DiT 載入 {time.time() - t0:.1f}s ok={ok}: {str(message)[:180]}")
    if not ok:
        raise RuntimeError(f"DiT 初始化失敗: {message}")

    _emit("stage", "載入 5Hz LM 模型 (acestep-5Hz-lm-1.7B)...")
    t0 = time.time()
    llm = LLMHandler()
    message, ok = llm.initialize(
        checkpoint_dir=str(MODELS_DIR), lm_model_path="acestep-5Hz-lm-1.7B",
        backend="pt", device="auto", offload_to_cpu=True, dtype=None,
    )
    _emit("info", f"LM 載入 {time.time() - t0:.1f}s ok={ok}: {str(message)[:180]}")
    if not ok:
        raise RuntimeError(f"LM 初始化失敗: {message}")
    return dit, llm


def unique_target(out_dir: Path, prefix: str, seed: int, ext: str = "wav") -> Path:
    stamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    stem = f"{prefix}_{stamp}_seed{seed}"
    target = out_dir / f"{stem}.{ext}"
    index = 2
    while target.exists():
        target = out_dir / f"{stem}_{index}.{ext}"
        index += 1
    return target


def run_job(dit, llm, cfg: dict) -> tuple[int, Path | None]:
    import numpy as np
    import soundfile as sf
    from acestep.inference import GenerationConfig, GenerationParams, generate_music

    out_dir = Path(cfg["output_dir"])
    run_dir = Path(tempfile.mkdtemp(prefix=".tmp_run_", dir=str(out_dir)))
    try:
        seed = cfg["seed"] if cfg["seed"] >= 0 else -1
        params = GenerationParams(
            task_type="text2music",
            thinking=bool(cfg["thinking"]),
            caption=cfg["caption"],
            lyrics=str(cfg.get("lyrics") or "[Instrumental]"),
            instrumental=bool(cfg["instrumental"]),
            bpm=cfg["bpm"],
            keyscale=str(cfg["keyscale"]),
            timesignature=str(cfg["timesignature"]),
            vocal_language=str(cfg["vocal_language"]),
            duration=cfg["duration"],
            inference_steps=cfg["inference_steps"],
            guidance_scale=cfg["guidance_scale"],
            lm_negative_prompt=cfg["lm_negative_prompt"],
            fade_in_duration=cfg["fade_in_duration"],
            fade_out_duration=cfg["fade_out_duration"],
            shift=cfg["shift"],
            seed=seed,
        )
        config = GenerationConfig(
            batch_size=1,
            audio_format=cfg["audio_format"],
            mp3_bitrate=cfg["mp3_bitrate"],
            mp3_sample_rate=cfg["mp3_sample_rate"],
            use_random_seed=seed < 0,
            seeds=None if seed < 0 else [seed],
        )

        last_percent = {"value": -1}

        def on_progress(value, desc=None):
            try:
                percent = int(round(float(value) * 100))
            except (TypeError, ValueError):
                return
            percent = max(0, min(100, percent))
            if percent == last_percent["value"]:
                return
            last_percent["value"] = percent
            _emit("progress", f"{percent} {desc or ''}".rstrip())

        _emit("stage", f"生成音樂中（{cfg['duration']:.0f}s, {cfg['inference_steps']} steps）...")
        t0 = time.time()
        result = generate_music(dit, llm, params=params, config=config,
                                save_dir=str(run_dir), progress=on_progress)
        elapsed = time.time() - t0
        _emit("info", f"生成耗時 {elapsed:.1f}s ok={getattr(result, 'success', False)}")
        if not getattr(result, "success", False):
            _emit("error", f"生成失敗: {getattr(result, 'error', None) or getattr(result, 'status_message', '')}")
            return 1, None

        produced = None
        audios = getattr(result, "audios", []) or []
        for audio in audios:
            candidate = audio.get("path") if isinstance(audio, dict) else None
            if candidate and Path(candidate).is_file():
                produced = Path(candidate)
        if produced is None:
            wavs = sorted(run_dir.glob("*"), key=lambda p: p.stat().st_mtime, reverse=True)
            produced = next((p for p in wavs if p.is_file()), None)
        if produced is None:
            _emit("error", "模型回報成功，但找不到輸出音檔")
            return 1, None

        actual_seed = seed
        if audios and isinstance(audios[0], dict):
            audio_params = audios[0].get("params")
            if isinstance(audio_params, dict) and audio_params.get("seed") is not None:
                actual_seed = int(audio_params["seed"])

        ext = "wav" if cfg["audio_format"] == "wav32" else cfg["audio_format"]
        target = unique_target(out_dir, str(cfg["filename_prefix"]), actual_seed, ext)
        try:
            os.replace(str(produced), str(target))
        except OSError:
            shutil.copy2(produced, target)
            try:
                produced.unlink()
            except OSError:
                pass

        sample_rate = 0
        channels = 0
        frames = 0
        peak = 0.0
        rms = 0.0
        try:
            info_sf = sf.info(str(target))
            sample_rate = int(info_sf.samplerate)
            channels = int(info_sf.channels)
            frames = int(info_sf.frames)
            sq_sum = 0.0
            for block in sf.blocks(str(target), blocksize=max(1, sample_rate * 10),
                                   dtype="float32", always_2d=True):
                if block.size == 0:
                    continue
                peak = max(peak, float(np.max(np.abs(block))))
                sq_sum += float(np.sum(np.square(block, dtype=np.float64)))
            rms = (sq_sum / max(1, frames * channels)) ** 0.5
        except Exception as exc:
            _emit("info", f"音訊統計略過: {type(exc).__name__}: {exc}")
        info = {
            "path": str(target),
            "sample_rate": sample_rate,
            "channels": channels,
            "frames": frames,
            "duration_sec": round(frames / float(sample_rate), 2) if sample_rate else 0.0,
            "size_mb": round(target.stat().st_size / 1024**2, 2),
            "peak": round(peak, 4),
            "rms": round(rms, 5),
        }
        sidecar = target.with_suffix(".json")
        sidecar.write_text(json.dumps(
            {"created": datetime.now().isoformat(timespec="seconds"),
             "config": cfg, "actual_seed": actual_seed, "audio": info,
             "generate_seconds": round(elapsed, 1)},
            indent=2, ensure_ascii=False, default=str), encoding="utf-8")

        _emit("audio", f"{info['duration_sec']}s {info['sample_rate']}Hz "
                       f"{info['channels']}ch {info['size_mb']}MB peak={info['peak']}")
        _emit("done", str(target))
        return 0, target
    finally:
        shutil.rmtree(run_dir, ignore_errors=True)
        try:
            import torch
            gc.collect()
            if torch.cuda.is_available():
                torch.cuda.empty_cache()
        except Exception:
            pass


def release_models(dit, llm) -> None:
    dit = None
    llm = None
    try:
        import torch
        gc.collect()
        if torch.cuda.is_available():
            torch.cuda.empty_cache()
            free_b, total_b = torch.cuda.mem_get_info()
            _emit("info", f"VRAM 釋放後 free {free_b / 1024**3:.2f}/{total_b / 1024**3:.2f} GB")
    except Exception:
        pass


def run_oneshot(cfg: dict, gpu_wait: float = 0.0) -> int:
    lock = acquire_gpu("oneshot", gpu_wait)
    if lock is None:
        return 75
    dit = llm = None
    try:
        dit, llm = load_handlers(cfg["offload_mode"])
        code, _ = run_job(dit, llm, cfg)
        return code
    finally:
        release_models(dit, llm)
        lock.release()


def run_daemon(offload_mode: str = "auto", gpu_wait: float = 0.0) -> int:
    lock = acquire_gpu("daemon", gpu_wait)
    if lock is None:
        return 75
    dit = llm = None
    try:
        dit, llm = load_handlers(offload_mode)
        _emit("ready", "models loaded; send one JSON job per line")
        for raw in sys.stdin:
            line = raw.strip()
            if not line:
                continue
            if line in ("quit", "exit"):
                break
            try:
                data = json.loads(line)
                if isinstance(data, dict) and data.get("command") == "quit":
                    break
                cfg = normalize_config(data)
                _emit("job", f"start seed={cfg['seed']}")
                code, _ = run_job(dit, llm, cfg)
            except Exception as exc:  # noqa: BLE001 - keep the daemon alive
                _emit("error", f"{type(exc).__name__}: {exc}")
                traceback.print_exc()
                code = 1
            _emit("idle", f"code={code}")
        return 0
    except Exception as exc:  # noqa: BLE001
        _emit("error", f"{type(exc).__name__}: {exc}")
        traceback.print_exc()
        return 1
    finally:
        release_models(dit, llm)
        lock.release()


def run_album(album_path: Path, gpu_wait: float = 0.0) -> int:
    data = json.loads(album_path.read_text(encoding="utf-8-sig"))
    shared = data.get("shared") or {}
    tracks = data.get("tracks") or []
    if not tracks:
        _emit("error", "album 沒有 tracks")
        return 1
    out_dir = Path(data.get("output_dir") or DEFAULT_OUT)
    out_dir.mkdir(parents=True, exist_ok=True)
    album_json = out_dir / "album.json"
    files = []
    invalid = []
    valid = []

    for index, track in enumerate(tracks, 1):
        title = str(track.get("title") or ("Track " + str(index)))
        try:
            merged = dict(shared)
            merged.update({k: v for k, v in track.items()
                           if k not in ("title", "prompt")})
            if track.get("prompt"):
                merged["caption"] = str(track["prompt"])
            cfg = normalize_config(merged)
            cfg["output_dir"] = str(out_dir)
            safe = re.sub(r"[^\w\-\u4e00-\u9fff]+", "_", title).strip("_") or \
                ("track" + str(index))
            cfg["filename_prefix"] = f"{index:02d}_{safe}"
            valid.append((index, title, cfg))
        except Exception as exc:
            message = f"{type(exc).__name__}: {exc}"
            _emit("error", f"album track {index} invalid: {message}")
            invalid.append({"index": index, "title": title, "error": message})

    def write_meta() -> None:
        meta = {"album_title": data.get("album_title", ""),
                "artist": data.get("artist", ""),
                "created": datetime.now().isoformat(timespec="seconds"),
                "tracks": files,
                "invalid": invalid}
        album_json.write_text(json.dumps(meta, ensure_ascii=False, indent=2),
                              encoding="utf-8")

    if not valid:
        write_meta()
        _emit("albumdone", str(album_json))
        return 1

    lock = acquire_gpu("album", gpu_wait)
    if lock is None:
        return 75
    dit = llm = None
    try:
        dit, llm = load_handlers(str(shared.get("offload_mode") or "auto"))
        for index, title, cfg in valid:
            _emit("album", f"{index}/{len(tracks)} {title}")
            try:
                code, produced = run_job(dit, llm, cfg)
                if code != 0 or produced is None:
                    raise RuntimeError(f"generate failed with code {code}")
                files.append({"index": index, "title": title,
                              "file": str(produced), "prompt": cfg["caption"]})
            except Exception as exc:
                message = f"{type(exc).__name__}: {exc}"
                _emit("error", f"album track {index} failed: {message}")
                invalid.append({"index": index, "title": title, "error": message})
    finally:
        write_meta()
        _emit("albumdone", str(album_json))
        if dit is not None or llm is not None:
            release_models(dit, llm)
        lock.release()
    return 0 if len(files) == len(tracks) else 1


def main() -> int:
    _stdio()
    parser = argparse.ArgumentParser(description="ACE-Step 1.5 headless generator")
    parser.add_argument("--config", type=Path, help="JSON config path (one-shot mode)")
    parser.add_argument("--daemon", action="store_true", help="keep models loaded and read JSON jobs from stdin")
    parser.add_argument("--preflight", action="store_true", help="check files/packages only")
    parser.add_argument("--limits", action="store_true", help="print gpu_config limits and exit")
    parser.add_argument("--album", type=Path, help="generate a multi-track album from JSON")
    parser.add_argument("--offload-mode", choices=("auto", "save_vram"), default="auto", help="daemon model offload policy")
    parser.add_argument("--gpu-wait", type=float, default=0.0,
                        help="seconds to wait for the GPU lock")
    args = parser.parse_args()

    if args.preflight:
        bootstrap()
        return preflight()
    if args.limits:
        bootstrap()
        emit_limits()
        return 0
    if args.album:
        bootstrap()
        try:
            return run_album(args.album, args.gpu_wait)
        except Exception as exc:  # noqa: BLE001
            _emit("error", f"{type(exc).__name__}: {exc}")
            traceback.print_exc()
            return 1
    if not args.daemon and not args.config:
        parser.print_help()
        return 2

    _emit("info", f"workspace: {WORKSPACE}")
    bootstrap()
    if args.daemon:
        return run_daemon(args.offload_mode, args.gpu_wait)

    try:
        cfg = load_config(args.config)
        _emit("config", json.dumps({k: cfg[k] for k in
                                    ("caption", "duration", "bpm", "inference_steps",
                                     "seed", "thinking", "output_dir", "audio_format",
                                     "lm_negative_prompt", "fade_in_duration", "fade_out_duration",
                                     "shift", "mp3_bitrate", "mp3_sample_rate")}, ensure_ascii=False))
        return run_oneshot(cfg, args.gpu_wait)
    except Exception as exc:  # noqa: BLE001
        _emit("error", f"{type(exc).__name__}: {exc}")
        traceback.print_exc()
        return 1


if __name__ == "__main__":
    sys.exit(main())
