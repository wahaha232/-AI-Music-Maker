"""Telegram remote control for AI Music Maker.

Commands:
  /gen <prompt>  generate a 60-second MP3 and send it back
  /status        show whether a job is running
  /help          show commands

Config: %APPDATA%\\AIMusicGenerator\\telegram.json
  {"bot_token": "...", "chat_id": 123456789}
"""

from __future__ import annotations

import json
import os
import queue
import subprocess
import sys
import tempfile
import threading
import time
from datetime import datetime
from pathlib import Path

import requests

BASE = Path(__file__).resolve().parents[1]
BACKEND = BASE / "app" / "generate_backend.py"
CONFIG = Path(os.environ.get("APPDATA", str(Path.home()))) / "AIMusicGenerator" / "telegram.json"
OUT_DIR = BASE / "generated" / "telegram"
LOG = Path(os.environ.get("TEMP", tempfile.gettempdir())) / "AIMusicGenerator" / "telegram.log"

busy = threading.Lock()
current = {"running": False, "prompt": "", "started": 0.0}


def log(message: str) -> None:
    LOG.parent.mkdir(parents=True, exist_ok=True)
    line = f"{datetime.now():%Y-%m-%d %H:%M:%S} {message}"
    with open(LOG, "a", encoding="utf-8") as handle:
        handle.write(line + "\n")
    print(line, flush=True)


def load_config() -> dict:
    if not CONFIG.is_file():
        raise SystemExit(f"missing config: {CONFIG}")
    return json.loads(CONFIG.read_text(encoding="utf-8"))


def api(token: str, method: str, **fields):
    url = f"https://api.telegram.org/bot{token}/{method}"
    response = requests.post(url, data=fields, timeout=60)
    response.raise_for_status()
    return response.json()


def send(token: str, chat_id: int, text: str) -> None:
    try:
        api(token, "sendMessage", chat_id=chat_id, text=text[:4000])
    except Exception as exc:
        log(f"sendMessage failed: {exc}")


def run_job(token: str, chat_id: int, prompt: str) -> None:
    current.update(running=True, prompt=prompt, started=time.time())
    job_id = datetime.now().strftime("%Y%m%d_%H%M%S")
    out_dir = OUT_DIR / job_id
    out_dir.mkdir(parents=True, exist_ok=True)
    config = {
        "caption": prompt,
        "lyrics": "[Instrumental]",
        "instrumental": True,
        "thinking": True,
        "bpm": None,
        "keyscale": "",
        "timesignature": "",
        "vocal_language": "unknown",
        "duration": int(os.environ.get("AI_MUSIC_TG_DURATION", "60")),
        "inference_steps": 8,
        "guidance_scale": 1.0,
        "seed": -1,
        "output_dir": str(out_dir),
        "filename_prefix": "telegram",
        "audio_format": "mp3",
        "offload_mode": "auto",
    }
    config_path = out_dir / "config.json"
    config_path.write_text(json.dumps(config, ensure_ascii=False), encoding="utf-8")
    send(token, chat_id, f"開始生成（60 秒 MP3）…\n{prompt[:200]}")
    log_path = out_dir / "run.log"
    try:
        with open(log_path, "w", encoding="utf-8") as log_handle:
            process = subprocess.run(
                [sys.executable, "-u", str(BACKEND), "--config", str(config_path)],
                stdout=log_handle, stderr=subprocess.STDOUT, timeout=1800,
            )
        produced = None
        for line in reversed(log_path.read_text(encoding="utf-8", errors="replace").splitlines()):
            if line.startswith("[done]"):
                produced = line[6:].strip()
                break
        if process.returncode != 0 or not produced or not Path(produced).is_file():
            send(token, chat_id, f"生成失敗（exit {process.returncode}），請查看日誌。")
            return
        size = Path(produced).stat().st_size
        with open(produced, "rb") as handle:
            response = requests.post(
                f"https://api.telegram.org/bot{token}/sendDocument",
                data={"chat_id": chat_id, "caption": prompt[:200]},
                files={"document": (Path(produced).name, handle, "audio/mpeg")},
                timeout=300,
            )
        response.raise_for_status()
        send(token, chat_id, f"完成：{Path(produced).name}（{size/1024/1024:.1f} MB）")
    except Exception as exc:
        log(f"job failed: {exc}")
        send(token, chat_id, f"生成失敗：{exc}")
    finally:
        current.update(running=False, prompt="", started=0.0)


def run_album_job(token: str, chat_id: int, title: str, prompts: list[str]) -> None:
    current.update(running=True, prompt="album: " + title, started=time.time())
    job_id = datetime.now().strftime("%Y%m%d_%H%M%S")
    out_dir = BASE / "generated" / "telegram_album" / job_id
    out_dir.mkdir(parents=True, exist_ok=True)
    duration = int(os.environ.get("AI_MUSIC_TG_DURATION", "60"))
    tracks = []
    for index, prompt in enumerate(prompts, 1):
        tracks.append({"title": f"Track {index:02d}", "prompt": prompt, "duration": duration})
    shared = {
        "instrumental": True, "thinking": True, "bpm": None, "keyscale": "",
        "timesignature": "", "vocal_language": "unknown", "duration": duration,
        "inference_steps": 8, "guidance_scale": 1.0, "seed": -1,
        "output_dir": str(out_dir), "filename_prefix": "album",
        "audio_format": "mp3", "offload_mode": "auto",
    }
    album = {"album_title": title, "artist": "AI Music Maker",
             "output_dir": str(out_dir), "shared": shared, "tracks": tracks}
    config_path = out_dir / "album_input.json"
    config_path.write_text(json.dumps(album, ensure_ascii=False), encoding="utf-8")
    send(token, chat_id, f"開始製作專輯：{title}（{len(prompts)} 首，每首 {duration} 秒）")
    log_path = out_dir / "run.log"
    try:
        with open(log_path, "w", encoding="utf-8") as log_handle:
            process = subprocess.run(
                [sys.executable, "-u", str(BACKEND), "--album", str(config_path)],
                stdout=log_handle, stderr=subprocess.STDOUT, timeout=3600,
            )
        meta_path = out_dir / "album.json"
        if process.returncode != 0 or not meta_path.is_file():
            send(token, chat_id, f"專輯失敗（exit {process.returncode}），請查看日誌。")
            return
        meta = json.loads(meta_path.read_text(encoding="utf-8"))
        for track in meta.get("tracks", []):
            file_path = Path(track.get("file", ""))
            if not file_path.is_file():
                continue
            with open(file_path, "rb") as handle:
                requests.post(
                    f"https://api.telegram.org/bot{token}/sendDocument",
                    data={"chat_id": chat_id, "caption": track.get("title", file_path.name)},
                    files={"document": (file_path.name, handle, "audio/mpeg")},
                    timeout=300,
                ).raise_for_status()
        upload_meta = out_dir / "album.json"
        with open(upload_meta, "rb") as handle:
            requests.post(
                f"https://api.telegram.org/bot{token}/sendDocument",
                data={"chat_id": chat_id, "caption": "album.json"},
                files={"document": ("album.json", handle, "application/json")},
                timeout=120,
            )
        send(token, chat_id, f"專輯完成：{title}（{len(meta.get('tracks', []))} 首）")
    except Exception as exc:
        log(f"album job failed: {exc}")
        send(token, chat_id, f"專輯失敗：{exc}")
    finally:
        current.update(running=False, prompt="", started=0.0)


def main() -> int:
    config = load_config()
    token = str(config.get("bot_token", "")).strip()
    chat_id = int(config.get("chat_id", 0))
    if not token or not chat_id:
        raise SystemExit("telegram.json needs bot_token and chat_id")
    log("bot started")
    offset = 0
    while True:
        try:
            response = requests.get(
                f"https://api.telegram.org/bot{token}/getUpdates",
                params={"offset": offset, "timeout": 25},
                timeout=40,
            )
            response.raise_for_status()
            updates = response.json().get("result", [])
            for update in updates:
                offset = update["update_id"] + 1
                message = update.get("message") or {}
                text = (message.get("text") or "").strip()
                from_chat = (message.get("chat") or {}).get("id")
                if from_chat != chat_id or not text:
                    continue
                if text.startswith("/gen "):
                    prompt = text[5:].strip()
                    if not prompt:
                        send(token, chat_id, "用法：/gen <音樂描述>")
                        continue
                    if not busy.acquire(blocking=False):
                        send(token, chat_id, "目前已有工作進行中，請稍後。")
                        continue
                    def start_job():
                        try:
                            run_job(token, chat_id, prompt)
                        finally:
                            busy.release()
                    threading.Thread(target=start_job, daemon=True).start()
                elif text.startswith("/album "):
                    payload = text[7:].strip()
                    if "|" not in payload:
                        send(token, chat_id, "用法：/album 專輯名稱 | 提示詞1 ; 提示詞2")
                        continue
                    title, prompt_text = payload.split("|", 1)
                    prompts = [p.strip() for p in prompt_text.split(";") if p.strip()]
                    if not title.strip() or not prompts:
                        send(token, chat_id, "專輯名稱或提示詞不可為空。")
                        continue
                    if not busy.acquire(blocking=False):
                        send(token, chat_id, "目前已有工作進行中，請稍後。")
                        continue
                    def start_album_job():
                        try:
                            run_album_job(token, chat_id, title.strip(), prompts)
                        finally:
                            busy.release()
                    threading.Thread(target=start_album_job, daemon=True).start()
                elif text == "/status":
                    if current["running"]:
                        elapsed = int(time.time() - current["started"])
                        send(token, chat_id,
                             f"生成中：{current['prompt'][:80]}（已 {elapsed//60}:{elapsed%60:02d}）")
                    else:
                        send(token, chat_id, "閒置中，可用 /gen <prompt> 生成。")
                elif text in ("/start", "/help"):
                    send(token, chat_id,
                         "AI Music Maker\n/gen <prompt> 生成 60 秒 MP3\n/status 查詢狀態")
        except Exception as exc:
            log(f"poll failed: {exc}")
            time.sleep(5)
    return 0


if __name__ == "__main__":
    if len(sys.argv) >= 2 and sys.argv[1] == "--test-gen":
        cfg = load_config()
        prompt = sys.argv[2] if len(sys.argv) >= 3 else "Warm acoustic guitar instrumental, no vocals."
        run_job(cfg["bot_token"], int(cfg["chat_id"]), prompt)
        sys.exit(0)
    if len(sys.argv) >= 2 and sys.argv[1] == "--test-album":
        cfg = load_config()
        prompts = sys.argv[3:] if len(sys.argv) > 3 else [
            "Warm acoustic guitar instrumental, morning cafe.",
            "Mellow jazz piano instrumental, rainy window.",
        ]
        run_album_job(cfg["bot_token"], int(cfg["chat_id"]),
                      sys.argv[2] if len(sys.argv) >= 3 else "Telegram Album", prompts)
        sys.exit(0)
    sys.exit(main())
