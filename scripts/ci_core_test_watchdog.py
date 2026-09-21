#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later

from __future__ import annotations

import os
from pathlib import Path
import signal
import subprocess
import sys
from typing import Callable


ROOT = Path(__file__).resolve().parents[1]
BLAME_HANG_SECONDS = 300
WATCHDOG_SECONDS = 600
TERM_GRACE_SECONDS = 10
KILL_GRACE_SECONDS = 5
ACTIONS_STEP_SECONDS = 900
TIMEOUT_EXIT = 124
SUPERVISOR_FAILURE_EXIT = 125
SIGHUP = getattr(signal, "SIGHUP", 1)
SIGKILL = getattr(signal, "SIGKILL", 9)
PARENT_SIGNALS = (signal.SIGINT, signal.SIGTERM, SIGHUP)

COMMAND = (
    "dotnet", "test", "FEBuilderGBA.Core.Tests/FEBuilderGBA.Core.Tests.csproj",
    "-c", "Release", "-warnaserror", "--disable-build-servers",
    "-p:UseSharedCompilation=false", "--logger", "trx;LogFileName=core-tests.trx",
    "--logger", "console;verbosity=minimal", "--results-directory",
    "TestResults/core-ci-diagnostics/raw", "--blame-hang",
    "--blame-hang-timeout", "5m", "--blame-hang-dump-type", "none",
)


class ParentSignal(Exception):
    def __init__(self, number: int):
        super().__init__(number)
        self.number = number


def kill_process_group(group_id: int, number: int) -> None:
    os.killpg(group_id, number)


class ProcessGroupWatchdog:
    def __init__(self, *, kill_group: Callable[[int, int], None] | None = None):
        self.kill_group = kill_group or kill_process_group
        self.process: subprocess.Popen | None = None
        self.cleanup_attempted = False

    def cleanup(self) -> bool:
        if self.cleanup_attempted or self.process is None:
            return True
        self.cleanup_attempted = True
        process = self.process
        if process.poll() is not None:
            return True

        try:
            self.kill_group(process.pid, signal.SIGTERM)
        except ProcessLookupError:
            return True

        try:
            process.wait(timeout=TERM_GRACE_SECONDS)
            return True
        except subprocess.TimeoutExpired:
            pass

        try:
            self.kill_group(process.pid, SIGKILL)
        except ProcessLookupError:
            return True

        try:
            process.wait(timeout=KILL_GRACE_SECONDS)
            return True
        except subprocess.TimeoutExpired:
            return False

    def handle_signal(self, number: int, _frame) -> None:
        self.cleanup()
        raise ParentSignal(number)


def run(
    *,
    platform: str | None = None,
    popen_factory: Callable[..., subprocess.Popen] = subprocess.Popen,
    signal_setter: Callable = signal.signal,
    kill_group: Callable[[int, int], None] | None = None,
) -> int:
    if (platform or sys.platform) != "darwin":
        print("ci_core_test_watchdog.py is restricted to macOS.", file=sys.stderr)
        return 2

    state = ProcessGroupWatchdog(kill_group=kill_group)
    previous_handlers = {}
    try:
        for number in PARENT_SIGNALS:
            previous_handlers[number] = signal_setter(number, state.handle_signal)

        state.process = popen_factory(
            COMMAND,
            cwd=ROOT,
            stdin=subprocess.DEVNULL,
            stdout=None,
            stderr=None,
            shell=False,
            process_group=0,
        )
        try:
            return state.process.wait(timeout=WATCHDOG_SECONDS)
        except subprocess.TimeoutExpired:
            print(
                f"Core tests exceeded the {WATCHDOG_SECONDS}-second watchdog deadline.",
                file=sys.stderr,
            )
            return TIMEOUT_EXIT if state.cleanup() else SUPERVISOR_FAILURE_EXIT
        except ParentSignal as exc:
            return 128 + exc.number
    except ParentSignal as exc:
        return 128 + exc.number
    except (OSError, subprocess.SubprocessError) as exc:
        print(f"Core test watchdog failed: {exc}", file=sys.stderr)
        return SUPERVISOR_FAILURE_EXIT
    finally:
        if state.process is not None and not state.cleanup_attempted:
            if not state.cleanup():
                print("Core test process group could not be reaped.", file=sys.stderr)
        for number, previous in previous_handlers.items():
            signal_setter(number, previous)


if __name__ == "__main__":
    raise SystemExit(run())
