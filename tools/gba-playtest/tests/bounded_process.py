"""Incremental, bounded subprocess capture for native proof drivers."""

from __future__ import annotations

import ctypes
import os
import signal
import subprocess
import sys
import threading
import time
from ctypes import wintypes
from pathlib import Path


PIPE_CHUNK_BYTES = 64 * 1024
TERMINATION_TIMEOUT_SECONDS = 5.0
DRAIN_JOIN_TIMEOUT_SECONDS = 5.0


class BoundedProcessError(RuntimeError):
    """Base class for stable proof-process failures."""


class ProcessOutputLimitError(BoundedProcessError):
    """A child exceeded its stdout or stderr capture bound."""


class ProcessTimeoutError(BoundedProcessError):
    """A child exceeded its wall-clock timeout."""


class ProcessTerminationError(BoundedProcessError):
    """A child or its redirected pipes resisted bounded cleanup."""


class _JobBasicLimitInformation(ctypes.Structure):
    _fields_ = [
        ("PerProcessUserTimeLimit", ctypes.c_longlong),
        ("PerJobUserTimeLimit", ctypes.c_longlong),
        ("LimitFlags", wintypes.DWORD),
        ("MinimumWorkingSetSize", ctypes.c_size_t),
        ("MaximumWorkingSetSize", ctypes.c_size_t),
        ("ActiveProcessLimit", wintypes.DWORD),
        ("Affinity", ctypes.c_size_t),
        ("PriorityClass", wintypes.DWORD),
        ("SchedulingClass", wintypes.DWORD),
    ]


class _JobIoCounters(ctypes.Structure):
    _fields_ = [
        ("ReadOperationCount", ctypes.c_ulonglong),
        ("WriteOperationCount", ctypes.c_ulonglong),
        ("OtherOperationCount", ctypes.c_ulonglong),
        ("ReadTransferCount", ctypes.c_ulonglong),
        ("WriteTransferCount", ctypes.c_ulonglong),
        ("OtherTransferCount", ctypes.c_ulonglong),
    ]


class _JobExtendedLimitInformation(ctypes.Structure):
    _fields_ = [
        ("BasicLimitInformation", _JobBasicLimitInformation),
        ("IoInfo", _JobIoCounters),
        ("ProcessMemoryLimit", ctypes.c_size_t),
        ("JobMemoryLimit", ctypes.c_size_t),
        ("PeakProcessMemoryUsed", ctypes.c_size_t),
        ("PeakJobMemoryUsed", ctypes.c_size_t),
    ]


class _WindowsJob:
    JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000
    JOB_OBJECT_EXTENDED_LIMIT_INFORMATION_CLASS = 9

    def __init__(self, process: subprocess.Popen):
        kernel32 = ctypes.WinDLL(
            "kernel32",
            use_last_error=True,
        )
        kernel32.CreateJobObjectW.argtypes = [
            ctypes.c_void_p,
            wintypes.LPCWSTR,
        ]
        kernel32.CreateJobObjectW.restype = wintypes.HANDLE
        kernel32.SetInformationJobObject.argtypes = [
            wintypes.HANDLE,
            ctypes.c_int,
            ctypes.c_void_p,
            wintypes.DWORD,
        ]
        kernel32.SetInformationJobObject.restype = wintypes.BOOL
        kernel32.AssignProcessToJobObject.argtypes = [
            wintypes.HANDLE,
            wintypes.HANDLE,
        ]
        kernel32.AssignProcessToJobObject.restype = wintypes.BOOL
        kernel32.TerminateJobObject.argtypes = [
            wintypes.HANDLE,
            wintypes.UINT,
        ]
        kernel32.TerminateJobObject.restype = wintypes.BOOL
        kernel32.CloseHandle.argtypes = [wintypes.HANDLE]
        kernel32.CloseHandle.restype = wintypes.BOOL

        self._kernel32 = kernel32
        self._handle = kernel32.CreateJobObjectW(None, None)
        if not self._handle:
            raise ctypes.WinError(ctypes.get_last_error())
        try:
            limits = _JobExtendedLimitInformation()
            limits.BasicLimitInformation.LimitFlags = (
                self.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
            )
            if not kernel32.SetInformationJobObject(
                self._handle,
                self.JOB_OBJECT_EXTENDED_LIMIT_INFORMATION_CLASS,
                ctypes.byref(limits),
                ctypes.sizeof(limits),
            ):
                raise ctypes.WinError(ctypes.get_last_error())
            if not kernel32.AssignProcessToJobObject(
                self._handle,
                wintypes.HANDLE(process._handle),
            ):
                raise ctypes.WinError(ctypes.get_last_error())
        except BaseException:
            self.close()
            raise

    def terminate(self) -> None:
        if self._handle:
            self._kernel32.TerminateJobObject(self._handle, 1)

    def close(self) -> None:
        if self._handle:
            self._kernel32.CloseHandle(self._handle)
            self._handle = None


class _ProcessGroup:
    def __init__(self, process: subprocess.Popen):
        self._process = process

    def terminate(self) -> None:
        try:
            os.killpg(self._process.pid, signal.SIGKILL)
        except (ProcessLookupError, PermissionError):
            pass

    def close(self) -> None:
        pass


class _Capture:
    def __init__(self, limit: int):
        self.limit = limit
        self.data = bytearray()
        self.exceeded = False

    def append(self, chunk: bytes) -> None:
        remaining = max(0, self.limit - len(self.data))
        if remaining:
            self.data.extend(chunk[:remaining])
        if len(chunk) > remaining:
            self.exceeded = True


def _drain(pipe, capture: _Capture, stop, errors) -> None:
    try:
        while True:
            chunk = pipe.read(PIPE_CHUNK_BYTES)
            if not chunk:
                return
            capture.append(chunk)
            if capture.exceeded:
                stop.set()
    except (OSError, ValueError) as exc:
        errors.append(exc)
        stop.set()
    finally:
        try:
            pipe.close()
        except (OSError, ValueError):
            pass


def _terminate(process: subprocess.Popen, containment) -> bool:
    containment.terminate()
    try:
        process.wait(timeout=TERMINATION_TIMEOUT_SECONDS)
        return True
    except subprocess.TimeoutExpired:
        pass
    for action in (process.kill, process.terminate):
        if process.poll() is not None:
            return True
        try:
            action()
        except OSError:
            pass
        try:
            process.wait(timeout=TERMINATION_TIMEOUT_SECONDS)
            return True
        except subprocess.TimeoutExpired:
            continue
    return process.poll() is not None


def _close_pipes(process: subprocess.Popen) -> None:
    for pipe in (process.stdout, process.stderr):
        if pipe is None:
            continue
        try:
            pipe.close()
        except (OSError, ValueError):
            pass


def _join_drains(threads) -> bool:
    deadline = time.monotonic() + DRAIN_JOIN_TIMEOUT_SECONDS
    for thread in threads:
        remaining = max(0.0, deadline - time.monotonic())
        thread.join(timeout=remaining)
    return not any(thread.is_alive() for thread in threads)


def run_bounded(
    command,
    *,
    cwd,
    env,
    timeout_seconds: float,
    stdout_limit: int,
    stderr_limit: int,
) -> subprocess.CompletedProcess:
    if timeout_seconds <= 0:
        raise ValueError("timeout_seconds must be positive")
    if stdout_limit <= 0 or stderr_limit <= 0:
        raise ValueError("output limits must be positive")

    try:
        popen_options = {}
        launch_command = list(command)
        stdin = subprocess.DEVNULL
        if os.name == "nt":
            popen_options["creationflags"] = (
                subprocess.CREATE_NEW_PROCESS_GROUP
            )
            launch_command = [
                sys.executable,
                str(
                    Path(__file__).with_name(
                        "bounded_process_worker.py"
                    )
                ),
                *launch_command,
            ]
            stdin = subprocess.PIPE
        else:
            popen_options["start_new_session"] = True
        process = subprocess.Popen(
            launch_command,
            cwd=cwd,
            env=env,
            stdin=stdin,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            **popen_options,
        )
    except OSError as exc:
        raise BoundedProcessError(
            f"process launch failed: {type(exc).__name__}"
        ) from exc

    try:
        containment = (
            _WindowsJob(process)
            if os.name == "nt"
            else _ProcessGroup(process)
        )
    except OSError as exc:
        try:
            process.kill()
            process.wait(timeout=TERMINATION_TIMEOUT_SECONDS)
        except (OSError, subprocess.TimeoutExpired):
            pass
        raise BoundedProcessError(
            f"process containment failed: {type(exc).__name__}"
        ) from exc
    if os.name == "nt":
        try:
            process.stdin.write(b"\x01")
            process.stdin.flush()
            process.stdin.close()
        except (OSError, ValueError) as exc:
            containment.terminate()
            try:
                process.wait(timeout=TERMINATION_TIMEOUT_SECONDS)
            except subprocess.TimeoutExpired:
                pass
            containment.close()
            raise BoundedProcessError(
                f"process release failed: {type(exc).__name__}"
            ) from exc

    stdout = _Capture(stdout_limit)
    stderr = _Capture(stderr_limit)
    stop = threading.Event()
    errors = []
    stdout_thread = threading.Thread(
        target=_drain,
        args=(process.stdout, stdout, stop, errors),
        daemon=True,
    )
    stderr_thread = threading.Thread(
        target=_drain,
        args=(process.stderr, stderr, stop, errors),
        daemon=True,
    )
    drains = (stdout_thread, stderr_thread)
    for thread in drains:
        thread.start()

    deadline = time.monotonic() + timeout_seconds
    timed_out = False
    interrupted = False
    while process.poll() is None:
        remaining = deadline - time.monotonic()
        if remaining <= 0:
            timed_out = True
            break
        if stop.wait(min(0.05, remaining)):
            interrupted = True
            break

    try:
        if timed_out or interrupted:
            if not _terminate(process, containment):
                _close_pipes(process)
                _join_drains(drains)
                raise ProcessTerminationError(
                    "process tree did not terminate after bounded cleanup"
                )

        returncode = process.wait()
        if not _join_drains(drains):
            containment.terminate()
            _close_pipes(process)
            if not _join_drains(drains):
                raise ProcessTerminationError(
                    "process output pipes did not close after bounded cleanup"
                )

        if stdout.exceeded or stderr.exceeded:
            streams = []
            if stdout.exceeded:
                streams.append("stdout")
            if stderr.exceeded:
                streams.append("stderr")
            raise ProcessOutputLimitError(
                f"process {'/'.join(streams)} exceeded the capture limit"
            )
        if timed_out:
            raise ProcessTimeoutError(
                "process exceeded its wall-clock timeout"
            )
        if errors:
            raise BoundedProcessError(
                f"process output capture failed: {type(errors[0]).__name__}"
            )
        return subprocess.CompletedProcess(
            list(command),
            returncode,
            bytes(stdout.data),
            bytes(stderr.data),
        )
    finally:
        containment.close()
