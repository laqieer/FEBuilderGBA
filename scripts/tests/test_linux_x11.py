# SPDX-License-Identifier: GPL-3.0-or-later
"""Pure tests: never load Xlib, open a display, or invoke a C callback."""

import ctypes as C
from dataclasses import replace
import importlib.util
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


if __name__ == "__main__":
    unittest.main()
