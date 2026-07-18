"""Incremental, bounded subprocess capture for native proof drivers."""

from __future__ import annotations

import subprocess
import threading
import time


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


def _terminate(process: subprocess.Popen) -> bool:
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
        process = subprocess.Popen(
            list(command),
            cwd=cwd,
            env=env,
            stdin=subprocess.DEVNULL,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
        )
    except OSError as exc:
        raise BoundedProcessError(
            f"process launch failed: {type(exc).__name__}"
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

    if timed_out or interrupted:
        if not _terminate(process):
            _close_pipes(process)
            _join_drains(drains)
            raise ProcessTerminationError(
                "process did not terminate after bounded cleanup"
            )

    returncode = process.wait()
    if not _join_drains(drains):
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
        raise ProcessTimeoutError("process exceeded its wall-clock timeout")
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
