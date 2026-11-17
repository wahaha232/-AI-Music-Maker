"""Post-process audio: fade in/out and merge multiple tracks with ffmpeg."""

from __future__ import annotations

import argparse
import re
import subprocess
import sys
from datetime import datetime
from pathlib import Path

from _common import BASE_DIR, safe_stdio

FFMPEG = BASE_DIR / "tools" / "ffmpeg" / "bin" / "ffmpeg.exe"
AUDIO_EXTS = {".mp3", ".wav", ".flac"}


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Audio fade and merge tools.")
    parser.add_argument("--ffmpeg", type=Path, default=FFMPEG)
    sub = parser.add_subparsers(dest="command", required=True)

    fade = sub.add_parser("fade", help="apply fade in/out to one audio file")
    fade.add_argument("input", type=Path)
    fade.add_argument("--fade-in", type=float, default=0.0)
    fade.add_argument("--fade-out", type=float, default=0.0)
    fade.add_argument("--out", type=Path)
    fade.add_argument("--overwrite", action="store_true")

    merge = sub.add_parser("merge", help="concatenate two or more audio files")
    merge.add_argument("inputs", nargs="+", type=Path)
    merge.add_argument("--out", type=Path)
    merge.add_argument("--overwrite", action="store_true")

    normalize = sub.add_parser("normalize", help="normalize loudness to a target LUFS")
    normalize.add_argument("inputs", nargs="+", type=Path)
    normalize.add_argument("--target", type=float, default=-14.0)
    normalize.add_argument("--outdir", type=Path)
    normalize.add_argument("--overwrite", action="store_true")
    return parser.parse_args()


def probe_duration(ffmpeg: Path, audio: Path) -> float | None:
    result = subprocess.run(
        [str(ffmpeg), "-hide_banner", "-i", str(audio)],
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
    )
    match = re.search(r"Duration:\s*(\d+):(\d+):(\d+(?:\.\d+)?)", result.stderr)
    if not match:
        return None
    hours, minutes, seconds = match.groups()
    return int(hours) * 3600 + int(minutes) * 60 + float(seconds)


def unique_output(path: Path) -> Path:
    if not path.exists():
        return path
    stamp = datetime.now().strftime("%H%M%S")
    return path.with_name(f"{path.stem}_{stamp}{path.suffix}")


def audio_codec_args(path: Path) -> list[str]:
    ext = path.suffix.lower()
    if ext == ".mp3":
        return ["-c:a", "libmp3lame", "-b:a", "320k", "-ar", "48000"]
    if ext == ".flac":
        return ["-c:a", "flac"]
    return ["-c:a", "pcm_s16le"]


def run_ffmpeg(ffmpeg: Path, command: list[str]) -> int:
    result = subprocess.run([str(ffmpeg), "-hide_banner", "-loglevel", "error", *command])
    return result.returncode


def fade(args: argparse.Namespace, ffmpeg: Path) -> int:
    if not args.input.is_file():
        print(f"[error] missing input: {args.input}")
        return 2
    if args.fade_in <= 0 and args.fade_out <= 0:
        print("[error] fade-in and fade-out are both zero")
        return 2
    duration = probe_duration(ffmpeg, args.input)
    if not duration:
        print(f"[error] cannot read duration: {args.input}")
        return 2

    fade_in = max(0.0, min(args.fade_in, duration))
    fade_out = max(0.0, min(args.fade_out, duration))
    filters = []
    if fade_in > 0:
        filters.append(f"afade=t=in:st=0:d={fade_in:.3f}")
    if fade_out > 0:
        start = max(0.0, duration - fade_out)
        filters.append(f"afade=t=out:st={start:.3f}:d={fade_out:.3f}")

    if args.out is not None:
        output = args.out
    else:
        output = args.input.with_name(f"{args.input.stem}_fade{args.input.suffix}")
        output = unique_output(output)
    output.parent.mkdir(parents=True, exist_ok=True)

    command = [
        "-y" if args.overwrite else "-n",
        "-i",
        str(args.input),
        "-af",
        ",".join(filters),
        "-map_metadata",
        "0",
        *audio_codec_args(output),
        str(output),
    ]
    code = run_ffmpeg(ffmpeg, command)
    if code != 0:
        print(f"[error] ffmpeg exit={code}")
        return code
    print(f"[done] {output}")
    return 0


def merge(args: argparse.Namespace, ffmpeg: Path) -> int:
    inputs = [path for path in args.inputs if path.is_file()]
    if len(inputs) < 2:
        print("[error] merge needs at least two existing audio files")
        return 2
    if args.out is not None:
        output = args.out
    else:
        output = inputs[0].with_name(
            f"merged_{datetime.now().strftime('%Y%m%d_%H%M%S')}.mp3")
    output.parent.mkdir(parents=True, exist_ok=True)

    parts = []
    labels = []
    for index in range(len(inputs)):
        parts.append(
            f"[{index}:a]aresample=48000,"
            f"aformat=sample_fmts=fltp:channel_layouts=stereo[a{index}]")
        labels.append(f"[a{index}]")
    filter_graph = ";".join(parts) + ";" + "".join(labels) + \
        f"concat=n={len(inputs)}:v=0:a=1[out]"

    command = ["-y" if args.overwrite else "-n"]
    for path in inputs:
        command += ["-i", str(path)]
    command += [
        "-filter_complex",
        filter_graph,
        "-map",
        "[out]",
        "-map_metadata",
        "0",
        *audio_codec_args(output),
        str(output),
    ]
    code = run_ffmpeg(ffmpeg, command)
    if code != 0:
        print(f"[error] ffmpeg exit={code}")
        return code
    print(f"[done] {output}")
    return 0


def normalize(args: argparse.Namespace, ffmpeg: Path) -> int:
    inputs = [path for path in args.inputs if path.is_file()]
    if not inputs:
        print("[error] no existing input files")
        return 2
    if args.outdir is not None:
        args.outdir.mkdir(parents=True, exist_ok=True)
    failed = 0
    for source in inputs:
        output_dir = args.outdir if args.outdir is not None else source.parent
        output = output_dir / f"{source.stem}_norm{source.suffix}"
        if not args.overwrite:
            output = unique_output(output)
        output_dir.mkdir(parents=True, exist_ok=True)
        command = [
            "-y" if args.overwrite else "-n",
            "-i",
            str(source),
            "-af",
            f"loudnorm=I={args.target:.1f}:TP=-1.5:LRA=11",
            "-ar",
            "48000",
            "-map_metadata",
            "0",
            *audio_codec_args(output),
            str(output),
        ]
        code = run_ffmpeg(ffmpeg, command)
        if code == 0:
            print(f"[done] {output}")
        else:
            print(f"[error] ffmpeg exit={code}: {source}")
            failed += 1
    return 0 if failed == 0 else 1


def main() -> int:
    safe_stdio()
    args = parse_args()
    ffmpeg = args.ffmpeg
    if not ffmpeg.is_file():
        print(f"[error] ffmpeg not found: {ffmpeg}")
        return 2
    if args.command == "fade":
        return fade(args, ffmpeg)
    if args.command == "normalize":
        return normalize(args, ffmpeg)
    return merge(args, ffmpeg)


if __name__ == "__main__":
    sys.exit(main())
