"""Telegram remote control for AI Music Maker.

Commands:
  /gen <prompt>          generate audio and send it back
  /album <title> | ...   generate an album (prompts separated by ;)
  /status                show current job and settings
  /cancel                cancel the running job
  /duration <time>       set length: seconds, mm:ss, or hh:mm:ss
  /video on|off          convert generated audio to MP4 before sending
  /fade <in> <out>       set fade in/out seconds for future jobs
  /fade off              disable fade
  /merge [n]             merge the last n generated tracks (default 2)
  /outputs               list recent generated files
  /help                  show commands

Config: %APPDATA%\\AIMusicGenerator\\telegram.json
  {"bot_token": "...", "chat_id": 123456789}
"""

from __future__ import annotations

import json
import os
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
VIDEO_TOOL = BASE / "scripts" / "music_to_video.py"
AUDIO_TOOL = BASE / "scripts" / "audio_edit.py"
CONFIG = Path(os.environ.get("APPDATA", str(Path.home()))) / "AIMusicGenerator" / "telegram.json"
STATE_FILE = CONFIG.parent / "telegram_settings.json"
OUT_DIR = BASE / "generated" / "telegram"
LOG = Path(os.environ.get("TEMP", tempfile.gettempdir())) / "AIMusicGenerator" / "telegram.log"

TOKEN = ""
busy = threading.Lock()
current = {"running": False, "prompt": "", "started": 0.0, "process": None, "cancel": False}
recent: list[str] = []
recent_lock = threading.Lock()
settings = {
    "duration": int(os.environ.get("AI_MUSIC_TG_DURATION", "60")),
    "video": False,
    "fade_in": 0.0,
    "fade_out": 0.0,
}


def redact(text: str) -> str:
    return text.replace(TOKEN, "<TOKEN>") if TOKEN else text


def log(message: str) -> None:
    LOG.parent.mkdir(parents=True, exist_ok=True)
    message = redact(str(message))
    line = f"{datetime.now():%Y-%m-%d %H:%M:%S} {message}"
    with open(LOG, "a", encoding="utf-8") as handle:
        handle.write(line + "\n")
    print(line, flush=True)


def load_config() -> dict:
    if not CONFIG.is_file():
        raise SystemExit(f"missing config: {CONFIG}")
    return json.loads(CONFIG.read_text(encoding="utf-8"))


def load_settings() -> None:
    try:
        if STATE_FILE.is_file():
            data = json.loads(STATE_FILE.read_text(encoding="utf-8"))
            if 10 <= int(data.get("duration", settings["duration"])) <= 7200:
                settings["duration"] = int(data.get("duration"))
            settings["video"] = bool(data.get("video", settings["video"]))
            settings["fade_in"] = max(0.0, min(30.0, float(data.get("fade_in", 0.0))))
            settings["fade_out"] = max(0.0, min(30.0, float(data.get("fade_out", 0.0))))
    except Exception as exc:
        log(f"settings load failed: {exc}")


def save_settings() -> None:
    try:
        STATE_FILE.parent.mkdir(parents=True, exist_ok=True)
        STATE_FILE.write_text(json.dumps(settings, ensure_ascii=False, indent=2), encoding="utf-8")
    except Exception as exc:
        log(f"settings save failed: {exc}")


def api(token: str, method: str, **fields):
    url = f"https://api.telegram.org/bot{token}/{method}"
    response = requests.post(url, data=fields, timeout=60)
    response.raise_for_status()
    return response.json()


def send(token: str, chat_id: int, text: str) -> None:
    try:
        api(token, "sendMessage", chat_id=chat_id, text=redact(text)[:4000])
    except Exception as exc:
        log(f"sendMessage failed: {exc}")


def send_document(token: str, chat_id: int, path: Path, caption: str, mime: str) -> bool:
    try:
        if not path.is_file() or path.stat().st_size > 49 * 1024 * 1024:
            send(token, chat_id, f"檔案過大或不存在，未傳送：{path.name}")
            return False
        with open(path, "rb") as handle:
            response = requests.post(
                f"https://api.telegram.org/bot{token}/sendDocument",
                data={"chat_id": chat_id, "caption": caption[:200]},
                files={"document": (path.name, handle, mime)},
                timeout=600,
            )
        response.raise_for_status()
        return True
    except Exception as exc:
        log(f"sendDocument failed: {exc}")
        send(token, chat_id, f"傳送檔案失敗：{exc}")
        return False


def send_video(token: str, chat_id: int, path: Path, caption: str) -> bool:
    try:
        if not path.is_file() or path.stat().st_size > 49 * 1024 * 1024:
            return False
        with open(path, "rb") as handle:
            response = requests.post(
                f"https://api.telegram.org/bot{token}/sendVideo",
                data={"chat_id": chat_id, "caption": caption[:200],
                      "supports_streaming": "true"},
                files={"video": (path.name, handle, "video/mp4")},
                timeout=900,
            )
        response.raise_for_status()
        return True
    except Exception as exc:
        log(f"sendVideo failed: {exc}")
        return False


def parse_duration(text: str) -> int | None:
    value = text.strip()
    if not value:
        return None
    try:
        if ":" in value:
            parts = [int(part) for part in value.split(":")]
            if len(parts) == 2:
                total = parts[0] * 60 + parts[1]
            elif len(parts) == 3:
                total = parts[0] * 3600 + parts[1] * 60 + parts[2]
            else:
                return None
        else:
            total = int(value)
    except ValueError:
        return None
    return total if 10 <= total <= 7200 else None


def format_duration(seconds: int) -> str:
    if seconds >= 3600:
        return f"{seconds // 3600}:{seconds % 3600 // 60:02d}:{seconds % 60:02d}"
    return f"{seconds // 60}:{seconds % 60:02d}"


def busy_owner(log_path: Path) -> str:
    try:
        for line in log_path.read_text(encoding="utf-8", errors="replace").splitlines():
            if line.startswith("[busy]"):
                return line[6:].strip() or "unknown"
    except Exception:
        pass
    return "unknown"


def add_recent(path: Path) -> None:
    with recent_lock:
        recent.append(str(path))
        if len(recent) > 20:
            del recent[: len(recent) - 20]


def kill_process(process) -> None:
    if process is None:
        return
    try:
        if os.name == "nt":
            subprocess.run(["taskkill", "/PID", str(process.pid), "/T", "/F"],
                           capture_output=True, timeout=30)
        else:
            process.terminate()
    except Exception as exc:
        log(f"kill failed: {exc}")


def apply_fade(path: Path, out_dir: Path, job_settings: dict) -> Path:
    fade_in = job_settings["fade_in"]
    fade_out = job_settings["fade_out"]
    if fade_in <= 0 and fade_out <= 0:
        return path
    output = out_dir / f"{path.stem}_fade{path.suffix}"
    command = [
        sys.executable, "-u", str(AUDIO_TOOL), "fade", str(path),
        "--fade-in", f"{fade_in:.1f}", "--fade-out", f"{fade_out:.1f}",
        "--out", str(output), "--overwrite",
    ]
    try:
        result = subprocess.run(command, capture_output=True, text=True,
                                encoding="utf-8", errors="replace", timeout=1800)
        log(f"fade exit={result.returncode}")
        if result.returncode == 0 and output.is_file():
            return output
    except Exception as exc:
        log(f"fade failed: {exc}")
    return path


def make_video(path: Path, duration: int) -> Path | None:
    command = [sys.executable, "-u", str(VIDEO_TOOL), str(path)]
    try:
        result = subprocess.run(command, capture_output=True, text=True,
                                encoding="utf-8", errors="replace",
                                timeout=max(1800, duration * 4))
        log(f"video exit={result.returncode}")
        output = path.with_suffix(".mp4")
        if result.returncode == 0 and output.is_file():
            return output
    except Exception as exc:
        log(f"video failed: {exc}")
    return None


def run_job(token: str, chat_id: int, prompt: str) -> None:
    job_settings = dict(settings)
    duration = job_settings["duration"]
    current.update(running=True, prompt=prompt, started=time.time(),
                   process=None, cancel=False)
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
        "duration": duration,
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
    extras = []
    if job_settings["video"]:
        extras.append("自動轉影片")
    if job_settings["fade_in"] > 0 or job_settings["fade_out"] > 0:
        extras.append(f"淡入 {job_settings['fade_in']:.1f}s / 淡出 {job_settings['fade_out']:.1f}s")
    suffix = "（" + "、".join(extras) + "）" if extras else ""
    send(token, chat_id, f"開始生成 {format_duration(duration)} MP3{suffix}…\n{prompt[:200]}")
    log_path = out_dir / "run.log"
    try:
        with open(log_path, "w", encoding="utf-8") as log_handle:
            process = subprocess.Popen(
                [sys.executable, "-u", str(BACKEND), "--config", str(config_path),
                 "--gpu-wait", "20"],
                stdout=log_handle, stderr=subprocess.STDOUT,
            )
            current["process"] = process
            try:
                code = process.wait(timeout=21600)
            except subprocess.TimeoutExpired:
                kill_process(process)
                code = -1
        current["process"] = None
        if current["cancel"]:
            send(token, chat_id, "已取消生成。")
            return
        if code == 75:
            owner = busy_owner(log_path)
            send(token, chat_id,
                 f"GPU 正被使用中（{owner}）。若是電腦上的 AI Music Maker 常駐了模型，"
                 "請關閉 App 釋放模型後再試。")
            return
        produced = None
        for line in reversed(log_path.read_text(encoding="utf-8", errors="replace").splitlines()):
            if line.startswith("[done]"):
                produced = line[6:].strip()
                break
        if code != 0 or not produced or not Path(produced).is_file():
            send(token, chat_id, f"生成失敗（exit {code}），請查看日誌。")
            return
        audio = apply_fade(Path(produced), out_dir, job_settings)
        add_recent(audio)
        if job_settings["video"]:
            video = make_video(audio, duration)
            if video is not None:
                if send_video(token, chat_id, video,
                              f"影片 {video.name}（{video.stat().st_size / 1024 / 1024:.1f} MB）"):
                    send(token, chat_id, f"影片完成：{video.name}")
                else:
                    send(token, chat_id, "影片過大或傳送失敗，改傳音訊。")
        size = audio.stat().st_size
        if send_document(token, chat_id, audio, prompt[:200], "audio/mpeg"):
            send(token, chat_id, f"完成：{audio.name}（{size / 1024 / 1024:.1f} MB）")
    except Exception as exc:
        log(f"job failed: {exc}")
        if current["cancel"]:
            send(token, chat_id, "已取消生成。")
        else:
            send(token, chat_id, f"生成失敗：{exc}")
    finally:
        current.update(running=False, prompt="", started=0.0, process=None, cancel=False)


def run_album_job(token: str, chat_id: int, title: str, prompts: list[str]) -> None:
    duration = settings["duration"]
    current.update(running=True, prompt="album: " + title, started=time.time(),
                   process=None, cancel=False)
    job_id = datetime.now().strftime("%Y%m%d_%H%M%S")
    out_dir = BASE / "generated" / "telegram_album" / job_id
    out_dir.mkdir(parents=True, exist_ok=True)
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
    send(token, chat_id, f"開始製作專輯：{title}（{len(prompts)} 首，每首 {format_duration(duration)}）")
    log_path = out_dir / "run.log"
    try:
        with open(log_path, "w", encoding="utf-8") as log_handle:
            process = subprocess.Popen(
                [sys.executable, "-u", str(BACKEND), "--album", str(config_path),
                 "--gpu-wait", "20"],
                stdout=log_handle, stderr=subprocess.STDOUT,
            )
            current["process"] = process
            try:
                code = process.wait(timeout=21600)
            except subprocess.TimeoutExpired:
                kill_process(process)
                code = -1
        current["process"] = None
        if current["cancel"]:
            send(token, chat_id, "已取消專輯製作。")
            return
        if code == 75:
            owner = busy_owner(log_path)
            send(token, chat_id,
                 f"GPU 正被使用中（{owner}）。若是電腦上的 AI Music Maker 常駐了模型，"
                 "請關閉 App 釋放模型後再試。")
            return
        meta_path = out_dir / "album.json"
        if code != 0 or not meta_path.is_file():
            send(token, chat_id, f"專輯失敗（exit {code}），請查看日誌。")
            return
        meta = json.loads(meta_path.read_text(encoding="utf-8"))
        sent = 0
        for track in meta.get("tracks", []):
            file_path = Path(track.get("file", ""))
            if not file_path.is_file():
                continue
            if send_document(token, chat_id, file_path,
                             track.get("title", file_path.name), "audio/mpeg"):
                sent += 1
                add_recent(file_path)
        send(token, chat_id, f"專輯完成：{title}（已傳送 {sent} 首）")
    except Exception as exc:
        log(f"album job failed: {exc}")
        send(token, chat_id, f"專輯失敗：{exc}")
    finally:
        current.update(running=False, prompt="", started=0.0, process=None, cancel=False)


def merge_recent(token: str, chat_id: int, count: int) -> None:
    with recent_lock:
        files = [Path(path) for path in recent[-count:]] if count > 0 else []
    files = [path for path in files if path.is_file()]
    if len(files) < 2:
        send(token, chat_id, "可合併的近期檔案不足 2 首。")
        return
    output = OUT_DIR / f"merged_{datetime.now():%Y%m%d_%H%M%S}.mp3"
    command = [sys.executable, "-u", str(AUDIO_TOOL), "merge",
               *[str(path) for path in files], "--out", str(output), "--overwrite"]
    try:
        result = subprocess.run(command, capture_output=True, text=True,
                                encoding="utf-8", errors="replace", timeout=1800)
        if result.returncode != 0 or not output.is_file():
            send(token, chat_id, "合併失敗，請查看日誌。")
            return
        add_recent(output)
        if send_document(token, chat_id, output,
                         f"合併 {len(files)} 首（{output.stat().st_size / 1024 / 1024:.1f} MB）",
                         "audio/mpeg"):
            send(token, chat_id, f"合併完成：{output.name}")
    except Exception as exc:
        log(f"merge failed: {exc}")
        send(token, chat_id, f"合併失敗：{exc}")


def status_text() -> str:
    lines = []
    if current["running"]:
        elapsed = int(time.time() - current["started"])
        lines.append(f"執行中：{current['prompt'][:80]}（{format_duration(elapsed)}）")
    else:
        lines.append("閒置中")
    lines.append(f"長度：{format_duration(settings['duration'])}")
    lines.append(f"自動轉影片：{'開' if settings['video'] else '關'}")
    lines.append(f"淡入/淡出：{settings['fade_in']:.1f}s / {settings['fade_out']:.1f}s")
    with recent_lock:
        lines.append(f"近期檔案：{len(recent)} 個")
    return "\n".join(lines)


def main() -> int:
    load_settings()
    config = load_config()
    token = str(config.get("bot_token", "")).strip()
    chat_id = int(config.get("chat_id", 0))
    if not token or not chat_id:
        raise SystemExit("telegram.json needs bot_token and chat_id")
    global TOKEN
    TOKEN = token
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
                        send(token, chat_id, "已有工作執行中，請等待或使用 /cancel。")
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
                        send(token, chat_id, "專輯名稱或提示詞不可空白。")
                        continue
                    if not busy.acquire(blocking=False):
                        send(token, chat_id, "已有工作執行中，請等待或使用 /cancel。")
                        continue

                    def start_album_job():
                        try:
                            run_album_job(token, chat_id, title.strip(), prompts)
                        finally:
                            busy.release()
                    threading.Thread(target=start_album_job, daemon=True).start()
                elif text == "/status":
                    send(token, chat_id, status_text())
                elif text == "/cancel":
                    if not current["running"]:
                        send(token, chat_id, "目前沒有執行中的工作。")
                        continue
                    current["cancel"] = True
                    kill_process(current.get("process"))
                    send(token, chat_id, "已送出取消指令。")
                elif text.startswith("/duration"):
                    parts = text.split(maxsplit=1)
                    if len(parts) == 1:
                        send(token, chat_id, f"目前長度：{format_duration(settings['duration'])}")
                        continue
                    value = parse_duration(parts[1])
                    if value is None:
                        send(token, chat_id, "格式：/duration 95、1:35 或 1:02:03（10 秒–2 小時）")
                        continue
                    settings["duration"] = value
                    save_settings()
                    warning = "（注意：引擎單次生成建議不超過約 8 分鐘）" if value > 600 else ""
                    send(token, chat_id, f"長度已設為 {format_duration(value)}。{warning}")
                elif text.startswith("/video"):
                    parts = text.split(maxsplit=1)
                    if len(parts) == 2 and parts[1].lower() in ("on", "off"):
                        settings["video"] = parts[1].lower() == "on"
                        save_settings()
                    send(token, chat_id, f"自動轉影片：{'開' if settings['video'] else '關'}")
                elif text.startswith("/fade"):
                    parts = text.split()
                    if len(parts) == 2 and parts[1].lower() == "off":
                        settings["fade_in"] = 0.0
                        settings["fade_out"] = 0.0
                        save_settings()
                        send(token, chat_id, "已關閉淡入淡出。")
                        continue
                    if len(parts) == 3:
                        try:
                            fade_in = max(0.0, min(30.0, float(parts[1])))
                            fade_out = max(0.0, min(30.0, float(parts[2])))
                        except ValueError:
                            fade_in = fade_out = -1.0
                        if fade_in >= 0 and fade_out >= 0:
                            settings["fade_in"] = fade_in
                            settings["fade_out"] = fade_out
                            save_settings()
                            send(token, chat_id, f"淡入/淡出已設為 {fade_in:.1f}s / {fade_out:.1f}s。")
                            continue
                    send(token, chat_id, "格式：/fade 2 3 或 /fade off（0–30 秒）")
                elif text.startswith("/merge"):
                    parts = text.split(maxsplit=1)
                    count = 2
                    if len(parts) == 2:
                        try:
                            count = int(parts[1])
                        except ValueError:
                            count = 2
                    count = max(2, min(10, count))
                    if not busy.acquire(blocking=False):
                        send(token, chat_id, "已有工作執行中，請稍後再合併。")
                        continue

                    def start_merge():
                        try:
                            merge_recent(token, chat_id, count)
                        finally:
                            busy.release()
                    threading.Thread(target=start_merge, daemon=True).start()
                elif text == "/outputs":
                    with recent_lock:
                        items = list(recent[-5:])
                    if not items:
                        send(token, chat_id, "尚無近期檔案。")
                    else:
                        lines = ["近期檔案："]
                        for item in reversed(items):
                            path = Path(item)
                            if path.is_file():
                                lines.append(f"{path.name}（{path.stat().st_size / 1024 / 1024:.1f} MB）")
                        send(token, chat_id, "\n".join(lines))
                elif text in ("/start", "/help"):
                    send(token, chat_id,
                         "AI Music Maker 遠端控制\n"
                         "/gen <提示詞> 生成並回傳音訊\n"
                         "/album 名稱 | 提示詞1 ; 提示詞2\n"
                         "/status 狀態與設定\n"
                         "/cancel 取消目前工作\n"
                         "/duration 95｜1:35｜1:02:03 設定長度\n"
                         "/video on|off 自動轉 MP4\n"
                         "/fade 2 3｜/fade off 設定淡入淡出\n"
                         "/merge 2 合併近期檔案\n"
                         "/outputs 列出近期檔案")
        except Exception as exc:
            log(f"poll failed: {exc}")
            time.sleep(5)
    return 0


if __name__ == "__main__":
    if len(sys.argv) >= 2 and sys.argv[1] == "--test-gen":
        load_settings()
        cfg = load_config()
        TOKEN = str(cfg["bot_token"])
        prompt = sys.argv[2] if len(sys.argv) >= 3 else "Warm acoustic guitar instrumental, no vocals."
        run_job(cfg["bot_token"], int(cfg["chat_id"]), prompt)
    else:
        sys.exit(main())
