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

import os
import shutil
import subprocess
import sys
import threading
import time
from contextlib import contextmanager
from contextvars import ContextVar
from pathlib import Path
from typing import Optional

MAX_VERSION_TEXT_LEN = 4096
_OUTPUT_READ_CHARS = 8192
_CLEANUP_TIMEOUT_SECONDS = 0.5

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
    except (OSError, ValueError):
        pass


def _cleanup_bounded_process(process, streams, readers) -> None:
    """Bound teardown when a process or an inherited pipe refuses to finish."""
    if process.poll() is None:
        try:
            process.kill()
        except OSError:
            pass
    try:
        process.wait(timeout=_CLEANUP_TIMEOUT_SECONDS)
    except subprocess.TimeoutExpired:
        pass

    for stream in streams:
        _force_close_pipe(stream)

    deadline = time.monotonic() + _CLEANUP_TIMEOUT_SECONDS
    for reader in readers:
        remaining = deadline - time.monotonic()
        if remaining <= 0:
            break
        reader.join(remaining)


def _run_cli_bounded(
        cmd: list[str], timeout: int, max_chars: int) -> subprocess.CompletedProcess:
    """Run a command with concurrent, bounded decoded stdout/stderr capture."""
    process = subprocess.Popen(
        cmd,
        stdin=subprocess.DEVNULL,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True,
        encoding="utf-8",
    )
    # Popen creates these streams whenever PIPE is requested.  The explicit
    # guard keeps the reader contract clear to type checkers and future edits.
    if process.stdout is None or process.stderr is None:
        raise OSError("Failed to create backend output pipes")

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
    except BaseException:
        _cleanup_bounded_process(
            process,
            (process.stdout, process.stderr),
            readers,
        )
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
    1. FEBUILDERGBA_CLI env var (explicit path)
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
            exe_path = pkg_dir / "FEBuilderGBA.CLI" / "bin" / config / "net9.0" / rid / "publish"
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
        for arch in ["net9.0", "net9.0-windows"]:
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
    except FileNotFoundError:
        raise RuntimeError(
            f"Failed to run: {' '.join(cmd)}\n"
            "Is .NET 9.0 SDK installed? https://dotnet.microsoft.com/download"
        )
    except subprocess.TimeoutExpired:
        raise RuntimeError(
            f"Command timed out after {timeout}s: {' '.join(cmd)}"
        )
    except OSError as e:
        raise RuntimeError(
            f"Failed to run {' '.join(cmd)}: {e}"
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
    (Click included).
    """
    if not isinstance(text, str) or not text or not snapshot_path:
        return text
    if snapshot_path == real_path or snapshot_path not in text:
        return text
    sanitized = text.replace(snapshot_path, real_path)
    if isinstance(text, _BoundedOutput):
        return _BoundedOutput(sanitized, text.original_length, text.truncated)
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
            "error": str(e),
        }
