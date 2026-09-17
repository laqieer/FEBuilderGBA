# SPDX-License-Identifier: GPL-3.0-or-later
"""Injected metadata contracts: no host/guest metadata or process operations."""

import gzip
import base64
import errno
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
            st_uid=node.get("uid", 1000), st_gid=node.get("gid", 1000),
            st_size=len(node["target"] or node["data"]),
            st_mtime_ns=node.get("mtime", 0), st_ctime_ns=node.get("ctime", 0),
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

    def test_valid_gzip_header_with_corrupt_deflate_is_optional_unavailable(self):
        fake = FakeOS()
        fake.add(
            metadata.MANUALS[0],
            data=b"\x1f\x8b\x08\x00\x00\x00\x00\x00\x00\xff\x07",
        )
        result = self.run_observation(fake)
        self.assertEqual("observed", result["status"])
        self.assertEqual("unavailable", result["manuals"][metadata.MANUALS[0]]["status"])
        self.assertEqual(
            "invalid_compressed_manual",
            result["manuals"][metadata.MANUALS[0]]["reason"],
        )

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
        self.assertEqual(
            {"observe", "observe_isolation_interface", "help"},
            {a.dest for a in parser._actions},
        )


class InterfaceOS(FakeOS):
    def __init__(self):
        super().__init__()
        self.add("/usr/bin/unshare", stat.S_IFREG | 0o755, b"verified ELF fixture")
        self.nodes["/usr/bin/unshare"].update(uid=0, gid=0)
        self.capability_error = errno.ENODATA
        self.pipes = {}
        self.nonblocking = []

    def getxattr(self, descriptor, name):
        assert self.handles[descriptor] == "/usr/bin/unshare"
        assert name == "security.capability"
        if self.capability_error:
            raise OSError(self.capability_error, "injected capability result")
        return b""

    def set_blocking(self, descriptor, blocking):
        assert not blocking
        self.nonblocking.append(descriptor)

    def read(self, descriptor, count):
        if descriptor in self.pipes:
            return self.pipes[descriptor].read(count)
        return super().read(descriptor, count)


class InterfacePipe:
    def __init__(self, fake, descriptor, data):
        self.fake, self.descriptor, self.data = fake, descriptor, data
        self.position = 0
        self.closed = False
        self.pending = False
        self.error = None
        self.close_error = False
        self.requests = []
        fake.pipes[descriptor] = self

    def fileno(self):
        return self.descriptor

    def read(self, count):
        assert 0 < count <= 4096
        self.requests.append(count)
        if self.error:
            raise OSError("injected read failure")
        if self.pending:
            raise BlockingIOError()
        value = self.data[self.position:self.position + count]
        self.position += len(value)
        return value

    def close(self):
        self.closed = True
        if self.close_error:
            raise OSError("injected pipe close failure")


class InterfaceHarness:
    def __init__(self):
        self.os = InterfaceOS()
        self.seconds = 0.0
        self.calls = []
        self.children = []
        self.outputs = [b"unshare fixture\n", b"literal help fixture\n"]
        self.errors = [b"", b""]
        self.returncode = 0
        self.spawn_error = False
        self.pending = False
        self.wait_error = False
        self.on_spawn = None
        self.max_live = 0

    def clock(self):
        return self.seconds

    def select(self, readers, writers, errors, timeout):
        self.seconds += min(timeout, 0.01)
        return readers, [], []

    def spawn(self, args, **kwargs):
        assert not any(c.poll() is None for c in self.children)
        self.calls.append((args, kwargs))
        # CPython 3.12 Popen setup: /dev/null, two pipe pairs, errpipe pair.
        self.max_live = max(self.max_live, 3 + len(self.os.handles) + 7)
        if self.spawn_error:
            raise OSError("injected descriptor-exec setup failure")
        index = len(self.children)
        child = types.SimpleNamespace(
            stdout=InterfacePipe(self.os, 1001 + index * 2, self.outputs[index]),
            stderr=InterfacePipe(self.os, 1002 + index * 2, self.errors[index]),
            returncode=None if self.pending else self.returncode,
            kills=0, waits=[],
        )
        child.stdout.pending = self.pending
        child.stderr.pending = self.pending
        child.poll = lambda: child.returncode

        def kill():
            child.kills += 1
            if not self.wait_error:
                child.returncode = -9

        def wait(timeout):
            child.waits.append(timeout)
            assert 0 < timeout <= 3 - self.seconds
            if self.wait_error:
                self.seconds += timeout
                raise TimeoutError("injected confirmation deadline")
            return child.returncode

        child.kill, child.wait = kill, wait
        self.children.append(child)
        if self.on_spawn:
            self.on_spawn(child)
        return child

    def run(self, **changes):
        return metadata.observe_isolation_interface(
            os_api=self.os, clock=self.clock, identity=identity(**changes),
            spawn=self.spawn, select_ready=self.select,
        )


class IsolationInterfaceContracts(unittest.TestCase):
    def assert_failed(self, harness, expected_calls=1):
        result = harness.run()
        self.assertEqual("failed", result["status"])
        self.assertFalse(result["binary_documentation_observed"])
        self.assertEqual(expected_calls, len(harness.calls))
        self.assertFalse(harness.os.handles)
        for child in harness.children:
            self.assertTrue(child.stdout.closed and child.stderr.closed)
        return result

    def test_two_literal_commands_execute_only_the_retained_descriptor(self):
        harness = InterfaceHarness()
        result = harness.run()
        self.assertEqual("observed", result["status"])
        self.assertEqual("issue2160-isolation-interface-v1", result["schema"])
        self.assertTrue(result["binary_documentation_attempted"])
        self.assertTrue(result["binary_documentation_observed"])
        self.assertEqual(2, len(harness.calls))
        for (args, options), flag in zip(harness.calls, ("--version", "--help")):
            self.assertEqual(("/usr/bin/unshare", flag), args)
            descriptor, = options["pass_fds"]
            self.assertEqual(f"/proc/self/fd/{descriptor}", options["executable"])
            self.assertEqual({
                "PATH": "/usr/bin:/bin", "LANG": "C.UTF-8",
                "PYTHONDONTWRITEBYTECODE": "1",
            }, options["env"])
            self.assertFalse(options["shell"])
            self.assertTrue(options["close_fds"])
            self.assertEqual(0, options["bufsize"])
        self.assertEqual(harness.calls[0][1]["pass_fds"], harness.calls[1][1]["pass_fds"])
        self.assertLessEqual(harness.max_live, 16)
        self.assertEqual(harness.max_live, result["descriptor_bound_including_setup"])
        self.assertFalse(harness.os.handles)
        self.assertTrue(all(p.closed for p in harness.os.pipes.values()))
        self.assertLessEqual(len(metadata.encode_result(result)), 16384)
        for flag in (
            "native_attempted", "namespace_attempted", "primitive_accepted",
            "application_accepted", "isolation_accepted", "descendant_cleanup_proven",
        ):
            self.assertIs(result[flag], False)
        self.assertEqual(
            harness.outputs,
            [base64.b64decode(c["streams"]["stdout"]["base64"]) for c in result["commands"]],
        )

    def test_all_fixed_identity_mismatches_fail_before_binary_access_or_spawn(self):
        for changes in (
            {"uid": 0}, {"euid": 0}, {"platform": "win32"}, {"cwd": "/elsewhere"},
            {"python": "/other"}, {"version": (3, 13, 0)}, {"architecture": "arm64"},
        ):
            with self.subTest(changes=changes):
                harness = InterfaceHarness()
                result = harness.run(**changes)
                self.assertEqual("failed", result["status"])
                self.assertFalse(result["binary_documentation_attempted"])
                self.assertFalse(harness.calls or harness.os.opened)

    def test_binary_ownership_mode_type_and_capabilities_are_fail_closed(self):
        for change in (
            {"uid": 1000}, {"gid": 1000}, {"mode": stat.S_IFREG | 0o4755},
            {"mode": stat.S_IFREG | 0o775}, {"mode": stat.S_IFLNK | 0o777},
        ):
            with self.subTest(change=change):
                harness = InterfaceHarness()
                harness.os.nodes["/usr/bin/unshare"].update(change)
                self.assert_failed(harness, 0)
        for capability_error in (None, errno.EPERM, errno.ENOTSUP):
            harness = InterfaceHarness()
            harness.os.capability_error = capability_error
            self.assert_failed(harness, 0)

    def test_binary_read_is_capped_with_one_overflow_sentinel(self):
        harness = InterfaceHarness()
        harness.os.nodes["/usr/bin/unshare"]["data"] = b"x" * (1048576 + 1)
        self.assert_failed(harness, 0)
        self.assertLessEqual(sum(n for _, n in harness.os.reads), 1048577)

    def test_exact_binary_cap_and_eof_is_not_overflow(self):
        harness = InterfaceHarness()
        harness.os.nodes["/usr/bin/unshare"]["data"] = b"x" * 1048576
        self.assertEqual("observed", harness.run()["status"])
        self.assertEqual(1, harness.os.reads[-1][1])

    def test_no_descriptor_exec_fallback_after_setup_failure(self):
        harness = InterfaceHarness()
        harness.spawn_error = True
        result = self.assert_failed(harness)
        self.assertTrue(result["binary_documentation_attempted"])
        self.assertLessEqual(harness.max_live, 16)

    def test_identity_drift_after_first_child_prevents_second(self):
        for field, value in (("inode", 999), ("mtime", 1), ("ctime", 1)):
            with self.subTest(field=field):
                harness = InterfaceHarness()
                harness.on_spawn = lambda child: harness.os.nodes["/usr/bin/unshare"].update(
                    {field: value},
                )
                self.assert_failed(harness)

    def test_nonzero_exit_and_any_stderr_prevent_second_child(self):
        harness = InterfaceHarness()
        harness.returncode = 1
        self.assert_failed(harness)
        harness = InterfaceHarness()
        harness.errors[0] = b"bounded diagnostic"
        self.assert_failed(harness)

    def test_exact_stdout_caps_and_eof_pass(self):
        harness = InterfaceHarness()
        harness.outputs = [b"x" * 512, b"y" * 8192]
        result = harness.run()
        self.assertEqual("observed", result["status"])
        for command in result["commands"]:
            self.assertTrue(command["streams"]["stdout"]["eof"])
        self.assertTrue(all(1 in c.stdout.requests for c in harness.children))

    def test_each_stdout_cap_retains_at_most_one_sentinel_and_stops(self):
        for index, cap in ((0, 512), (1, 8192)):
            harness = InterfaceHarness()
            harness.outputs[index] = b"x" * (cap + 100)
            result = self.assert_failed(harness, index + 1)
            stream = result["commands"][index]["streams"]["stdout"]
            self.assertTrue(stream["overflow"])
            self.assertEqual(cap + 1, stream["observed_bytes"])
            self.assertEqual(cap, len(base64.b64decode(stream["base64"])))
            self.assertFalse(stream["eof"])

    def test_stderr_cap_is_not_mistaken_for_complete_diagnostics(self):
        harness = InterfaceHarness()
        harness.errors[0] = b"x" * 600
        result = self.assert_failed(harness)
        stream = result["commands"][0]["streams"]["stderr"]
        self.assertEqual(513, stream["observed_bytes"])
        self.assertTrue(stream["overflow"])

    def test_timeout_aborts_only_original_child_once_with_remaining_budget(self):
        for wait_error in (False, True):
            harness = InterfaceHarness()
            harness.pending = True
            harness.wait_error = wait_error
            result = self.assert_failed(harness)
            child = harness.children[0]
            self.assertEqual(1, child.kills)
            self.assertLessEqual(harness.seconds, 3)
            self.assertFalse(result["commands"][0]["streams"]["stdout"]["eof"])
            self.assertEqual(not wait_error, result["commands"][0]["termination_confirmed"])

    def test_read_and_close_errors_fail_without_a_second_child(self):
        for kind in ("read", "close", "setup"):
            harness = InterfaceHarness()
            if kind == "setup":
                harness.os.set_blocking = lambda *a: (_ for _ in ()).throw(OSError())
            else:
                harness.on_spawn = lambda child: setattr(
                    child.stdout, "error" if kind == "read" else "close_error", True,
                )
            result = self.assert_failed(harness)
            if kind == "close":
                self.assertTrue(result["cleanup_failures"])

    def test_profile_cli_modes_are_mutually_exclusive_and_fixed(self):
        parser = metadata.argument_parser()
        self.assertTrue(parser.parse_args(["--observe-isolation-interface"]).observe_isolation_interface)
        with self.assertRaises(SystemExit):
            parser.parse_args(["--observe", "--observe-isolation-interface"])
        with self.assertRaises(SystemExit):
            parser.parse_args(["--observe-isolation-interface", "--command", "other"])


if __name__ == "__main__":
    unittest.main()
