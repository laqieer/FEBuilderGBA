# SPDX-License-Identifier: GPL-3.0-or-later
"""Pure tests: never load Xlib, open a display, or invoke a C callback."""

import ctypes as C
from contextlib import ExitStack
from dataclasses import replace
import importlib.util
import io
import json
import sys
from pathlib import Path
import threading
import unittest
from unittest.mock import patch

from scripts import linux_x11 as x11


DISPLAY = 0x1234
WINDOW = 0x4567
REQUEST = x11.PropertyRequest(DISPLAY, 42, WINDOW)
ERROR = x11.ErrorRecord(DISPLAY, DISPLAY, 42, WINDOW, 3, 20, 0, 0)


class FakeXlib:
    """Python-only Xlib facade with real ctypes output containers."""

    def __init__(self, collector):
        self.collector = collector
        self.calls = []
        self.serial = 40
        self.status = 0
        self.actual = 1
        self.format = 8
        self.count = 5
        self.remaining = 0
        self.buffer = C.create_string_buffer(b"hello")
        self.on_get = None
        self.on_sync = None
        self.extra_requests = 0
        self.free_count = 0
        self.tree_status = 1
        self.tree_count = 1
        self.tree_buffer = (C.c_ulong * 1)(WINDOW)

    def XLockDisplay(self, display):
        self.calls.append(("lock", display))

    def XUnlockDisplay(self, display):
        self.calls.append(("unlock", display))

    def XSync(self, display, discard):
        self.calls.append(("sync", display))
        self.serial += 1
        if self.on_sync:
            self.on_sync(self, display)
        return 0

    def XInternAtom(self, display, name, only_if_exists):
        self.calls.append(("atom", display))
        self.serial += 1
        return 99

    def XNextRequest(self, display):
        self.calls.append(("serial", display))
        return self.serial

    def XGetWindowProperty(self, display, window, atom, offset, length,
                           delete, requested_type, actual, size, count,
                           remaining, data):
        self.calls.append(("get", display))
        self.last_request = x11.PropertyRequest(display, self.serial, window)
        self.serial = (self.serial + 1 + self.extra_requests) & x11.ULONG_MAX
        actual._obj.value = self.actual
        size._obj.value = self.format
        count._obj.value = self.count
        remaining._obj.value = self.remaining
        data._obj.value = C.addressof(self.buffer) if self.buffer is not None else None
        if self.on_get:
            self.on_get(self, display)
        return self.status

    def XFree(self, data):
        self.free_count += 1
        return 0

    def XDefaultRootWindow(self, display):
        return 1

    def XQueryTree(self, display, window, root, parent, children, count):
        self.calls.append(("tree", display))
        root._obj.value = 1
        parent._obj.value = 0
        count._obj.value = self.tree_count
        C.cast(children, C.POINTER(C.POINTER(C.c_ulong)))[0] = (
            C.cast(self.tree_buffer, C.POINTER(C.c_ulong))
            if self.tree_buffer is not None else C.POINTER(C.c_ulong)()
        )
        return self.tree_status

    def XCloseDisplay(self, display):
        self.calls.append(("close", display))
        return 0


def connection(display=DISPLAY, collector=None, native=None):
    collector = collector if collector is not None else x11.ErrorCollector()
    native = native if native is not None else FakeXlib(collector)
    return x11.PropertyConnection(native, display, collector), native, collector


def emit_bad_window(native, display):
    request = native.last_request
    native.collector.record(replace(
        ERROR, callback_display=display, display=display,
        serial=request.serial, resourceid=request.window))


class ImportAndLayoutTests(unittest.TestCase):
    def test_import_does_not_load_libraries_or_create_callback(self):
        spec = importlib.util.spec_from_file_location("_x11_import_test", x11.__file__)
        module = importlib.util.module_from_spec(spec)
        with patch.object(C, "CDLL", side_effect=AssertionError("native load")), \
                patch.object(C, "CFUNCTYPE", side_effect=AssertionError("callback")), \
                patch.dict(sys.modules, {spec.name: module}):
            spec.loader.exec_module(module)
        self.assertIsNone(module._runtime)

    def test_runtime_installs_one_strongly_held_handler_for_all_connections(self):
        instances = []
        class Function:
            def __init__(self, result):
                self.result = result
                self.calls = []
            def __call__(self, *args):
                self.calls.append(args)
                return self.result
        class Library:
            def __init__(self):
                for name in ("XCloseDisplay", "XLockDisplay", "XUnlockDisplay",
                             "XSync", "XNextRequest", "XInternAtom", "XDefaultRootWindow",
                             "XGetWindowProperty", "XQueryTree", "XFree", "XSetErrorHandler"):
                    setattr(self, name, Function(0))
                self.XInitThreads = Function(1)
                self.XOpenDisplay = Function(DISPLAY)
                instances.append(self)
        def callback_type(*args):
            return lambda handler: handler
        with patch.object(x11, "_runtime", None), \
                patch.object(x11.sys, "platform", "linux"), \
                patch.object(C, "CDLL", side_effect=lambda _: Library()), \
                patch.object(C, "CFUNCTYPE", side_effect=callback_type):
            first = x11.open_display(":123")
            second = x11.open_display(":124")
            self.assertEqual(len(instances), 1)
            runtime = x11._runtime
            self.assertIs(first.collector, second.collector)
            self.assertIs(runtime.x.XSetErrorHandler.calls[0][0], runtime.callback)
            first.close()
            second.close()
            self.assertEqual(len(runtime.x.XSetErrorHandler.calls), 1)
            self.assertEqual(runtime.x.XNextRequest.restype, C.c_ulong)
            self.assertEqual(runtime.x.XGetWindowProperty.argtypes[-1], C.POINTER(C.c_void_p))

    def test_xerror_event_uses_native_field_types_and_alignment(self):
        expected = [
            ("type", C.c_int), ("display", C.c_void_p),
            ("resourceid", C.c_ulong), ("serial", C.c_ulong),
            ("error_code", C.c_ubyte), ("request_code", C.c_ubyte),
            ("minor_code", C.c_ubyte),
        ]
        self.assertEqual(x11.XErrorEvent._fields_, expected)
        end = 0
        alignment = 1
        for name, kind in expected:
            alignment = max(alignment, C.alignment(kind))
            end = (end + C.alignment(kind) - 1) // C.alignment(kind) * C.alignment(kind)
            self.assertEqual(getattr(x11.XErrorEvent, name).offset, end)
            end += C.sizeof(kind)
        self.assertEqual(C.sizeof(x11.XErrorEvent),
                         (end + alignment - 1) // alignment * alignment)


class AttributionTests(unittest.TestCase):
    def test_success_requires_no_native_errors(self):
        self.assertFalse(x11.attribute_property(0, REQUEST, ()))

    def test_only_exact_badwindow_is_candidate(self):
        for status in (1, 3):
            with self.subTest(status=status):
                self.assertTrue(x11.attribute_property(status, REQUEST, (ERROR,)))

    def test_generic_statuses_are_fatal_without_native_evidence(self):
        for status in (1, 3, 2, -1, 99):
            with self.subTest(status=status):
                with self.assertRaises(x11.X11PropertyError):
                    x11.attribute_property(status, REQUEST, ())

    def test_each_wrong_or_missing_field_is_rejected(self):
        wrong = {
            "callback_display": (0, DISPLAY + 1),
            "display": (0, DISPLAY + 1),
            "serial": (0, 41, 43),
            "resourceid": (0, WINDOW + 1),
            "error_code": (0, 2, 5),
            "request_code": (0, 19, 21),
            "minor_code": (1, 255),
            "type": (1,),
        }
        for field, values in wrong.items():
            for value in values:
                with self.subTest(field=field, value=value):
                    with self.assertRaises(x11.X11PropertyError):
                        x11.attribute_property(1, REQUEST, (replace(ERROR, **{field: value}),))

    def test_duplicate_stale_and_interleaved_records_fail_closed(self):
        for records in ((ERROR, ERROR),
                        (replace(ERROR, serial=41), ERROR),
                        (ERROR, replace(ERROR, display=5678, callback_display=5678))):
            with self.subTest(records=records):
                with self.assertRaises(x11.X11PropertyError):
                    x11.attribute_property(1, REQUEST, records)

    def test_success_or_unexpected_status_with_error_is_inconsistent(self):
        for status in (0, 2, 99):
            with self.subTest(status=status):
                with self.assertRaises(x11.X11PropertyError):
                    x11.attribute_property(status, REQUEST, (ERROR,))

    def test_evidence_rejects_noninteger_fields_and_invalid_requests(self):
        for field, value in (("type", False), ("minor_code", False),
                             ("serial", 42.0), ("error_code", 3.0)):
            with self.subTest(field=field), self.assertRaises(x11.X11PropertyError):
                x11.attribute_property(1, REQUEST, (replace(ERROR, **{field: value}),))
        for field, value in (("display", 0), ("serial", -1), ("window", True)):
            with self.subTest(field=field), self.assertRaises(x11.X11PropertyError):
                x11.attribute_property(0, replace(REQUEST, **{field: value}), ())


class CollectorTests(unittest.TestCase):
    def test_capacity_has_a_hard_bound(self):
        for capacity in (0, -1, True, 257):
            with self.subTest(capacity=capacity), self.assertRaises(ValueError):
                x11.ErrorCollector(capacity=capacity)

    def test_callback_copies_typed_record_without_native_work(self):
        collector = x11.ErrorCollector()
        event = x11.XErrorEvent(0, DISPLAY, WINDOW, 42, 3, 20, 0)
        self.assertEqual(collector.capture(DISPLAY, C.pointer(event)), 0)
        event.resourceid = 999
        self.assertEqual(collector.snapshot(), (ERROR,))

    def test_overflow_is_bounded_sticky_and_does_not_drop_old_records(self):
        collector = x11.ErrorCollector(capacity=2)
        for _ in range(4):
            collector.record(ERROR)
        self.assertEqual(len(collector._records), 2)
        for _ in range(2):
            with self.assertRaisesRegex(x11.X11PropertyError, "overflow"):
                collector.snapshot()
        self.assertEqual(tuple(collector._records), (ERROR, ERROR))

    def test_callback_fault_is_sticky(self):
        collector = x11.ErrorCollector()
        self.assertEqual(collector.capture(DISPLAY, None), 0)
        with self.assertRaisesRegex(x11.X11PropertyError, "callback"):
            collector.snapshot()

    def test_callback_never_waits_for_collector_lock(self):
        collector = x11.ErrorCollector()
        collector._lock.acquire()
        try:
            self.assertFalse(collector.record(ERROR))
        finally:
            collector._lock.release()
        with self.assertRaisesRegex(x11.X11PropertyError, "callback"):
            collector.snapshot()

    def test_consume_rejects_changed_snapshot_without_discarding(self):
        collector = x11.ErrorCollector()
        collector.record(ERROR)
        records = collector.snapshot()
        other = replace(ERROR, display=5678, callback_display=5678)
        collector.record(other)
        with self.assertRaises(x11.X11PropertyError):
            collector.consume(records)
        self.assertEqual(collector.snapshot(), (ERROR, other))


class PropertyTests(unittest.TestCase):
    def test_successful_text_order_and_cleanup(self):
        conn, native, _ = connection()
        self.assertEqual(conn.property(WINDOW, "WM_NAME"), "hello")
        self.assertEqual([item[0] for item in native.calls],
                         ["lock", "sync", "atom", "sync", "serial", "get",
                          "serial", "sync", "unlock"])
        self.assertEqual(native.free_count, 1)

    def test_missing_empty_utf8_and_cardinal_payloads(self):
        cases = [
            (0, 0, 0, None, None),
            (1, 8, 0, None, ""),
            (1, 8, 1, C.create_string_buffer(b"\xff"), "\ufffd"),
            (1, 32, 1, (C.c_ulong * 1)(0xFFFFFFFF), 0xFFFFFFFF),
        ]
        for actual, size, count, buffer, expected in cases:
            with self.subTest(size=size, count=count, actual=actual):
                conn, native, _ = connection()
                native.actual, native.format, native.count, native.buffer = (
                    actual, size, count, buffer)
                self.assertEqual(conn.property(WINDOW, "TEST"), expected)
                self.assertEqual(native.free_count, int(buffer is not None))

    def test_payload_bound_and_malformed_payload_cleanup(self):
        cases = [
            {"remaining": 1}, {"count": 4097}, {"format": 16},
            {"format": 32, "count": 2}, {"actual": 0},
            {"format": 0}, {"count": 1, "buffer": None},
            {"actual": 0, "format": 0, "count": 1},
        ]
        for values in cases:
            with self.subTest(values=values):
                conn, native, _ = connection()
                for key, value in values.items():
                    setattr(native, key, value)
                with self.assertRaises(x11.X11PropertyError):
                    conn.property(WINDOW, "TEST")
                self.assertEqual(native.free_count, int(native.buffer is not None))

    def test_maximum_payload_is_accepted(self):
        conn, native, _ = connection()
        native.count = 4096
        native.buffer = C.create_string_buffer(b"a" * 4096)
        self.assertEqual(conn.property(WINDOW, "TEST"), "a" * 4096)

    def test_error_cleanup_including_native_call_exception(self):
        for status in (1, 3, 99):
            with self.subTest(status=status):
                conn, native, _ = connection()
                native.status = status
                with self.assertRaises(x11.X11PropertyError):
                    conn.property(WINDOW, "TEST")
                self.assertEqual(native.free_count, 1)
        conn, native, _ = connection()
        def fail(*args):
            raise RuntimeError("injected")
        native.on_get = fail
        with self.assertRaisesRegex(RuntimeError, "injected"):
            conn.property(WINDOW, "TEST")
        self.assertEqual(native.free_count, 1)
        self.assertEqual(native.calls[-1][0], "unlock")

    def test_badwindow_is_only_candidate_after_post_sync(self):
        conn, native, collector = connection()
        native.status = 1
        def delayed_error(fake, display):
            if hasattr(fake, "last_request"):
                emit_bad_window(fake, display)
        native.on_sync = delayed_error
        with self.assertRaises(x11.BadWindowCandidate) as raised:
            conn.property(WINDOW, "TEST")
        self.assertEqual(raised.exception.request, native.last_request)
        self.assertEqual(raised.exception.records[0].resourceid, WINDOW)
        self.assertEqual(collector.snapshot(), ())
        self.assertEqual(native.free_count, 1)

    def test_pending_and_atom_errors_rejected_before_get(self):
        for phase in ("pending", "atom"):
            with self.subTest(phase=phase):
                conn, native, collector = connection()
                if phase == "pending":
                    collector.record(ERROR)
                else:
                    def atom_error(fake, display):
                        if ("atom", display) in fake.calls:
                            fake.collector.record(replace(ERROR, request_code=16))
                    native.on_sync = atom_error
                with self.assertRaises(x11.X11PropertyError):
                    conn.property(WINDOW, "TEST")
                self.assertNotIn(("get", DISPLAY), native.calls)
                self.assertTrue(collector.snapshot())

    def test_interleaved_display_is_not_consumed_or_tolerated(self):
        conn, native, collector = connection()
        native.status = 1
        def interleaved(fake, display):
            emit_bad_window(fake, display)
            fake.collector.record(replace(ERROR, display=5678, callback_display=5678))
        native.on_get = interleaved
        with self.assertRaises(x11.X11PropertyError) as raised:
            conn.property(WINDOW, "TEST")
        self.assertNotIsInstance(raised.exception, x11.BadWindowCandidate)
        self.assertEqual(len(collector.snapshot()), 2)
        self.assertEqual(native.free_count, 1)

    def test_serial_interleaving_and_overflow_are_fatal(self):
        for mode in ("serial", "overflow", "callback"):
            with self.subTest(mode=mode):
                collector = x11.ErrorCollector(capacity=1)
                conn, native, _ = connection(collector=collector)
                native.status = 1
                def bad(fake, display):
                    emit_bad_window(fake, display)
                    if mode == "overflow":
                        emit_bad_window(fake, display)
                    if mode == "callback":
                        collector.capture(display, None)
                native.on_get = bad
                native.extra_requests = int(mode == "serial")
                with self.assertRaises(x11.X11PropertyError) as raised:
                    conn.property(WINDOW, "TEST")
                self.assertNotIsInstance(raised.exception, x11.BadWindowCandidate)
                self.assertEqual(native.free_count, 1)

    def test_invalid_inputs_fail_before_native_calls(self):
        for window, name in ((0, "TEST"), (-1, "TEST"), (2**32, "TEST"),
                             (True, "TEST"), (WINDOW, ""), (WINDOW, "A\0B"),
                             (WINDOW, "a" * 256), (WINDOW, b"TEST")):
            with self.subTest(window=window, name=name):
                conn, native, _ = connection()
                with self.assertRaises((ValueError, TypeError)):
                    conn.property(window, name)
                self.assertEqual(native.calls, [])

    def test_connections_serialize_whole_transactions(self):
        collector = x11.ErrorCollector()
        conn1, native1, _ = connection(collector=collector)
        conn2, native2, _ = connection(DISPLAY + 1, collector=collector)
        entered, release, started = threading.Event(), threading.Event(), threading.Event()
        errors = []
        def pause(fake, display):
            entered.set()
            if not release.wait(2):
                raise RuntimeError("test release timed out")
        native1.on_get = pause
        def run(conn, signal=None):
            try:
                if signal:
                    signal.set()
                conn.property(WINDOW, "TEST")
            except BaseException as error:
                errors.append(error)
        first = threading.Thread(target=run, args=(conn1,))
        second = threading.Thread(target=run, args=(conn2, started))
        first.start()
        try:
            self.assertTrue(entered.wait(2))
            second.start()
            self.assertTrue(started.wait(2))
            self.assertEqual(native2.calls, [])
        finally:
            release.set()
            first.join(3)
            if second.ident:
                second.join(3)
        self.assertFalse(first.is_alive())
        self.assertFalse(second.is_alive())
        self.assertEqual(errors, [])
        self.assertIn(("get", DISPLAY + 1), native2.calls)


class ConfirmationTests(unittest.TestCase):
    def candidate(self):
        return x11.BadWindowCandidate(REQUEST, (ERROR,))

    def test_fresh_absence_confirms_but_live_window_is_fatal(self):
        calls = []
        def absent():
            calls.append("fresh")
            return [WINDOW + 1]
        self.assertTrue(x11.confirm_disappearance(self.candidate(), absent))
        self.assertEqual(calls, ["fresh"])
        with self.assertRaisesRegex(x11.X11PropertyError, "still"):
            x11.confirm_disappearance(self.candidate(), lambda: [WINDOW])

    def test_failed_or_malformed_confirmation_is_fatal(self):
        def failed():
            raise RuntimeError("tree failed")
        for query in (failed, lambda: None, lambda: [0], lambda: [True],
                      lambda: [1, 1], lambda: list(range(1, 202))):
            with self.subTest(query=query):
                with self.assertRaises(x11.X11PropertyError):
                    x11.confirm_disappearance(self.candidate(), query)
        with self.assertRaises((TypeError, x11.X11PropertyError)):
            x11.confirm_disappearance(RuntimeError("status 3"), lambda: [])

    def test_confirmation_rechecks_candidate_evidence(self):
        candidate = x11.BadWindowCandidate(REQUEST, (replace(ERROR, serial=41),))
        with self.assertRaises(x11.X11PropertyError):
            x11.confirm_disappearance(candidate, lambda: [])

    def test_native_tree_facade_checks_errors_bounds_and_cleanup(self):
        conn, native, _ = connection()
        self.assertEqual(conn.children(), [WINDOW])
        self.assertEqual(native.free_count, 1)
        for values in ({"tree_status": 0}, {"tree_count": 201},
                       {"tree_count": 1, "tree_buffer": None}):
            with self.subTest(values=values):
                conn, native, _ = connection()
                for key, value in values.items():
                    setattr(native, key, value)
                with self.assertRaises(x11.X11PropertyError):
                    conn.children()
                self.assertEqual(native.free_count, int(native.tree_buffer is not None))
        conn, native, collector = connection()
        native.on_sync = lambda fake, display: collector.record(ERROR)
        with self.assertRaises(x11.X11PropertyError):
            conn.children()
        self.assertNotIn(("tree", DISPLAY), native.calls)

    def test_close_releases_connection_even_when_collector_is_poisoned(self):
        conn, native, collector = connection()
        collector.record(ERROR)
        with self.assertRaises(x11.X11PropertyError):
            conn.close()
        self.assertIn(("close", DISPLAY), native.calls)
        count = len(native.calls)
        conn.close()
        self.assertEqual(len(native.calls), count)
        with self.assertRaises(x11.X11PropertyError):
            conn.property(WINDOW, "TEST")


class SmokeSourceTests(unittest.TestCase):
    def smoke_module(self):
        spec = importlib.util.spec_from_file_location(
            "_x11_smoke_test", Path(__file__).with_name("linux_x11_native_smoke.py"))
        module = importlib.util.module_from_spec(spec)
        with patch.object(C, "CDLL", side_effect=AssertionError("native load")), \
                patch("subprocess.Popen", side_effect=AssertionError("process launch")):
            spec.loader.exec_module(module)
        return module

    def test_smoke_import_is_inert_and_requires_explicit_grant_flag(self):
        smoke = self.smoke_module()
        with self.assertRaises(ValueError):
            smoke.validate_options(False, 20, "linux-x11-smoke-test")
        with patch.object(smoke, "supervise", side_effect=AssertionError("native supervisor")):
            with self.assertRaises(ValueError):
                smoke.main([])
        with patch.dict("os.environ", {}, clear=True), \
                patch.object(smoke, "primitive", side_effect=AssertionError("native primitive")):
            with self.assertRaises(ValueError):
                smoke.main(["--allow-native-smoke", "--worker"])

    def test_smoke_deadline_and_output_name_are_bounded(self):
        smoke = self.smoke_module()
        smoke.validate_options(True, 20, "linux-x11-smoke-test")
        for timeout in (-1, 0, 9, 61, True):
            with self.subTest(timeout=timeout), self.assertRaises(ValueError):
                smoke.validate_options(True, timeout, "linux-x11-smoke-test")
        for directory in ("", ".", "..", "/tmp/run", "C:\\temp\\run",
                          "linux-x11-smoke-../a", "linux-x11-smoke-"):
            with self.subTest(directory=directory), self.assertRaises(ValueError):
                smoke.validate_options(True, 20, directory)

    def test_smoke_authority_is_cookie_only_not_access_control_disable(self):
        smoke = self.smoke_module()
        data = smoke.authority_bytes(bytes(range(16)))
        self.assertEqual(data[:2], b"\xff\xff")
        self.assertIn(b"MIT-MAGIC-COOKIE-1", data)
        self.assertEqual(data[-18:], b"\x00\x10" + bytes(range(16)))

    def test_smoke_hashes_exact_sources_and_rejects_elapsed_deadline(self):
        smoke = self.smoke_module()
        hashes = smoke.source_hashes()
        self.assertEqual(set(hashes), {"linux_x11.py", "linux_x11_native_smoke.py"})
        self.assertTrue(all(len(digest) == 64 for digest in hashes.values()))
        with patch.object(smoke.time, "monotonic", return_value=10):
            self.assertEqual(smoke.remaining(11), 1)
            with self.assertRaises(TimeoutError):
                smoke.remaining(10)


class FakeSmokeIO:
    """In-memory descriptors and clock; no pipes or processes are created."""

    def __init__(self):
        self.events = {}
        self.now = 0
        self.tick = 0.01
        self.selections = []
        self.reads = []
        self.nonblocking = []
        self.closed = []

    def select(self, readers, writers, errors, timeout):
        self.selections.append((tuple(readers), timeout))
        ready = [fd for fd in readers if self.events.get(fd)]
        self.now += min(self.tick, timeout) if ready else timeout
        return ready, [], []

    def read(self, descriptor, count):
        self.reads.append((descriptor, count))
        event = self.events[descriptor][0]
        if callable(event):
            event = event()
        else:
            self.events[descriptor].pop(0)
            if isinstance(event, bytes) and len(event) > count:
                self.events[descriptor].insert(0, event[count:])
        if isinstance(event, BaseException):
            raise event
        return event[:count]

    def set_blocking(self, descriptor, blocking):
        if blocking:
            raise AssertionError("Blocking pipe requested")
        self.nonblocking.append(descriptor)

    def close(self, descriptor):
        if descriptor in self.closed:
            raise AssertionError("Descriptor closed twice")
        self.closed.append(descriptor)


class FakeSmokeStream:
    def __init__(self, pipes, descriptor):
        self.pipes = pipes
        self.descriptor = descriptor

    def fileno(self):
        return self.descriptor

    def close(self):
        self.pipes.close(self.descriptor)


class FakeSmokeProcess:
    def __init__(self, pipes, pid, stdout=None, stderr=None, code=None):
        self.pid = pid
        self.stdout = FakeSmokeStream(pipes, stdout) if stdout is not None else None
        self.stderr = FakeSmokeStream(pipes, stderr) if stderr is not None else None
        self.returncode = code
        self.killed = False
        self.waits = []
        self.wait_error = None
        self.kill_error = None

    def poll(self):
        return self.returncode

    def kill(self):
        self.killed = True
        if self.kill_error:
            raise self.kill_error
        self.returncode = -9

    def wait(self, timeout):
        self.waits.append(timeout)
        if self.wait_error:
            raise self.wait_error
        return self.returncode


class RetainedSmokeReceipt(io.StringIO):
    def close(self):
        self.saved = self.getvalue()
        super().close()


class SmokeDiagnosticsTests(unittest.TestCase):
    def setUp(self):
        self.smoke = SmokeSourceTests.smoke_module(self)
        self.pipes = FakeSmokeIO()
        self.stack = ExitStack()
        self.addCleanup(self.stack.close)
        for target, name, replacement in (
                (self.smoke.os, "read", self.pipes.read),
                (self.smoke.os, "set_blocking", self.pipes.set_blocking),
                (self.smoke.os, "close", self.pipes.close),
                (self.smoke.select, "select", self.pipes.select),
                (self.smoke.time, "monotonic", lambda: self.pipes.now)):
            self.stack.enter_context(patch.object(target, name, replacement, create=True))
        self.stack.enter_context(patch.object(C, "CDLL", side_effect=AssertionError("native load")))
        self.stack.enter_context(patch.object(
            self.smoke, "primitive", side_effect=AssertionError("native primitive")))
        self.stack.enter_context(patch.object(
            self.smoke.subprocess, "Popen", side_effect=AssertionError("real process")))
        self.stack.enter_context(patch.object(
            self.smoke.os, "pipe", side_effect=AssertionError("real pipe")))

    def pump(self, events, limit=4096):
        self.pipes.events = {10: list(events)}
        pump = self.smoke.BytePump()
        pump.add("xvfb_stderr", 10, limit)
        return pump

    def prepare_supervisor(self, events=None, server_code=None, spawn_error=None):
        smoke = self.smoke
        self.server = FakeSmokeProcess(self.pipes, 101, stderr=12, code=server_code)
        self.worker = FakeSmokeProcess(self.pipes, 102, stdout=20, stderr=21, code=0)
        self.spawned = []
        hashes = {"linux_x11.py": "a" * 64, "linux_x11_native_smoke.py": "b" * 64}
        self.worker_report = {
            "status": "passed", "sources": hashes,
            "worker": {"pid": 102, "start_ticks": 202},
            "live_value": "owned smoke", "pending_error_rejected": True,
        }
        self.pipes.events = {
            10: [b"7\n"], 12: [],
            20: [json.dumps(self.worker_report).encode(), b""], 21: [b""],
        }
        if events:
            self.pipes.events.update(events)
        self.receipt_file = RetainedSmokeReceipt()

        def fake_open(path, mode, **kwargs):
            if path.name == "authority" and mode == "xb":
                return io.BytesIO()
            if path.name == "receipt.json" and mode == "x":
                return self.receipt_file
            raise AssertionError("Unexpected file access")

        def spawn(command, **kwargs):
            index = len(self.spawned)
            if spawn_error == index:
                raise OSError("injected spawn failure")
            process = (self.server, self.worker)[index]
            if index == 0:
                self.assertEqual(kwargs["stderr"], smoke.subprocess.PIPE)
                self.assertEqual(kwargs["pass_fds"], (11,))
            self.spawned.append(process)
            return process

        self.stack.enter_context(patch.object(smoke.sys, "platform", "linux"))
        self.stack.enter_context(patch.object(smoke.os, "geteuid", return_value=1000, create=True))
        self.stack.enter_context(patch.object(smoke.shutil, "which", return_value="/fake/Xvfb"))
        self.stack.enter_context(patch.object(
            Path, "cwd", return_value=Path(smoke.__file__).resolve().parents[2]))
        self.stack.enter_context(patch.object(Path, "mkdir"))
        self.stack.enter_context(patch.object(Path, "open", fake_open))
        self.unlink = self.stack.enter_context(patch.object(Path, "unlink"))
        self.stack.enter_context(patch.object(smoke.os, "chmod"))
        self.stack.enter_context(patch.object(smoke.os, "pipe", return_value=(10, 11)))
        self.stack.enter_context(patch.object(smoke.secrets, "token_bytes", return_value=b"x" * 16))
        self.stack.enter_context(patch.object(smoke, "source_hashes", return_value=hashes))
        self.stack.enter_context(patch.object(
            smoke, "process_identity", side_effect=lambda pid: {"pid": pid, "start_ticks": pid + 100}))
        self.stack.enter_context(patch.object(smoke.subprocess, "Popen", side_effect=spawn))
        self.stack.enter_context(patch("builtins.print"))

    def supervise(self):
        code = self.smoke.supervise(20, "linux-x11-smoke-pure-test")
        receipt = json.loads(self.receipt_file.saved)
        self.assertEqual(code == 0, receipt["status"] == "passed")
        self.unlink.assert_called_once_with(missing_ok=True)
        return code, receipt

    def test_stderr_exact_cap_without_newline_is_retained(self):
        pump = self.pump([b"x" * 4096, b""])
        pump.drain_available()
        pump.check()
        diagnostic = pump.snapshot()["xvfb_stderr"]
        self.assertEqual(pump.data("xvfb_stderr"), b"x" * 4096)
        self.assertEqual(diagnostic["observed_bytes"], 4096)
        self.assertEqual(diagnostic["retained_bytes"], 4096)
        self.assertTrue(diagnostic["eof"])
        self.assertFalse(diagnostic["truncated"])
        self.assertTrue(all(timeout == 0 for _, timeout in self.pipes.selections))

    def test_oversized_stderr_reads_only_cap_plus_sentinel_and_fails(self):
        pump = self.pump([b"x" * 100000])
        pump.drain_available()
        with self.assertRaisesRegex(RuntimeError, "xvfb_stderr.*bound"):
            pump.check()
        diagnostic = pump.snapshot()["xvfb_stderr"]
        self.assertEqual(diagnostic["observed_bytes"], 4097)
        self.assertEqual(diagnostic["retained_bytes"], 4096)
        self.assertTrue(diagnostic["truncated"])
        self.assertFalse(diagnostic["eof"])
        self.assertEqual(sum(count for _, count in self.pipes.reads), 4097)
        before = len(self.pipes.reads)
        pump.drain_available()
        self.assertEqual(len(self.pipes.reads), before)

    def test_partial_invalid_utf8_and_eagain_do_not_block_or_invent_eof(self):
        pump = self.pump([BlockingIOError(), b"long", b"\xff", b""])
        pump.read_once(0)
        self.assertFalse(pump.snapshot()["xvfb_stderr"]["eof"])
        pump.drain_available()
        pump.check()
        self.assertEqual(pump.snapshot()["xvfb_stderr"]["text"], "long\ufffd")
        self.assertEqual(pump.snapshot()["xvfb_stderr"]["observed_bytes"], 5)

    def test_eof_descriptors_are_removed_and_never_reread(self):
        pump = self.pump([b""])
        pump.read_once(0)
        pump.read_once(0)
        self.assertEqual(self.pipes.reads, [(10, 4096)])
        self.assertNotIn(10, self.pipes.selections[-1][0])

    def test_read_error_fails_closed_without_starving_other_ready_stream(self):
        pump = self.pump([OSError("injected read failure")])
        self.pipes.events[11] = [b"other diagnostic", b""]
        pump.add("worker_stderr", 11, 4096)
        pump.drain_available()
        with self.assertRaisesRegex(RuntimeError, "read failure"):
            pump.check()
        self.assertEqual(pump.data("worker_stderr"), b"other diagnostic")
        self.assertIn("injected read failure", pump.snapshot()["xvfb_stderr"]["error"])

    def test_select_error_is_bounded_failure_not_a_cleanup_exception(self):
        pump = self.pump([b"unread"])
        with patch.object(self.smoke.select, "select", side_effect=OSError("select failure")):
            pump.drain_available()
            with self.assertRaisesRegex(RuntimeError, "select failure"):
                pump.check()

    def test_no_acquired_pipes_is_not_a_capture_failure(self):
        pump = self.smoke.BytePump()
        pump.drain_available()
        pump.check()
        self.assertEqual(pump.snapshot(), {})

    def test_work_phase_immediate_drain_rechecks_original_deadline_each_round(self):
        pump = self.pump([lambda: b"x"])
        original = self.pipes.read

        def slow_read(descriptor, count):
            self.pipes.now += 0.25
            return original(descriptor, count)

        with patch.object(self.smoke.os, "read", side_effect=slow_read):
            with self.assertRaises(TimeoutError):
                pump.drain_available(deadline=1)
        self.assertEqual(pump.snapshot()["xvfb_stderr"]["observed_bytes"], 4)

    def test_each_ready_stream_gets_one_bounded_read_per_round(self):
        pump = self.pump([b"x" * 4096])
        self.pipes.events.update({11: [b"7\n"], 20: [b"y" * 16384], 21: [b"z"]})
        pump.add("displayfd", 11, 32)
        pump.add("worker_stdout", 20, 16384)
        pump.add("worker_stderr", 21, 4096)
        pump.read_once(0)
        self.assertEqual([fd for fd, _ in self.pipes.reads], [10, 11, 20, 21])
        self.assertTrue(all(count <= 4096 for _, count in self.pipes.reads))

    def test_early_exit_preserves_known_stderr_and_observed_final_status(self):
        self.prepare_supervisor({10: [b""], 12: [b"known startup failure\n", b""]}, server_code=1)
        code, receipt = self.supervise()
        self.assertEqual(code, 1)
        self.assertEqual(receipt["phase"], "readiness")
        self.assertIn("exited before readiness", receipt["failure"])
        self.assertEqual(receipt["xvfb_observed_exit_code"], 1)
        self.assertEqual(receipt["xvfb_exit_code"], 1)
        self.assertEqual(receipt["io"]["xvfb_stderr"]["text"], "known startup failure\n")
        self.assertTrue(receipt["io"]["displayfd"]["eof"])
        self.assertEqual(self.spawned, [self.server])
        self.assertCountEqual(self.pipes.closed, [10, 11, 12])
        self.assertEqual(self.server.waits, [1])

    def test_displayfd_eof_preserves_stderr_without_claiming_server_exit(self):
        self.prepare_supervisor({10: [b""], 12: [b"known EOF diagnostic", b""]})
        code, receipt = self.supervise()
        self.assertEqual(code, 1)
        self.assertIn("displayfd EOF", receipt["failure"])
        self.assertIsNone(receipt["xvfb_observed_exit_code"])
        self.assertEqual(receipt["io"]["xvfb_stderr"]["text"], "known EOF diagnostic")
        self.assertEqual(len(self.spawned), 1)

    def test_valid_readiness_worker_controls_and_all_pipe_closures_are_preserved(self):
        self.prepare_supervisor({10: [b"7", b"\n"], 12: [b"startup note"]})
        code, receipt = self.supervise()
        self.assertEqual(code, 0)
        self.assertEqual(receipt["diagnostic"], self.worker_report)
        self.assertEqual(receipt["io"]["xvfb_stderr"]["text"], "startup note")
        self.assertCountEqual(self.pipes.closed, [10, 11, 12, 20, 21])
        self.assertCountEqual(self.pipes.nonblocking, [10, 12, 20, 21])
        self.assertEqual(self.worker.waits, [1])
        self.assertEqual(self.server.waits, [1])
        self.assertFalse(self.worker.killed)
        self.assertTrue(self.server.killed)

    def test_malformed_and_oversized_displayfd_never_launch_worker(self):
        for response, expected in ((b":7\n", "Malformed"), (b"7" * 33, "bound"),
                                   (b"1234567\n", "Malformed"), (b"7\n8\n", "Malformed")):
            with self.subTest(response=response):
                self.pipes.closed.clear()
                self.prepare_supervisor({10: [response]})
                code, receipt = self.supervise()
                self.assertEqual(code, 1)
                self.assertIn(expected, receipt["failure"])
                self.assertEqual(len(self.spawned), 1)

    def test_stderr_overflow_is_fatal_during_readiness_and_worker_wait(self):
        for late in (False, True):
            with self.subTest(late=late):
                self.pipes.closed.clear()
                self.prepare_supervisor({12: [
                    lambda: b"x" * 4097 if len(self.spawned) == 2 else BlockingIOError()
                ] if late else [b"x" * 4097]})
                code, receipt = self.supervise()
                self.assertEqual(code, 1)
                self.assertTrue(receipt["io"]["xvfb_stderr"]["truncated"])
                self.assertEqual(receipt["io"]["xvfb_stderr"]["observed_bytes"], 4097)
                self.assertEqual(len(self.spawned), 2 if late else 1)

    def test_worker_output_caps_still_fail_closed(self):
        for descriptor, limit in ((20, 16384), (21, 4096)):
            with self.subTest(descriptor=descriptor):
                self.pipes.closed.clear()
                self.prepare_supervisor({descriptor: [b"x" * (limit + 1)]})
                code, receipt = self.supervise()
                name = "worker_stdout" if descriptor == 20 else "worker_stderr"
                self.assertEqual(code, 1)
                self.assertEqual(receipt["io"][name]["retained_bytes"], limit)
                self.assertTrue(receipt["io"][name]["truncated"])
                self.assertCountEqual(self.pipes.closed, [10, 11, 12, 20, 21])

    def test_worker_hash_identity_status_and_server_liveness_checks_remain_fatal(self):
        for change in ("sources", "worker", "status", "server_exit", "worker_exit"):
            with self.subTest(change=change):
                self.pipes.closed.clear()
                self.prepare_supervisor()
                if change == "server_exit":
                    self.worker.poll = lambda: setattr(self.server, "returncode", 1) or 0
                elif change == "worker_exit":
                    self.worker.returncode = 1
                else:
                    self.worker_report[change] = {} if change != "status" else "failed"
                    self.pipes.events[20] = [json.dumps(self.worker_report).encode(), b""]
                code, _ = self.supervise()
                self.assertEqual(code, 1)

    def test_continuous_input_cannot_starve_readiness_deadline(self):
        self.prepare_supervisor({10: [], 12: [lambda: b"x"]})
        self.pipes.tick = 0.05
        code, receipt = self.supervise()
        self.assertEqual(code, 1)
        self.assertIn("TimeoutError", receipt["failure"])
        self.assertEqual(len(self.spawned), 1)
        self.assertTrue(self.server.killed)
        self.assertLessEqual(receipt["elapsed_seconds"], 20)

    def test_expired_deadline_cleanup_uses_only_zero_timeout_drains(self):
        self.prepare_supervisor({10: []})
        original = self.smoke.remaining
        expired_calls = []

        def checked_remaining(deadline):
            if self.pipes.now >= deadline:
                expired_calls.append(len(self.pipes.selections))
                self.pipes.events[12] = [b"available at timeout", b""]
            return original(deadline)

        with patch.object(self.smoke, "remaining", side_effect=checked_remaining):
            code, receipt = self.supervise()
        self.assertEqual(code, 1)
        self.assertIn("TimeoutError", receipt["failure"])
        self.assertEqual(len(expired_calls), 1, "Cleanup must not call remaining()")
        self.assertTrue(all(timeout == 0 for _, timeout in
                            self.pipes.selections[expired_calls[0]:]))
        self.assertEqual(receipt["io"]["xvfb_stderr"]["text"], "available at timeout")
        self.assertTrue(receipt["io"]["xvfb_stderr"]["eof"])
        self.assertCountEqual(self.pipes.closed, [10, 11, 12])

    def test_worker_with_closed_pipes_but_no_exit_still_has_deadline(self):
        self.prepare_supervisor()
        self.worker.returncode = None
        code, receipt = self.supervise()
        self.assertEqual(code, 1)
        self.assertIn("TimeoutError", receipt["failure"])
        self.assertTrue(self.worker.killed)
        self.assertTrue(self.server.killed)

    def test_spawn_failures_close_all_acquired_descriptors(self):
        for failure in (0, 1):
            with self.subTest(failure=failure):
                self.pipes.closed.clear()
                self.prepare_supervisor(spawn_error=failure)
                code, receipt = self.supervise()
                self.assertEqual(code, 1)
                self.assertIn("injected spawn failure", receipt["failure"])
                self.assertCountEqual(self.pipes.closed, [10, 11] if failure == 0 else [10, 11, 12])

    def test_pipe_and_nonblocking_setup_failures_close_acquired_handles(self):
        for failing_descriptor in (None, 10, 12, 20, 21):
            with self.subTest(failing_descriptor=failing_descriptor):
                self.pipes.closed.clear()
                self.prepare_supervisor()

                def configure(descriptor, blocking):
                    if descriptor == failing_descriptor:
                        raise OSError("injected nonblocking failure")
                    self.pipes.set_blocking(descriptor, blocking)

                with patch.object(self.smoke.os, "set_blocking", side_effect=configure), \
                        patch.object(self.smoke.os, "pipe",
                                     side_effect=OSError("injected pipe failure")
                                     if failing_descriptor is None else None,
                                     return_value=(10, 11)):
                    code, _ = self.supervise()
                self.assertEqual(code, 1)
                expected = [] if failing_descriptor is None else [10, 11]
                if self.spawned:
                    expected.append(12)
                if len(self.spawned) == 2:
                    expected.extend([20, 21])
                self.assertCountEqual(self.pipes.closed, expected)

    def test_cleanup_errors_do_not_skip_other_processes_handles_or_receipt(self):
        self.prepare_supervisor()
        self.worker.wait_error = self.smoke.subprocess.TimeoutExpired("owned worker", 1)
        self.server.kill_error = OSError("injected owned kill failure")
        code, receipt = self.supervise()
        self.assertEqual(code, 1)
        self.assertIn("worker_wait", receipt["cleanup_failures"])
        self.assertIn("xvfb_kill", receipt["cleanup_failures"])
        self.assertEqual(self.server.waits, [1])
        self.assertCountEqual(self.pipes.closed, [10, 11, 12, 20, 21])

    def test_descriptor_close_and_authority_failures_cannot_skip_receipt(self):
        self.prepare_supervisor()
        original = self.pipes.close

        def failing_close(descriptor):
            original(descriptor)
            if descriptor == 10:
                raise OSError("injected descriptor close failure")

        self.unlink.side_effect = OSError("injected authority removal failure")
        with patch.object(self.smoke.os, "close", side_effect=failing_close):
            code, receipt = self.supervise()
        self.assertEqual(code, 1)
        self.assertIn("displayfd_read_close", receipt["cleanup_failures"])
        self.assertIn("authority_remove", receipt["cleanup_failures"])
        self.assertCountEqual(self.pipes.closed, [10, 11, 12, 20, 21])

    def test_deadline_overrun_during_owned_cleanup_cannot_pass(self):
        self.prepare_supervisor()
        original = self.server.wait

        def delayed_wait(timeout):
            self.pipes.now = 20.1
            return original(timeout)

        self.server.wait = delayed_wait
        code, receipt = self.supervise()
        self.assertEqual(code, 1)
        self.assertTrue(receipt["deadline_exceeded"])
        self.assertCountEqual(self.pipes.closed, [10, 11, 12, 20, 21])


if __name__ == "__main__":
    unittest.main()
