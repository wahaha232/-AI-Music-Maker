"""Cross-process GPU lock for generate_backend.py."""

from __future__ import annotations

import json
import os
import time
from datetime import datetime
from pathlib import Path

BASE_DIR = Path(__file__).resolve().parent.parent
LOCK_FILE = BASE_DIR / ".gpu.lock"
OWNER_FILE = BASE_DIR / ".gpu.owner.json"


class GpuBusy(Exception):
    pass


def _process_alive(pid: int) -> bool:
    if pid <= 0:
        return False
    try:
        if os.name == "nt":
            import ctypes
            handle = ctypes.windll.kernel32.OpenProcess(0x1000, False, pid)
            if not handle:
                return False
            ctypes.windll.kernel32.CloseHandle(handle)
            return True
        os.kill(pid, 0)
        return True
    except Exception:
        return False


def owner_info() -> str:
    try:
        data = json.loads(OWNER_FILE.read_text(encoding="utf-8"))
        pid = int(data.get("pid", 0))
        source = str(data.get("source", "unknown"))
        started = str(data.get("started", ""))
        if _process_alive(pid):
            return f"pid={pid} source={source} started={started}"
        return f"stale pid={pid} source={source}"
    except Exception:
        return "unknown"


class GpuLock:
    def __init__(self, fd: int, source: str):
        self._fd = fd
        self.source = source
        self._released = False

    def __enter__(self) -> "GpuLock":
        return self

    def __exit__(self, exc_type, exc, tb) -> None:
        self.release()

    def release(self) -> None:
        if self._released:
            return
        self._released = True
        try:
            if os.name == "nt":
                import msvcrt
                os.lseek(self._fd, 0, os.SEEK_SET)
                msvcrt.locking(self._fd, msvcrt.LK_UNLCK, 1)
            else:
                import fcntl
                fcntl.flock(self._fd, fcntl.LOCK_UN)
        except Exception:
            pass
        try:
            os.close(self._fd)
        except Exception:
            pass
        try:
            data = json.loads(OWNER_FILE.read_text(encoding="utf-8"))
            if int(data.get("pid", -1)) == os.getpid():
                OWNER_FILE.unlink()
        except Exception:
            pass


def acquire(source: str, wait_seconds: float = 0.0) -> GpuLock:
    deadline = time.time() + max(0.0, wait_seconds)
    flags = os.O_RDWR | os.O_CREAT | getattr(os, "O_BINARY", 0)
    while True:
        fd = None
        try:
            fd = os.open(str(LOCK_FILE), flags, 0o666)
            if os.fstat(fd).st_size == 0:
                os.write(fd, b"0")
            os.lseek(fd, 0, os.SEEK_SET)
            if os.name == "nt":
                import msvcrt
                msvcrt.locking(fd, msvcrt.LK_NBLCK, 1)
            else:
                import fcntl
                fcntl.flock(fd, fcntl.LOCK_EX | fcntl.LOCK_NB)
            lock = GpuLock(fd, source)
            OWNER_FILE.write_text(json.dumps({
                "pid": os.getpid(),
                "source": source,
                "started": datetime.now().isoformat(timespec="seconds"),
            }, ensure_ascii=False), encoding="utf-8")
            return lock
        except Exception as exc:
            if fd is not None:
                try:
                    os.close(fd)
                except Exception:
                    pass
            if time.time() >= deadline:
                raise GpuBusy(owner_info()) from exc
            time.sleep(0.25)
