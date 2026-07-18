"""Contracts for incremental proof-process output capture."""

import sys
import time

import pytest

import bounded_process


def _run(script: str, *, timeout=10, stdout_limit=1024, stderr_limit=1024):
    return bounded_process.run_bounded(
        [sys.executable, "-c", script],
        cwd=None,
        env=None,
        timeout_seconds=timeout,
        stdout_limit=stdout_limit,
        stderr_limit=stderr_limit,
    )


def test_bounded_process_preserves_both_streams():
    completed = _run(
        "import sys;"
        "sys.stdout.buffer.write(b'out');"
        "sys.stderr.buffer.write(b'err')"
    )
    assert completed.returncode == 0
    assert completed.stdout == b"out"
    assert completed.stderr == b"err"


@pytest.mark.parametrize("stream", ("stdout", "stderr"))
def test_bounded_process_terminates_on_stream_overflow(stream):
    script = (
        "import sys,time;"
        f"target=sys.{stream}.buffer;"
        "target.write(b'x'*1048576);target.flush();time.sleep(30)"
    )
    started = time.monotonic()
    with pytest.raises(
        bounded_process.ProcessOutputLimitError,
        match=stream,
    ):
        _run(script, stdout_limit=1024, stderr_limit=1024)
    assert time.monotonic() - started < 15


def test_bounded_process_terminates_on_timeout():
    started = time.monotonic()
    with pytest.raises(
        bounded_process.ProcessTimeoutError,
        match="wall-clock timeout",
    ):
        _run("import time;time.sleep(30)", timeout=0.1)
    assert time.monotonic() - started < 15
