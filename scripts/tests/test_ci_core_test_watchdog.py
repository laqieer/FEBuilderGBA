# SPDX-License-Identifier: GPL-3.0-or-later

from __future__ import annotations

import signal
import subprocess
import unittest
from unittest import mock

from scripts import ci_core_test_watchdog as watchdog


class FakeProcess:
    def __init__(self, waits, returncode=0):
        self.pid = 4321
        self._waits = list(waits)
        self.returncode = None
        self.final_returncode = returncode

    def wait(self, timeout=None):
        result = self._waits.pop(0)
        if isinstance(result, BaseException):
            raise result
        self.returncode = self.final_returncode if result is None else result
        return self.returncode

    def poll(self):
        return self.returncode


class CoreTestWatchdogTests(unittest.TestCase):
    def test_fixed_command_and_timing_invariants(self):
        self.assertEqual(
            (
                "dotnet", "test", "FEBuilderGBA.Core.Tests/FEBuilderGBA.Core.Tests.csproj",
                "-c", "Release", "-warnaserror", "--disable-build-servers",
                "-p:UseSharedCompilation=false", "--logger", "trx;LogFileName=core-tests.trx",
                "--logger", "console;verbosity=minimal", "--results-directory",
                "TestResults/core-ci-diagnostics/raw", "--blame-hang",
                "--blame-hang-timeout", "5m", "--blame-hang-dump-type", "none",
            ),
            watchdog.COMMAND,
        )
        self.assertEqual(300, watchdog.BLAME_HANG_SECONDS)
        self.assertEqual(600, watchdog.WATCHDOG_SECONDS)
        self.assertEqual(10, watchdog.TERM_GRACE_SECONDS)
        self.assertEqual(5, watchdog.KILL_GRACE_SECONDS)
        self.assertEqual(900, watchdog.ACTIONS_STEP_SECONDS)
        self.assertLess(watchdog.BLAME_HANG_SECONDS, watchdog.WATCHDOG_SECONDS)
        self.assertLess(
            watchdog.WATCHDOG_SECONDS + watchdog.TERM_GRACE_SECONDS + watchdog.KILL_GRACE_SECONDS,
            watchdog.ACTIONS_STEP_SECONDS,
        )

    def test_success_propagates_and_uses_same_session_process_group(self):
        process = FakeProcess([0])
        popen = mock.Mock(return_value=process)
        setters = []

        result = watchdog.run(
            platform="darwin",
            popen_factory=popen,
            signal_setter=lambda number, handler: setters.append((number, handler)) or signal.SIG_DFL,
            kill_group=mock.Mock(),
        )

        self.assertEqual(0, result)
        popen.assert_called_once_with(
            watchdog.COMMAND,
            cwd=watchdog.ROOT,
            stdin=subprocess.DEVNULL,
            stdout=None,
            stderr=None,
            shell=False,
            process_group=0,
        )
        self.assertEqual(
            set(watchdog.PARENT_SIGNALS),
            {number for number, _ in setters[:3]},
        )

    def test_nonzero_exit_is_propagated(self):
        process = FakeProcess([7], returncode=7)
        result = watchdog.run(
            platform="darwin",
            popen_factory=mock.Mock(return_value=process),
            signal_setter=lambda _number, _handler: signal.SIG_DFL,
            kill_group=mock.Mock(),
        )
        self.assertEqual(7, result)

    def test_timeout_terminates_group_and_returns_124(self):
        process = FakeProcess([
            subprocess.TimeoutExpired(watchdog.COMMAND, watchdog.WATCHDOG_SECONDS),
            0,
        ])
        kill_group = mock.Mock()

        result = watchdog.run(
            platform="darwin",
            popen_factory=mock.Mock(return_value=process),
            signal_setter=lambda _number, _handler: signal.SIG_DFL,
            kill_group=kill_group,
        )

        self.assertEqual(124, result)
        kill_group.assert_called_once_with(process.pid, signal.SIGTERM)

    def test_timeout_forces_group_kill_after_term_grace(self):
        process = FakeProcess([
            subprocess.TimeoutExpired(watchdog.COMMAND, watchdog.WATCHDOG_SECONDS),
            subprocess.TimeoutExpired(watchdog.COMMAND, watchdog.TERM_GRACE_SECONDS),
            0,
        ])
        kill_group = mock.Mock()

        result = watchdog.run(
            platform="darwin",
            popen_factory=mock.Mock(return_value=process),
            signal_setter=lambda _number, _handler: signal.SIG_DFL,
            kill_group=kill_group,
        )

        self.assertEqual(124, result)
        self.assertEqual(
            [
                mock.call(process.pid, signal.SIGTERM),
                mock.call(process.pid, watchdog.SIGKILL),
            ],
            kill_group.call_args_list,
        )

    def test_parent_signal_cleans_group_and_preserves_signal_exit_code(self):
        process = FakeProcess([0])
        kill_group = mock.Mock()
        state = watchdog.ProcessGroupWatchdog(kill_group=kill_group)
        state.process = process

        with self.assertRaises(watchdog.ParentSignal) as raised:
            state.handle_signal(signal.SIGTERM, None)

        self.assertEqual(signal.SIGTERM, raised.exception.number)
        kill_group.assert_called_once_with(process.pid, signal.SIGTERM)
        self.assertEqual(0, process.returncode)

    def test_parent_signal_without_child_is_bounded(self):
        state = watchdog.ProcessGroupWatchdog(kill_group=mock.Mock())
        with self.assertRaises(watchdog.ParentSignal) as raised:
            state.handle_signal(watchdog.SIGHUP, None)
        self.assertEqual(watchdog.SIGHUP, raised.exception.number)
        state.kill_group.assert_not_called()

    def test_non_macos_invocation_is_refused(self):
        self.assertEqual(2, watchdog.run(platform="linux"))


if __name__ == "__main__":
    unittest.main()
