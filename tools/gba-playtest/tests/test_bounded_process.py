"""Contracts for incremental proof-process output capture."""

import ctypes
import os
from pathlib import Path
import sys
import time
from ctypes import wintypes

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


def test_windows_worker_waits_for_release_gate():
    worker = Path(bounded_process.__file__).with_name(
        "bounded_process_worker.py"
    )
    process = bounded_process.subprocess.Popen(
        [sys.executable, str(worker), sys.executable, "-c", "print('ran')"],
        stdin=bounded_process.subprocess.PIPE,
        stdout=bounded_process.subprocess.PIPE,
        stderr=bounded_process.subprocess.PIPE,
    )
    time.sleep(0.1)
    assert process.poll() is None
    process.stdin.write(b"\x01")
    process.stdin.close()
    process.stdin = None
    stdout, stderr = process.communicate(timeout=5)
    assert process.returncode == 0
    assert stdout.replace(b"\r\n", b"\n") == b"ran\n"
    assert stderr == b""


@pytest.mark.parametrize("stream", ("stdout", "stderr"))
def test_bounded_process_terminates_on_stream_overflow(stream):
    script = (
        "import sys,time;"
        f"target=sys.{stream}.buffer;"
        "target.write(b'x'*1025);target.flush();time.sleep(30)"
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


def _process_is_alive(pid: int) -> bool:
    if os.name != "nt":
        try:
            os.kill(pid, 0)
            return True
        except ProcessLookupError:
            return False
    kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
    kernel32.OpenProcess.argtypes = [
        wintypes.DWORD,
        wintypes.BOOL,
        wintypes.DWORD,
    ]
    kernel32.OpenProcess.restype = wintypes.HANDLE
    kernel32.GetExitCodeProcess.argtypes = [
        wintypes.HANDLE,
        ctypes.POINTER(wintypes.DWORD),
    ]
    kernel32.GetExitCodeProcess.restype = wintypes.BOOL
    kernel32.CloseHandle.argtypes = [wintypes.HANDLE]
    kernel32.CloseHandle.restype = wintypes.BOOL
    handle = kernel32.OpenProcess(0x1000, False, pid)
    if not handle:
        return False
    try:
        code = wintypes.DWORD()
        if not kernel32.GetExitCodeProcess(handle, ctypes.byref(code)):
            return False
        return code.value == 259
    finally:
        kernel32.CloseHandle(handle)


def test_bounded_process_terminates_descendant_tree(tmp_path):
    pid_file = tmp_path / "grandchild.pid"
    child_script = (
        "import subprocess,sys,time;"
        f"p=subprocess.Popen([sys.executable,'-c','import time;time.sleep(30)']);"
        f"open({str(pid_file)!r},'w').write(str(p.pid));"
        "sys.stdout.buffer.write(b'x'*1048576);"
        "sys.stdout.buffer.flush();time.sleep(30)"
    )
    with pytest.raises(bounded_process.ProcessOutputLimitError):
        _run(child_script, stdout_limit=1024)
    grandchild_pid = int(Path(pid_file).read_text(encoding="utf-8"))
    for _ in range(100):
        if not _process_is_alive(grandchild_pid):
            break
        time.sleep(0.01)
    assert _process_is_alive(grandchild_pid) is False
