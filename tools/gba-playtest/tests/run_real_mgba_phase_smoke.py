"""Data-free native phase smoke for the pinned mGBA binding.

The child owns all native calls so a native crash terminates only the child.
The parent accepts one exact, fixed stderr marker stream and never creates an
artifact.
"""

from __future__ import annotations

import faulthandler
import os
from pathlib import Path
import subprocess
import sys
import threading
import time
from typing import Optional, Tuple

sys.dont_write_bytecode = True

TEST_DIR = Path(__file__).resolve().parent
TOOL_DIR = TEST_DIR.parent
if str(TOOL_DIR) not in sys.path:
    sys.path.insert(0, str(TOOL_DIR))

from febuildergba_playtest.mgba_backend import MgbaBackend  # noqa: E402
from synthetic_gba import DEFAULT_MARKER, build_synthetic_rom  # noqa: E402


A_MASK = 1 << 0
MARKER_ADDRESS = 0
MARKER_WIDTH = 8
MAX_CHILD_OUTPUT = 1 << 20
CHILD_TIMEOUT_SECONDS = 180

PHASE_MARKERS = (
    "phase:construct:begin",
    "phase:construct:end",
    "phase:load:begin",
    "phase:load:end",
    "phase:reset:begin",
    "phase:reset:end",
    "phase:read:begin",
    "phase:read:end",
    "phase:set-keys:begin",
    "phase:set-keys:end",
    "phase:frame:begin",
    "phase:frame:end",
    "phase:crash-query:begin",
    "phase:crash-query:end",
    "phase:frame:begin",
    "phase:frame:end",
    "phase:crash-query:begin",
    "phase:crash-query:end",
    "phase:read:begin",
    "phase:read:end",
    "phase:screenshot:begin",
    "phase:screenshot:end",
    "phase:close:begin",
    "phase:close:end",
    "phase:done",
)
EXPECTED_STDERR = ("\n".join(PHASE_MARKERS) + "\n").encode("ascii")


class SmokeFailure(RuntimeError):
    """A stable, data-free parent validation failure."""


def _emit(marker: str) -> None:
    sys.stderr.write(marker + "\n")
    sys.stderr.flush()


def _phase(name: str, operation):
    _emit(f"phase:{name}:begin")
    result = operation()
    _emit(f"phase:{name}:end")
    return result


def _child() -> int:
    faulthandler.enable(file=sys.stderr, all_threads=True)
    rom = build_synthetic_rom()
    backend: Optional[MgbaBackend] = None
    try:
        backend = _phase("construct", lambda: MgbaBackend(want_screenshot=True))
        _phase("load", lambda: backend.load_rom(rom))
        _phase("reset", backend.reset)
        initial = _phase(
            "read",
            lambda: backend.read("wram", MARKER_ADDRESS, MARKER_WIDTH),
        )
        if initial != 0:
            raise SmokeFailure("initial marker mismatch")
        _phase("set-keys", lambda: backend.set_keys(A_MASK))
        for _ in range(2):
            _phase("frame", backend.run_frame)
            crash = _phase("crash-query", backend.crash_message)
            if crash is not None:
                raise SmokeFailure("native crash reported")
        final = _phase(
            "read",
            lambda: backend.read("wram", MARKER_ADDRESS, MARKER_WIDTH),
        )
        if final != DEFAULT_MARKER:
            raise SmokeFailure("held-A marker mismatch")
        png = _phase("screenshot", backend.screenshot_png)
        if not isinstance(png, bytes) or not png.startswith(b"\x89PNG\r\n\x1a\n"):
            raise SmokeFailure("PNG signature mismatch")
        _phase("close", backend.close)
        backend = None
        _emit("phase:done")
        return 0
    except BaseException:
        _emit("phase:child-failure")
        return 1


def _drain(pipe, limit: int, overflow: threading.Event) -> bytes:
    chunks = []
    size = 0
    while True:
        chunk = pipe.read(65536)
        if not chunk:
            break
        size += len(chunk)
        if size <= limit:
            chunks.append(chunk)
        else:
            overflow.set()
    return b"".join(chunks)


def _run_child() -> Tuple[int, bytes, bytes, bool, bool]:
    env = os.environ.copy()
    env["PYTHONFAULTHANDLER"] = "1"
    env["PYTHONDONTWRITEBYTECODE"] = "1"
    env["PYTHONNOUSERSITE"] = "1"
    process = subprocess.Popen(
        [sys.executable, str(Path(__file__).resolve()), "--child"],
        cwd=str(TOOL_DIR),
        env=env,
        stdin=subprocess.DEVNULL,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
    )
    overflow = threading.Event()
    stdout_chunks = []
    stderr_chunks = []

    def drain(pipe, target):
        target.append(_drain(pipe, MAX_CHILD_OUTPUT, overflow))

    stdout_thread = threading.Thread(
        target=drain, args=(process.stdout, stdout_chunks), daemon=True
    )
    stderr_thread = threading.Thread(
        target=drain, args=(process.stderr, stderr_chunks), daemon=True
    )
    stdout_thread.start()
    stderr_thread.start()
    timed_out = False
    deadline = time.monotonic() + CHILD_TIMEOUT_SECONDS
    while process.poll() is None:
        if overflow.is_set():
            process.kill()
            break
        if time.monotonic() >= deadline:
            timed_out = True
            process.kill()
            break
        time.sleep(0.05)
    returncode = process.wait()
    stdout_thread.join()
    stderr_thread.join()
    stdout = stdout_chunks[0] if stdout_chunks else b""
    stderr = stderr_chunks[0] if stderr_chunks else b""
    return returncode, stdout, stderr, timed_out, overflow.is_set()


def _forward_stderr(data: bytes) -> None:
    if not data:
        return
    stream = getattr(sys.stderr, "buffer", None)
    if stream is not None:
        stream.write(data)
        stream.flush()
    else:  # pragma: no cover - ordinary stderr has a buffer
        sys.stderr.write(data.decode("utf-8", errors="replace"))
        sys.stderr.flush()


def _validate_child(
    returncode: int,
    stdout: bytes,
    stderr: bytes,
    timed_out: bool = False,
    overflowed: bool = False,
) -> None:
    _forward_stderr(stderr)
    if overflowed:
        raise SmokeFailure("child output exceeded the bounded capture")
    if len(stdout) > MAX_CHILD_OUTPUT:
        raise SmokeFailure("child stdout exceeded the bounded capture")
    if len(stderr) > MAX_CHILD_OUTPUT:
        raise SmokeFailure("child stderr exceeded the bounded capture")
    if stdout:
        raise SmokeFailure("child wrote to stdout")
    if timed_out:
        raise SmokeFailure("child timed out")
    if returncode < 0:
        raise SmokeFailure(f"child terminated by signal {-returncode}")
    if returncode >= 0x80000000:
        raise SmokeFailure(f"child terminated with signal-like status {returncode}")
    if returncode != 0:
        raise SmokeFailure(f"child exited with status {returncode}")
    if stderr != EXPECTED_STDERR:
        raise SmokeFailure("child phase markers were missing, extra, or out of order")


def main(argv=None) -> int:
    args = list(sys.argv[1:] if argv is None else argv)
    if args == ["--child"]:
        return _child()
    if args:
        sys.stderr.write("usage: run_real_mgba_phase_smoke.py\n")
        return 1
    try:
        result = _run_child()
        _validate_child(*result)
    except OSError:
        sys.stderr.write("phase smoke failed: child launch failure\n")
        sys.stderr.flush()
        return 1
    except SmokeFailure as exc:
        sys.stderr.write(f"phase smoke failed: {exc}\n")
        sys.stderr.flush()
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
