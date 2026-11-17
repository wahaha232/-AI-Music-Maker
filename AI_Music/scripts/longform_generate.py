"""Generate a long track by chunked album generation and ffmpeg crossfade."""

from __future__ import annotations

import argparse
import json
import subprocess
import sys
import tempfile
from datetime import datetime
from pathlib import Path

BASE = Path(__file__).resolve().parents[1]
BACKEND = BASE / "app" / "generate_backend.py"
FFMPEG = BASE / "tools" / "ffmpeg" / "bin" / "ffmpeg.exe"
DEFAULT_OUT = BASE / "generated"


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--config", type=Path, required=True,
                        help="base config JSON, same format as generate_backend")
    parser.add_argument("--duration", type=float,
                        help="target seconds; defaults to config duration")
    parser.add_argument("--chunk", type=float, default=240.0,
                        help="seconds per chunk (default 240)")
    parser.add_argument("--crossfade", type=float, default=5.0,
                        help="crossfade seconds (default 5)")
    parser.add_argument("--out", type=Path, help="final MP3 path")
    parser.add_argument("--keep-chunks", action="store_true")
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--gpu-wait", type=float, default=60.0)
    return parser.parse_args()


def build_album(base: dict, target: float, chunk: float, crossfade: float,
                out_dir: Path) -> tuple[dict, int]:
    if chunk <= crossfade or chunk < 30:
        raise ValueError("chunk 必須大於 crossfade，且至少 30 秒")
    if target <= chunk:
        raise ValueError("target 必須大於單一 chunk；短曲請用一般生成")
    count = 1
    while count * chunk - (count - 1) * crossfade < target:
        count += 1
    caption = str(base.get("caption") or "").strip()
    if not caption:
        raise ValueError("config 缺少 caption")
    tracks = []
    for index in range(1, count + 1):
        if index == 1:
            prompt = caption + ", intro, establish the theme, seamless continuation"
        elif index == count:
            prompt = caption + ", outro, resolve and fade to a close"
        else:
            prompt = caption + ", seamless continuation, no intro, no outro, same groove"
        tracks.append({
            "title": f"Part {index:02d}",
            "prompt": prompt,
            "duration": chunk,
        })
    shared = dict(base)
    shared["duration"] = chunk
    shared["fade_in_duration"] = 0
    shared["fade_out_duration"] = 0
    album = {
        "album_title": base.get("caption", "Longform"),
        "artist": "AI Music Maker",
        "output_dir": str(out_dir),
        "shared": shared,
        "tracks": tracks,
    }
    return album, count


def merge(chunks: list[Path], output: Path, target: float, crossfade: float,
          fade_in: float, fade_out: float, work_dir: Path) -> int:
    filters = []
    last = "[0:a]"
    for index in range(1, len(chunks)):
        label = f"[a{index}]"
        filters.append(
            f"{last}[{index}:a]acrossfade=d={crossfade:.3f}:c1=tri:c2=tri{label}")
        last = label
    final_filters = [f"loudnorm=I=-14:TP=-1.5:LRA=11"]
    if fade_in > 0:
        final_filters.append(f"afade=t=in:st=0:d={fade_in:.3f}")
    if fade_out > 0:
        final_filters.append(
            f"afade=t=out:st={max(0.0, target - fade_out):.3f}:d={fade_out:.3f}")
    filters.append(f"{last}{','.join(final_filters)}[out]")
    command = [str(FFMPEG), "-hide_banner", "-loglevel", "error", "-y"]
    for path in chunks:
        command += ["-i", str(path)]
    command += [
        "-filter_complex", ";".join(filters),
        "-map", "[out]",
        "-t", f"{target:.3f}",
        "-c:a", "libmp3lame", "-b:a", "320k", "-ar", "48000",
        str(output),
    ]
    result = subprocess.run(command, cwd=str(work_dir))
    return result.returncode


def write_metadata(output: Path, base: dict, target: float, count: int,
                   crossfade: float) -> None:
    caption = str(base.get("caption") or "").strip()
    title = caption[:90] or output.stem
    description = (
        f"{caption}\n\n"
        f"AI-generated longform music ({target / 60:.1f} min).\n"
        f"Generated with AI Music Maker / ACE-Step.\n"
    )
    words = [word.strip(",.;:()[]") for word in caption.split()]
    tags = [word for word in words if 2 <= len(word) <= 20][:12]
    tags += ["AI music", "instrumental", "background music"]
    metadata = {
        "title": title,
        "description": description,
        "tags": tags,
        "categoryId": "10",
        "privacyStatus": "private",
        "madeForKids": False,
        "duration_seconds": target,
        "chunks": count,
        "crossfade_seconds": crossfade,
    }
    output.with_suffix(".youtube.json").write_text(
        json.dumps(metadata, ensure_ascii=False, indent=2), encoding="utf-8")


def main() -> int:
    args = parse_args()
    if not args.config.is_file():
        print(f"[error] missing config: {args.config}")
        return 2
    base = json.loads(args.config.read_text(encoding="utf-8-sig"))
    target = float(args.duration or base.get("duration") or 0)
    if target < 60:
        print("[error] duration must be at least 60 seconds")
        return 2
    out_dir = Path(base.get("output_dir") or DEFAULT_OUT)
    out_dir.mkdir(parents=True, exist_ok=True)
    stamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    work_dir = Path(tempfile.mkdtemp(prefix="longform_", dir=str(out_dir)))
    try:
        album, count = build_album(base, target, args.chunk, args.crossfade, work_dir)
        width = max(2, len(str(count)))
        album_path = work_dir / "album_input.json"
        album_path.write_text(json.dumps(album, ensure_ascii=False, indent=2),
                              encoding="utf-8")
        print(f"[longform] target={target:.0f}s chunks={count} chunk={args.chunk:.0f}s")
        if args.dry_run:
            print(f"[dry-run] album={album_path}")
            return 0
        command = [sys.executable, "-u", str(BACKEND), "--album", str(album_path),
                   "--gpu-wait", str(args.gpu_wait)]
        print("[longform] generating chunks; model stays loaded...")
        result = subprocess.run(command)
        if result.returncode != 0:
            print(f"[error] backend exit={result.returncode}")
            return result.returncode
        album_out = work_dir / "album.json"
        if not album_out.is_file():
            print("[error] backend did not write album.json")
            return 1
        meta = json.loads(album_out.read_text(encoding="utf-8"))
        chunks = [Path(track["file"]) for track in meta.get("tracks", [])
                  if Path(track.get("file", "")).is_file()]
        if len(chunks) != count:
            print(f"[error] expected {count} chunks, got {len(chunks)}")
            return 1
        output = args.out or (out_dir / f"longform_{stamp}.mp3")
        fade_in = float(base.get("fade_in_duration") or 0)
        fade_out = float(base.get("fade_out_duration") or 0)
        print(f"[longform] merging {len(chunks)} chunks crossfade={args.crossfade}s")
        code = merge(chunks, output, target, args.crossfade, fade_in, fade_out, work_dir)
        if code != 0 or not output.is_file():
            print(f"[error] merge exit={code}")
            return 1
        write_metadata(output, base, target, count, args.crossfade)
        print(f"[done] {output}")
        print(f"[meta] {output.with_suffix('.youtube.json')}")
        if not args.keep_chunks:
            for path in chunks:
                try:
                    path.unlink()
                except OSError:
                    pass
            try:
                album_out.unlink()
                album_path.unlink()
            except OSError:
                pass
        return 0
    finally:
        if args.keep_chunks:
            print(f"[chunks] kept in {work_dir}")
        else:
            import shutil
            shutil.rmtree(work_dir, ignore_errors=True)


if __name__ == "__main__":
    raise SystemExit(main())
