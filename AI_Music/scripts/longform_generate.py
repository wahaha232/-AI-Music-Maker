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
    parser.add_argument("--chunk", type=float, default=180.0,
                        help="seconds per chunk (default 180)")
    parser.add_argument("--crossfade", type=float, default=5.0,
                        help="crossfade seconds (default 5)")
    parser.add_argument("--chunks-per-process", type=int, default=3,
                        help="chunks generated per backend process; "
                             "0 keeps one process for everything (default 3)")
    parser.add_argument("--out", type=Path, help="final MP3 path")
    parser.add_argument("--keep-chunks", action="store_true")
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--gpu-wait", type=float, default=60.0)
    parser.add_argument("--python", help="interpreter for the backend "
                                         "(default: AI_Music\\.venv if present)")
    return parser.parse_args()


def backend_python(explicit: str | None) -> str:
    if explicit:
        return explicit
    for candidate in (BASE.parent / "AI_Music.venv" / "Scripts" / "python.exe",
                      BASE / ".venv" / "Scripts" / "python.exe"):
        if candidate.is_file():
            return str(candidate)
    return sys.executable


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
    seed = int(base.get("seed") if base.get("seed") is not None else -1)
    for index in range(1, count + 1):
        if index == 1:
            prompt = caption + ", intro, establish the theme, seamless continuation"
        elif index == count:
            prompt = caption + ", outro, resolve and fade to a close"
        else:
            prompt = caption + ", seamless continuation, no intro, no outro, same groove"
        track = {
            "title": f"Part {index:02d}",
            "prompt": prompt,
            "duration": chunk,
        }
        if seed >= 0:
            track["seed"] = seed + index - 1
        tracks.append(track)
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


def split_batches(album: dict, per_process: int) -> list[list[dict]]:
    tracks = list(album.get("tracks") or [])
    if per_process <= 0 or per_process >= len(tracks):
        return [tracks]
    return [tracks[start:start + per_process]
            for start in range(0, len(tracks), per_process)]


def generate(album: dict, batches: list[list[dict]], work_dir: Path,
             gpu_wait: float, dry_run: bool, python: str | None = None) -> list[Path]:
    chunks: list[Path] = []
    for number, group in enumerate(batches, 1):
        batch_dir = work_dir / f"batch_{number:02d}"
        batch_dir.mkdir(parents=True, exist_ok=True)
        batch_album = dict(album)
        batch_album["tracks"] = group
        batch_album["output_dir"] = str(batch_dir)
        album_path = batch_dir / "album_input.json"
        album_path.write_text(json.dumps(batch_album, ensure_ascii=False, indent=2),
                              encoding="utf-8")
        label = f"batch {number}/{len(batches)} tracks={len(group)}"
        if dry_run:
            print(f"[dry-run] {label} -> {album_path}")
            continue
        print(f"[longform] {label}")
        album_out = batch_dir / "album.json"
        try:
            album_out.unlink()
        except OSError:
            pass
        result = subprocess.run([python or backend_python(None), "-u", str(BACKEND),
                                 "--album", str(album_path),
                                 "--gpu-wait", str(gpu_wait)])
        produced: list[Path] = []
        if album_out.is_file():
            meta = json.loads(album_out.read_text(encoding="utf-8"))
            produced = [Path(track["file"]) for track in meta.get("tracks", [])
                        if Path(track.get("file", "")).is_file()]
            failed = meta.get("invalid") or []
            if failed:
                detail = ", ".join(f"#{item.get('index')} {item.get('error')}"
                                   for item in failed)
                raise RuntimeError(f"[error] {label} failed: {detail}")
        if result.returncode != 0:
            raise RuntimeError(f"[error] {label} backend exit={result.returncode}")
        if len(produced) != len(group):
            raise RuntimeError(f"[error] {label} produced {len(produced)}/{len(group)}")
        chunks.extend(produced)
    return chunks


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
        batches = split_batches(album, args.chunks_per_process)
        print(f"[longform] target={target:.0f}s chunks={count} chunk={args.chunk:.0f}s "
              f"batches={len(batches)}")
        chunks = generate(album, batches, work_dir, args.gpu_wait, args.dry_run,
                          backend_python(args.python))
        if args.dry_run:
            return 0
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
        return 0
    except RuntimeError as exc:
        print(str(exc))
        return 1
    finally:
        if args.keep_chunks:
            print(f"[chunks] kept in {work_dir}")
        else:
            import shutil
            shutil.rmtree(work_dir, ignore_errors=True)


if __name__ == "__main__":
    raise SystemExit(main())
