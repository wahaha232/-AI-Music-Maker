"""Parallel (aria2-style) downloader for the official ACE-Step 1.5 files.

Why this exists
---------------
The official Hugging Face clients are unusable on this machine:
  * plain HTTP downloader  -> capped at ~300 KB/s for anonymous clients
  * hf_transfer            -> fails on Xet-backed repos
  * hf_xet (Xet engine)    -> stalls with 0 bytes on this network
This script fetches the *same official files* directly over HTTPS with many
parallel byte-range requests, supports resume, and verifies the SHA256 that the
Hugging Face API publishes for every LFS file.

Usage:
    python download_ace_models_parallel.py                 # download everything
    python download_ace_models_parallel.py --limit-mb 128  # speed benchmark only
    python download_ace_models_parallel.py --workers 16
"""

from __future__ import annotations

import argparse
import fnmatch
import hashlib
import json
import shutil
import sys
import threading
import time
from concurrent.futures import ThreadPoolExecutor, as_completed
from pathlib import Path
from typing import Dict, List, Optional, Tuple

import requests

REPO_ID = "ACE-Step/Ace-Step1.5"
# <workspace>\AI_Music\scripts\download_ace_models_parallel.py -> <workspace>\AI_Music
BASE_DIR = Path(__file__).resolve().parents[1]
TARGET_DIR = BASE_DIR / "models" / "Ace-Step1.5"
API_URL = f"https://huggingface.co/api/models/{REPO_ID}?blobs=true"
RESOLVE_BASE = f"https://huggingface.co/{REPO_ID}/resolve/main"
PARTS_DIR = TARGET_DIR / ".parts"
VERIFIED_FILE = TARGET_DIR / ".verified.json"
CHUNK_SIZE = 8 * 1024 * 1024
READ_BLOCK = 1024 * 1024
CHUNK_RETRIES = 12

# Only the official files this offline setup needs: turbo DiT + 1.7B LM +
# Qwen3 text encoder + VAE + their configs/tokenizers.
ALLOW_PATTERNS = [
    ".gitattributes",
    "README.md",
    "*.json",
    "*.py",
    "*.jinja",
    "*.txt",
    "vae/*",
    "Qwen3-Embedding-0.6B/*",
    "acestep-v15-turbo/*",
    "acestep-5Hz-lm-1.7B/*",
]

_local = threading.local()


def _session() -> requests.Session:
    session = getattr(_local, "session", None)
    if session is None:
        session = requests.Session()
        session.headers.update({"User-Agent": "acestep-local-downloader/1.0"})
        _local.session = session
    return session


def _human(num: float) -> str:
    for unit in ("B", "KB", "MB", "GB", "TB"):
        if abs(num) < 1024.0:
            return f"{num:6.1f}{unit}"
        num /= 1024.0
    return f"{num:.1f}PB"


class Progress:
    """Thread-safe byte counter with a background reporter."""

    def __init__(self, total_bytes: int, label: str) -> None:
        self._lock = threading.Lock()
        self._done = 0
        self._total = total_bytes
        self._label = label
        self._start = time.time()
        self._stop = threading.Event()
        self._thread = threading.Thread(target=self._report, daemon=True)
        self._thread.start()

    def add(self, num: int) -> None:
        with self._lock:
            self._done += num

    def _report(self) -> None:
        while not self._stop.wait(10):
            with self._lock:
                done = self._done
            elapsed = max(time.time() - self._start, 0.001)
            rate = done / elapsed
            if self._total > 0 and rate > 0:
                tail = f" ETA {(self._total - done) / rate / 60:5.1f} min"
            else:
                tail = ""
            print(
                f"[{self._label}] {_human(float(done))}/{_human(float(self._total))} "
                f"({100.0 * done / max(self._total, 1):5.1f}%) {rate / 1024:6.0f} KB/s{tail}",
                flush=True,
            )

    def finish(self) -> None:
        self._stop.set()
        self._thread.join(timeout=1)


def _list_repo_files(patterns: Optional[List[str]] = None) -> List[Dict]:
    selected_patterns = patterns or ALLOW_PATTERNS
    response = _session().get(API_URL, timeout=60)
    response.raise_for_status()
    siblings = response.json().get("siblings", [])
    selected = []
    for entry in siblings:
        rel = entry.get("rfilename", "")
        if any(fnmatch.fnmatch(rel, pattern) for pattern in selected_patterns):
            selected.append(entry)
    selected.sort(key=lambda e: -int(e.get("size") or 0))
    return selected


def _fetch_chunk(url: str, start: int, end: int, part_path: Path) -> int:
    expected = end - start + 1
    last_error: Optional[Exception] = None
    for attempt in range(1, CHUNK_RETRIES + 1):
        try:
            response = _session().get(
                url,
                headers={"Range": f"bytes={start}-{end}"},
                stream=True,
                timeout=(20, 120),
            )
            if response.status_code == 416:
                raise RuntimeError(f"range rejected ({start}-{end})")
            if response.status_code == 200:
                raise RuntimeError("server ignored Range header")
            response.raise_for_status()
            tmp_path = part_path.with_suffix(".tmp")
            written = 0
            with open(tmp_path, "wb") as handle:
                for block in response.iter_content(READ_BLOCK):
                    if block:
                        handle.write(block)
                        written += len(block)
            if written != expected:
                raise RuntimeError(f"short read {written}/{expected}")
            tmp_path.replace(part_path)
            return written
        except Exception as exc:  # noqa: BLE001 - network flakiness is expected
            last_error = exc
            time.sleep(min(3 * attempt, 90))
    raise RuntimeError(f"chunk {start}-{end} failed after {CHUNK_RETRIES} tries: {last_error}")


def _sha256_of(path: Path) -> str:
    digest = hashlib.sha256()
    with open(path, "rb") as handle:
        for block in iter(lambda: handle.read(4 * 1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def _load_verified() -> dict:
    try:
        data = json.loads(VERIFIED_FILE.read_text(encoding="utf-8"))
        if isinstance(data, dict) and isinstance(data.get("files"), dict):
            return data
    except Exception:
        pass
    return {"version": 1, "files": {}}


def _save_verified(data: dict) -> None:
    VERIFIED_FILE.parent.mkdir(parents=True, exist_ok=True)
    tmp = VERIFIED_FILE.with_suffix(".tmp")
    tmp.write_text(json.dumps(data, indent=2, sort_keys=True), encoding="utf-8")
    tmp.replace(VERIFIED_FILE)


def _record_verified(rel_path: str, path: Path, sha256: str) -> None:
    data = _load_verified()
    stat = path.stat()
    data["files"][rel_path] = {
        "size": int(stat.st_size),
        "mtime": int(stat.st_mtime),
        "sha256": sha256.lower(),
        "verified_at": time.strftime("%Y-%m-%dT%H:%M:%S"),
    }
    _save_verified(data)


def _marker_matches(rel_path: str, size: int, sha256: str) -> bool:
    final_path = TARGET_DIR / rel_path
    if not final_path.is_file():
        return False
    entry = _load_verified()["files"].get(rel_path)
    if not isinstance(entry, dict):
        return False
    stat = final_path.stat()
    return (
        int(entry.get("size", -1)) == size
        and int(entry.get("size", -1)) == int(stat.st_size)
        and int(entry.get("mtime", -1)) == int(stat.st_mtime)
        and str(entry.get("sha256", "")).lower() == sha256.lower()
    )


def _already_complete(final_path: Path, size: int, sha256: Optional[str], rel_path: str) -> bool:
    if not final_path.exists() or final_path.stat().st_size != size:
        return False
    if not sha256:
        return True
    if _marker_matches(rel_path, size, sha256):
        return True
    if _sha256_of(final_path) == sha256:
        _record_verified(rel_path, final_path, sha256)
        return True
    return False


def _download_big_file(
    rel_path: str,
    size: int,
    sha256: Optional[str],
    workers: int,
    note: str,
    limit_bytes: Optional[int],
) -> Tuple[bool, int]:
    final_path = TARGET_DIR / rel_path
    url = f"{RESOLVE_BASE}/{rel_path}"
    effective_size = min(size, limit_bytes) if limit_bytes else size

    part_key = rel_path.replace("/", "__")
    part_dir = PARTS_DIR / f"{part_key}__{effective_size}__{CHUNK_SIZE // (1024 * 1024)}MB"
    part_dir.mkdir(parents=True, exist_ok=True)

    chunks: List[Tuple[int, int, Path]] = []
    offset = 0
    index = 0
    while offset < effective_size:
        end = min(offset + CHUNK_SIZE, effective_size) - 1
        chunks.append((offset, end, part_dir / f"{index:05d}.part"))
        offset = end + 1
        index += 1

    pending = []
    for start, end, part_path in chunks:
        expected = end - start + 1
        if part_path.exists() and part_path.stat().st_size == expected:
            continue
        pending.append((start, end, part_path))

    print(
        f"[file] {rel_path}: {_human(float(size))}, {len(chunks)} chunks, "
        f"{len(pending)} to fetch{note}",
        flush=True,
    )

    progress = Progress(effective_size, rel_path.split("/")[0])
    progress.add(effective_size - sum(end - start + 1 for start, end, _ in pending))

    pool = ThreadPoolExecutor(max_workers=workers)
    try:
        futures = {
            pool.submit(_fetch_chunk, url, start, end, part_path): (start, end, part_path)
            for start, end, part_path in pending
        }
        for future in as_completed(futures):
            start, end, part_path = futures[future]
            written = future.result()
            progress.add(written)
        pool.shutdown(wait=True)
    except Exception:
        print(f"[file] {rel_path}: cancelling queued chunks after error", flush=True)
        pool.shutdown(wait=False, cancel_futures=True)
        raise
    finally:
        progress.finish()

    if limit_bytes:
        print(f"[file] {rel_path}: benchmark stopped at {_human(float(effective_size))}", flush=True)
        return False, effective_size

    tmp_final = final_path.with_suffix(final_path.suffix + ".assembling")
    final_path.parent.mkdir(parents=True, exist_ok=True)
    with open(tmp_final, "wb") as out:
        for _, _, part_path in chunks:
            with open(part_path, "rb") as part:
                shutil.copyfileobj(part, out, 4 * 1024 * 1024)
    tmp_final.replace(final_path)
    shutil.rmtree(part_dir, ignore_errors=True)

    if sha256:
        actual = _sha256_of(final_path)
        if actual != sha256:
            final_path.unlink(missing_ok=True)
            print(f"[file] {rel_path}: SHA256 MISMATCH (got {actual})", flush=True)
            return False, effective_size
        _record_verified(rel_path, final_path, sha256)
        print(f"[file] {rel_path}: SHA256 verified", flush=True)
    return True, effective_size


def _download_small_file(rel_path: str, size: int) -> Tuple[bool, int]:
    final_path = TARGET_DIR / rel_path
    final_path.parent.mkdir(parents=True, exist_ok=True)
    url = f"{RESOLVE_BASE}/{rel_path}"
    for attempt in range(1, 6):
        try:
            response = _session().get(url, stream=True, timeout=(20, 120))
            response.raise_for_status()
            data = response.content
            with open(final_path, "wb") as handle:
                handle.write(data)
            return True, len(data)
        except Exception as exc:  # noqa: BLE001
            print(f"[file] {rel_path}: attempt {attempt} failed: {exc}", flush=True)
            time.sleep(3 * attempt)
    return False, 0


def _parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--workers", type=int, default=16, help="parallel range connections per file")
    parser.add_argument("--limit-mb", type=int, default=0, help="benchmark: only fetch this many MB per file")
    parser.add_argument("--passes", type=int, default=50, help="sweeps over the file list (unattended retry)")
    parser.add_argument("--pass-delay", type=int, default=120, help="seconds to wait between sweeps")
    parser.add_argument("--chunk-mb", type=int, default=8, help="range chunk size in MB (bigger = fewer requests)")
    parser.add_argument("--only", action="append", default=None,
                        help="only fetch files matching this glob (repeatable)")
    return parser.parse_args()


def _attempt_pass(
    files: List[Dict], args: argparse.Namespace, limit_bytes: Optional[int]
) -> Tuple[int, List[str], List[str]]:
    done_bytes = 0
    failed: List[str] = []
    skipped: List[str] = []
    total_size = sum(int(entry.get("size") or 0) for entry in files)
    print(f"[main] sweep over {len(files)} files, {_human(float(total_size))} total", flush=True)

    for rank, entry in enumerate(files, start=1):
        rel_path = entry["rfilename"]
        size = int(entry.get("size") or 0)
        sha256 = (entry.get("lfs") or {}).get("sha256")
        final_path = TARGET_DIR / rel_path

        if not limit_bytes and _already_complete(final_path, size, sha256, rel_path):
            print(f"[{rank}/{len(files)}] {rel_path}: already present ({_human(float(size))})", flush=True)
            skipped.append(rel_path)
            done_bytes += size
            continue

        note = " (sha256 verified)" if sha256 and not limit_bytes else ""
        t0 = time.time()
        try:
            if size >= CHUNK_SIZE:
                ok, got = _download_big_file(
                    rel_path, size, sha256, args.workers, note, limit_bytes
                )
            else:
                ok, got = _download_small_file(rel_path, size)
        except Exception as exc:  # noqa: BLE001 - keep sweeping the remaining files
            ok, got = False, 0
            print(
                f"[{rank}/{len(files)}] {rel_path}: ERROR {type(exc).__name__}: {exc}",
                flush=True,
            )

        elapsed = max(time.time() - t0, 0.001)
        print(
            f"[{rank}/{len(files)}] {rel_path}: {'OK' if ok else 'INCOMPLETE'} "
            f"{_human(float(got))} in {elapsed:.0f}s ({got / elapsed / 1024:.0f} KB/s){note}",
            flush=True,
        )
        if ok:
            done_bytes += got
        elif not limit_bytes:
            failed.append(rel_path)

    return done_bytes, failed, skipped


def main() -> int:
    global CHUNK_SIZE
    args = _parse_args()
    CHUNK_SIZE = max(1, args.chunk_mb) * 1024 * 1024
    limit_bytes = args.limit_mb * 1024 * 1024 if args.limit_mb > 0 else None
    TARGET_DIR.mkdir(parents=True, exist_ok=True)

    files = _list_repo_files(args.only)
    if not files:
        print("[main] no files selected", flush=True)
        return 1

    print(f"[main] repo={REPO_ID}", flush=True)
    print(f"[main] target={TARGET_DIR}", flush=True)
    print(f"[main] workers={args.workers} passes={args.passes} "
          f"pass_delay={args.pass_delay}s", flush=True)

    grand_start = time.time()
    failed: List[str] = []
    for pass_index in range(1, args.passes + 1):
        print(f"[main] === pass {pass_index}/{args.passes} ===", flush=True)
        done_bytes, failed, skipped = _attempt_pass(files, args, limit_bytes)
        print(
            f"[main] pass {pass_index}: {len(skipped)} complete, {len(failed)} pending, "
            f"{_human(float(done_bytes))} accounted",
            flush=True,
        )
        if limit_bytes:
            print("[main] benchmark mode: nothing was assembled or verified", flush=True)
            return 0
        if not failed:
            elapsed = max(time.time() - grand_start, 0.001)
            on_disk = sum(
                p.stat().st_size
                for p in TARGET_DIR.rglob("*")
                if p.is_file() and PARTS_DIR not in p.parents
            )
            print(f"[main] ALL FILES COMPLETE in {elapsed / 60:.1f} min", flush=True)
            print(f"[main] on-disk total: {_human(float(on_disk))}", flush=True)
            return 0
        if pass_index < args.passes:
            print(
                f"[main] sleeping {args.pass_delay}s before the next sweep; pending: {failed}",
                flush=True,
            )
            time.sleep(args.pass_delay)

    print(f"[main] gave up after {args.passes} passes; still pending: {failed}", flush=True)
    return 1


if __name__ == "__main__":
    sys.exit(main())
