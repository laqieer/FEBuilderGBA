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
        "sources": source_hashes(),
    }
    server = worker = None
    read_fd = write_fd = None
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
        server_command = [
            executable, "-displayfd", str(write_fd), "-screen", "0", "320x240x24",
            "-nolisten", "tcp", "-auth", str(authority), "-noreset",
        ]
        server = subprocess.Popen(
            server_command, pass_fds=(write_fd,), env=environment,
            stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL, start_new_session=True)
        os.close(write_fd)
        write_fd = None
        receipt["xvfb"] = {**process_identity(server.pid), "command": server_command}
        number = b""
        while b"\n" not in number:
            if server.poll() is not None:
                raise RuntimeError("Owned Xvfb exited before readiness")
            ready, _, _ = select.select([read_fd], [], [], remaining(deadline))
            if not ready:
                raise TimeoutError("Owned Xvfb readiness deadline")
            chunk = os.read(read_fd, 32)
            if not chunk or len(number) + len(chunk) > 32:
                raise RuntimeError("Invalid owned Xvfb displayfd response")
            number += chunk
        if not re.fullmatch(rb"[0-9]{1,6}\n", number):
            raise RuntimeError("Malformed owned Xvfb display number")
        environment["DISPLAY"] = ":" + number.decode("ascii").strip()
        environment["FEBUILDER_X11_SMOKE_PARENT"] = str(os.getpid())
        worker_command = [
            sys.executable, "-B", "-m", "scripts.tests.linux_x11_native_smoke",
            "--allow-native-smoke", "--worker",
        ]
        worker = subprocess.Popen(
            worker_command, cwd=root, env=environment, stdin=subprocess.DEVNULL,
            stdout=subprocess.PIPE, stderr=subprocess.PIPE, start_new_session=True)
        receipt["worker"] = {**process_identity(worker.pid), "command": worker_command}
        output, errors = worker.communicate(timeout=remaining(deadline))
        if len(output) > 16384 or len(errors) > 4096:
            raise RuntimeError("Worker diagnostic bound exceeded")
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
        if server.poll() is not None:
            raise RuntimeError("Owned Xvfb exited during smoke")
        receipt["status"] = "passed"
        exit_code = 0
    except BaseException as error:
        receipt["failure"] = f"{type(error).__name__}: {error}"[:1000]
    finally:
        for descriptor in (read_fd, write_fd):
            if descriptor is not None:
                os.close(descriptor)
        for name, process in (("worker", worker), ("xvfb", server)):
            if process is not None:
                if process.poll() is None:
                    process.kill()
                try:
                    receipt[name + "_exit_code"] = process.wait(timeout=1)
                except subprocess.TimeoutExpired:
                    receipt["status"] = "failed"
                    receipt[name + "_cleanup"] = "exact PID kill did not reap within reserve"
                    exit_code = 1
        authority.unlink(missing_ok=True)
        receipt["elapsed_seconds"] = round(time.monotonic() - started, 3)
        if receipt["elapsed_seconds"] > timeout:
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
