"""Generate the fixed 3-minute lo-fi test track via generate_backend.py.

The generation logic lives in app/generate_backend.py; this wrapper only
supplies the fixed config and mirrors the result into logs/generate_report.json
so write_setup_report.py keeps working.
"""

from __future__ import annotations

import json
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

from _common import safe_stdio

BASE_DIR = Path(__file__).resolve().parents[1]
BACKEND = BASE_DIR / "app" / "generate_backend.py"
GENERATED_DIR = BASE_DIR / "generated"
LOG_DIR = BASE_DIR / "logs"
TARGET_WAV = GENERATED_DIR / "test_lofi_01.wav"

CAPTION = (
    "Warm lo-fi chill instrumental. Dusty vinyl crackle and soft tape hiss, mellow "
    "Rhodes electric piano chords, gentle jazzy guitar licks, laid-back dusty "
    "hip-hop drum groove with brushed snare and soft kick, warm rounded upright "
    "bass line, relaxed swing feel, nostalgic late-night study atmosphere, "
    "instrumental only, no vocals."
)

CONFIG = {
    "caption": CAPTION,
    "lyrics": "[Instrumental]",
    "instrumental": True,
    "thinking": True,
    "bpm": 78,
    "keyscale": "C Major",
    "timesignature": "4",
    "vocal_language": "unknown",
    "duration": 180,
    "inference_steps": 8,
    "guidance_scale": 1.0,
    "seed": 20260928,
    "output_dir": str(GENERATED_DIR),
    "filename_prefix": "test_lofi",
    "audio_format": "wav",
    "offload_mode": "auto",
}


def main() -> int:
    safe_stdio()
    LOG_DIR.mkdir(parents=True, exist_ok=True)
    GENERATED_DIR.mkdir(parents=True, exist_ok=True)

    config_path = Path(tempfile.gettempdir()) / "aimusic_generate_test.json"
    config_path.write_text(json.dumps(CONFIG, ensure_ascii=False, indent=2), encoding="utf-8")

    produced = None
    process = subprocess.Popen(
        [sys.executable, "-u", str(BACKEND), "--config", str(config_path)],
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        text=True,
        encoding="utf-8",
        errors="replace",
        bufsize=1,
    )
    assert process.stdout is not None
    for line in process.stdout:
        print(line, end="", flush=True)
        if line.startswith("[done]"):
            produced = Path(line[6:].strip())
    code = process.wait()

    audio = {}
    report = {"ok": code == 0, "stage": "done" if code == 0 else "generate", "errors": []}
    if produced is not None and produced.is_file():
        shutil.copy2(produced, TARGET_WAV)
        sidecar_path = produced.with_suffix(".json")
        if sidecar_path.is_file():
            sidecar = json.loads(sidecar_path.read_text(encoding="utf-8"))
            audio = sidecar.get("audio", {})
            audio["path"] = str(TARGET_WAV)
            report["generate_seconds"] = sidecar.get("generate_seconds")
            report["actual_seed"] = sidecar.get("actual_seed")
        report["audio"] = audio
        report["raw_output"] = str(produced)
    elif code != 0:
        report["errors"].append(f"generate_backend exit={code}")

    (LOG_DIR / "generate_report.json").write_text(
        json.dumps(report, indent=2, ensure_ascii=False, default=str), encoding="utf-8")
    return 0 if code == 0 and produced is not None else 1


if __name__ == "__main__":
    sys.exit(main())
