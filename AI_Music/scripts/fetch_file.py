"""Fetch one large file with many parallel HTTP range requests.

Generic, resumable, sha256-verifying downloader used for the official PyTorch
CUDA wheels (the link on this machine is heavily rate limited per connection, so
single-stream downloaders stall).

Usage:
    python fetch_file.py --url <url> --out <path> [--sha256 <hex>] [--workers 24]

Part files live in "<out>.parts" so an interrupted run resumes where it stopped.
"""

from __future__ import annotations

import argparse
import hashlib
import shutil
import sys
import threading
import time
from concurrent.futures import ThreadPoolExecutor, as_completed
from pathlib import Path
from typing import List, Optional, Tuple

import requests

CHUNK_SIZE = 8 * 1024 * 1024
READ_BLOCK = 1024 * 1024
RETRIES = 8
_local = threading.local()


def human(num: float) -> str:
    for unit in ("B", "KB", "MB", "GB", "TB"):
        if abs(num) < 1024.0:
            return f"{num:.2f}{unit}"
        num /= 1024.0
    return f"{num:.2f}PB"


def session() -> requests.Session:
    client = getattr(_local, "client", None)
    if client is None:
        client = requests.Session()
        client.headers.update({"User-Agent": "acestep-local-fetch/1.0"})
        _local.client = client
    return client


def remote_size(url: str) -> int:
    response = session().head(url, allow_redirects=True, timeout=(20, 60))
    response.raise_for_status()
    length = response.headers.get("Content-Length")
    if length is None:
        raise RuntimeError("server did not report Content-Length")
    return int(length)


def fetch_chunk(url: str, start: int, end: int, part_path: Path) -> int:
    expected = end - start + 1
    last_error: Optional[Exception] = None
    for attempt in range(1, RETRIES + 1):
        try:
            response = session().get(
                url, headers={"Range": f"bytes={start}-{end}"}, stream=True, timeout=(20, 120)
            )
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
        except Exception as exc:  # noqa: BLE001 - flaky link, retry
            last_error = exc
            time.sleep(min(2 * attempt, 15))
    raise RuntimeError(f"chunk {start}-{end} failed after {RETRIES} tries: {last_error}")


def sha256_of(path: Path) -> str:
    digest = hashlib.sha256()
    with open(path, "rb") as handle:
        for block in iter(lambda: handle.read(4 * 1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--url", required=True)
    parser.add_argument("--out", required=True)
    parser.add_argument("--sha256", default=None)
    parser.add_argument("--workers", type=int, default=24)
    args = parser.parse_args()

    out_path = Path(args.out)
    out_path.parent.mkdir(parents=True, exist_ok=True)

    if out_path.is_file():
        if args.sha256 and sha256_of(out_path) == args.sha256:
            print(f"[fetch] {out_path.name}: already present and sha256 verified")
            return 0
        if not args.sha256 and out_path.stat().st_size == remote_size(args.url):
            print(f"[fetch] {out_path.name}: already present ({human(float(out_path.stat().st_size))})")
            return 0

    size = remote_size(args.url)
    chunk_mb = CHUNK_SIZE // (1024 * 1024)
    part_dir = out_path.with_name(f"{out_path.name}.{size}.{chunk_mb}mb.parts")
    part_dir.mkdir(parents=True, exist_ok=True)
    print(f"[fetch] parts dir     : {part_dir}", flush=True)
    print(f"[fetch] {out_path.name}: {human(float(size))} <- {args.url}", flush=True)

    chunks: List[Tuple[int, int, Path]] = []
    offset, index = 0, 0
    while offset < size:
        end = min(offset + CHUNK_SIZE, size) - 1
        chunks.append((offset, end, part_dir / f"{index:05d}.part"))
        offset = end + 1
        index += 1

    pending = [
        item
        for item in chunks
        if not (item[2].is_file() and item[2].stat().st_size == item[1] - item[0] + 1)
    ]
    done_bytes = sum(end - start + 1 for start, end, _ in chunks) - sum(
        end - start + 1 for start, end, _ in pending
    )
    print(f"[fetch] {len(chunks)} chunks, {len(pending)} to fetch, "
          f"{human(float(done_bytes))} already on disk", flush=True)

    started = time.time()
    lock = threading.Lock()
    counter = {"done": float(done_bytes)}
    stop = threading.Event()

    def reporter() -> None:
        while not stop.wait(15):
            with lock:
                done = counter["done"]
            elapsed = max(time.time() - started, 0.001)
            rate = (done - done_bytes) / elapsed
            eta = (size - done) / rate / 60 if rate > 0 else 0.0
            print(f"[fetch] {human(done)}/{human(float(size))} "
                  f"({100.0 * done / size:5.1f}%) {rate / 1024:6.0f} KB/s ETA {eta:5.1f} min",
                  flush=True)

    report_thread = threading.Thread(target=reporter, daemon=True)
    report_thread.start()
    pool = ThreadPoolExecutor(max_workers=args.workers)
    try:
        futures = {
            pool.submit(fetch_chunk, args.url, start, end, part_path): (start, end, part_path)
            for start, end, part_path in pending
        }
        for future in as_completed(futures):
            _, _, part_path = futures[future]
            written = future.result()
            with lock:
                counter["done"] += written
        pool.shutdown(wait=True)
    except Exception:
        print("[fetch] cancelling queued chunks after error", flush=True)
        pool.shutdown(wait=False, cancel_futures=True)
        raise
    finally:
        stop.set()
        report_thread.join(timeout=1)

    tmp_out = out_path.with_suffix(out_path.suffix + ".assembling")
    with open(tmp_out, "wb") as out:
        for _, _, part_path in chunks:
            with open(part_path, "rb") as part:
                shutil.copyfileobj(part, out, 4 * 1024 * 1024)
    tmp_out.replace(out_path)
    shutil.rmtree(part_dir, ignore_errors=True)

    elapsed = max(time.time() - started, 0.001)
    written = out_path.stat().st_size
    print(f"[fetch] assembled {human(float(written))} in {elapsed / 60:.1f} min "
          f"({written / elapsed / 1024:.0f} KB/s)")

    if args.sha256:
        actual = sha256_of(out_path)
        if actual != args.sha256:
            out_path.unlink(missing_ok=True)
            print(f"[fetch] SHA256 MISMATCH: got {actual}")
            return 1
        print("[fetch] SHA256 verified")
    return 0


if __name__ == "__main__":
    sys.exit(main())
