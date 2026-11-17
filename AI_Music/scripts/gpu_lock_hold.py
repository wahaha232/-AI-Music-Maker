"""Hold the cross-process GPU lock while an external worker uses the GPU."""

from __future__ import annotations

import os
import sys
from pathlib import Path

BASE = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(BASE / "app"))

from gpu_lock import GpuBusy, GpuLockError, acquire  # noqa: E402


def main() -> int:
    source = sys.argv[1] if len(sys.argv) > 1 else "external"
    wait_seconds = float(sys.argv[2]) if len(sys.argv) > 2 else 0.0
    try:
        lock = acquire(source, wait_seconds)
    except GpuBusy as exc:
        print(f"[busy] {exc}", flush=True)
        return 75
    except GpuLockError as exc:
        print(f"[error] GPU lock error: {exc}", flush=True)
        return 75
    print(f"[locked] pid={os.getpid()} source={source}", flush=True)
    try:
        for line in sys.stdin:
            if line.strip().lower() in ("quit", "exit"):
                break
    except Exception:
        pass
    finally:
        lock.release()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
