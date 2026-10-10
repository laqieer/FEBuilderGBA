"""Backend module: wraps the real FEBuilderGBA.CLI executable.

This module finds and invokes the actual FEBuilderGBA.CLI binary.
The CLI harness does NOT reimplement ROM manipulation — it calls
the real .NET application for all operations.

Trust boundary (issue #1942 / PR #1971): the backend executable is treated
as untrusted for the purposes of any ``--rom`` argument.  ``run_cli``
enforces a *seam guard* — while MCP's dynamic scope is active (see
``prebuilt_backend_only``), every ``--rom`` argument (either the
``--rom=<path>`` or two-token ``["--rom", "<path>"]`` form) must name a
path already registered via ``register_rom_snapshot`` (in practice, a
private validated snapshot created by ``core.project.validated_rom_snapshot``
/ ``mutating_rom_snapshot``).  Click retains its historic direct-path
behavior: the guard is completely inert outside MCP's dynamic scope.
"""

import errno
import os
import signal
import shutil
import subprocess
import sys
import threading
import time
from contextlib import contextmanager
from contextvars import ContextVar
from dataclasses import dataclass
from pathlib import Path
from typing import Optional

if os.name == "nt":
    import ctypes
    from ctypes import wintypes

MAX_VERSION_TEXT_LEN = 4096
_OUTPUT_READ_CHARS = 8192
_CLEANUP_TIMEOUT_SECONDS = 0.5
_WINDOWS_CREATE_SUSPENDED = 0x00000004
_PRIVATE_ROM_SNAPSHOT_LABEL = "<private ROM snapshot>"
_ERROR_TEXT_LIMIT = 65536
_CLEANUP_TEXT_LIMIT = 16384
_CLEANUP_CAUSE_LIMIT = 4096
_CLEANUP_CAUSES_TEXT_LIMIT = 16000


class _PosixBoundedProcessLifetime:
    """Own the isolated process group used by one bounded backend call."""

    def __init__(self, process_id: int):
        self.process_id = process_id

    def resume(self) -> None:
        pass

    def terminate(self) -> None:
        try:
            os.killpg(self.process_id, signal.SIGKILL)
        except ProcessLookupError:
            pass

    def close(self) -> None:
        pass

    def finish(self) -> None:
        self.terminate()


if os.name == "nt":
    _TH32CS_SNAPTHREAD = 0x00000004
    _THREAD_SUSPEND_RESUME = 0x0002
    _JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000
    _JOB_OBJECT_EXTENDED_LIMIT_INFORMATION_CLASS = 9
    _INVALID_HANDLE_VALUE = ctypes.c_void_p(-1).value
    _RESUME_THREAD_FAILED = 0xFFFFFFFF

    class _JobObjectBasicLimitInformation(ctypes.Structure):
        _fields_ = [
            ("PerProcessUserTimeLimit", ctypes.c_int64),
            ("PerJobUserTimeLimit", ctypes.c_int64),
            ("LimitFlags", wintypes.DWORD),
            ("MinimumWorkingSetSize", ctypes.c_size_t),
            ("MaximumWorkingSetSize", ctypes.c_size_t),
            ("ActiveProcessLimit", wintypes.DWORD),
            ("Affinity", ctypes.c_size_t),
            ("PriorityClass", wintypes.DWORD),
            ("SchedulingClass", wintypes.DWORD),
        ]

    class _IoCounters(ctypes.Structure):
        _fields_ = [
            ("ReadOperationCount", ctypes.c_uint64),
            ("WriteOperationCount", ctypes.c_uint64),
            ("OtherOperationCount", ctypes.c_uint64),
            ("ReadTransferCount", ctypes.c_uint64),
            ("WriteTransferCount", ctypes.c_uint64),
            ("OtherTransferCount", ctypes.c_uint64),
        ]

    class _JobObjectExtendedLimitInformation(ctypes.Structure):
        _fields_ = [
            ("BasicLimitInformation", _JobObjectBasicLimitInformation),
            ("IoInfo", _IoCounters),
            ("ProcessMemoryLimit", ctypes.c_size_t),
            ("JobMemoryLimit", ctypes.c_size_t),
            ("PeakProcessMemoryUsed", ctypes.c_size_t),
            ("PeakJobMemoryUsed", ctypes.c_size_t),
        ]

    class _ThreadEntry32(ctypes.Structure):
        _fields_ = [
            ("dwSize", wintypes.DWORD),
            ("cntUsage", wintypes.DWORD),
            ("th32ThreadID", wintypes.DWORD),
            ("th32OwnerProcessID", wintypes.DWORD),
            ("tpBasePri", wintypes.LONG),
            ("tpDeltaPri", wintypes.LONG),
            ("dwFlags", wintypes.DWORD),
        ]

    _kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
    _kernel32.CreateJobObjectW.argtypes = [
        ctypes.c_void_p,
        wintypes.LPCWSTR,
    ]
    _kernel32.CreateJobObjectW.restype = wintypes.HANDLE
    _kernel32.SetInformationJobObject.argtypes = [
        wintypes.HANDLE,
        ctypes.c_int,
        ctypes.c_void_p,
        wintypes.DWORD,
    ]
    _kernel32.SetInformationJobObject.restype = wintypes.BOOL
    _kernel32.AssignProcessToJobObject.argtypes = [
        wintypes.HANDLE,
        wintypes.HANDLE,
    ]
    _kernel32.AssignProcessToJobObject.restype = wintypes.BOOL
    _kernel32.TerminateJobObject.argtypes = [
        wintypes.HANDLE,
        wintypes.UINT,
    ]
    _kernel32.TerminateJobObject.restype = wintypes.BOOL
    _kernel32.CreateToolhelp32Snapshot.argtypes = [
        wintypes.DWORD,
        wintypes.DWORD,
    ]
    _kernel32.CreateToolhelp32Snapshot.restype = wintypes.HANDLE
    _kernel32.Thread32First.argtypes = [
        wintypes.HANDLE,
        ctypes.POINTER(_ThreadEntry32),
    ]
    _kernel32.Thread32First.restype = wintypes.BOOL
    _kernel32.Thread32Next.argtypes = [
        wintypes.HANDLE,
        ctypes.POINTER(_ThreadEntry32),
    ]
    _kernel32.Thread32Next.restype = wintypes.BOOL
    _kernel32.OpenThread.argtypes = [
        wintypes.DWORD,
        wintypes.BOOL,
        wintypes.DWORD,
    ]
    _kernel32.OpenThread.restype = wintypes.HANDLE
    _kernel32.ResumeThread.argtypes = [wintypes.HANDLE]
    _kernel32.ResumeThread.restype = wintypes.DWORD
    _kernel32.CloseHandle.argtypes = [wintypes.HANDLE]
    _kernel32.CloseHandle.restype = wintypes.BOOL

    def _windows_error(
            operation: str, error_code: Optional[int] = None) -> OSError:
        if error_code is None:
            error_code = ctypes.get_last_error()
        return ctypes.WinError(
            error_code,
            f"{operation} failed for bounded backend isolation",
        )

    def _close_windows_handle(handle) -> None:
        if handle and not _kernel32.CloseHandle(handle):
            raise _windows_error("CloseHandle")

    class _WindowsBoundedProcessLifetime:
        """Assign a suspended backend to a kill-on-close Windows Job."""

        def __init__(self):
            self._handle = _kernel32.CreateJobObjectW(None, None)
            if not self._handle:
                raise _windows_error("CreateJobObjectW")

            info = _JobObjectExtendedLimitInformation()
            info.BasicLimitInformation.LimitFlags = (
                _JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE)
            if not _kernel32.SetInformationJobObject(
                    self._handle,
                    _JOB_OBJECT_EXTENDED_LIMIT_INFORMATION_CLASS,
                    ctypes.byref(info),
                    ctypes.sizeof(info)):
                error_code = ctypes.get_last_error()
                close_error = None
                try:
                    _close_windows_handle(self._handle)
                except OSError as exc:
                    close_error = exc
                finally:
                    self._handle = None
                error = _windows_error(
                    "SetInformationJobObject", error_code)
                if close_error is not None:
                    raise error from close_error
                raise error

            self._process_id = None

        def assign(self, process) -> None:
            process_handle = wintypes.HANDLE(int(process._handle))
            if not _kernel32.AssignProcessToJobObject(
                    self._handle, process_handle):
                raise _windows_error("AssignProcessToJobObject")
            self._process_id = process.pid

        def resume(self) -> None:
            snapshot = _kernel32.CreateToolhelp32Snapshot(
                _TH32CS_SNAPTHREAD, 0)
            if snapshot == _INVALID_HANDLE_VALUE:
                raise _windows_error("CreateToolhelp32Snapshot")

            resumed = 0
            close_error = None
            try:
                entry = _ThreadEntry32()
                entry.dwSize = ctypes.sizeof(entry)
                has_entry = _kernel32.Thread32First(
                    snapshot, ctypes.byref(entry))
                while has_entry:
                    if entry.th32OwnerProcessID == self._process_id:
                        thread_handle = _kernel32.OpenThread(
                            _THREAD_SUSPEND_RESUME,
                            False,
                            entry.th32ThreadID,
                        )
                        if not thread_handle:
                            raise _windows_error("OpenThread")
                        try:
                            prior_count = _kernel32.ResumeThread(thread_handle)
                            if prior_count == _RESUME_THREAD_FAILED:
                                raise _windows_error("ResumeThread")
                            resumed += 1
                        finally:
                            _close_windows_handle(thread_handle)
                    entry.dwSize = ctypes.sizeof(entry)
                    has_entry = _kernel32.Thread32Next(
                        snapshot, ctypes.byref(entry))
            finally:
                try:
                    _close_windows_handle(snapshot)
                except OSError as exc:
                    close_error = exc

            if close_error is not None:
                raise close_error
            if resumed == 0:
                raise RuntimeError(
                    "Failed to find the suspended bounded backend thread")

        def terminate(self) -> None:
            if self._handle and not _kernel32.TerminateJobObject(
                    self._handle, 1):
                raise _windows_error("TerminateJobObject")

        def close(self) -> None:
            if not self._handle:
                return
            handle = self._handle
            _close_windows_handle(handle)
            self._handle = None

        def finish(self) -> None:
            # KILL_ON_JOB_CLOSE removes successful-call descendants that
            # detached their pipes and would otherwise outlive the backend.
            self.close()

# ``run_cli`` is shared with the Click surface, which must retain its historic
# full-capture behavior.  MCP enables this ContextVar only while a tool handler
# runs, so that its untrusted backend output is bounded at the pipe boundary.
_bounded_capture_limit: ContextVar[Optional[int]] = ContextVar(
    "febuildergba_bounded_capture_limit",
    default=None,
)
_prebuilt_backend_only: ContextVar[bool] = ContextVar(
    "febuildergba_prebuilt_backend_only",
    default=False,
)

# Absolute paths of private ROM snapshots explicitly registered as safe for
# ``run_cli`` to hand to the backend.  Only consulted while
# ``_prebuilt_backend_only`` is active (see ``_enforce_rom_snapshot_gate``);
# Click's direct-path invocations never populate or consult this set.
_registered_rom_snapshots: ContextVar[frozenset] = ContextVar(
    "febuildergba_registered_rom_snapshots",
    default=frozenset(),
)


class _BoundedOutput(str):
    """A captured text prefix whose source length survives string trimming."""

    def __new__(cls, value: str, original_length: int, truncated: bool):
        obj = super().__new__(cls, value)
        obj.original_length = original_length
        obj.truncated = truncated
        return obj

    def strip(self, chars=None):
        # str.strip returns a plain str, so explicitly rebuild the metadata
        # carrier used by all existing core wrappers.
        return type(self)(
            super().strip(chars),
            self.original_length,
            self.truncated,
        )


class _BoundedStreamCapture:
    """Incrementally count decoded text while retaining only its prefix."""

    def __init__(self, limit: int):
        self.limit = limit
        self.parts: list[str] = []
        self.retained_length = 0
        self.original_length = 0
        self.error = None

    def add(self, text: str) -> None:
        self.original_length += len(text)
        remaining = self.limit - self.retained_length
        if remaining > 0:
            prefix = text[:remaining]
            self.parts.append(prefix)
            self.retained_length += len(prefix)

    def value(self) -> _BoundedOutput:
        return _BoundedOutput(
            "".join(self.parts),
            self.original_length,
            self.original_length > self.limit,
        )


@contextmanager
def bounded_capture(max_chars: int):
    """Bound captured stdout/stderr for the dynamic scope of one caller.

    Callers outside this context continue through the legacy
    ``subprocess.run(capture_output=...)`` seam unchanged.
    """
    if isinstance(max_chars, bool) or not isinstance(max_chars, int) or max_chars < 0:
        raise ValueError("max_chars must be a non-negative integer")
    token = _bounded_capture_limit.set(max_chars)
    try:
        yield
    finally:
        _bounded_capture_limit.reset(token)


@contextmanager
def prebuilt_backend_only():
    """Require a prebuilt backend instead of the development ``dotnet run`` path."""
    token = _prebuilt_backend_only.set(True)
    try:
        yield
    finally:
        _prebuilt_backend_only.reset(token)


def is_prebuilt_backend_only() -> bool:
    """Return whether MCP's ``prebuilt_backend_only`` dynamic scope (issue
    #1942 / PR #1971) is active right now.

    ``core.project`` uses this to decide, per ROM-touching wrapper call,
    whether to take the private-snapshot path (inside MCP) or the historic
    direct-path Click behavior (outside MCP) — see
    ``core.project.backend_rom_snapshot`` / ``backend_mutating_rom_snapshot``.
    Always ``False`` for ordinary Click invocations, which never enter this
    scope.
    """
    return _prebuilt_backend_only.get()


@contextmanager
def register_rom_snapshot(path: str):
    """Register *path* as a trusted private ROM snapshot for this scope.

    ``run_cli`` only accepts a ``--rom`` argument (``--rom=<path>`` or the
    two-token ``["--rom", "<path>"]`` form) naming a registered path while
    MCP's ``prebuilt_backend_only`` scope is active (see
    ``_enforce_rom_snapshot_gate``).  Registration is scoped to the dynamic
    extent of this context manager and is thread/task-local (``ContextVar``),
    so it cannot leak into unrelated concurrent calls.

    Wrapper authors should not normally call this directly — use
    ``core.project.validated_rom_snapshot`` / ``mutating_rom_snapshot``,
    which register their snapshot automatically.
    """
    absolute = os.path.abspath(path)
    token = _registered_rom_snapshots.set(_registered_rom_snapshots.get() | {absolute})
    try:
        yield
    finally:
        _registered_rom_snapshots.reset(token)


def _enforce_rom_snapshot_gate(args: list[str]) -> None:
    """Fail closed on any ``--rom`` argument that is not a registered
    private snapshot, while MCP's dynamic scope is active.

    This is the run_cli trust-boundary seam for issue #1942 / PR #1971.  It
    is intentionally not just an argv/flag-name allowlist: it inspects the
    actual path value of every ``--rom`` argument — recognizing both the
    single-token ``--rom=<path>`` form and the two-token ``["--rom",
    "<path>"]`` form — and compares that value against paths explicitly
    registered by ``register_rom_snapshot``. A two-token ``--rom`` with no
    following element, or whose following element is not a non-empty
    string, is rejected as a missing value. An unknown, future, or
    accidentally-unwrapped ``--rom`` argument is rejected here — before
    ``find_febuildergba_cli`` resolves a backend and before any subprocess
    is started.

    Completely inert outside MCP's dynamic scope: Click's historic
    direct-path behavior is unaffected.
    """
    if not _prebuilt_backend_only.get():
        return
    registered = _registered_rom_snapshots.get()
    index = 0
    while index < len(args):
        arg = args[index]
        if not isinstance(arg, str):
            index += 1
            continue
        if arg.startswith("--rom="):
            rom_value = arg[len("--rom="):]
            index += 1
        elif arg == "--rom":
            has_value = (
                index + 1 < len(args)
                and isinstance(args[index + 1], str)
                and args[index + 1] != ""
            )
            if not has_value:
                raise RuntimeError(
                    "Refusing to invoke the backend: --rom requires a "
                    "non-empty value (MCP requires every ROM command to "
                    "consume a validated snapshot)"
                )
            rom_value = args[index + 1]
            index += 2
        else:
            index += 1
            continue
        if os.path.abspath(rom_value) not in registered:
            raise RuntimeError(
                "Refusing to invoke the backend: --rom argument is not a "
                "registered private ROM snapshot (MCP requires every ROM "
                f"command to consume a validated snapshot): {rom_value!r}"
            )


def _registered_snapshot_spellings(cmd: list[str]) -> tuple[str, ...]:
    """Return every active private-snapshot spelling present in *cmd*."""
    registered = _registered_rom_snapshots.get()
    if not registered:
        return ()

    spellings = set(registered)
    index = 0
    while index < len(cmd):
        arg = cmd[index]
        value = None
        if isinstance(arg, str) and arg.startswith("--rom="):
            value = arg.split("=", 1)[1]
        elif (
            arg == "--rom"
            and index + 1 < len(cmd)
            and isinstance(cmd[index + 1], str)
        ):
            value = cmd[index + 1]
            index += 1
        if value and os.path.abspath(value) in registered:
            spellings.add(value)
        index += 1
    return tuple(sorted(spellings, key=len, reverse=True))


def _redact_registered_snapshot_paths(text: str, cmd: list[str]) -> str:
    """Remove active private-snapshot paths from caller-visible errors."""
    for spelling in _registered_snapshot_spellings(cmd):
        text = text.replace(spelling, _PRIVATE_ROM_SNAPSHOT_LABEL)
    return text


@dataclass(frozen=True)
class _CleanupEvidence:
    causes: tuple[str, ...]
    omitted: int

    def text(self) -> str:
        parts = ["Secondary cleanup failures:", *self.causes]
        if self.omitted:
            parts.append(f"{self.omitted} cleanup causes omitted")
        return "\n".join(parts)


@dataclass(frozen=True)
class _BackendErrorEvidence:
    primary: str
    cleanup: Optional[_CleanupEvidence]


class _BoundedCleanupFailure(RuntimeError):
    def __init__(self, evidence: _CleanupEvidence):
        super().__init__(
            "Failed to fully terminate the bounded backend process tree")
        self.evidence = evidence


def _bounded_error_text(text: str, limit: int) -> str:
    marker = "... [truncated]"
    if len(text) <= limit:
        return text
    original_length = getattr(text, "original_length", len(text))
    if limit <= len(marker):
        prefix = text[:max(0, limit)]
    else:
        prefix = text[:limit - len(marker)] + marker
    return _BoundedOutput(prefix, original_length, True)


class _CleanupErrors:
    """Retain bounded descriptions, not an unbounded list of exceptions."""

    def __init__(self, cmd: list[str]):
        self.cmd = cmd
        self.first: Optional[BaseException] = None
        self.causes: list[str] = []
        self.retained_chars = 0
        self.omitted = 0

    def add(self, error: BaseException) -> None:
        if self.first is None:
            self.first = error
        text = _redact_registered_snapshot_paths(
            f"{type(error).__name__}: {error}", self.cmd)
        text = _bounded_error_text(text, _CLEANUP_CAUSE_LIMIT)
        size = len(text) + 1
        if self.retained_chars + size <= _CLEANUP_CAUSES_TEXT_LIMIT:
            self.causes.append(text)
            self.retained_chars += size
        else:
            self.omitted += 1

    def raise_if_failed(self) -> None:
        if self.first is not None:
            raise _BoundedCleanupFailure(
                _CleanupEvidence(tuple(self.causes), self.omitted)
            ) from self.first


def _backend_error_evidence(error: BaseException) -> Optional[_BackendErrorEvidence]:
    evidence = getattr(error, "_febuildergba_error_evidence", None)
    return evidence if isinstance(evidence, _BackendErrorEvidence) else None


def _capture_backend_error(
        error: BaseException, cmd: list[str],
        cleanup: Optional[_CleanupEvidence] = None) -> None:
    # Capture while snapshot registration and relative command spellings still
    # exist. Never mutate the original exception's message/args/traceback.
    previous = _backend_error_evidence(error)
    if cleanup is None:
        if previous is not None:
            cleanup = previous.cleanup
        elif isinstance(error, _BoundedCleanupFailure):
            cleanup = error.evidence
    primary = (
        previous.primary if previous is not None
        else _redact_registered_snapshot_paths(str(error), cmd)
    )
    limit = _ERROR_TEXT_LIMIT - _CLEANUP_TEXT_LIMIT - 1 if cleanup else _ERROR_TEXT_LIMIT
    setattr(
        error, "_febuildergba_error_evidence",
        _BackendErrorEvidence(_bounded_error_text(primary, limit), cleanup))


def _format_backend_error(error: BaseException, limit: int = _ERROR_TEXT_LIMIT) -> str:
    evidence = _backend_error_evidence(error)
    if evidence is None:
        _capture_backend_error(error, [])
        evidence = _backend_error_evidence(error)
    assert evidence is not None
    if evidence.cleanup is None:
        return _bounded_error_text(evidence.primary, limit)
    secondary = evidence.cleanup.text()
    # Reserve secondary space before the MCP field's final prefix cap.
    secondary = _bounded_error_text(secondary, min(_CLEANUP_TEXT_LIMIT, limit // 2))
    primary = _bounded_error_text(evidence.primary, limit - len(secondary) - 1)
    return _BoundedOutput(
        primary + "\n" + secondary,
        getattr(primary, "original_length", len(primary))
        + 1 + getattr(secondary, "original_length", len(secondary)),
        bool(getattr(primary, "truncated", False)
             or getattr(secondary, "truncated", False)),
    )


def _normalized_backend_error(
        message: str, source: BaseException, cmd: list[str]) -> RuntimeError:
    error = RuntimeError(message)
    evidence = _backend_error_evidence(source)
    _capture_backend_error(
        error, cmd, evidence.cleanup if evidence is not None else None)
    return error


def _cleanup_preserving_primary(
        primary: BaseException, process, streams, readers, lifetime,
        cmd: list[str]) -> None:
    try:
        _cleanup_bounded_process(process, streams, readers, lifetime, cmd)
    except _BoundedCleanupFailure as cleanup:
        _capture_backend_error(primary, cmd, cleanup.evidence)
    else:
        _capture_backend_error(primary, cmd)


def _drain_bounded_stream(stream, captured: _BoundedStreamCapture) -> None:
    """Drain one text pipe to EOF without retaining data beyond its prefix."""
    try:
        while True:
            text = stream.read(_OUTPUT_READ_CHARS)
            if not text:
                return
            captured.add(text)
    except (OSError, UnicodeError, ValueError) as exc:
        # Surface a pipe/decoder failure after the parent has bounded cleanup.
        captured.error = exc
    finally:
        try:
            stream.close()
        except (OSError, ValueError):
            pass


def _force_close_pipe(stream) -> None:
    """Close an OS pipe without waiting on a reader's TextIOWrapper lock."""
    try:
        os.close(stream.fileno())
    except OSError as exc:
        if exc.errno != errno.EBADF:
            raise
    except ValueError:
        if not stream.closed:
            raise


def _start_bounded_process(cmd: list[str]):
    """Start one backend in an isolated lifetime container.

    POSIX uses a new session/process group. Windows starts suspended, assigns
    the process to a kill-on-close Job Object, then lets the caller start its
    pipe readers before resuming the primary thread.
    """
    kwargs = {
        "stdin": subprocess.DEVNULL,
        "stdout": subprocess.PIPE,
        "stderr": subprocess.PIPE,
        "text": True,
        "encoding": "utf-8",
    }
    if os.name != "nt":
        process = subprocess.Popen(cmd, start_new_session=True, **kwargs)
        return process, _PosixBoundedProcessLifetime(process.pid)

    lifetime = _WindowsBoundedProcessLifetime()
    process = None
    try:
        process = subprocess.Popen(
            cmd,
            creationflags=_WINDOWS_CREATE_SUSPENDED,
            **kwargs,
        )
        lifetime.assign(process)
        return process, lifetime
    except BaseException as primary:
        streams = tuple(
            stream for stream in (
                getattr(process, "stdout", None),
                getattr(process, "stderr", None),
            ) if stream is not None
        )
        _cleanup_preserving_primary(primary, process, streams, (), lifetime, cmd)
        raise


def _cleanup_bounded_process(
        process, streams, readers, lifetime,
        cmd: Optional[list[str]] = None) -> None:
    """Bound teardown when a process or an inherited pipe refuses to finish."""
    deadline = time.monotonic() + _CLEANUP_TIMEOUT_SECONDS
    cleanup_errors = _CleanupErrors(cmd if cmd is not None else [])
    # Aggregate even unexpected teardown failures so later owned resources
    # still receive cleanup; every failure is raised or reported explicitly.
    try:
        lifetime.terminate()
    except BaseException as exc:
        cleanup_errors.add(exc)

    if process is not None:
        running = True
        try:
            running = process.poll() is None
        except BaseException as exc:
            cleanup_errors.add(exc)
        if running:
            try:
                process.kill()
            except BaseException as exc:
                cleanup_errors.add(exc)
        try:
            process.wait(timeout=max(0.0, deadline - time.monotonic()))
        except BaseException as exc:
            cleanup_errors.add(exc)

    for stream in streams:
        try:
            _force_close_pipe(stream)
        except BaseException as exc:
            cleanup_errors.add(exc)

    for reader in readers:
        try:
            remaining = deadline - time.monotonic()
            if remaining > 0:
                reader.join(remaining)
        except BaseException as exc:
            cleanup_errors.add(exc)
    reader_alive = False
    for reader in readers:
        try:
            reader_alive = reader.is_alive() or reader_alive
        except BaseException as exc:
            cleanup_errors.add(exc)
    if reader_alive:
        cleanup_errors.add(
            RuntimeError("Bounded backend pipe reader did not stop"))

    try:
        lifetime.close()
    except BaseException as exc:
        cleanup_errors.add(exc)

    cleanup_errors.raise_if_failed()


def _run_cli_bounded(
        cmd: list[str], timeout: int, max_chars: int) -> subprocess.CompletedProcess:
    """Run a command with concurrent, bounded decoded stdout/stderr capture."""
    process, lifetime = _start_bounded_process(cmd)
    # Popen creates these streams whenever PIPE is requested.  The explicit
    # guard keeps the reader contract clear to type checkers and future edits.
    if process.stdout is None or process.stderr is None:
        try:
            raise OSError("Failed to create backend output pipes")
        except OSError as primary:
            _cleanup_preserving_primary(
                primary, process,
                tuple(
                    stream for stream in (process.stdout, process.stderr)
                    if stream is not None
                ),
                (), lifetime, cmd)
            raise

    stdout_capture = _BoundedStreamCapture(max_chars)
    stderr_capture = _BoundedStreamCapture(max_chars)
    readers = [
        threading.Thread(
            target=_drain_bounded_stream,
            args=(process.stdout, stdout_capture),
            daemon=True,
        ),
        threading.Thread(
            target=_drain_bounded_stream,
            args=(process.stderr, stderr_capture),
            daemon=True,
        ),
    ]
    deadline = time.monotonic() + timeout
    try:
        for reader in readers:
            reader.start()
        lifetime.resume()

        # Poll rather than waiting for one pipe at a time: both reader threads
        # keep draining while this deadline covers process completion *and* EOF
        # from both pipes (including accidentally inherited pipe handles).
        while process.poll() is None:
            if stdout_capture.error is not None:
                raise stdout_capture.error
            if stderr_capture.error is not None:
                raise stderr_capture.error
            if time.monotonic() >= deadline:
                raise subprocess.TimeoutExpired(cmd, timeout)
            time.sleep(min(0.01, max(0.0, deadline - time.monotonic())))

        while any(reader.is_alive() for reader in readers):
            if stdout_capture.error is not None:
                raise stdout_capture.error
            if stderr_capture.error is not None:
                raise stderr_capture.error
            if time.monotonic() >= deadline:
                raise subprocess.TimeoutExpired(cmd, timeout)
            for reader in readers:
                reader.join(min(0.01, max(0.0, deadline - time.monotonic())))
        if stdout_capture.error is not None:
            raise stdout_capture.error
        if stderr_capture.error is not None:
            raise stderr_capture.error
    except BaseException as primary:
        _cleanup_preserving_primary(
            primary, process, (process.stdout, process.stderr),
            readers, lifetime, cmd)
        raise

    try:
        lifetime.finish()
    except BaseException as primary:
        _capture_backend_error(primary, cmd)
        raise

    return subprocess.CompletedProcess(
        cmd,
        process.returncode,
        stdout=stdout_capture.value(),
        stderr=stderr_capture.value(),
    )


def find_febuildergba_cli() -> list[str]:
    """Find the FEBuilderGBA.CLI executable.

    Search order:
    1. FEBUILDERGBA_CLI_EXE, then FEBUILDERGBA_CLI env var (explicit path)
    2. Published/build apphost or DLL in repo FEBuilderGBA.CLI/bin/
    3. Outside prebuilt-only mode, 'dotnet run' via the project file

    Returns:
        Command list to invoke the CLI (e.g., ["dotnet", "run", ...] or ["/path/to/exe"]).

    Raises:
        RuntimeError: If FEBuilderGBA.CLI cannot be found.
    """
    # 1. Explicit env var (prefer FEBUILDERGBA_CLI_EXE if set)
    env_path = os.environ.get("FEBUILDERGBA_CLI_EXE") or os.environ.get("FEBUILDERGBA_CLI")
    if env_path:
        if os.path.isfile(env_path):
            return [env_path]
        raise RuntimeError(
            f"FEBUILDERGBA_CLI_EXE/FEBUILDERGBA_CLI={env_path} but file does not exist."
        )

    # 2. Walk up from this file to find the repo root containing FEBuilderGBA.CLI/
    pkg_dir = Path(__file__).resolve().parent
    for _ in range(8):
        pkg_dir = pkg_dir.parent
        if (pkg_dir / "FEBuilderGBA.CLI").is_dir():
            break
    else:
        # Fallback: try __file__ without resolve() (handles Cygwin/editable installs)
        pkg_dir = Path(__file__).parent
        for _ in range(8):
            pkg_dir = pkg_dir.parent
            if (pkg_dir / "FEBuilderGBA.CLI").is_dir():
                break

    # Check for published apphost or DLL.
    for config in ["Release", "Debug"]:
        for rid in ["win-x64", "linux-x64", "osx-arm64"]:
            exe_path = pkg_dir / "FEBuilderGBA.CLI" / "bin" / config / "net10.0" / rid / "publish"
            for name in ["FEBuilderGBA.CLI.exe", "FEBuilderGBA.CLI"]:
                candidate = exe_path / name
                if candidate.is_file():
                    return [str(candidate)]
            dll = exe_path / "FEBuilderGBA.CLI.dll"
            if dll.is_file():
                dotnet = shutil.which("dotnet")
                if dotnet:
                    return [dotnet, str(dll)]

    # Check for build apphost or DLL (not published).
    for config in ["Release", "Debug"]:
        for arch in ["net10.0", "net10.0-windows"]:
            exe_dir = pkg_dir / "FEBuilderGBA.CLI" / "bin" / config / arch
            for name in ["FEBuilderGBA.CLI.exe", "FEBuilderGBA.CLI"]:
                candidate = exe_dir / name
                if candidate.is_file():
                    return [str(candidate)]
            dll = exe_dir / "FEBuilderGBA.CLI.dll"
            if dll.is_file():
                dotnet = shutil.which("dotnet")
                if dotnet:
                    return [dotnet, str(dll)]

    if _prebuilt_backend_only.get():
        raise RuntimeError(
            "MCP requires a prebuilt FEBuilderGBA.CLI executable, apphost, "
            "or DLL; dotnet run fallback is disabled."
        )

    # 3. Use 'dotnet run' with the project file
    csproj = pkg_dir / "FEBuilderGBA.CLI" / "FEBuilderGBA.CLI.csproj"
    if csproj.is_file():
        dotnet = shutil.which("dotnet")
        if dotnet:
            return [dotnet, "run", "--project", str(csproj), "--"]

    raise RuntimeError(
        "FEBuilderGBA.CLI not found. Ensure it is built:\n"
        "  dotnet build FEBuilderGBA.CLI/FEBuilderGBA.CLI.csproj\n"
        "Or set FEBUILDERGBA_CLI=/path/to/FEBuilderGBA.CLI executable"
    )


def run_cli(args: list[str], capture: bool = True,
            timeout: int = 300) -> subprocess.CompletedProcess:
    """Run a FEBuilderGBA.CLI command.

    Args:
        args: CLI arguments (e.g., ["--rom", "rom.gba", "--lint"]).
        capture: Whether to capture stdout/stderr.
        timeout: Timeout in seconds.

    Returns:
        CompletedProcess with stdout/stderr.

    Raises:
        RuntimeError: If CLI not found or execution fails.
    """
    _enforce_rom_snapshot_gate(args)
    max_chars = _bounded_capture_limit.get()
    if max_chars is not None and not capture:
        raise RuntimeError(
            "Bounded MCP capture requires capture=True; capture=False is not allowed."
        )

    cmd = find_febuildergba_cli() + args
    try:
        if max_chars is not None and capture:
            return _run_cli_bounded(cmd, timeout, max_chars)
        result = subprocess.run(
            cmd,
            capture_output=capture,
            text=True,
            timeout=timeout,
        )
        return result
    except FileNotFoundError as e:
        command = _redact_registered_snapshot_paths(
            " ".join(cmd), cmd)
        raise _normalized_backend_error(
            f"Failed to run: {command}\n"
            "Is .NET 10.0 SDK installed? https://dotnet.microsoft.com/download",
            e, cmd,
        ) from e
    except subprocess.TimeoutExpired as e:
        command = _redact_registered_snapshot_paths(
            " ".join(cmd), cmd)
        raise _normalized_backend_error(
            f"Command timed out after {timeout}s: {command}", e, cmd,
        ) from e
    except OSError as e:
        command = _redact_registered_snapshot_paths(
            " ".join(cmd), cmd)
        detail = _redact_registered_snapshot_paths(str(e), cmd)
        raise _normalized_backend_error(
            f"Failed to run {command}: {detail}", e, cmd,
        ) from e


def sanitize_snapshot_path(text, snapshot_path: str, real_path: str):
    """Replace a leaked internal *snapshot_path* with *real_path* in backend
    output, preserving ``_BoundedOutput`` metadata (``original_length`` /
    ``truncated``) when present.

    Snapshot paths are internal implementation detail (see
    ``core.project.validated_rom_snapshot`` / ``mutating_rom_snapshot``) and
    must never reach a caller through stdout/stderr/result fields.  This is a
    cheap no-op unless *text* actually contains *snapshot_path*, so it is
    safe to call unconditionally after every snapshot-backed invocation
    (Click included). For bounded MCP output, a longer caller path is capped
    back to the already retained prefix length and marks the value truncated.
    """
    if not isinstance(text, str) or not text or not snapshot_path:
        return text
    if snapshot_path == real_path or snapshot_path not in text:
        return text
    sanitized = text.replace(snapshot_path, real_path)
    if isinstance(text, _BoundedOutput):
        retained_limit = len(text)
        replacement_truncated = len(sanitized) > retained_limit
        return _BoundedOutput(
            sanitized[:retained_limit],
            text.original_length,
            text.truncated or replacement_truncated,
        )
    return sanitized


def successful_output_size(result: subprocess.CompletedProcess,
                           output_path: str) -> int:
    """Return an output file's size only when the backend succeeded."""
    if result.returncode != 0 or not os.path.isfile(output_path):
        return 0
    return os.path.getsize(output_path)


def get_version() -> str:
    """Get FEBuilderGBA version string."""
    result = run_cli(["--version"])
    if result.returncode != 0:
        detail = (result.stderr or result.stdout or "").strip()
        suffix = f": {detail[:4096]}" if detail else ""
        raise RuntimeError(
            f"FEBuilderGBA.CLI version check failed with exit code "
            f"{result.returncode}{suffix}"
        )
    raw_version = result.stdout or ""
    if len(raw_version) > MAX_VERSION_TEXT_LEN:
        raise RuntimeError(
            "FEBuilderGBA.CLI version check output exceeded "
            f"{MAX_VERSION_TEXT_LEN} characters"
        )
    version = raw_version.strip()
    if not version:
        raise RuntimeError("FEBuilderGBA.CLI version check returned no version text")
    return version


def check_backend() -> dict:
    """Check if the FEBuilderGBA.CLI backend is available.

    Returns:
        Dict with status info.
    """
    try:
        cmd = find_febuildergba_cli()
        version = get_version()
        return {
            "available": True,
            "command": cmd,
            "version": version,
        }
    except (RuntimeError, OSError, UnicodeError) as e:
        return {
            "available": False,
            "error": _format_backend_error(e),
        }
