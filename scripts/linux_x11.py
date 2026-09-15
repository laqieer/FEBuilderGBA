# SPDX-License-Identifier: GPL-3.0-or-later
"""Bounded X11 property/error seam for a dedicated, owned-display process.

Import is pure. Only open_display() loads Xlib and installs the process handler.
Do not mix this runtime with another Xlib error handler or uncoordinated users
of its display connections. Native calls still require an outer process deadline.
"""

from contextlib import contextmanager
import ctypes as C
from dataclasses import dataclass
import sys
import threading


GET_PROPERTY = 20
BAD_WINDOW = 3
ULONG_MAX = (1 << (8 * C.sizeof(C.c_ulong))) - 1
XID_MAX = (1 << 32) - 1
MAX_PROPERTY_BYTES = 4096
MAX_CHILDREN = 200
_TRANSACTIONS = threading.RLock()
_runtime = None


class XErrorEvent(C.Structure):
    _fields_ = [
        ("type", C.c_int),
        ("display", C.c_void_p),
        ("resourceid", C.c_ulong),
        ("serial", C.c_ulong),
        ("error_code", C.c_ubyte),
        ("request_code", C.c_ubyte),
        ("minor_code", C.c_ubyte),
    ]


@dataclass(frozen=True)
class ErrorRecord:
    callback_display: int
    display: int
    serial: int
    resourceid: int
    error_code: int
    request_code: int
    minor_code: int
    type: int


@dataclass(frozen=True)
class PropertyRequest:
    display: int
    serial: int
    window: int


class X11PropertyError(RuntimeError):
    def __init__(self, message, records=()):
        super().__init__(message)
        self.records = tuple(records)


class BadWindowCandidate(X11PropertyError):
    """Attributable BadWindow, NOT proof that a window disappeared."""

    def __init__(self, request, records):
        super().__init__(
            f"GetProperty BadWindow candidate: display={request.display:#x}, "
            f"serial={request.serial}, xid={request.window:#x}", records)
        self.request = request


def _integer(value, minimum, maximum):
    return type(value) is int and minimum <= value <= maximum


def attribute_property(status, request, records):
    """Pure decision: True means candidate only; every ambiguity raises."""
    if not isinstance(request, PropertyRequest) or not (
        _integer(request.display, 1, (1 << (8 * C.sizeof(C.c_void_p))) - 1)
        and _integer(request.serial, 0, ULONG_MAX)
        and _integer(request.window, 1, XID_MAX)
    ):
        raise X11PropertyError("Invalid property request evidence", records)
    if type(status) is not int:
        raise X11PropertyError("Invalid property status", records)
    if status == 0 and not records:
        return False
    if status in (1, BAD_WINDOW) and len(records) == 1:
        event = records[0]
        if isinstance(event, ErrorRecord) and all(
            type(getattr(event, field)) is int for field in ErrorRecord.__dataclass_fields__
        ) and (
            event.callback_display == event.display == request.display
            and event.serial == request.serial
            and event.resourceid == request.window
            and event.error_code == BAD_WINDOW
            and event.request_code == GET_PROPERTY
            and event.minor_code == 0
            and event.type == 0
        ):
            return True
    raise X11PropertyError(
        f"Unattributed/inconsistent GetProperty failure: status={status}, "
        f"events={len(records)}, serial={request.serial}", records)


class ErrorCollector:
    """One native-runtime collector; fake instances are for pure tests only.

    No automatic eviction or recovery: unmatched evidence and sticky faults
    survive the checked boundary. Restart the owned process after a fatal error.
    """

    def __init__(self, capacity=64):
        if not _integer(capacity, 1, 256):
            raise ValueError("Collector capacity must be in 1..256")
        self.capacity = capacity
        self._records = []
        self._lock = threading.Lock()
        self._overflow = False
        self._callback_fault = False

    def capture(self, display, event_pointer):
        # Called by the C callback: copy only, never call Xlib or wait for a lock.
        try:
            event = event_pointer.contents
            self.record(ErrorRecord(
                int(display or 0), int(event.display or 0), int(event.serial),
                int(event.resourceid), int(event.error_code),
                int(event.request_code), int(event.minor_code), int(event.type)))
        except BaseException:
            self._callback_fault = True
        return 0

    def record(self, event):
        if not self._lock.acquire(blocking=False):
            self._callback_fault = True
            return False
        try:
            if len(self._records) >= self.capacity:
                self._overflow = True
                return False
            self._records.append(event)
            return True
        except BaseException:
            self._callback_fault = True
            return False
        finally:
            self._lock.release()

    def _check_faults(self):
        if self._callback_fault:
            raise X11PropertyError("X11 callback collection fault", self._records)
        if self._overflow:
            raise X11PropertyError("X11 error collector overflow", self._records)

    def snapshot(self):
        with self._lock:
            self._check_faults()
            return tuple(self._records)

    def require_empty(self):
        records = self.snapshot()
        if records:
            raise X11PropertyError("Pending/unmatched X11 errors", records)

    def consume(self, expected):
        with self._lock:
            self._check_faults()
            if tuple(self._records) != expected:
                raise X11PropertyError("Interleaved X11 errors", self._records)
            self._records.clear()


def _name_bytes(name):
    if not isinstance(name, str):
        raise TypeError("An explicit string name is required")
    encoded = name.encode("utf-8")
    if not 1 <= len(encoded) <= 255 or b"\0" in encoded:
        raise ValueError("Name must be 1..255 bytes with no NUL")
    return encoded


def _children_list(children):
    if not isinstance(children, (list, tuple, set, frozenset)) or len(children) > MAX_CHILDREN:
        raise X11PropertyError("Invalid/bounded root-child confirmation")
    if any(not _integer(window, 1, XID_MAX) for window in children):
        raise X11PropertyError("Malformed root-child XID")
    if len(set(children)) != len(children):
        raise X11PropertyError("Duplicate root-child XIDs")
    return list(children)


def confirm_disappearance(candidate, fresh_root_children):
    """Observer must supply a fresh, checked query of the same display's root."""
    if not isinstance(candidate, BadWindowCandidate):
        raise TypeError("Only an attributable BadWindow candidate can be confirmed")
    attribute_property(1, candidate.request, candidate.records)
    try:
        children = _children_list(fresh_root_children())
    except Exception as error:
        raise X11PropertyError("Fresh root-child confirmation failed", candidate.records) from error
    if candidate.request.window in children:
        raise X11PropertyError("BadWindow candidate is still a root child", candidate.records)
    return True


def _decode_property(actual, size, count, remaining, data):
    if remaining:
        raise X11PropertyError(f"Property exceeds {MAX_PROPERTY_BYTES}-byte bound")
    if actual == size == count == 0:
        return None
    if not actual or (count and not data.value):
        raise X11PropertyError("Malformed property type/data")
    if size == 32 and count == 1:
        # Xlib expands protocol CARD32 values into native unsigned-long slots.
        return C.cast(data, C.POINTER(C.c_ulong))[0] & XID_MAX
    if size == 8 and count <= MAX_PROPERTY_BYTES:
        return C.string_at(data, count).decode("utf-8", "replace") if count else ""
    raise X11PropertyError(f"Malformed property: type={actual}, format={size}, count={count}")


class PropertyConnection:
    """Owns a connection; construct natively with open_display(), not a borrowed handle."""

    def __init__(self, xlib, display, collector):
        if not _integer(display, 1, (1 << (8 * C.sizeof(C.c_void_p))) - 1):
            raise ValueError("A non-null owned Display pointer is required")
        self._x = xlib
        self.display = display
        self.collector = collector
        self._closed = False

    @contextmanager
    def _locked(self):
        with _TRANSACTIONS:
            if self._closed:
                raise X11PropertyError("Display is closed")
            self._x.XLockDisplay(self.display)
            try:
                yield
            finally:
                self._x.XUnlockDisplay(self.display)

    def _sync_clean(self):
        self._x.XSync(self.display, 0)
        self.collector.require_empty()

    @contextmanager
    def checked(self):
        """Serialize owned primitive calls; no error is tolerated in this scope."""
        with self._locked():
            self._sync_clean()
            try:
                yield self._x, self.display
            finally:
                self._sync_clean()

    def property(self, window, name):
        """v12-compatible text/single-CARD32/absent result, strict error evidence."""
        if not _integer(window, 1, XID_MAX):
            raise ValueError("Window must be a nonzero protocol XID")
        atom_name = _name_bytes(name)
        with self._locked():
            self._sync_clean()
            atom = self._x.XInternAtom(self.display, atom_name, 0)
            self._sync_clean()
            if not atom:
                raise X11PropertyError("Property atom unavailable")
            request = PropertyRequest(
                self.display, self._x.XNextRequest(self.display), window)
            actual, size, count = C.c_ulong(), C.c_int(), C.c_ulong()
            remaining, data = C.c_ulong(), C.c_void_p()
            try:
                try:
                    status = self._x.XGetWindowProperty(
                        self.display, window, atom, 0, MAX_PROPERTY_BYTES // 4, 0, 0,
                        C.byref(actual), C.byref(size), C.byref(count),
                        C.byref(remaining), C.byref(data))
                    following = self._x.XNextRequest(self.display)
                finally:
                    self._x.XSync(self.display, 0)
                records = self.collector.snapshot()
                if following != ((request.serial + 1) & ULONG_MAX):
                    raise X11PropertyError("Interleaved GetProperty request serials", records)
                candidate = attribute_property(status, request, records)
                if candidate:
                    self.collector.consume(records)
                    result = None
                else:
                    result = _decode_property(
                        actual.value, size.value, count.value, remaining.value, data)
            finally:
                if data.value:
                    self._x.XFree(data)
            self.collector.require_empty()
            if candidate:
                raise BadWindowCandidate(request, records)
            return result

    def children(self):
        """Fresh, bounded root children for lifecycle confirmation, never cached."""
        with self.checked():
            root_window = self._x.XDefaultRootWindow(self.display)
            root, parent, count = C.c_ulong(), C.c_ulong(), C.c_uint()
            data = C.POINTER(C.c_ulong)()
            try:
                status = self._x.XQueryTree(
                    self.display, root_window, C.byref(root), C.byref(parent),
                    C.byref(data), C.byref(count))
                self._sync_clean()
                if not status or root.value != root_window or parent.value != 0:
                    raise X11PropertyError("Root-child query failed")
                if count.value > MAX_CHILDREN or (count.value and not data):
                    raise X11PropertyError("Root-child payload bound/data failure")
                return _children_list([data[index] for index in range(count.value)])
            finally:
                if data:
                    self._x.XFree(C.cast(data, C.c_void_p))

    def close(self):
        with _TRANSACTIONS:
            if self._closed:
                return
            try:
                self._x.XCloseDisplay(self.display)
            finally:
                self._closed = True
            self.collector.require_empty()

    def __enter__(self):
        return self

    def __exit__(self, *exc):
        self.close()


class _NativeRuntime:
    def __init__(self):
        if sys.platform != "linux":
            raise RuntimeError("Native X11 binding is Linux-only")
        self.x = C.CDLL("libX11.so.6")
        self.x.XInitThreads.argtypes = []
        self.x.XInitThreads.restype = C.c_int
        if not self.x.XInitThreads():
            raise X11PropertyError("XInitThreads unavailable")
        signatures = {
            "XOpenDisplay": (C.c_void_p, [C.c_char_p]),
            "XCloseDisplay": (C.c_int, [C.c_void_p]),
            "XLockDisplay": (None, [C.c_void_p]),
            "XUnlockDisplay": (None, [C.c_void_p]),
            "XSync": (C.c_int, [C.c_void_p, C.c_int]),
            "XNextRequest": (C.c_ulong, [C.c_void_p]),
            "XInternAtom": (C.c_ulong, [C.c_void_p, C.c_char_p, C.c_int]),
            "XDefaultRootWindow": (C.c_ulong, [C.c_void_p]),
            "XGetWindowProperty": (C.c_int, [
                C.c_void_p, C.c_ulong, C.c_ulong, C.c_long, C.c_long,
                C.c_int, C.c_ulong, C.POINTER(C.c_ulong), C.POINTER(C.c_int),
                C.POINTER(C.c_ulong), C.POINTER(C.c_ulong), C.POINTER(C.c_void_p)]),
            "XQueryTree": (C.c_int, [
                C.c_void_p, C.c_ulong, C.POINTER(C.c_ulong), C.POINTER(C.c_ulong),
                C.POINTER(C.POINTER(C.c_ulong)), C.POINTER(C.c_uint)]),
            "XFree": (C.c_int, [C.c_void_p]),
        }
        for name, (result, arguments) in signatures.items():
            function = getattr(self.x, name)
            function.restype, function.argtypes = result, arguments
        self.collector = ErrorCollector()
        callback_type = C.CFUNCTYPE(C.c_int, C.c_void_p, C.POINTER(XErrorEvent))
        self.callback = callback_type(self.collector.capture)
        self.x.XSetErrorHandler.argtypes = [callback_type]
        self.x.XSetErrorHandler.restype = C.c_void_p
        self.x.XSetErrorHandler(self.callback)


def open_display(name):
    """Explicit native opt-in; caller must own the named display and process."""
    encoded = _name_bytes(name)
    global _runtime
    with _TRANSACTIONS:
        if _runtime is None:
            _runtime = _NativeRuntime()
        _runtime.collector.require_empty()
        display = _runtime.x.XOpenDisplay(encoded)
        if not display:
            raise X11PropertyError("Explicit owned display unavailable")
        connection = PropertyConnection(_runtime.x, display, _runtime.collector)
        try:
            with connection.checked():
                pass
        except BaseException:
            connection.close()
            raise
        return connection
