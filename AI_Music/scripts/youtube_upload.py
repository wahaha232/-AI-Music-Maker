"""Upload a rendered video to YouTube as private (OAuth installed-app flow).

Prerequisites:
  %APPDATA%\\AIMusicGenerator\\youtube_client.json  (Google OAuth client, Desktop app)
  python scripts/youtube_upload.py --auth
  python scripts/youtube_upload.py --video out.mp4 --metadata out.youtube.json
"""

from __future__ import annotations

import argparse
import json
import os
import secrets
import sys
import time
import urllib.parse
import webbrowser
from http.server import BaseHTTPRequestHandler, HTTPServer
from pathlib import Path

import requests

APP_DIR = Path(os.environ.get("APPDATA", str(Path.home()))) / "AIMusicGenerator"
CLIENT_FILE = APP_DIR / "youtube_client.json"
TOKEN_FILE = APP_DIR / "youtube_token.json"
SCOPES = ("https://www.googleapis.com/auth/youtube.upload "
          "https://www.googleapis.com/auth/youtube.force-ssl")
TOKEN_URL = "https://oauth2.googleapis.com/token"
AUTH_URL = "https://accounts.google.com/o/oauth2/v2/auth"
UPLOAD_URL = ("https://www.googleapis.com/upload/youtube/v3/videos"
              "?uploadType=resumable&part=snippet,status")


def load_client() -> dict:
    if not CLIENT_FILE.is_file():
        raise SystemExit(f"missing {CLIENT_FILE}")
    data = json.loads(CLIENT_FILE.read_text(encoding="utf-8"))
    return data.get("installed") or data.get("web") or data


def load_token() -> dict | None:
    if not TOKEN_FILE.is_file():
        return None
    try:
        return json.loads(TOKEN_FILE.read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return None


def save_token(token: dict) -> None:
    APP_DIR.mkdir(parents=True, exist_ok=True)
    TOKEN_FILE.write_text(json.dumps(token, ensure_ascii=False, indent=2),
                          encoding="utf-8")


def authorize() -> int:
    client = load_client()
    result = {}

    class Handler(BaseHTTPRequestHandler):
        def do_GET(self):
            query = urllib.parse.urlparse(self.path).query
            params = urllib.parse.parse_qs(query)
            result["code"] = (params.get("code") or [""])[0]
            self.send_response(200)
            self.end_headers()
            self.wfile.write("授權完成，可以關閉這個視窗。".encode("utf-8"))

        def log_message(self, fmt, *args):
            pass

    server = HTTPServer(("127.0.0.1", 0), Handler)
    port = server.server_port
    redirect = f"http://127.0.0.1:{port}/"
    state = secrets.token_urlsafe(16)
    query = urllib.parse.urlencode({
        "client_id": client["client_id"],
        "redirect_uri": redirect,
        "response_type": "code",
        "scope": SCOPES,
        "access_type": "offline",
        "prompt": "consent",
        "state": state,
    })
    print("Opening browser for Google authorization...")
    webbrowser.open(AUTH_URL + "?" + query)
    server.handle_request()
    code = result.get("code")
    if not code:
        print("[error] no authorization code")
        return 1
    response = requests.post(TOKEN_URL, data={
        "code": code,
        "client_id": client["client_id"],
        "client_secret": client["client_secret"],
        "redirect_uri": redirect,
        "grant_type": "authorization_code",
    }, timeout=60)
    if response.status_code != 200:
        print("[error] token exchange:", response.text)
        return 1
    token = response.json()
    token["expires_at"] = time.time() + int(token.get("expires_in", 3600))
    save_token(token)
    print(f"[ok] token saved to {TOKEN_FILE}")
    return 0


def access_token() -> str:
    client = load_client()
    token = load_token()
    if not token:
        raise SystemExit("not authorized; run --auth first")
    if token.get("expires_at", 0) > time.time() + 60:
        return token["access_token"]
    refresh = token.get("refresh_token")
    if not refresh:
        raise SystemExit("token expired and no refresh_token; run --auth again")
    response = requests.post(TOKEN_URL, data={
        "client_id": client["client_id"],
        "client_secret": client["client_secret"],
        "refresh_token": refresh,
        "grant_type": "refresh_token",
    }, timeout=60)
    if response.status_code != 200:
        raise SystemExit("refresh failed: " + response.text)
    refreshed = response.json()
    token["access_token"] = refreshed["access_token"]
    token["expires_at"] = time.time() + int(refreshed.get("expires_in", 3600))
    save_token(token)
    return token["access_token"]


def load_metadata(video: Path, metadata_path: Path | None) -> dict:
    if metadata_path is None:
        metadata_path = video.with_suffix(".youtube.json")
    if not metadata_path.is_file():
        raise SystemExit(f"missing metadata: {metadata_path}")
    data = json.loads(metadata_path.read_text(encoding="utf-8"))
    title = str(data.get("title") or video.stem)[:100]
    description = str(data.get("description") or "")[:5000]
    tags = [str(tag) for tag in (data.get("tags") or [])][:30]
    return {
        "snippet": {
            "title": title,
            "description": description,
            "tags": tags,
            "categoryId": str(data.get("categoryId") or "10"),
        },
        "status": {
            "privacyStatus": str(data.get("privacyStatus") or "private"),
            "madeForKids": bool(data.get("madeForKids", False)),
        },
    }


def upload(video: Path, metadata: dict, thumbnail: Path | None) -> int:
    token = access_token()
    size = video.stat().st_size
    headers = {
        "Authorization": "Bearer " + token,
        "Content-Type": "application/json; charset=UTF-8",
        "X-Upload-Content-Type": "video/mp4",
        "X-Upload-Content-Length": str(size),
    }
    start = requests.post(UPLOAD_URL, headers=headers,
                          data=json.dumps(metadata), timeout=60)
    if start.status_code != 200:
        print("[error] upload init:", start.status_code, start.text)
        return 1
    location = start.headers.get("Location")
    if not location:
        print("[error] no resumable Location header")
        return 1
    with open(video, "rb") as handle:
        response = requests.put(location, data=handle, headers={
            "Content-Type": "video/mp4",
            "Content-Length": str(size),
        }, timeout=1800)
    if response.status_code not in (200, 201):
        print("[error] upload:", response.status_code, response.text)
        return 1
    result = response.json()
    video_id = result.get("id")
    print(f"[ok] uploaded https://youtu.be/{video_id}")
    if thumbnail is not None and thumbnail.is_file() and video_id:
        with open(thumbnail, "rb") as handle:
            response = requests.post(
                "https://www.googleapis.com/upload/youtube/v3/thumbnails/set",
                params={"videoId": video_id},
                headers={"Authorization": "Bearer " + token},
                files={"image": (thumbnail.name, handle, "image/png")},
                timeout=120)
        print("[thumb]", response.status_code)
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--auth", action="store_true")
    parser.add_argument("--video", type=Path)
    parser.add_argument("--metadata", type=Path)
    parser.add_argument("--thumbnail", type=Path)
    parser.add_argument("--privacy", choices=("private", "unlisted", "public"))
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()
    if args.auth:
        return authorize()
    if args.video is None:
        parser.error("--video is required")
    if not args.video.is_file():
        raise SystemExit(f"missing video: {args.video}")
    metadata = load_metadata(args.video, args.metadata)
    if args.privacy:
        metadata["status"]["privacyStatus"] = args.privacy
    if args.dry_run:
        print(json.dumps(metadata, ensure_ascii=False, indent=2))
        print(f"[dry-run] video={args.video} bytes={args.video.stat().st_size}")
        return 0
    return upload(args.video, metadata, args.thumbnail)


if __name__ == "__main__":
    sys.exit(main())
