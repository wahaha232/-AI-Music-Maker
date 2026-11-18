from __future__ import annotations

import sys
import unittest
from pathlib import Path

BASE = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(BASE / "scripts"))

import longform_generate as longform  # noqa: E402


def make_album(count: int, seed: int | None = 100) -> tuple[dict, int]:
    base = {"caption": "test longform caption", "duration": 60}
    if seed is not None:
        base["seed"] = seed
    return longform.build_album(base, count * 175.0, 180.0, 5.0,
                                Path("unused"))


class SplitBatchTests(unittest.TestCase):
    def test_splits_into_groups_of_three(self):
        album, count = make_album(7)
        self.assertEqual(count, 7)
        batches = longform.split_batches(album, 3)
        self.assertEqual([len(group) for group in batches], [3, 3, 1])

    def test_zero_keeps_single_batch(self):
        album, count = make_album(5)
        self.assertEqual([len(longform.split_batches(album, 0)[0])], [count])

    def test_large_size_keeps_single_batch(self):
        album, _ = make_album(5)
        self.assertEqual(len(longform.split_batches(album, 10)), 1)


class SeedTests(unittest.TestCase):
    def test_each_chunk_gets_next_seed(self):
        album, count = make_album(4, seed=500)
        seeds = [track["seed"] for track in album["tracks"]]
        self.assertEqual(seeds, [500, 501, 502, 503])
        self.assertEqual(len(seeds), count)

    def test_random_seed_is_not_forced(self):
        album, _ = make_album(3, seed=-1)
        self.assertTrue(all("seed" not in track for track in album["tracks"]))

    def test_prompt_stays_seamless(self):
        album, count = make_album(3)
        self.assertIn("intro", album["tracks"][0]["prompt"])
        self.assertIn("outro", album["tracks"][-1]["prompt"])
        self.assertIn("seamless continuation", album["tracks"][1]["prompt"])


if __name__ == "__main__":
    unittest.main()
