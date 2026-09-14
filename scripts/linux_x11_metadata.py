# SPDX-License-Identifier: GPL-3.0-or-later
"""Fixed, read-only issue-2160 observation; importing this module is inert."""

import argparse
from contextlib import contextmanager
import gzip
import io
import json
import os
import re
import signal
import stat
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

    def run(self):
        expected = {
            "platform": "linux", "uid": 1000, "euid": 1000,
            "python": "/usr/bin/python3", "version": (3, 12, 3),
            "architecture": "x86_64", "cwd": EXPECTED_ROOT,
        }
        try:
            self.check_time()
            self.result["checks"] = {
                name: self.identity.get(name) == value for name, value in expected.items()
            }
            if not all(self.result["checks"].values()):
                raise RuntimeError("fixed identity mismatch; metadata prohibited")
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


def encode_result(result):
    encoded = (json.dumps(result, separators=(",", ":"), allow_nan=False) + "\n").encode("utf-8")
    if len(encoded) > 16384:
        raise ValueError("metadata output exceeds 16384 bytes")
    return encoded


def argument_parser():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--observe", action="store_true", help="guard; not execution authorization")
    return parser


def main(argv=None):
    parser = argument_parser()
    if not parser.parse_args(argv).observe:
        parser.error("--observe and separate execution authorization are required")
    if sys.platform != "linux":
        parser.error("the fixed observation requires Linux")

    def alarm(_number, _frame):
        raise TimeoutError("fixed three-second metadata self-alarm")

    signal.signal(signal.SIGALRM, alarm)
    signal.setitimer(signal.ITIMER_REAL, 3.0)
    try:
        result = observe(
            os_api=os, clock=time.monotonic,
            identity={
                "platform": sys.platform, "uid": os.getuid(), "euid": os.geteuid(),
                "python": sys.executable, "version": sys.version_info[:3],
                "architecture": os.uname().machine, "cwd": os.getcwd(),
            },
        )
        sys.stdout.buffer.write(encode_result(result))
        sys.stdout.buffer.flush()
        return 0 if result["status"] == "observed" else 1
    finally:
        signal.setitimer(signal.ITIMER_REAL, 0)


if __name__ == "__main__":
    raise SystemExit(main())
