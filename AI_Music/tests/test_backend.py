from __future__ import annotations

import json
import sys
import tempfile
import unittest
from pathlib import Path

BASE = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(BASE / "app"))

import generate_backend as backend  # noqa: E402


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
        for value in (20, 300):
            self.assertEqual(self._config(bpm=value)["bpm"], value)
        for value in (19, 301):
            with self.assertRaises(ValueError):
                self._config(bpm=value)
        self.assertIsNone(self._config(bpm=None)["bpm"])

    def test_duration_boundaries(self):
        for value in (5, 600):
            self.assertEqual(self._config(duration=value)["duration"], float(value))
        for value in (4, 601):
            with self.assertRaises(ValueError):
                self._config(duration=value)

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
