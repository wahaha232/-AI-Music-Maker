from __future__ import annotations

import sys
import unittest
from pathlib import Path

BASE = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(BASE / "scripts"))

import music_to_video as video  # noqa: E402


class VideoStyleTests(unittest.TestCase):
    def test_static_cover_has_no_motion(self):
        graph = video.build_filter("cover", 1920, 1080, 24)
        self.assertIn("crop=1920:1080", graph)
        self.assertNotIn("zoompan", graph)

    def test_kenburns_uses_zoompan_with_fps(self):
        graph = video.build_filter("kenburns", 1280, 720, 24)
        self.assertIn("zoompan=", graph)
        self.assertIn("s=1280x720:fps=24", graph)
        self.assertNotIn("noise", graph)

    def test_ambient_adds_grain_and_vignette(self):
        graph = video.build_filter("ambient", 1280, 720, 24)
        self.assertIn("zoompan=", graph)
        self.assertIn("noise=alls=3", graph)
        self.assertIn("vignette", graph)

    def test_blur_and_waveform_unchanged(self):
        self.assertIn("gblur", video.build_filter("blur", 1920, 1080))
        waveform = video.build_filter("waveform", 1920, 1080)
        self.assertIn("[2:v]", waveform)
        self.assertIn("gblur", waveform)


if __name__ == "__main__":
    unittest.main()
