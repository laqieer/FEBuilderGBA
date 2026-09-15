# SPDX-License-Identifier: GPL-3.0-or-later
"""Fixed, read-only issue-2160 observation; importing this module is inert."""

import argparse
import base64
from contextlib import contextmanager
import errno
import gzip
import hashlib
import io
import json
import os
import re
import select
import signal
import stat
import subprocess
import sys
import time
import zlib


EXPECTED_ROOT = (
    "/mnt/c/Projects/laqieer/FEBuilderGBA/TestResults/worktrees/"
    "issue-2160-linux-x11-20260913T075332Z"
)
WSLG_TARGET = "/mnt/wslg/.X11-unix"
CANDIDATES = (
    "/usr/bin/unshare", "/usr/bin/bwrap", "/usr/bin/podman",
    "/usr/bin/docker", "/usr/bin/Xvfb", "/lib/x86_64-linux-gnu/libX11.so.6",
)
SYSCTLS = (
    "/proc/sys/user/max_user_namespaces",
    "/proc/sys/kernel/unprivileged_userns_clone",
    "/proc/sys/kernel/apparmor_restrict_unprivileged_userns",
)
MANUALS = (
    "/usr/share/man/man1/unshare.1.gz",
    "/usr/share/man/man1/bwrap.1.gz",
)
SCHEMA = "issue2160-current-metadata-v1"
INTERFACE_SCHEMA = "issue2160-isolation-interface-v1"
UNSHARE = "/usr/bin/unshare"


class Unavailable(Exception):
    pass


def _facts(value):
    kind = (
        "directory" if stat.S_ISDIR(value.st_mode) else
        "symlink" if stat.S_ISLNK(value.st_mode) else
        "regular" if stat.S_ISREG(value.st_mode) else "other"
    )
    return {
        "kind": kind, "mode": format(stat.S_IMODE(value.st_mode), "04o"),
        "device": value.st_dev, "inode": value.st_ino,
        "uid": value.st_uid, "gid": value.st_gid,
    }


def _unchanged(before, after):
    fields = ("st_dev", "st_ino", "st_mode", "st_uid", "st_gid")
    if any(getattr(before, field) != getattr(after, field) for field in fields):
        raise RuntimeError("metadata target changed during observation")


class MetadataObserver:
    def __init__(self, *, os_api, clock, identity):
        self.os = os_api
        self.clock = clock
        self.identity = identity
        self.started = clock()
        self.deadline = self.started + 3.0
        self.descriptors = []
        self.root = None
        self.options = []
        self.result = {
            "schema": SCHEMA, "status": "failed", "checks": {},
            "paths": [], "candidates": {}, "sysctls": {}, "manuals": {},
            "cleanup_failures": [], "native_attempted": False,
            "primitive_accepted": False, "application_accepted": False,
            "isolation_accepted": False,
        }

    def check_time(self):
        if self.clock() >= self.deadline:
            raise TimeoutError("fixed three-second metadata deadline")

    def close_since(self, mark):
        while len(self.descriptors) > mark:
            descriptor = self.descriptors.pop()
            try:
                self.os.close(descriptor)
            except BaseException as error:
                self.result["cleanup_failures"].append(type(error).__name__)

    def open_checked(self, parent, name, *, directory):
        self.check_time()
        before = self.os.stat(name, dir_fd=parent, follow_symlinks=False)
        valid_type = stat.S_ISDIR if directory else stat.S_ISREG
        if not valid_type(before.st_mode):
            raise Unavailable("not_nofollow_" + ("directory" if directory else "regular"))
        if len(self.descriptors) >= 8:
            raise RuntimeError("metadata descriptor bound")
        flags = self.os.O_RDONLY | self.os.O_NOFOLLOW | self.os.O_CLOEXEC
        flags |= self.os.O_DIRECTORY if directory else self.os.O_NONBLOCK
        descriptor = self.os.open(name, flags, dir_fd=parent)
        self.descriptors.append(descriptor)
        _unchanged(before, self.os.fstat(descriptor))
        self.check_time()
        return descriptor

    def directory(self, parent, name, label):
        descriptor = self.open_checked(parent, name, directory=True)
        value = self.os.fstat(descriptor)
        filesystem = self.os.fstatvfs(descriptor)
        entry = {
            "path": label, **_facts(value),
            "filesystem_flags": filesystem.f_flag,
            "filesystem_read_only": bool(filesystem.f_flag & self.os.ST_RDONLY),
            "effective_write_search": self.os.access(
                ".", self.os.W_OK | self.os.X_OK, dir_fd=descriptor,
                effective_ids=True, follow_symlinks=False,
            ),
        }
        self.result["paths"].append(entry)
        self.check_time()
        return descriptor

    @contextmanager
    def parent(self, path):
        if path not in CANDIDATES + SYSCTLS + MANUALS:
            raise RuntimeError("path is outside the fixed observation allowlist")
        mark = len(self.descriptors)
        try:
            parent = self.root
            parts = path[1:].split("/")
            for part in parts[:-1]:
                parent = self.open_checked(parent, part, directory=True)
            yield parent, parts[-1]
        finally:
            self.close_since(mark)

    def socket_directory(self):
        mark = len(self.descriptors)
        try:
            temporary = self.directory(self.root, "tmp", "/tmp")
            self.check_time()
            before = self.os.stat(".X11-unix", dir_fd=temporary, follow_symlinks=False)
            if stat.S_ISLNK(before.st_mode):
                if before.st_size > 4096:
                    raise RuntimeError("socket symlink text bound")
                target = self.os.readlink(".X11-unix", dir_fd=temporary)
                if len(self.os.fsencode(target)) > 4096:
                    raise RuntimeError("socket symlink text bound")
                self.result["paths"].append({
                    "path": "/tmp/.X11-unix", **_facts(before), "target": target,
                })
                _unchanged(before, self.os.stat(
                    ".X11-unix", dir_fd=temporary, follow_symlinks=False,
                ))
                if target != WSLG_TARGET:
                    raise RuntimeError("unknown socket symlink; no follow or fallback")
                mount = self.directory(self.root, "mnt", "/mnt")
                wslg = self.directory(mount, "wslg", "/mnt/wslg")
                self.directory(wslg, ".X11-unix", WSLG_TARGET)
                _unchanged(before, self.os.stat(
                    ".X11-unix", dir_fd=temporary, follow_symlinks=False,
                ))
                if self.os.readlink(".X11-unix", dir_fd=temporary) != target:
                    raise RuntimeError("socket symlink target changed")
                self.result["observed_socket_path"] = WSLG_TARGET
            elif stat.S_ISDIR(before.st_mode):
                descriptor = self.directory(temporary, ".X11-unix", "/tmp/.X11-unix")
                _unchanged(before, self.os.fstat(descriptor))
                self.result["observed_socket_path"] = "/tmp/.X11-unix"
            else:
                self.result["paths"].append({
                    "path": "/tmp/.X11-unix", **_facts(before),
                })
                raise RuntimeError("socket path is not a directory")
        finally:
            self.close_since(mark)

    def candidate(self, path):
        with self.parent(path) as (parent, name):
            self.check_time()
            before = self.os.stat(name, dir_fd=parent, follow_symlinks=False)
            result = {"status": "observed", **_facts(before)}
            if stat.S_ISREG(before.st_mode):
                result["effective_executable"] = self.os.access(
                    name, self.os.X_OK, dir_fd=parent,
                    effective_ids=True, follow_symlinks=False,
                )
                result["effective_readable"] = self.os.access(
                    name, self.os.R_OK, dir_fd=parent,
                    effective_ids=True, follow_symlinks=False,
                )
                _unchanged(before, self.os.stat(
                    name, dir_fd=parent, follow_symlinks=False,
                ))
            self.check_time()
            return result

    def read_regular(self, path, limit):
        with self.parent(path) as (parent, name):
            descriptor = self.open_checked(parent, name, directory=False)
            before = self.os.fstat(descriptor)
            chunks = []
            used = 0
            while True:
                self.check_time()
                requested = min(4096, limit + 1 - used)
                chunk = self.os.read(descriptor, requested)
                if len(chunk) > requested:
                    raise RuntimeError("invalid metadata read count")
                self.check_time()
                if not chunk:
                    break
                used += len(chunk)
                if used > limit:
                    raise Unavailable("byte_limit")
                chunks.append(chunk)
            _unchanged(before, self.os.fstat(descriptor))
            _unchanged(before, self.os.stat(name, dir_fd=parent, follow_symlinks=False))
            return b"".join(chunks)

    def sysctl(self, path):
        raw = self.read_regular(path, 128)
        if not re.fullmatch(rb"[0-9]+\n?", raw):
            raise Unavailable("not_one_bounded_integer")
        return {"status": "observed", "value": int(raw)}

    def manual(self, path):
        compressed = self.read_regular(path, 16384)
        self.check_time()
        try:
            with gzip.GzipFile(fileobj=io.BytesIO(compressed)) as stream:
                expanded = stream.read(32769)
            if len(expanded) > 32768:
                raise Unavailable("decompressed_byte_limit")
            text = expanded.decode("utf-8", errors="strict").replace(r"\-", "-")
        except TimeoutError:
            raise
        except (OSError, EOFError, UnicodeError, zlib.error):
            raise Unavailable("invalid_compressed_manual") from None
        self.check_time()
        options = set(re.findall(r"--[a-z][a-z0-9-]*", text))
        combined = self.options + sorted(options)
        if len(combined) > 64 or any(len(token) > 64 for token in combined):
            raise Unavailable("documented_option_count_or_length")
        if len(json.dumps(sorted(combined)).encode("utf-8")) > 4096:
            raise Unavailable("documented_option_byte_limit")
        self.options = combined
        return {"status": "observed", "options": sorted(options)}

    def optional(self, action, path):
        try:
            return action(path)
        except TimeoutError:
            raise
        except FileNotFoundError:
            return {"status": "missing"}
        except Unavailable as error:
            return {"status": "unavailable", "reason": str(error)}
        except OSError as error:
            return {"status": "unavailable", "reason": type(error).__name__}

    def check_identity(self):
        expected = {
            "platform": "linux", "uid": 1000, "euid": 1000,
            "python": "/usr/bin/python3", "version": (3, 12, 3),
            "architecture": "x86_64", "cwd": EXPECTED_ROOT,
        }
        self.check_time()
        self.result["checks"] = {
            name: self.identity.get(name) == value for name, value in expected.items()
        }
        if not all(self.result["checks"].values()):
            raise RuntimeError("fixed identity mismatch; observation prohibited")

    def run(self):
        try:
            self.check_identity()
            self.root = self.open_checked(None, "/", directory=True)
            self.socket_directory()
            for label, paths, action in (
                ("candidates", CANDIDATES, self.candidate),
                ("sysctls", SYSCTLS, self.sysctl),
                ("manuals", MANUALS, self.manual),
            ):
                for path in paths:
                    self.check_time()
                    self.result[label][path] = self.optional(action, path)
            self.check_time()
            self.result["status"] = "observed"
        except BaseException as error:
            self.result["failure"] = (type(error).__name__ + ": " + str(error))[:300]
        finally:
            self.close_since(0)
            elapsed = self.clock() - self.started
            self.result["elapsed_seconds"] = round(elapsed, 6)
            if elapsed >= 3.0 or self.result["cleanup_failures"]:
                self.result["status"] = "failed"
                self.result.setdefault("failure", "metadata deadline or cleanup failure")
        return self.result


def observe(*, os_api, clock, identity):
    return MetadataObserver(os_api=os_api, clock=clock, identity=identity).run()


class IsolationInterfaceObserver(MetadataObserver):
    def __init__(self, *, os_api, clock, identity, spawn, select_ready):
        super().__init__(os_api=os_api, clock=clock, identity=identity)
        self.spawn = spawn
        self.select_ready = select_ready
        self.result = {
            "schema": INTERFACE_SCHEMA, "status": "failed", "checks": {},
            "binary": {}, "commands": [], "cleanup_failures": [],
            "binary_documentation_attempted": False,
            "binary_documentation_observed": False, "native_attempted": False,
            "namespace_attempted": False, "primitive_accepted": False,
            "application_accepted": False, "isolation_accepted": False,
            "descendant_cleanup_proven": False,
        }

    def binary_identity(self, descriptor, parent, name, original):
        self.check_time()
        for value in (
            self.os.fstat(descriptor),
            self.os.stat(name, dir_fd=parent, follow_symlinks=False),
        ):
            _unchanged(original, value)
            if any(getattr(original, field) != getattr(value, field) for field in (
                "st_size", "st_mtime_ns", "st_ctime_ns",
            )):
                raise RuntimeError("verified binary content identity changed")
            if (not stat.S_ISREG(value.st_mode) or
                    stat.S_IMODE(value.st_mode) != 0o755 or
                    value.st_uid != 0 or value.st_gid != 0):
                raise RuntimeError("binary must be root-owned regular 0755")
        try:
            self.os.getxattr(descriptor, "security.capability")
        except OSError as error:
            if error.errno != errno.ENODATA:
                raise RuntimeError("cannot establish absence of file capabilities") from None
        else:
            raise RuntimeError("file capability attribute is present")
        self.check_time()

    def hash_binary(self, descriptor):
        digest = hashlib.sha256()
        used = 0
        while True:
            self.check_time()
            requested = min(4096, 1048577 - used)
            chunk = self.os.read(descriptor, requested)
            if len(chunk) > requested:
                raise RuntimeError("invalid binary read count")
            self.check_time()
            if not chunk:
                return digest.hexdigest(), used
            used += len(chunk)
            if used > 1048576:
                raise RuntimeError("binary exceeds one-MiB hash bound")
            digest.update(chunk)

    def command(self, descriptor, option, stdout_limit):
        # Pinned CPython 3.12 Popen owns seven setup FDs: null stdin, two
        # pipe pairs and its exec-error pipe pair. Include stdio and traversal.
        bound = 3 + len(self.descriptors) + 7
        if bound > 16:
            raise RuntimeError("interface live descriptor bound")
        self.result["descriptor_bound_including_setup"] = bound
        record = {
            "option": option, "attempted": False, "normal_completion": False,
            "exit_code": None, "failure": None, "kill_attempted": False,
            "termination_confirmed": False, "streams": {},
        }
        self.result["commands"].append(record)
        process = None
        streams = []
        started = self.clock()
        capture_deadline = min(self.deadline, started + 1.0)
        try:
            self.check_time()
            record["attempted"] = True
            self.result["binary_documentation_attempted"] = True
            process = self.spawn(
                (UNSHARE, option), executable=f"/proc/self/fd/{descriptor}",
                stdin=subprocess.DEVNULL, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                shell=False, close_fds=True, pass_fds=(descriptor,), bufsize=0,
                env={"PATH": "/usr/bin:/bin", "LANG": "C.UTF-8",
                     "PYTHONDONTWRITEBYTECODE": "1"},
            )
            for name, limit in (("stdout", stdout_limit), ("stderr", 512)):
                streams.append({
                    "name": name, "pipe": getattr(process, name), "limit": limit,
                    "raw": bytearray(), "observed_bytes": 0, "eof": False,
                    "overflow": False, "error": None,
                })
            setup_failed = False
            for stream in streams:
                try:
                    if self.clock() >= capture_deadline:
                        raise TimeoutError("interface pipe setup deadline")
                    self.os.set_blocking(stream["pipe"].fileno(), False)
                except BaseException as error:
                    stream["error"] = type(error).__name__
                    setup_failed = True
            if setup_failed:
                raise RuntimeError("interface pipe setup failed")
            while True:
                remaining = capture_deadline - self.clock()
                if remaining <= 0:
                    raise TimeoutError("one-second interface capture deadline")
                pending = [s["pipe"].fileno() for s in streams if not s["eof"]]
                ready, _, _ = self.select_ready(pending, [], [], min(0.01, remaining))
                first_failure = None
                for stream in streams:
                    if stream["pipe"].fileno() not in ready or stream["eof"]:
                        continue
                    try:
                        if self.clock() >= capture_deadline:
                            raise TimeoutError("interface read deadline")
                        requested = min(4096, stream["limit"] + 1 - stream["observed_bytes"])
                        chunk = self.os.read(stream["pipe"].fileno(), requested)
                        if len(chunk) > requested:
                            raise RuntimeError("invalid interface read count")
                        if not chunk:
                            stream["eof"] = True
                        else:
                            stream["observed_bytes"] += len(chunk)
                            retained = stream["limit"] - len(stream["raw"])
                            stream["raw"].extend(chunk[:retained])
                            if stream["observed_bytes"] > stream["limit"]:
                                stream["overflow"] = True
                                raise RuntimeError("interface stream byte cap")
                    except BlockingIOError:
                        pass
                    except BaseException as error:
                        stream["error"] = type(error).__name__
                        first_failure = first_failure or error
                if first_failure is not None:
                    raise first_failure
                if streams[1]["observed_bytes"]:
                    raise RuntimeError("binary documentation produced stderr")
                self.check_time()
                if self.clock() >= capture_deadline:
                    raise TimeoutError("interface completion deadline")
                record["exit_code"] = process.poll()
                if record["exit_code"] is not None and all(s["eof"] for s in streams):
                    if record["exit_code"] != 0:
                        raise RuntimeError("binary documentation exited nonzero")
                    record["normal_completion"] = True
                    record["termination_confirmed"] = True
                    break
        except BaseException as error:
            record["failure"] = (type(error).__name__ + ": " + str(error))[:200]
        finally:
            if process is not None:
                try:
                    if process.poll() is None:
                        if self.clock() >= self.deadline:
                            raise TimeoutError("no original-child cleanup budget remains")
                        record["kill_attempted"] = True
                        process.kill()
                        remaining = self.deadline - self.clock()
                        if remaining <= 0:
                            raise TimeoutError("original-child confirmation deadline")
                        process.wait(timeout=remaining)
                    record["termination_confirmed"] = process.poll() is not None
                    if not record["termination_confirmed"]:
                        raise RuntimeError("original-child termination unconfirmed")
                except BaseException as error:
                    self.result["cleanup_failures"].append(type(error).__name__)
            for stream in streams:
                try:
                    stream["pipe"].close()
                except BaseException as error:
                    self.result["cleanup_failures"].append(type(error).__name__)
                record["streams"][stream["name"]] = {
                    name: stream[name] for name in (
                        "limit", "observed_bytes", "eof", "overflow", "error",
                    )
                }
                record["streams"][stream["name"]]["base64"] = base64.b64encode(
                    stream["raw"],
                ).decode("ascii")
            record["elapsed_seconds"] = round(self.clock() - started, 6)
        if (not record["normal_completion"] or record["failure"] or
                self.result["cleanup_failures"]):
            raise RuntimeError("binary documentation command failed; no next child")

    def run(self):
        try:
            self.check_identity()
            self.root = self.open_checked(None, "/", directory=True)
            with self.parent(UNSHARE) as (parent, name):
                descriptor = self.open_checked(parent, name, directory=False)
                original = self.os.fstat(descriptor)
                self.binary_identity(descriptor, parent, name, original)
                digest, size = self.hash_binary(descriptor)
                self.result["binary"] = {
                    "path": UNSHARE, **_facts(original), "bytes": size, "sha256": digest,
                    "file_capabilities_absent": True,
                }
                for option, limit in (("--version", 512), ("--help", 8192)):
                    self.binary_identity(descriptor, parent, name, original)
                    self.command(descriptor, option, limit)
                    self.binary_identity(descriptor, parent, name, original)
            self.check_time()
            self.result["status"] = "observed"
            self.result["binary_documentation_observed"] = True
        except BaseException as error:
            self.result["failure"] = (type(error).__name__ + ": " + str(error))[:300]
        finally:
            self.close_since(0)
            elapsed = self.clock() - self.started
            self.result["elapsed_seconds"] = round(elapsed, 6)
            if elapsed >= 3.0 or self.result["cleanup_failures"]:
                self.result["status"] = "failed"
                self.result["binary_documentation_observed"] = False
                self.result.setdefault("failure", "interface deadline or cleanup failure")
        return self.result


def observe_isolation_interface(*, os_api, clock, identity, spawn, select_ready):
    return IsolationInterfaceObserver(
        os_api=os_api, clock=clock, identity=identity,
        spawn=spawn, select_ready=select_ready,
    ).run()


def encode_result(result):
    encoded = (json.dumps(result, separators=(",", ":"), allow_nan=False) + "\n").encode("utf-8")
    if len(encoded) > 16384:
        raise ValueError("metadata output exceeds 16384 bytes")
    return encoded


def argument_parser():
    parser = argparse.ArgumentParser(description=__doc__)
    modes = parser.add_mutually_exclusive_group(required=True)
    modes.add_argument("--observe", action="store_true", help="guard; not execution authorization")
    modes.add_argument("--observe-isolation-interface", action="store_true",
                       help="fixed binary documentation; separate authorization required")
    return parser


def main(argv=None):
    parser = argument_parser()
    arguments = parser.parse_args(argv)
    if sys.platform != "linux":
        parser.error("the fixed observation requires Linux")

    def alarm(_number, _frame):
        raise TimeoutError("fixed three-second metadata self-alarm")

    signal.signal(signal.SIGALRM, alarm)
    signal.setitimer(signal.ITIMER_REAL, 3.0)
    try:
        options = dict(
            os_api=os, clock=time.monotonic,
            identity={
                "platform": sys.platform, "uid": os.getuid(), "euid": os.geteuid(),
                "python": sys.executable, "version": sys.version_info[:3],
                "architecture": os.uname().machine, "cwd": os.getcwd(),
            },
        )
        if arguments.observe_isolation_interface:
            result = observe_isolation_interface(
                **options, spawn=subprocess.Popen, select_ready=select.select,
            )
        else:
            result = observe(**options)
        sys.stdout.buffer.write(encode_result(result))
        sys.stdout.buffer.flush()
        return 0 if result["status"] == "observed" else 1
    finally:
        signal.setitimer(signal.ITIMER_REAL, 0)


if __name__ == "__main__":
    raise SystemExit(main())
