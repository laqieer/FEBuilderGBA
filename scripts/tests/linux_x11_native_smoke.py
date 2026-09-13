# SPDX-License-Identifier: GPL-3.0-or-later
"""Gated native primitive source, NOT part of unittest discovery.

Run only after independent source/security review and a separate native grant.
The supervisor never loads Xlib; a killable worker uses only its fresh Xvfb.
"""

import argparse
import ctypes as C
from dataclasses import asdict
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import re
import secrets
import select
import shutil
import struct
import subprocess
import sys
import time

from scripts import linux_x11 as x11


def validate_options(allowed, timeout, directory):
    if allowed is not True:
        raise ValueError("Separate native grant and --allow-native-smoke are required")
    if type(timeout) is not int or not 10 <= timeout <= 60:
        raise ValueError("Timeout must be 10..60 seconds, including cleanup reserve")
    if not isinstance(directory, str) or not re.fullmatch(
            r"linux-x11-smoke-[A-Za-z0-9][A-Za-z0-9._-]{0,79}", directory):
        raise ValueError("Receipt directory must be a fresh linux-x11-smoke-<name> in cwd")


def authority_bytes(cookie):
    if not isinstance(cookie, bytes) or len(cookie) != 16:
        raise ValueError("A 16-byte private cookie is required")
    # FamilyWild + empty display number lets -displayfd allocate a fresh number.
    fields = (b"", b"", b"MIT-MAGIC-COOKIE-1", cookie)
    return b"\xff\xff" + b"".join(struct.pack("!H", len(field)) + field for field in fields)


def process_identity(pid):
    stat = (Path("/proc") / str(pid) / "stat").read_text()
    return {"pid": pid, "start_ticks": int(stat.rsplit(")", 1)[1].split()[19])}


def source_hashes():
    return {
        path.name: hashlib.sha256(path.read_bytes()).hexdigest()
        for path in (Path(__file__).resolve(), Path(x11.__file__).resolve())
    }


def remaining(deadline):
    seconds = deadline - time.monotonic()
    if seconds <= 0:
        raise TimeoutError("Native smoke outer deadline expired")
    return seconds


class BytePump:
    """Fair, finite reads from nonblocking owned pipes, including after timeout."""

    def __init__(self):
        self.streams = {}
        self.select_error = None

    def add(self, name, descriptor, limit):
        state = {
            "fd": descriptor, "limit": limit, "data": bytearray(),
            "observed": 0, "eof": False, "truncated": False,
            "error": None, "active": False,
        }
        self.streams[name] = state
        try:
            os.set_blocking(descriptor, False)
        except (OSError, ValueError) as error:
            state["error"] = f"{name} nonblocking setup failed: {error}"[:1000]
            self.check()
        state["active"] = True

    def stop(self, name):
        if name in self.streams:
            self.streams[name]["active"] = False

    def data(self, name):
        return bytes(self.streams[name]["data"])

    def eof(self, name):
        return self.streams[name]["eof"]

    def read_once(self, timeout):
        if self.select_error:
            return False
        active = [state for state in self.streams.values() if state["active"]]
        try:
            ready, _, _ = select.select([state["fd"] for state in active], [], [], timeout)
        except (OSError, ValueError) as error:
            self.select_error = f"Owned pipe select failed: {error}"[:1000]
            return False
        progressed = False
        for state in active:
            if state["fd"] not in ready:
                continue
            capacity = state["limit"] - len(state["data"])
            try:
                chunk = os.read(state["fd"], min(4096, capacity + 1))
            except BlockingIOError:
                continue
            except OSError as error:
                state["error"] = f"Owned pipe read failure: {error}"[:1000]
                state["active"] = False
                progressed = True
                continue
            progressed = True
            if not chunk:
                state["eof"] = True
                state["active"] = False
                continue
            state["observed"] += len(chunk)
            state["data"].extend(chunk[:capacity])
            if len(chunk) > capacity:
                state["truncated"] = True
                state["active"] = False
        return progressed

    def drain_available(self, deadline=None):
        # Each progressing round consumes a byte or retires a descriptor.
        # EAGAIN/no readiness stops immediately, even after the work deadline.
        for _ in range(1 + sum(state["limit"] + 2 for state in self.streams.values())):
            if deadline is not None:
                remaining(deadline)
            if not self.read_once(0):
                return
        self.select_error = "Immediate diagnostic drain bound exceeded"

    def check(self):
        if self.select_error:
            raise RuntimeError(self.select_error)
        for name, state in self.streams.items():
            if state["error"]:
                raise RuntimeError(f"{name}: {state['error']}")
            if state["truncated"]:
                raise RuntimeError(f"{name} diagnostic bound exceeded")

    def snapshot(self):
        return {
            name: {
                "limit_bytes": state["limit"], "observed_bytes": state["observed"],
                "retained_bytes": len(state["data"]), "eof": state["eof"],
                "truncated": state["truncated"], "error": state["error"],
                "text": state["data"].decode("utf-8", "replace"),
            }
            for name, state in self.streams.items()
        }


def wait_for_display(server, pump, deadline, receipt):
    while b"\n" not in pump.data("displayfd"):
        receipt["xvfb_observed_exit_code"] = server.poll()
        if receipt["xvfb_observed_exit_code"] is not None:
            raise RuntimeError("Owned Xvfb exited before readiness")
        pump.read_once(min(0.05, remaining(deadline)))
        pump.check()
        if pump.eof("displayfd"):
            raise RuntimeError("Owned Xvfb displayfd EOF before readiness")
    number = pump.data("displayfd")
    if not re.fullmatch(rb"[0-9]{1,6}\n", number):
        raise RuntimeError("Malformed owned Xvfb display number")
    pump.stop("displayfd")
    pump.drain_available(deadline)
    pump.check()
    remaining(deadline)
    return ":" + number.decode("ascii").strip()


def wait_for_worker(worker, pump, deadline):
    while True:
        pump.read_once(min(0.05, remaining(deadline)))
        pump.check()
        if (worker.poll() is not None and pump.eof("worker_stdout")
                and pump.eof("worker_stderr")):
            remaining(deadline)
            return pump.data("worker_stdout"), pump.data("worker_stderr")


def primitive():
    display = os.environ["DISPLAY"]
    if not re.fullmatch(r":[0-9]+", display):
        raise ValueError("Only the supervisor's fresh local display is permitted")
    report = {"worker": process_identity(os.getpid()), "display": display,
              "sources": source_hashes()}
    connections = []
    expected_cleanup_errors = None
    try:
        owner = x11.open_display(display)
        connections.append(owner)
        observer = x11.open_display(display)
        connections.append(observer)
        with owner.checked() as (native, handle):
            native.XCreateSimpleWindow.argtypes = [
                C.c_void_p, C.c_ulong, C.c_int, C.c_int, C.c_uint,
                C.c_uint, C.c_uint, C.c_ulong, C.c_ulong]
            native.XCreateSimpleWindow.restype = C.c_ulong
            native.XDestroyWindow.argtypes = [C.c_void_p, C.c_ulong]
            native.XDestroyWindow.restype = C.c_int
            native.XChangeProperty.argtypes = [
                C.c_void_p, C.c_ulong, C.c_ulong, C.c_ulong,
                C.c_int, C.c_int, C.c_void_p, C.c_int]
            native.XChangeProperty.restype = C.c_int
            native.XDeleteProperty.argtypes = [C.c_void_p, C.c_ulong, C.c_ulong]
            native.XDeleteProperty.restype = C.c_int
            root = native.XDefaultRootWindow(handle)
            window = native.XCreateSimpleWindow(handle, root, 0, 0, 16, 16, 0, 0, 0)
            if not window:
                raise RuntimeError("Owned primitive window creation failed")
            atom = native.XInternAtom(handle, b"_FEBUILDER_X11_SMOKE", 0)
            value = C.create_string_buffer(b"owned smoke")
            native.XChangeProperty(handle, window, atom, 31, 8, 0, value, 11)
        report["window"] = window
        present = observer.children()
        if present != [window]:
            raise RuntimeError("Fresh server contains unexpected root children")
        report["present_before_destroy"] = present
        live = observer.property(window, "_FEBUILDER_X11_SMOKE")
        if live != "owned smoke" or observer.property(window, "_FEBUILDER_X11_MISSING") is not None:
            raise RuntimeError("Live-window success/missing-property control failed")
        report["live_value"] = live
        with owner.checked() as (native, handle):
            native.XDestroyWindow(handle, window)
        try:
            observer.property(window, "_FEBUILDER_X11_SMOKE")
        except x11.BadWindowCandidate as candidate:
            report["badwindow_request"] = asdict(candidate.request)
            report["badwindow_events"] = [asdict(event) for event in candidate.records]
            def fresh_children():
                children = observer.children()
                report["fresh_children_after_destroy"] = children
                return children
            x11.confirm_disappearance(candidate, fresh_children)
        else:
            raise RuntimeError("Destroyed owned window produced no BadWindow candidate")
        if report["fresh_children_after_destroy"] != []:
            raise RuntimeError("Fresh server acquired unexpected root children")
        try:
            with observer.checked() as (native, handle):
                native.XDeleteProperty(handle, window, atom)
        except x11.X11PropertyError as error:
            if isinstance(error, x11.BadWindowCandidate) or len(error.records) != 1:
                raise RuntimeError("Unrelated-error control misclassified") from error
            event = error.records[0]
            if not (event.request_code == 19 and event.error_code == 3
                    and event.resourceid == window and event.display == observer.display
                    and event.callback_display == observer.display):
                raise RuntimeError("Unexpected unrelated native error") from error
            expected_cleanup_errors = error.records
            report["unrelated_rejected"] = [asdict(event)]
        else:
            raise RuntimeError("Unrelated native error was suppressed")
        try:
            observer.property(window, "_FEBUILDER_X11_SMOKE")
        except x11.X11PropertyError as error:
            if isinstance(error, x11.BadWindowCandidate) or error.records != expected_cleanup_errors:
                raise RuntimeError("Pending unrelated error was stolen/discarded") from error
            report["pending_error_rejected"] = True
        else:
            raise RuntimeError("Pending unrelated error was ignored")
    finally:
        cleanup_errors = []
        for connection in reversed(connections):
            try:
                connection.close()
            except x11.X11PropertyError as error:
                if expected_cleanup_errors is None or error.records != expected_cleanup_errors:
                    cleanup_errors.append(str(error))
        if cleanup_errors:
            raise RuntimeError("Connection cleanup failed: " + "; ".join(cleanup_errors))
    report["status"] = "passed"
    return report


def supervise(timeout, directory):
    if sys.platform != "linux" or os.geteuid() == 0:
        raise RuntimeError("Smoke requires non-root Linux; no installation or elevation")
    executable = shutil.which("Xvfb")
    if not executable:
        raise RuntimeError("Already-installed Xvfb is required; nothing was installed")
    root = Path(__file__).resolve().parents[2]
    if Path.cwd().resolve() != root:
        raise ValueError("Run from the reviewed repository root")
    destination = root / directory
    destination.mkdir(mode=0o700)
    authority = destination / "authority"
    started = time.monotonic()
    deadline = started + timeout - 2
    receipt = {
        "status": "failed", "timeout_seconds": timeout,
        "started_utc": datetime.now(timezone.utc).isoformat(),
        "supervisor": process_identity(os.getpid()),
        "sources": source_hashes(), "phase": "setup",
    }
    server = worker = None
    read_fd = write_fd = None
    pump = BytePump()
    exit_code = 1
    try:
        with authority.open("xb") as stream:
            os.chmod(authority, 0o600)
            stream.write(authority_bytes(secrets.token_bytes(16)))
        environment = {
            "PATH": os.environ.get("PATH", os.defpath), "LANG": "C.UTF-8",
            "XAUTHORITY": str(authority), "PYTHONDONTWRITEBYTECODE": "1",
        }
        read_fd, write_fd = os.pipe()
        pump.add("displayfd", read_fd, 32)
        server_command = [
            executable, "-displayfd", str(write_fd), "-screen", "0", "320x240x24",
            "-nolisten", "tcp", "-auth", str(authority), "-noreset",
        ]
        server = subprocess.Popen(
            server_command, pass_fds=(write_fd,), env=environment,
            stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL,
            stderr=subprocess.PIPE, start_new_session=True)
        pump.add("xvfb_stderr", server.stderr.fileno(), 4096)
        descriptor, write_fd = write_fd, None
        os.close(descriptor)
        receipt["xvfb"] = {**process_identity(server.pid), "command": server_command}
        receipt["phase"] = "readiness"
        environment["DISPLAY"] = wait_for_display(server, pump, deadline, receipt)
        environment["FEBUILDER_X11_SMOKE_PARENT"] = str(os.getpid())
        worker_command = [
            sys.executable, "-B", "-m", "scripts.tests.linux_x11_native_smoke",
            "--allow-native-smoke", "--worker",
        ]
        remaining(deadline)
        receipt["phase"] = "worker"
        worker = subprocess.Popen(
            worker_command, cwd=root, env=environment, stdin=subprocess.DEVNULL,
            stdout=subprocess.PIPE, stderr=subprocess.PIPE, start_new_session=True)
        pump.add("worker_stdout", worker.stdout.fileno(), 16384)
        pump.add("worker_stderr", worker.stderr.fileno(), 4096)
        receipt["worker"] = {**process_identity(worker.pid), "command": worker_command}
        output, errors = wait_for_worker(worker, pump, deadline)
        receipt["phase"] = "validation"
        receipt["worker_exit_code"] = worker.returncode
        receipt["worker_stderr"] = errors.decode("utf-8", "replace")
        receipt["diagnostic"] = json.loads(output)
        if worker.returncode != 0 or receipt["diagnostic"].get("status") != "passed":
            raise RuntimeError("Native primitive worker failed")
        if receipt["diagnostic"]["sources"] != receipt["sources"]:
            raise RuntimeError("Worker source hashes differ from supervised source")
        if receipt["diagnostic"]["worker"] != {
                key: receipt["worker"][key] for key in ("pid", "start_ticks")}:
            raise RuntimeError("Worker process identity differs from supervised child")
        receipt["xvfb_observed_exit_code"] = server.poll()
        if receipt["xvfb_observed_exit_code"] is not None:
            raise RuntimeError("Owned Xvfb exited during smoke")
        receipt["status"] = "passed"
        exit_code = 0
    except BaseException as error:
        receipt["failure"] = f"{type(error).__name__}: {error}"[:1000]
    finally:
        cleanup_failures = {}

        def drain_diagnostics():
            try:
                pump.drain_available()
                pump.check()
            except BaseException as error:
                receipt["capture_failure"] = f"{type(error).__name__}: {error}"[:1000]

        drain_diagnostics()
        pump.stop("displayfd")
        for name, descriptor in (("displayfd_read", read_fd), ("displayfd_write", write_fd)):
            if descriptor is not None:
                try:
                    os.close(descriptor)
                except BaseException as error:
                    cleanup_failures[name + "_close"] = str(error)[:1000]
        for name, process in (("worker", worker), ("xvfb", server)):
            if process is not None:
                try:
                    if process.poll() is None:
                        process.kill()
                except BaseException as error:
                    cleanup_failures[name + "_kill"] = str(error)[:1000]
                try:
                    receipt[name + "_exit_code"] = process.wait(timeout=1)
                except BaseException as error:
                    cleanup_failures[name + "_wait"] = str(error)[:1000]
        drain_diagnostics()
        for name, process in (("worker", worker), ("xvfb", server)):
            if process is not None:
                for pipe_name in ("stdout", "stderr"):
                    stream = getattr(process, pipe_name)
                    if stream is not None:
                        try:
                            stream.close()
                        except BaseException as error:
                            cleanup_failures[name + "_" + pipe_name + "_close"] = str(error)[:1000]
        try:
            authority.unlink(missing_ok=True)
        except BaseException as error:
            cleanup_failures["authority_remove"] = str(error)[:1000]
        receipt["io"] = pump.snapshot()
        if cleanup_failures:
            receipt["cleanup_failures"] = cleanup_failures
        if cleanup_failures or "capture_failure" in receipt:
            receipt["status"] = "failed"
            exit_code = 1
        elapsed = time.monotonic() - started
        receipt["elapsed_seconds"] = round(elapsed, 3)
        if elapsed > timeout:
            receipt["status"] = "failed"
            receipt["deadline_exceeded"] = True
            exit_code = 1
        with (destination / "receipt.json").open("x", encoding="utf-8") as stream:
            json.dump(receipt, stream, indent=2, sort_keys=True)
            stream.write("\n")
    print(json.dumps({"status": receipt["status"], "receipt": str(destination / "receipt.json")}))
    return exit_code


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--allow-native-smoke", action="store_true")
    parser.add_argument("--timeout", type=int, default=20)
    parser.add_argument("--receipt-dir")
    parser.add_argument("--worker", action="store_true", help=argparse.SUPPRESS)
    args = parser.parse_args(argv)
    if args.worker:
        if not args.allow_native_smoke or os.environ.get("FEBUILDER_X11_SMOKE_PARENT") != str(os.getppid()):
            raise ValueError("Worker must be launched by the bounded supervisor")
        try:
            result = primitive()
        except BaseException as error:
            print(json.dumps({"status": "failed", "failure": f"{type(error).__name__}: {error}"[:1000]}))
            return 1
        print(json.dumps(result, sort_keys=True))
        return 0
    validate_options(args.allow_native_smoke, args.timeout, args.receipt_dir)
    return supervise(args.timeout, args.receipt_dir)


if __name__ == "__main__":
    raise SystemExit(main())
