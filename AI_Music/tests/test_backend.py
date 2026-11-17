from __future__ import annotations

import json
import sys
import tempfile
import unittest
from pathlib import Path

BASE = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(BASE / "app"))

import generate_backend as backend  # noqa: E402

backend._stdio()


class NormalizeConfigTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.base = {
            "caption": "test caption",
            "lyrics": "[Instrumental]",
            "instrumental": True,
            "thinking": False,
            "bpm": 78,
            "duration": 60,
            "inference_steps": 8,
            "guidance_scale": 1.0,
            "output_dir": self.tmp.name,
        }

    def _config(self, **changes):
        data = dict(self.base)
        data.update(changes)
        path = Path(self.tmp.name) / "cfg.json"
        path.write_text(json.dumps(data), encoding="utf-8")
        return backend.load_config(path)

    def test_bpm_boundaries(self):
        for value in (30, 300):
            self.assertEqual(self._config(bpm=value)["bpm"], value)
        for value in (29, 301):
            with self.assertRaises(ValueError):
                self._config(bpm=value)
        self.assertIsNone(self._config(bpm=None)["bpm"])

    def test_duration_boundaries(self):
        for value in (10, 7200):
            self.assertEqual(self._config(duration=value)["duration"], float(value))
        for value in (9, 7201):
            with self.assertRaises(ValueError):
                self._config(duration=value)

    def test_long_thinking_requires_opt_in(self):
        with self.assertRaises(ValueError):
            self._config(thinking=True, duration=481)
        cfg = self._config(thinking=True, duration=481, allow_long_thinking=True)
        self.assertEqual(cfg["duration"], 481.0)

    def test_caption_must_not_be_blank(self):
        for value in ("", "   "):
            with self.assertRaises(ValueError):
                self._config(caption=value)

    def test_auto_music_fields(self):
        cfg = self._config(keyscale="", timesignature="")
        self.assertEqual(cfg["keyscale"], "")
        self.assertEqual(cfg["timesignature"], "")

    def test_audio_format_and_offload(self):
        self.assertEqual(self._config(audio_format="flac")["audio_format"], "flac")
        self.assertEqual(self._config(offload_mode="save_vram")["offload_mode"], "save_vram")
        with self.assertRaises(ValueError):
            self._config(audio_format="ogg")
        with self.assertRaises(ValueError):
            self._config(offload_mode="never")


class AlbumTests(unittest.TestCase):
    def _album_file(self, out: Path, tracks, shared=None):
        album = {
            "album_title": "Test",
            "artist": "AI",
            "output_dir": str(out),
            "shared": shared or {"caption": "shared", "duration": 30, "thinking": False},
            "tracks": tracks,
        }
        path = out / "album_input.json"
        path.write_text(json.dumps(album), encoding="utf-8")
        return path

    def test_album_records_audio_not_sidecar(self):
        from unittest import mock

        with tempfile.TemporaryDirectory() as tmp:
            out = Path(tmp)
            path = self._album_file(out, [{"title": "One", "prompt": "one", "duration": 30}])

            def fake_job(dit, llm, cfg):
                target = out / "01_One.mp3"
                target.write_bytes(b"audio")
                return 0, target

            with mock.patch.object(backend, "acquire_gpu", return_value=mock.MagicMock()), \
                    mock.patch.object(backend, "load_handlers", return_value=(None, None)), \
                    mock.patch.object(backend, "release_models"), \
                    mock.patch.object(backend, "run_job", side_effect=fake_job):
                code = backend.run_album(path)
            self.assertEqual(code, 0)
            meta = json.loads((out / "album.json").read_text(encoding="utf-8"))
            self.assertEqual(meta["tracks"][0]["file"], str(out / "01_One.mp3"))

    def test_invalid_track_does_not_abort_valid_track(self):
        from unittest import mock

        with tempfile.TemporaryDirectory() as tmp:
            out = Path(tmp)
            path = self._album_file(out, [
                {"title": "Good", "duration": 30},
                {"title": "Bad", "duration": 7201},
            ])
            run_job = mock.MagicMock(return_value=(0, out / "01_Good.mp3"))
            with mock.patch.object(backend, "acquire_gpu", return_value=mock.MagicMock()), \
                    mock.patch.object(backend, "load_handlers", return_value=(None, None)), \
                    mock.patch.object(backend, "release_models"), \
                    mock.patch.object(backend, "run_job", run_job):
                code = backend.run_album(path)
            self.assertEqual(code, 1)
            meta = json.loads((out / "album.json").read_text(encoding="utf-8"))
            self.assertEqual(len(meta["tracks"]), 1)
            self.assertEqual(meta["invalid"][0]["index"], 2)
            self.assertEqual(run_job.call_count, 1)

    def test_all_invalid_skips_model_load(self):
        from unittest import mock

        with tempfile.TemporaryDirectory() as tmp:
            out = Path(tmp)
            path = self._album_file(out, [{"title": "Bad", "duration": 7201}])
            load = mock.MagicMock()
            acquire = mock.MagicMock()
            with mock.patch.object(backend, "acquire_gpu", acquire), \
                    mock.patch.object(backend, "load_handlers", load):
                code = backend.run_album(path)
            self.assertEqual(code, 1)
            load.assert_not_called()
            acquire.assert_not_called()
            meta = json.loads((out / "album.json").read_text(encoding="utf-8"))
            self.assertEqual(meta["tracks"], [])
            self.assertEqual(meta["invalid"][0]["index"], 1)

    def test_mid_run_exception_keeps_completed_tracks(self):
        from unittest import mock

        with tempfile.TemporaryDirectory() as tmp:
            out = Path(tmp)
            path = self._album_file(out, [
                {"title": "One", "duration": 30},
                {"title": "Two", "duration": 30},
            ])
            calls = []

            def fake_job(dit, llm, cfg):
                calls.append(cfg["filename_prefix"])
                if len(calls) == 1:
                    target = out / "01_One.mp3"
                    target.write_bytes(b"audio")
                    return 0, target
                raise RuntimeError("boom")

            with mock.patch.object(backend, "acquire_gpu", return_value=mock.MagicMock()), \
                    mock.patch.object(backend, "load_handlers", return_value=(None, None)), \
                    mock.patch.object(backend, "release_models"), \
                    mock.patch.object(backend, "run_job", side_effect=fake_job):
                code = backend.run_album(path)
            self.assertEqual(code, 1)
            meta = json.loads((out / "album.json").read_text(encoding="utf-8"))
            self.assertEqual(len(meta["tracks"]), 1)
            self.assertEqual(meta["invalid"][0]["index"], 2)


class UniqueTargetTests(unittest.TestCase):
    def test_duplicate_names_increment(self):
        with tempfile.TemporaryDirectory() as tmp:
            out = Path(tmp)
            first = backend.unique_target(out, "ace", 42, "wav")
            first.write_bytes(b"x")
            second = backend.unique_target(out, "ace", 42, "wav")
            self.assertNotEqual(first, second)
            second.write_bytes(b"x")
            third = backend.unique_target(out, "ace", 42, "wav")
            self.assertNotEqual(second, third)
            self.assertEqual(len({first, second, third}), 3)

    def test_extension_is_honoured(self):
        with tempfile.TemporaryDirectory() as tmp:
            target = backend.unique_target(Path(tmp), "ace", 7, "flac")
            self.assertEqual(target.suffix, ".flac")


if __name__ == "__main__":
    unittest.main()
