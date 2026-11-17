from __future__ import annotations

import os
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

BASE = Path(__file__).resolve().parents[1]
APP = BASE / "app"
sys.path.insert(0, str(APP))

import generate_backend as backend  # noqa: E402
from gpu_lock import GpuBusy, acquire  # noqa: E402

backend._stdio()


class GpuLockTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.lock_dir = tempfile.mkdtemp(prefix="aimusic_gpu_lock_")
        cls.previous = os.environ.get("AIMUSIC_GPU_LOCK_DIR")
        os.environ["AIMUSIC_GPU_LOCK_DIR"] = cls.lock_dir

    @classmethod
    def tearDownClass(cls):
        if cls.previous is None:
            os.environ.pop("AIMUSIC_GPU_LOCK_DIR", None)
        else:
            os.environ["AIMUSIC_GPU_LOCK_DIR"] = cls.previous
        shutil.rmtree(cls.lock_dir, ignore_errors=True)

    def _hold_lock(self):
        script = (
            "import sys,time;"
            f"sys.path.insert(0, {str(APP)!r});"
            "from gpu_lock import acquire;"
            "lock=acquire('test-holder');"
            "print('locked', flush=True);"
            "time.sleep(30)"
        )
        process = subprocess.Popen(
            [sys.executable, "-c", script],
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            encoding="utf-8",
            errors="replace",
            env=os.environ.copy(),
        )
        line = process.stdout.readline().strip()
        if line != "locked":
            self.fail("lock holder failed: " + process.stderr.read())
        return process

    def _stop(self, process):
        process.kill()
        try:
            process.wait(timeout=10)
        finally:
            for stream in (process.stdout, process.stderr):
                try:
                    stream.close()
                except Exception:
                    pass

    def test_lock_is_cross_process_and_released_on_death(self):
        process = self._hold_lock()
        try:
            with self.assertRaises(GpuBusy):
                acquire("main", wait_seconds=0)
        finally:
            self._stop(process)
        lock = acquire("after-death", wait_seconds=5)
        lock.release()

    def test_oneshot_returns_75_without_loading_handlers(self):
        process = self._hold_lock()
        load = mock.MagicMock()
        try:
            with mock.patch.object(backend, "load_handlers", load):
                code = backend.run_oneshot({"offload_mode": "auto"}, gpu_wait=0)
        finally:
            self._stop(process)
        self.assertEqual(code, 75)
        load.assert_not_called()


if __name__ == "__main__":
    unittest.main()
