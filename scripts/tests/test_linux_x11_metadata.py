# SPDX-License-Identifier: GPL-3.0-or-later
"""Injected metadata contracts: no host/guest metadata or process operations."""

import gzip
import importlib.util
import json
import posixpath
import stat
import types
import unittest
from pathlib import Path
from unittest.mock import patch

from scripts import linux_x11_metadata as metadata


def identity(**changes):
    value = {
        "platform": "linux", "uid": 1000, "euid": 1000,
        "python": "/usr/bin/python3", "version": (3, 12, 3),
        "architecture": "x86_64", "cwd": metadata.EXPECTED_ROOT,
    }
    return {**value, **changes}


class FakeOS:
    O_RDONLY, O_DIRECTORY, O_NOFOLLOW, O_CLOEXEC, O_NONBLOCK = 0, 1, 2, 4, 8
    R_OK, W_OK, X_OK, ST_RDONLY = 4, 2, 1, 1

    def __init__(self):
        self.nodes = {}
        self.handles = {}
        self.positions = {}
        self.opened = []
        self.closed = []
        self.accesses = []
        self.reads = []
        self.max_handles = 0
        self.after_open = None
        self.close_error = None
        for path in (
            "/", "/tmp", "/tmp/.X11-unix", "/usr", "/usr/bin",
            "/usr/share", "/usr/share/man", "/usr/share/man/man1",
            "/proc", "/proc/sys", "/proc/sys/user", "/proc/sys/kernel",
            "/lib", "/lib/x86_64-linux-gnu",
        ):
            self.add(path, stat.S_IFDIR | 0o755)
        self.nodes["/tmp"]["mode"] = stat.S_IFDIR | 0o1777
        self.nodes["/tmp/.X11-unix"].update(
            mode=stat.S_IFDIR | 0o777, readonly=True, writable=False,
        )

    def add(self, path, mode=stat.S_IFREG | 0o644, data=b"", target=None):
        self.nodes[path] = {
            "mode": mode, "data": data, "target": target,
            "inode": len(self.nodes) + 1, "readonly": False, "writable": True,
        }

    def path(self, path, dir_fd=None):
        return posixpath.normpath(
            posixpath.join(self.handles[dir_fd], path) if dir_fd else path
        )

    def stat(self, path, *, dir_fd=None, follow_symlinks=False):
        if follow_symlinks:
            raise AssertionError("following a symlink is forbidden")
        name = self.path(path, dir_fd)
        if name not in self.nodes:
            raise FileNotFoundError(name)
        node = self.nodes[name]
        return types.SimpleNamespace(
            st_dev=1, st_ino=node["inode"], st_mode=node["mode"],
            st_uid=1000, st_gid=1000,
            st_size=len(node["target"] or node["data"]),
        )

    def open(self, path, flags, *, dir_fd=None):
        if flags & (self.O_NOFOLLOW | self.O_CLOEXEC) != 6:
            raise AssertionError("missing no-follow descriptor flags")
        name = self.path(path, dir_fd)
        self.stat(name)
        descriptor = max(self.opened, default=100) + 1
        self.handles[descriptor] = name
        self.positions[descriptor] = 0
        self.opened.append(descriptor)
        self.max_handles = max(self.max_handles, len(self.handles))
        if self.after_open:
            self.after_open(name, descriptor)
        return descriptor

    def fstat(self, descriptor):
        return self.stat(self.handles[descriptor])

    def fstatvfs(self, descriptor):
        return types.SimpleNamespace(
            f_flag=1 if self.nodes[self.handles[descriptor]]["readonly"] else 0,
        )

    def access(self, path, mode, *, dir_fd=None, effective_ids=False,
               follow_symlinks=True):
        if not effective_ids or follow_symlinks:
            raise AssertionError("access must be effective and no-follow")
        name = self.path(path, dir_fd)
        self.accesses.append((name, mode))
        return self.nodes[name]["writable"]

    def readlink(self, path, *, dir_fd=None):
        return self.nodes[self.path(path, dir_fd)]["target"]

    @staticmethod
    def fsencode(value):
        return value.encode("utf-8")

    def read(self, descriptor, count):
        if not 0 < count <= 4096:
            raise AssertionError("unbounded read")
        name = self.handles[descriptor]
        self.reads.append((name, count))
        position = self.positions[descriptor]
        answer = self.nodes[name]["data"][position:position + count]
        self.positions[descriptor] += len(answer)
        return answer

    def close(self, descriptor):
        self.closed.append(descriptor)
        del self.handles[descriptor]
        if self.close_error == descriptor:
            raise OSError("injected close failure")


class MetadataContracts(unittest.TestCase):
    def run_observation(self, fake=None, **changes):
        fake = fake or FakeOS()
        result = metadata.observe(
            os_api=fake, clock=lambda: 0.0, identity=identity(**changes),
        )
        self.assertFalse(fake.handles)
        self.assertEqual(sorted(fake.opened), sorted(fake.closed))
        self.assertLessEqual(fake.max_handles, 8)
        for flag in (
            "native_attempted", "primitive_accepted",
            "application_accepted", "isolation_accepted",
        ):
            self.assertIs(result[flag], False)
        return result

    def test_readonly_socket_directory_is_current_observation_not_acceptance(self):
        result = self.run_observation()
        self.assertEqual("observed", result["status"])
        socket = next(p for p in result["paths"] if p["path"] == "/tmp/.X11-unix")
        self.assertTrue(socket["filesystem_read_only"])
        self.assertFalse(socket["effective_write_search"])
        self.assertEqual("0777", socket["mode"])

    def test_identity_mismatch_prevents_all_metadata(self):
        for change in (
            {"uid": 0}, {"euid": 0}, {"platform": "win32"},
            {"version": (3, 13, 0)}, {"python": "/another/python"},
            {"architecture": "arm64"}, {"cwd": "/elsewhere"},
        ):
            with self.subTest(change=change):
                fake = FakeOS()
                self.assertEqual("failed", self.run_observation(fake, **change)["status"])
                self.assertFalse(fake.opened)

    def test_only_exact_observed_wslg_link_allows_conditional_branch(self):
        fake = FakeOS()
        fake.add("/tmp/.X11-unix", stat.S_IFLNK | 0o777,
                 target="/mnt/wslg/.X11-unix")
        for path in ("/mnt", "/mnt/wslg", "/mnt/wslg/.X11-unix"):
            fake.add(path, stat.S_IFDIR | 0o755)
        result = self.run_observation(fake)
        self.assertEqual("observed", result["status"])
        self.assertEqual("/mnt/wslg/.X11-unix", result["observed_socket_path"])

    def test_unknown_link_and_wrong_socket_type_fail_without_following(self):
        for mode, target in (
            (stat.S_IFLNK | 0o777, "/unrelated"),
            (stat.S_IFLNK | 0o777, "x" * 4097),
            (stat.S_IFREG | 0o644, None),
        ):
            with self.subTest(mode=mode, target_length=len(target or "")):
                fake = FakeOS()
                fake.add("/tmp/.X11-unix", mode, target=target)
                self.assertEqual("failed", self.run_observation(fake)["status"])
                self.assertEqual(2, len(fake.opened))

    def test_directory_substitution_is_not_accepted(self):
        fake = FakeOS()
        fake.after_open = lambda name, fd: fake.nodes[name].update(
            inode=fake.nodes[name]["inode"] + 1,
        )
        self.assertEqual("failed", self.run_observation(fake)["status"])

    def test_cleanup_failure_fails_observation(self):
        fake = FakeOS()
        fake.close_error = 101
        result = self.run_observation(fake)
        self.assertEqual("failed", result["status"])
        self.assertTrue(result["cleanup_failures"])

    def test_deadline_is_not_optional_missing_evidence(self):
        fake = FakeOS()
        ticks = iter([0.0, 0.0, 4.0])
        result = metadata.observe(
            os_api=fake, clock=lambda: next(ticks, 4.0), identity=identity(),
        )
        self.assertEqual("failed", result["status"])
        self.assertFalse(fake.handles)

    def test_missing_optional_candidates_are_explicit_not_failures(self):
        result = self.run_observation()
        self.assertTrue(all(v["status"] == "missing" for v in result["candidates"].values()))
        self.assertTrue(all(v["status"] == "missing" for v in result["sysctls"].values()))
        self.assertEqual("observed", result["status"])

    def test_binary_symlink_is_not_resolved_or_read(self):
        fake = FakeOS()
        fake.add("/usr/bin/unshare", stat.S_IFLNK | 0o777, target="/unrelated")
        result = self.run_observation(fake)
        self.assertEqual("symlink", result["candidates"]["/usr/bin/unshare"]["kind"])
        self.assertFalse(any(name == "/usr/bin/unshare" for name, _ in fake.reads))
        self.assertFalse(any(name == "/usr/bin/unshare" for name, _ in fake.accesses))

    def test_optional_regular_read_does_not_follow_parent_symlinks(self):
        fake = FakeOS()
        fake.add("/proc/sys/user", stat.S_IFLNK | 0o777, target="/unrelated")
        result = self.run_observation(fake)
        self.assertEqual("unavailable", result["sysctls"][metadata.SYSCTLS[0]]["status"])
        self.assertFalse(fake.reads)

    def test_sysctls_are_bounded_integer_only(self):
        for raw, status in (
            (b"0\n", "observed"), (b"28633\n", "observed"),
            (b"secret=value\n", "unavailable"), (b"1\n2\n", "unavailable"),
            (b"1" * 129, "unavailable"),
        ):
            with self.subTest(raw_length=len(raw)):
                fake = FakeOS()
                fake.add(metadata.SYSCTLS[0], data=raw)
                value = self.run_observation(fake)["sysctls"][metadata.SYSCTLS[0]]
                self.assertEqual(status, value["status"])
                self.assertLessEqual(sum(n for _, n in fake.reads), 258)
                self.assertNotIn("secret", json.dumps(value))

    def test_manual_options_are_actual_bounded_tokens_only(self):
        fake = FakeOS()
        raw = br"\-\-user \-\-mount --user --map-root-user"
        fake.add(metadata.MANUALS[0], data=gzip.compress(raw))
        result = self.run_observation(fake)
        value = result["manuals"][metadata.MANUALS[0]]
        self.assertEqual("observed", value["status"])
        self.assertEqual(["--map-root-user", "--mount", "--user"], value["options"])

    def test_manual_decode_compression_and_expansion_are_bounded(self):
        for data in (
            b"bad gzip", b"x" * 16385, gzip.compress(b"x" * 32769),
            gzip.compress(b"\xff"), gzip.compress(
                " ".join(f"--flag-{i}" for i in range(65)).encode(),
            ),
        ):
            with self.subTest(compressed_length=len(data)):
                fake = FakeOS()
                fake.add(metadata.MANUALS[0], data=data)
                value = self.run_observation(fake)["manuals"][metadata.MANUALS[0]]
                self.assertEqual("unavailable", value["status"])
                self.assertNotIn("options", value)

    def test_option_count_is_shared_across_both_manuals(self):
        fake = FakeOS()
        for index, path in enumerate(metadata.MANUALS):
            raw = " ".join(f"--option-{index}-{i}" for i in range(40)).encode()
            fake.add(path, data=gzip.compress(raw))
        result = self.run_observation(fake)
        count = sum(len(v.get("options", [])) for v in result["manuals"].values())
        self.assertLessEqual(count, 64)

    def test_repeated_manual_options_still_respect_total_output_cap(self):
        fake = FakeOS()
        raw = " ".join(f"--option-{i}" for i in range(40)).encode()
        for path in metadata.MANUALS:
            fake.add(path, data=gzip.compress(raw))
        result = self.run_observation(fake)
        self.assertLessEqual(sum(
            len(value.get("options", [])) for value in result["manuals"].values()
        ), 64)

    def test_manual_symlinks_and_nonregular_sysctls_are_not_opened(self):
        fake = FakeOS()
        fake.add(metadata.MANUALS[0], stat.S_IFLNK | 0o777, target="/unrelated")
        fake.add(metadata.SYSCTLS[0], stat.S_IFIFO | 0o644)
        result = self.run_observation(fake)
        self.assertEqual("unavailable", result["manuals"][metadata.MANUALS[0]]["status"])
        self.assertEqual("unavailable", result["sysctls"][metadata.SYSCTLS[0]]["status"])
        self.assertFalse(fake.reads)

    def test_serialized_output_limit_and_truthful_failure(self):
        with self.assertRaises(ValueError):
            metadata.encode_result({"extra": "x" * 16385})
        encoded = metadata.encode_result(self.run_observation())
        self.assertLessEqual(len(encoded), 16384)
        self.assertEqual("observed", json.loads(encoded)["status"])

    def test_import_is_inert_and_cli_has_no_rebinding_options(self):
        path = Path(metadata.__file__)
        spec = importlib.util.spec_from_file_location("metadata_inert_test", path)
        module = importlib.util.module_from_spec(spec)
        with patch("os.open", side_effect=AssertionError("metadata on import")), \
                patch("os.stat", side_effect=AssertionError("metadata on import")):
            spec.loader.exec_module(module)
        parser = metadata.argument_parser()
        self.assertEqual({"observe", "help"}, {a.dest for a in parser._actions})


if __name__ == "__main__":
    unittest.main()
