"""Render generated audio into YouTube-ready MP4 files with the bundled ffmpeg."""

from __future__ import annotations

import argparse
import json
import os
import re
import subprocess
import sys
import tempfile
from pathlib import Path

from _common import BASE_DIR, safe_stdio

FFMPEG = BASE_DIR / "tools" / "ffmpeg" / "bin" / "ffmpeg.exe"
DEFAULT_COVER = BASE_DIR / "app" / "cover.png"
AUDIO_EXTS = {".mp3", ".wav", ".flac", ".m4a", ".aac", ".ogg", ".opus"}
IMAGE_EXTS = (".png", ".jpg", ".jpeg", ".webp", ".bmp")
COVER_NAMES = ("album_cover.png", "album_cover.jpg", "cover.png", "cover.jpg")


def parse_size(text: str) -> tuple[int, int]:
    match = re.fullmatch(r"(\d+)x(\d+)", text.strip().lower())
    if not match:
        raise argparse.ArgumentTypeError("size must look like 1920x1080")
    width, height = int(match.group(1)), int(match.group(2))
    if width < 16 or height < 16 or width % 2 or height % 2:
        raise argparse.ArgumentTypeError("width and height must be even and at least 16")
    return width, height


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Turn audio files into MP4 videos using a cover image.",
    )
    parser.add_argument("audio", nargs="*", type=Path, help="audio files to render")
    parser.add_argument("--input-dir", type=Path, help="render every audio file in this folder")
    parser.add_argument("--from-settings", action="store_true",
                        help="use the output folder saved by AI Music Maker")
    parser.add_argument("--recursive", action="store_true", help="also scan subfolders of --input-dir")
    parser.add_argument("--outdir", type=Path, help="output folder (default: next to each audio file)")
    parser.add_argument("--image", type=Path, help="cover image for every file (default: auto-detect)")
    parser.add_argument("--style", choices=("blur", "cover", "waveform", "kenburns", "ambient"),
                        default="blur")
    parser.add_argument("--size", type=parse_size, default=(1920, 1080), help="WxH, default 1920x1080")
    parser.add_argument("--fps", type=int, default=5)
    parser.add_argument("--crf", type=int, default=23)
    parser.add_argument("--preset", default="veryfast")
    parser.add_argument("--audio-bitrate", default="192k")
    parser.add_argument("--overwrite", action="store_true")
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--ffmpeg", type=Path, default=FFMPEG)
    return parser


def find_ffmpeg(explicit: Path) -> Path | None:
    for candidate in (explicit, Path("ffmpeg.exe"), Path("ffmpeg")):
        if candidate.is_file():
            return candidate
        resolved = candidate.resolve() if candidate.parent != Path(".") else None
        if resolved and resolved.is_file():
            return resolved
    for name in ("ffmpeg.exe", "ffmpeg"):
        for folder in (BASE_DIR / "tools" / "ffmpeg" / "bin",):
            candidate = folder / name
            if candidate.is_file():
                return candidate
    return None


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


def find_cover(audio: Path, explicit: Path | None, default: Path) -> Path | None:
    if explicit is not None:
        return explicit if explicit.is_file() else None
    folder = audio.parent
    for suffix in (".cover.png", ".cover.jpg", ".cover.jpeg"):
        candidate = folder / (audio.stem + suffix)
        if candidate.is_file():
            return candidate
    for name in COVER_NAMES:
        candidate = folder / name
        if candidate.is_file():
            return candidate
    for ext in IMAGE_EXTS:
        candidate = folder / (audio.stem + ext)
        if candidate.is_file():
            return candidate
    return default if default.is_file() else None


def motion_filter(width: int, height: int, fps: int, ambient: bool) -> str:
    scale_width, scale_height = width * 2, height * 2
    zoom = f"min(1+0.02*on/({fps}*60),1.05)"
    extra = ",noise=alls=3:allf=t+u,vignette=PI/5" if ambient else ""
    return (
        f"scale={scale_width}:{scale_height}:force_original_aspect_ratio=increase,"
        f"crop={scale_width}:{scale_height},"
        f"zoompan=z='{zoom}':x='iw/2-(iw/zoom/2)':y='ih/2-(ih/zoom/2)':"
        f"d=1:s={width}x{height}:fps={fps},setsar=1{extra},format=yuv420p[v]"
    )


def build_filter(style: str, width: int, height: int, fps: int = 5) -> str:
    if style == "cover":
        return (
            f"scale={width}:{height}:force_original_aspect_ratio=increase,"
            f"crop={width}:{height},setsar=1,format=yuv420p[v]"
        )

    if style == "kenburns":
        return motion_filter(width, height, fps, False)

    if style == "ambient":
        return motion_filter(width, height, fps, True)

    if style == "waveform":
        size = max(16, int(height * 0.62) // 2 * 2)
        wave_height = max(16, int(height * 0.12) // 2 * 2)
        wave_y = height - wave_height - max(8, int(height * 0.03))
        panel_y = wave_y - max(8, int(height * 0.015))
        panel_height = wave_height + (wave_y - panel_y) * 2
        top = max(0, (height - size) // 2 - int(height * 0.04))
        filters = [
            "[0:v]split=2[bgsrc][fgsrc]",
            f"[bgsrc]scale={width}:{height}:force_original_aspect_ratio=increase,"
            f"crop={width}:{height},gblur=sigma=32,eq=brightness=-0.10:saturation=0.85,setsar=1[bg]",
            f"[fgsrc]scale={size}:{size}:force_original_aspect_ratio=increase,"
            f"crop={size}:{size},setsar=1[fg]",
            f"[bg][fg]overlay=(W-w)/2:{top}:format=auto[base]",
            f"[base]drawbox=x=0:y={panel_y}:w=iw:h={panel_height}:color=black@0.30:t=fill[panel]",
            f"[2:v]scale={width}:{wave_height},setsar=1,format=rgba,colorchannelmixer=aa=0.85[wave]",
            f"[panel][wave]overlay=0:{wave_y}:format=auto,format=yuv420p[v]",
        ]
        return ";".join(filters)

    size = max(16, int(height * 0.72) // 2 * 2)
    filters = [
        "[0:v]split=2[bgsrc][fgsrc]",
        f"[bgsrc]scale={width}:{height}:force_original_aspect_ratio=increase,"
        f"crop={width}:{height},gblur=sigma=32,eq=brightness=-0.10:saturation=0.85,setsar=1[bg]",
        f"[fgsrc]scale={size}:{size}:force_original_aspect_ratio=increase,"
        f"crop={size}:{size},setsar=1[fg]",
        f"[bg][fg]overlay=(W-w)/2:(H-h)/2:format=auto,format=yuv420p[v]",
    ]
    return ";".join(filters)


def render_waveform(ffmpeg: Path, audio: Path, width: int, height: int, target: Path) -> bool:
    command = [
        str(ffmpeg),
        "-hide_banner",
        "-loglevel",
        "error",
        "-i",
        str(audio),
        "-filter_complex",
        f"showwavespic=s={width}x{height}:colors=0x8FD3FF:scale=sqrt[w]",
        "-map",
        "[w]",
        "-frames:v",
        "1",
        "-y",
        str(target),
    ]
    result = subprocess.run(command, capture_output=True, text=True, encoding="utf-8", errors="replace")
    if result.returncode != 0:
        if result.stderr.strip():
            print(result.stderr.strip(), flush=True)
        return False
    return target.is_file()


def render(
    ffmpeg: Path,
    audio: Path,
    image: Path,
    output: Path,
    style: str,
    size: tuple[int, int],
    fps: int,
    crf: int,
    preset: str,
    audio_bitrate: str,
    overwrite: bool,
    wave_image: Path | None,
) -> int:
    width, height = size
    filter_graph = build_filter(style, width, height, fps)
    command = [
        str(ffmpeg),
        "-hide_banner",
        "-loglevel",
        "error",
        "-nostats",
        "-progress",
        "pipe:1",
        "-loop",
        "1",
        "-framerate",
        str(fps),
        "-i",
        str(image),
        "-i",
        str(audio),
    ]
    if wave_image is not None:
        command += ["-loop", "1", "-framerate", str(fps), "-i", str(wave_image)]
    command += ["-filter_complex", filter_graph, "-map", "[v]", "-map", "1:a:0"]
    command += [
        "-c:v",
        "libx264",
        "-preset",
        preset,
        "-crf",
        str(crf),
        "-pix_fmt",
        "yuv420p",
        "-r",
        str(fps),
        "-c:a",
        "aac",
        "-b:a",
        audio_bitrate,
        "-ar",
        "48000",
        "-movflags",
        "+faststart",
        "-tune",
        "stillimage",
    ]
    duration = probe_duration(ffmpeg, audio)
    if duration:
        command += ["-t", f"{duration:.3f}"]
    else:
        command += ["-shortest"]
    command += ["-y" if overwrite else "-n", str(output)]
    last_percent = -5
    progress_shown = False
    process = subprocess.Popen(
        command,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        text=True,
        encoding="utf-8",
        errors="replace",
    )
    assert process.stdout is not None
    for raw in process.stdout:
        line = raw.strip()
        if line.startswith(("out_time_us=", "out_time_ms=")):
            value = line.split("=", 1)[1]
            if value.isdigit() and duration:
                percent = min(100, int(value) / 1_000_000 / duration * 100)
                if percent >= last_percent + 5:
                    last_percent = int(percent)
                    progress_shown = True
                    print(f"\r[video] {last_percent:3d}%", end="", flush=True)
        elif line.startswith(("progress=", "frame=", "fps=", "bitrate=", "total_size=", "out_time=",
                              "dup_frames=", "drop_frames=", "speed=", "stream_")):
            continue
        elif line:
            print("\n" + line, flush=True)
    code = process.wait()
    if progress_shown:
        if last_percent < 100:
            print("\r[video] 100%", flush=True)
        print(flush=True)
    return code


def write_youtube_metadata(output: Path, audio: Path) -> None:
    sidecar = audio.with_suffix(".json")
    caption = audio.stem
    try:
        data = json.loads(sidecar.read_text(encoding="utf-8-sig"))
        caption = str((data.get("config") or {}).get("caption") or caption)
    except (OSError, ValueError):
        pass
    title = caption.strip()[:90] or output.stem
    words = [word.strip(",.;:()[]") for word in caption.split()]
    tags = [word for word in words if 2 <= len(word) <= 20][:12]
    tags += ["AI music", "instrumental", "background music"]
    metadata = {
        "title": title,
        "description": caption.strip() + "\n\nAI-generated music / video.",
        "tags": tags,
        "categoryId": "10",
        "privacyStatus": "private",
        "madeForKids": False,
    }
    output.with_suffix(".youtube.json").write_text(
        json.dumps(metadata, ensure_ascii=False, indent=2), encoding="utf-8")


def collect_audio(args: argparse.Namespace) -> list[Path]:
    files: list[Path] = []
    if args.input_dir is not None:
        pattern = "**/*" if args.recursive else "*"
        files += [
            path for path in sorted(args.input_dir.glob(pattern))
            if path.is_file() and path.suffix.lower() in AUDIO_EXTS
        ]
    files += [path for path in args.audio if path.suffix.lower() in AUDIO_EXTS]
    unique: dict[Path, None] = {}
    for path in files:
        unique[path.resolve()] = None
    return list(unique)


def main() -> int:
    safe_stdio()
    args = build_parser().parse_args()
    ffmpeg = find_ffmpeg(args.ffmpeg)
    if ffmpeg is None:
        print("[error] ffmpeg not found; pass --ffmpeg or install it in tools/ffmpeg/bin")
        return 2

    if args.from_settings and args.input_dir is None and not args.audio:
        settings_path = Path(os.environ.get("APPDATA", "")) / "AIMusicGenerator" / "settings.json"
        try:
            settings = json.loads(settings_path.read_text(encoding="utf-8"))
            output_dir = settings.get("output_dir")
            if output_dir:
                args.input_dir = Path(output_dir)
                print(f"[settings] output_dir={args.input_dir}", flush=True)
            else:
                print("[warn] output_dir not set in settings.json", flush=True)
        except (OSError, ValueError) as error:
            print(f"[warn] cannot read {settings_path}: {error}", flush=True)

    files = collect_audio(args)
    if not files:
        print("[error] no audio files found")
        return 2

    if args.outdir is not None:
        args.outdir.mkdir(parents=True, exist_ok=True)

    rendered = skipped = failed = 0
    for audio in files:
        if not audio.is_file():
            print(f"[error] missing audio: {audio}", flush=True)
            failed += 1
            continue
        image = find_cover(audio, args.image, DEFAULT_COVER)
        if image is None:
            print(f"[error] no cover image for: {audio}", flush=True)
            failed += 1
            continue
        output_dir = args.outdir if args.outdir is not None else audio.parent
        output = output_dir / (audio.stem + ".mp4")
        if output.exists() and not args.overwrite:
            print(f"[skip] exists: {output}", flush=True)
            skipped += 1
            continue

        print(f"[video] {audio.name} + {image.name} -> {output}", flush=True)
        if args.dry_run:
            print(f"[dry-run] {ffmpeg} ... {output}", flush=True)
            rendered += 1
            continue
        style = args.style
        wave_dir = None
        wave_image = None
        if style == "waveform":
            wave_dir = tempfile.TemporaryDirectory(prefix="aimusic_wave_")
            wave_image = Path(wave_dir.name) / "waveform.png"
            wave_height = max(16, int(args.size[1] * 0.12) // 2 * 2)
            if not render_waveform(ffmpeg, audio, min(args.size[0], 960), wave_height, wave_image):
                print("[warn] waveform render failed; falling back to blur", flush=True)
                style = "blur"
                wave_image = None
        try:
            code = render(
                ffmpeg,
                audio,
                image,
                output,
                style,
                args.size,
                args.fps,
                args.crf,
                args.preset,
                args.audio_bitrate,
                args.overwrite,
                wave_image,
            )
        finally:
            if wave_dir is not None:
                wave_dir.cleanup()
        if code == 0 and output.is_file():
            write_youtube_metadata(output, audio)
            size_mb = output.stat().st_size / (1024 * 1024)
            print(f"[done] {output} ({size_mb:.1f} MB)", flush=True)
            rendered += 1
        else:
            print(f"[error] ffmpeg exit={code}: {audio}", flush=True)
            failed += 1

    print(f"[summary] rendered={rendered} skipped={skipped} failed={failed}", flush=True)
    return 0 if failed == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
