# SPDX-License-Identifier: GPL-3.0-or-later
"""Bounded metadata and blame-sequence evidence; never a test runner."""

from __future__ import annotations

import collections
import contextlib
import datetime
import json
import os
from pathlib import Path
import platform
import re
import selectors
import stat
import subprocess
import sys
import time
from typing import Callable, Mapping
from xml.parsers import expat


CONTEXT_LIMIT = 8192
SUMMARY_LIMIT = 32768
QUERY_LIMIT = 16384
QUERY_SECONDS = 10
INPUT_LIMIT = 16 * 1024 * 1024
ENTRY_LIMIT = 256
SEQUENCE_LIMIT = 4
NAME_LIMIT = 256
SUMMARY_ENTRIES = 32
DIRECTORY = Path("TestResults") / "core-ci-diagnostics"
QUERIES = (
    ("git", "rev-parse", "HEAD"),
    ("git", "rev-parse", "HEAD^{tree}"),
    ("dotnet", "--version"),
    ("dotnet", "--list-runtimes"),
)
RUNNER_KEYS = (
    "RUNNER_OS", "RUNNER_ARCH", "RUNNER_NAME", "ImageOS", "ImageVersion",
    "GITHUB_SHA", "GITHUB_RUN_ID", "GITHUB_RUN_ATTEMPT",
)
HOST_KEYS = ("system", "release", "version", "machine", "python")
REASONS = frozenset((
    "arguments", "platform", "path", "io", "exists", "query", "query_timeout",
    "query_limit", "query_cleanup", "metadata", "output_limit", "entries",
    "sequences", "input_limit", "xml", "symbol",
))


class DiagnosticError(Exception):
    def __init__(self, reason: str) -> None:
        super().__init__(reason if reason in REASONS else "io")


def _require(condition: bool, reason: str) -> None:
    if not condition:
        raise DiagnosticError(reason)


def _regular(info: os.stat_result, *, directory: bool) -> None:
    _require(
        not stat.S_ISLNK(info.st_mode)
        and not ((getattr(info, "st_file_attributes", 0) or 0) & 0x400)
        and (stat.S_ISDIR(info.st_mode) if directory else stat.S_ISREG(info.st_mode)),
        "path",
    )
    if not directory:
        _require(info.st_nlink == 1, "path")


def _identity(info: os.stat_result) -> tuple[int, int]:
    return info.st_dev, info.st_ino


def _parts(path: Path) -> tuple[Path, tuple[str, ...]]:
    _require(path.is_absolute() and ".." not in path.parts, "path")
    absolute = path.absolute()
    _require(
        len(str(absolute)) <= 4096 and ".." not in absolute.parts
        and all("\x00" not in part for part in absolute.parts),
        "path",
    )
    return Path(absolute.anchor), absolute.parts[1:]


def _descriptor_paths_supported() -> bool:
    return os.open in os.supports_dir_fd and hasattr(os, "O_NOFOLLOW")


def _inspect_directory(path: Path) -> tuple[tuple[int, int], ...]:
    anchor, parts = _parts(path)
    current = anchor
    identities = []
    for part in ("", *parts):
        if part:
            current /= part
        info = os.lstat(current)
        _regular(info, directory=True)
        identities.append(_identity(info))
    return tuple(identities)


@contextlib.contextmanager
def _directory(path: Path, *, create: bool = False):
    anchor, parts = _parts(path)
    if not _descriptor_paths_supported():
        # Portable synthetic-file tests only; production CLI requires dir-fd no-follow.
        current = anchor
        _regular(os.lstat(current), directory=True)
        for part in parts:
            current /= part
            if create:
                try:
                    current.mkdir()
                except FileExistsError:
                    pass
            _regular(os.lstat(current), directory=True)
        identities = _inspect_directory(path)
        yield None
        _require(_inspect_directory(path) == identities, "path")
        return

    flags = os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW
    descriptor = os.open(anchor, flags)
    try:
        _regular(os.fstat(descriptor), directory=True)
        for part in parts:
            if create:
                try:
                    os.mkdir(part, mode=0o700, dir_fd=descriptor)
                except FileExistsError:
                    pass
            child = os.open(part, flags, dir_fd=descriptor)
            os.close(descriptor)
            descriptor = child
            _regular(os.fstat(descriptor), directory=True)
        yield descriptor
    finally:
        os.close(descriptor)


@contextlib.contextmanager
def _file(path: Path, *, write: bool = False):
    with _directory(path.parent, create=write) as parent:
        before = None
        if not write:
            before = os.stat(path.name, dir_fd=parent, follow_symlinks=False) if parent is not None else os.lstat(path)
            _regular(before, directory=False)
        flags = (os.O_WRONLY | os.O_CREAT | os.O_EXCL) if write else os.O_RDONLY
        flags |= getattr(os, "O_BINARY", 0) | getattr(os, "O_NOFOLLOW", 0)
        if not write:
            flags |= getattr(os, "O_NONBLOCK", 0)
        descriptor = os.open(path.name if parent is not None else path, flags, 0o600, dir_fd=parent)
        try:
            opened = os.fstat(descriptor)
            _regular(opened, directory=False)
            if before is not None:
                _require(_identity(before) == _identity(opened), "path")
            if parent is None:
                _inspect_directory(path.parent)
                _require(_identity(os.lstat(path)) == _identity(opened), "path")
            yield descriptor
        finally:
            os.close(descriptor)


def _write(path: Path, value: dict, limit: int) -> None:
    data = (json.dumps(value, ensure_ascii=True, separators=(",", ":")) + "\n").encode("ascii")
    _require(len(data) <= limit, "output_limit")
    with _file(path, write=True) as descriptor:
        offset = 0
        while offset < len(data):
            written = os.write(descriptor, data[offset:])
            _require(written > 0, "io")
            offset += written
        os.fsync(descriptor)


def query_metadata(command: tuple[str, ...], workspace: Path) -> str:
    _require(command in QUERIES, "query")
    _require(_descriptor_paths_supported(), "platform")
    process = None
    started = time.monotonic()
    try:
        process = subprocess.Popen(
            command, cwd=workspace, stdin=subprocess.DEVNULL,
            stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, shell=False,
        )
        output = bytearray()
        with selectors.DefaultSelector() as selector:
            selector.register(process.stdout, selectors.EVENT_READ)
            while True:
                remaining = QUERY_SECONDS - (time.monotonic() - started)
                _require(remaining > 0, "query_timeout")
                _require(bool(selector.select(remaining)), "query_timeout")
                chunk = os.read(process.stdout.fileno(), min(4096, QUERY_LIMIT + 1 - len(output)))
                if not chunk:
                    break
                output.extend(chunk)
                _require(len(output) <= QUERY_LIMIT, "query_limit")
        remaining = QUERY_SECONDS - (time.monotonic() - started)
        _require(remaining > 0, "query_timeout")
        _require(process.wait(timeout=remaining) == 0, "query")
        return output.decode("utf-8", errors="strict").strip()
    except subprocess.TimeoutExpired:
        raise DiagnosticError("query_timeout") from None
    except (OSError, UnicodeError, ValueError):
        raise DiagnosticError("query") from None
    finally:
        if process is not None:
            try:
                if process.poll() is None:
                    process.kill()
                    process.wait(timeout=1)
            except (OSError, subprocess.TimeoutExpired):
                raise DiagnosticError("query_cleanup") from None
            finally:
                if process.stdout is not None:
                    process.stdout.close()


def _text(value: object, pattern: str, limit: int = 256) -> str:
    _require(isinstance(value, str) and 0 < len(value) <= limit, "metadata")
    _require(re.fullmatch(pattern, value, flags=re.ASCII) is not None, "metadata")
    return value


def prepare(
    workspace: Path, *, environment: Mapping[str, str] | None = None,
    query: Callable[[tuple[str, ...]], str] | None = None,
    host: Mapping[str, str] | None = None,
) -> dict:
    try:
        environment = os.environ if environment is None else environment
        host = {
            "system": platform.system(), "release": platform.release(),
            "version": platform.version(), "machine": platform.machine(),
            "python": platform.python_version(),
        } if host is None else host
        _require(set(host) == set(HOST_KEYS), "metadata")
        metadata = {key: _text(host[key], r"[A-Za-z0-9 ._()+:/#~;-]+") for key in HOST_KEYS}
        runner = {}
        for key in RUNNER_KEYS:
            value = environment.get(key)
            if value is None:
                runner[key] = None
            elif key == "GITHUB_SHA":
                runner[key] = _text(value, r"[a-f0-9]{40}", 40)
            elif key in ("GITHUB_RUN_ID", "GITHUB_RUN_ATTEMPT"):
                runner[key] = _text(value, r"[0-9]+", 24)
            else:
                runner[key] = _text(value, r"[A-Za-z0-9_. -]+")
        responses = []
        for command in QUERIES:
            value = query_metadata(command, workspace) if query is None else query(command)
            _require(isinstance(value, str) and len(value.encode("utf-8")) <= QUERY_LIMIT, "query_limit")
            responses.append(value.strip())
        version_pattern = r"[0-9]+\.[0-9]+\.[0-9]+(?:-[A-Za-z0-9.-]+)?"
        runtimes = []
        for line in responses[3].splitlines():
            match = re.fullmatch(
                r"(Microsoft\.(?:NETCore|AspNetCore|WindowsDesktop)\.App) ("
                + version_pattern + r") \[[^\r\n\x00]+\]", line,
            )
            _require(match is not None and len(runtimes) < 64, "metadata")
            runtimes.append({"name": match.group(1), "version": _text(match.group(2), version_pattern, 64)})
        _require(bool(runtimes), "metadata")
        context = {
            "schema": "core-ci-context-v1",
            "utc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
            "git_head": _text(responses[0], r"[a-f0-9]{40}", 40),
            "git_tree": _text(responses[1], r"[a-f0-9]{40}", 40),
            "sdk": _text(responses[2], version_pattern, 64),
            "runtimes": runtimes, "host": metadata, "runner": runner,
            "unavailable": [key for key, value in runner.items() if value is None],
            "runtime_scope": "installed_inventory_not_selected_testhost",
        }
        _write(workspace / DIRECTORY / "context.json", context, CONTEXT_LIMIT)
        return context
    except FileExistsError:
        raise DiagnosticError("exists") from None
    except (OSError, UnicodeError, ValueError):
        raise DiagnosticError("io") from None


def _sequence_files(root: Path) -> list[Path]:
    pending = [root]
    files = []
    entries = 0
    while pending:
        directory = pending.pop()
        with _directory(directory) as descriptor:
            with os.scandir(descriptor if descriptor is not None else directory) as iterator:
                for entry in iterator:
                    entries += 1
                    _require(entries <= ENTRY_LIMIT, "entries")
                    path = directory / entry.name
                    info = os.stat(entry.name, dir_fd=descriptor, follow_symlinks=False) if descriptor is not None else os.lstat(path)
                    is_directory = stat.S_ISDIR(info.st_mode)
                    _regular(info, directory=is_directory)
                    if is_directory:
                        _require(len(path.relative_to(root).parts) <= 16, "entries")
                        pending.append(path)
                    elif re.fullmatch(r"(?:Sequence_[A-Za-z0-9_-]+|[A-Za-z0-9_-]+_Sequence)\.xml", entry.name):
                        files.append(path)
                        _require(len(files) <= SEQUENCE_LIMIT, "sequences")
    return sorted(files)


class _SequenceProjection:
    def __init__(self) -> None:
        self.total = 0
        self.incomplete = 0
        self.last: collections.deque[dict] = collections.deque(maxlen=SUMMARY_ENTRIES)
        self.unfinished: collections.deque[dict] = collections.deque(maxlen=SUMMARY_ENTRIES)

    def parser(self):
        parser = expat.ParserCreate()
        depth = 0
        root_seen = False

        def start(name: str, attributes: dict[str, str]) -> None:
            nonlocal depth, root_seen
            if depth == 0:
                _require(not root_seen and name == "TestSequence" and not attributes, "xml")
                root_seen = True
            else:
                _require(
                    depth == 1 and name == "Test"
                    and {"Name", "Completed"} <= set(attributes)
                    and set(attributes) <= {"Name", "Completed", "DisplayName", "Source"},
                    "xml",
                )
                symbol = attributes["Name"]
                _require(
                    len(symbol) <= NAME_LIMIT
                    and re.fullmatch(r"[A-Za-z_][A-Za-z0-9_]*(?:`[0-9]+)?(?:[.+][A-Za-z_][A-Za-z0-9_]*(?:`[0-9]+)?)+", symbol),
                    "symbol",
                )
                _require(attributes["Completed"] in ("True", "False"), "xml")
                completed = attributes["Completed"] == "True"
                self.total += 1
                item = {"ordinal": self.total, "name": symbol, "completed": completed}
                self.last.append(item)
                if not completed:
                    self.incomplete += 1
                    self.unfinished.append(item)
            depth += 1

        def end(name: str) -> None:
            nonlocal depth
            depth -= 1

        def characters(text: str) -> None:
            _require(not text.strip(), "xml")

        def reject(*args):
            raise DiagnosticError("xml")

        parser.StartElementHandler = start
        parser.EndElementHandler = end
        parser.CharacterDataHandler = characters
        parser.StartDoctypeDeclHandler = reject
        parser.EntityDeclHandler = reject
        parser.ExternalEntityRefHandler = reject
        parser.ProcessingInstructionHandler = reject
        parser.SetParamEntityParsing(expat.XML_PARAM_ENTITY_PARSING_NEVER)
        return parser


def finish(workspace: Path) -> dict:
    try:
        directory = workspace / DIRECTORY
        with _directory(directory, create=True):
            pass
        root = directory / "raw"
        try:
            _regular(os.lstat(root), directory=True)
        except FileNotFoundError:
            files = []
        else:
            files = _sequence_files(root)
        projection = _SequenceProjection()
        consumed = 0
        for path in files:
            parser = projection.parser()
            with _file(path) as descriptor:
                _require(os.fstat(descriptor).st_size <= INPUT_LIMIT - consumed, "input_limit")
                while True:
                    chunk = os.read(descriptor, min(65536, INPUT_LIMIT - consumed + 1))
                    consumed += len(chunk)
                    _require(consumed <= INPUT_LIMIT, "input_limit")
                    parser.Parse(chunk, not chunk)
                    if not chunk:
                        break
        entries = list(projection.unfinished if projection.incomplete else projection.last)
        # Include completed context when it fits without displacing incomplete leads.
        if len(entries) < SUMMARY_ENTRIES:
            retained = {entry["ordinal"] for entry in entries}
            entries += [entry for entry in projection.last if entry["ordinal"] not in retained][
                -(SUMMARY_ENTRIES - len(entries)):
            ]
            entries.sort(key=lambda entry: entry["ordinal"])
        summary = {
            "schema": "core-ci-sequence-summary-v1",
            "status": "available" if files else "unavailable",
            "sequences": len(files), "total_tests": projection.total,
            "incomplete_tests": projection.incomplete, "entries": entries,
            "truncated": projection.total > len(entries),
            "interpretation": "diagnostic_leads_only_no_cause_or_test_outcome_inferred",
        }
        _write(directory / "sequence-summary.json", summary, SUMMARY_LIMIT)
        return summary
    except FileExistsError:
        raise DiagnosticError("exists") from None
    except (OSError, UnicodeError, ValueError, expat.ExpatError):
        raise DiagnosticError("io") from None


def main(arguments: list[str] | None = None, *, workspace: Path | None = None) -> int:
    try:
        arguments = sys.argv[1:] if arguments is None else arguments
        _require(len(arguments) == 1 and arguments[0] in ("prepare", "finish"), "arguments")
        _require(sys.platform == "darwin" and _descriptor_paths_supported(), "platform")
        workspace = Path.cwd() if workspace is None else workspace
        if arguments[0] == "prepare":
            prepare(workspace)
        else:
            finish(workspace)
        print("CoreDiagnostics=recorded;Mode=" + arguments[0])
        return 0
    except DiagnosticError as error:
        print("CoreDiagnostics=failed;Reason=" + str(error), file=sys.stderr)
        return 1
    except (OSError, UnicodeError, ValueError):
        print("CoreDiagnostics=failed;Reason=io", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
