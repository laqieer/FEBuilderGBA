"""ROM project management — open, info, save, close."""

import hashlib
import os
import shutil
import stat
import tempfile
from contextlib import contextmanager

from cli_anything.febuildergba.utils.febuildergba_backend import (
    is_prebuilt_backend_only,
    register_rom_snapshot,
)


_MIN_ROM_SIZE = 0x100000
_MAX_ROM_SIZE = 0x2000000
_GBA_HEADER_SIZE = 0xC0
_GBA_FIXED_VALUE_OFFSET = 0xB2
_GBA_FIXED_VALUE = 0x96
_GBA_CHECKSUM_START = 0xA0
_GBA_CHECKSUM_OFFSET = 0xBD
_GBA_CHECKSUM_BIAS = 0x19
_SNAPSHOT_CHUNK_BYTES = 64 * 1024


def _rom_open_flags(write: bool = False) -> int:
    flags = os.O_RDWR if write else os.O_RDONLY
    for name in ("O_BINARY", "O_NONBLOCK", "O_CLOEXEC", "O_NOINHERIT"):
        flags |= getattr(os, name, 0)
    return flags


def _validate_opened_rom_size(rom_path: str, opened_stat) -> int:
    """Validate the regular-file and size invariants of an open descriptor."""
    if not stat.S_ISREG(opened_stat.st_mode):
        raise ValueError(f"Invalid GBA ROM (not a regular file): {rom_path}")
    rom_size = opened_stat.st_size
    if rom_size < _MIN_ROM_SIZE:
        raise ValueError(f"Invalid GBA ROM (smaller than 1 MiB): {rom_path}")
    if rom_size > _MAX_ROM_SIZE:
        raise ValueError(f"Invalid GBA ROM (larger than 32 MiB): {rom_path}")
    return rom_size


def _validate_opened_rom_header(
        rom_path: str, header: bytes, require_checksum: bool) -> None:
    """Validate header invariants read from an already-open descriptor."""
    if len(header) < _GBA_HEADER_SIZE:
        raise ValueError(f"Invalid GBA ROM (incomplete header): {rom_path}")
    if header[_GBA_FIXED_VALUE_OFFSET] != _GBA_FIXED_VALUE:
        raise ValueError(f"Invalid GBA ROM (missing fixed header byte): {rom_path}")
    if require_checksum:
        expected_checksum = (
            -sum(header[_GBA_CHECKSUM_START:_GBA_CHECKSUM_OFFSET])
            - _GBA_CHECKSUM_BIAS
        ) & 0xFF
        if header[_GBA_CHECKSUM_OFFSET] != expected_checksum:
            raise ValueError(
                f"Invalid GBA ROM (header checksum mismatch): {rom_path}",
            )


@contextmanager
def _open_validated_rom(rom_path: str, require_checksum: bool = True,
                         write: bool = False):
    """Yield one validated, rewound ROM descriptor with its trusted header.

    ``write=True`` opens the descriptor read-write (``O_RDWR``) and keeps it
    open for the entire body of the ``with`` block — used by
    ``mutating_rom_snapshot`` so the eventual write-back targets the exact
    same open file the source bytes were validated/hashed from.
    """
    fd = os.open(rom_path, _rom_open_flags(write))
    try:
        opened_stat = os.fstat(fd)
        rom_size = _validate_opened_rom_size(rom_path, opened_stat)
        mode = "r+b" if write else "rb"
        with os.fdopen(fd, mode, closefd=True) as stream:
            fd = None
            header = stream.read(_GBA_HEADER_SIZE)
            _validate_opened_rom_header(rom_path, header, require_checksum)
            stream.seek(0)
            yield stream, header, rom_size
    finally:
        if fd is not None:
            os.close(fd)


def _read_validated_header(
        rom_path: str, require_checksum: bool = True) -> tuple[bytes, int]:
    """Read and validate a GBA header from the same open file handle."""
    with _open_validated_rom(rom_path, require_checksum) as (_stream, header, size):
        return header, size


def _copy_validated_rom_bytes(source, header: bytes, rom_size: int,
                               rom_path: str, destination, hasher=None) -> None:
    """Copy exactly ``rom_size`` validated bytes from *source* to
    *destination*, rejecting any growth/shrink/change observed on *source*
    while copying, optionally folding every written chunk into *hasher*.

    *source* must already be rewound to offset 0, with *header* holding its
    first ``_GBA_HEADER_SIZE`` bytes (the shape yielded by
    ``_open_validated_rom``).
    """
    written = destination.write(header)
    if written != len(header):
        raise OSError("Failed to write complete ROM snapshot")
    if hasher is not None:
        hasher.update(header)
    copied = written
    source.seek(len(header))
    while copied < rom_size:
        chunk = source.read(min(_SNAPSHOT_CHUNK_BYTES, rom_size - copied))
        if not chunk:
            raise ValueError(
                f"Invalid GBA ROM (changed while snapshotting): {rom_path}",
            )
        written = destination.write(chunk)
        if written != len(chunk):
            raise OSError("Failed to write complete ROM snapshot")
        if hasher is not None:
            hasher.update(chunk)
        copied += written
    if source.read(1) or os.fstat(source.fileno()).st_size != rom_size:
        raise ValueError(
            f"Invalid GBA ROM (changed while snapshotting): {rom_path}",
        )
    destination.flush()
    if os.fstat(destination.fileno()).st_size != rom_size:
        raise OSError("ROM snapshot length verification failed")


def _hash_stream_exact(stream, size: int) -> bytes:
    """Return the SHA-256 digest of exactly *size* bytes read from the start
    of *stream*, raising ``ValueError`` if it is shorter or longer."""
    hasher = hashlib.sha256()
    stream.seek(0)
    remaining = size
    while remaining > 0:
        chunk = stream.read(min(_SNAPSHOT_CHUNK_BYTES, remaining))
        if not chunk:
            raise ValueError("ROM content shrank while the backend ran")
        hasher.update(chunk)
        remaining -= len(chunk)
    if stream.read(1):
        raise ValueError("ROM content grew while the backend ran")
    return hasher.digest()


@contextmanager
def validated_rom_snapshot(rom_path: str, require_checksum: bool = True):
    """Yield a secure snapshot copied from one validated ROM descriptor.

    The source pathname is opened exactly once.  The backend can safely open
    the returned snapshot after the original path has been replaced, removed,
    or otherwise changed by another process.
    """
    snapshot_path = None
    snapshot_fd = None
    try:
        with _open_validated_rom(rom_path, require_checksum) as (
                source, header, rom_size):
            snapshot_fd, snapshot_path = tempfile.mkstemp(suffix=".gba")
            with os.fdopen(snapshot_fd, "wb", closefd=True) as snapshot:
                snapshot_fd = None
                _copy_validated_rom_bytes(source, header, rom_size, rom_path, snapshot)
        with register_rom_snapshot(snapshot_path):
            yield snapshot_path
    finally:
        if snapshot_fd is not None:
            os.close(snapshot_fd)
        if snapshot_path is not None:
            try:
                os.unlink(snapshot_path)
            except FileNotFoundError:
                pass


class MutatingRomSnapshot:
    """A private, backend-writable ROM snapshot paired with the original
    writable descriptor it will eventually be committed back through.

    Obtained from :func:`mutating_rom_snapshot`.  ``path`` is the registered
    snapshot the backend must be pointed at.  The original file is left
    completely untouched unless and until :meth:`commit` is called *and*
    succeeds — callers should only call ``commit()`` after confirming the
    backend itself reported success (exit code 0).
    """

    def __init__(self, path: str, original_stream, original_path: str,
                 original_size: int, original_digest: bytes,
                 require_checksum: bool):
        self.path = path
        self.committed = False
        self._original_stream = original_stream
        self._original_path = original_path
        self._original_size = original_size
        self._original_digest = original_digest
        self._require_checksum = require_checksum

    def commit(self) -> None:
        """Revalidate the mutated snapshot and write it back through the
        original file descriptor, iff every safety check below passes.

        Order of checks (fail closed — raise, commit nothing — on any of
        them):

        1. The mutated snapshot must still be a regular, 1..32 MiB file with
           a valid GBA header (and checksum, when ``require_checksum``).
        2. The original path must still identify the exact same file this
           descriptor was opened from.  This is checked with ``os.stat(path)``
           against ``os.fstat(fd)`` via ``os.path.samestat`` *only* — never a
           string/normcase path comparison fallback.
        3. The bytes originally read through the descriptor must be byte-for
           -byte unchanged (same size, same content) since the snapshot was
           taken.

        Only then does this seek/write/truncate/flush/``os.fsync`` the
        original descriptor, followed by a final size + identity re-check.

        This write-back is identity-safe (it only ever targets the exact
        descriptor/path validated immediately beforehand) but it is **not
        crash-atomic**: interruption during write/truncate/flush/fsync can
        leave partially updated or mixed old/new bytes, retain an old
        trailing suffix when the replacement is shorter, or leave completed
        writes not durably persisted. It does not provide all-or-nothing
        replacement.
        """
        with _open_validated_rom(self.path, self._require_checksum) as (
                mutated_stream, _mutated_header, mutated_size):
            mutated_stream.seek(0)
            mutated_bytes = mutated_stream.read(mutated_size)
            if len(mutated_bytes) != mutated_size:
                raise OSError("Failed to read complete mutated ROM snapshot")

        try:
            current_path_stat = os.stat(self._original_path)
        except OSError as exc:
            raise ValueError(
                "Refusing to commit: cannot re-stat the original ROM path "
                f"(it may have been removed/replaced): {self._original_path!r}"
            ) from exc
        fd_stat = os.fstat(self._original_stream.fileno())
        if not os.path.samestat(current_path_stat, fd_stat):
            raise ValueError(
                "Refusing to commit: the ROM path no longer identifies the "
                "originally opened file — it was replaced while the backend "
                f"ran: {self._original_path!r}"
            )

        digest = _hash_stream_exact(self._original_stream, self._original_size)
        if digest != self._original_digest:
            raise ValueError(
                "Refusing to commit: the original ROM's content changed "
                f"while the backend ran: {self._original_path!r}"
            )

        self._original_stream.seek(0)
        written = self._original_stream.write(mutated_bytes)
        if written != len(mutated_bytes):
            raise OSError("Failed to write complete mutated ROM back")
        self._original_stream.truncate(len(mutated_bytes))
        self._original_stream.flush()
        os.fsync(self._original_stream.fileno())

        post_fd_stat = os.fstat(self._original_stream.fileno())
        if post_fd_stat.st_size != len(mutated_bytes):
            raise OSError("ROM write-back size verification failed")
        try:
            post_path_stat = os.stat(self._original_path)
        except OSError as exc:
            raise ValueError(
                "Cannot verify the ROM path immediately after write-back: "
                f"{self._original_path!r}"
            ) from exc
        if not os.path.samestat(post_path_stat, post_fd_stat):
            raise ValueError(
                "Refusing to confirm commit: the ROM path no longer "
                f"identifies the just-written file: {self._original_path!r}"
            )
        self.committed = True


@contextmanager
def mutating_rom_snapshot(rom_path: str, require_checksum: bool = True):
    """Hold the original ROM descriptor open read-write and yield a
    :class:`MutatingRomSnapshot` wrapping a private, validated snapshot for
    the backend to mutate.

    The original file is opened exactly once and kept open (read-write) for
    the entire body of the ``with`` block, so a later ``commit()`` writes
    back through the identical descriptor the source bytes/digest were
    captured from — never by reopening or replacing the caller's pathname.
    The snapshot file is always removed on the way out, whether or not a
    commit happened.
    """
    snapshot_path = None
    snapshot_fd = None
    try:
        with _open_validated_rom(rom_path, require_checksum, write=True) as (
                original_stream, header, rom_size):
            snapshot_fd, snapshot_path = tempfile.mkstemp(suffix=".gba")
            hasher = hashlib.sha256()
            with os.fdopen(snapshot_fd, "wb", closefd=True) as snapshot:
                snapshot_fd = None
                _copy_validated_rom_bytes(
                    original_stream, header, rom_size, rom_path, snapshot, hasher)
            original_stream.seek(0)
            with register_rom_snapshot(snapshot_path):
                yield MutatingRomSnapshot(
                    snapshot_path,
                    original_stream,
                    os.path.abspath(rom_path),
                    rom_size,
                    hasher.digest(),
                    require_checksum,
                )
    finally:
        if snapshot_fd is not None:
            os.close(snapshot_fd)
        if snapshot_path is not None:
            try:
                os.unlink(snapshot_path)
            except FileNotFoundError:
                pass


class _DirectRomHandle:
    """Legacy/Click counterpart to :class:`MutatingRomSnapshot`, used
    outside MCP's ``prebuilt_backend_only`` scope (issue #1942 / PR #1971).

    The backend is handed the caller's own path directly, exactly as every
    mutating wrapper always did before this fix, so there is nothing to
    write back — ``commit()`` only marks itself as having run.
    """

    def __init__(self, path: str):
        self.path = path
        self.committed = False

    def commit(self) -> None:
        self.committed = True


@contextmanager
def backend_rom_snapshot(rom_path: str, require_checksum: bool = True):
    """Read-only ROM access for one backend invocation (issue #1942 / PR
    #1971): private snapshot inside MCP, unchanged direct path outside it.

    Inside MCP's ``prebuilt_backend_only`` dynamic scope this validates
    *rom_path* and yields a private, registered snapshot — see
    ``validated_rom_snapshot``, which this delegates to. Outside that scope
    this performs no local validation at all and yields *rom_path*
    unchanged: the historic, direct-path Click behavior every read-only
    wrapper other than ``lint_rom`` had before this fix, preserved
    byte-for-byte. ``lint_rom`` predates this fix and always used
    ``validated_rom_snapshot`` directly and unconditionally; it does not go
    through this function.
    """
    if not is_prebuilt_backend_only():
        yield rom_path
        return
    with validated_rom_snapshot(rom_path, require_checksum) as snapshot_path:
        yield snapshot_path


@contextmanager
def backend_mutating_rom_snapshot(rom_path: str, require_checksum: bool = True):
    """Mutating ROM access for one backend invocation (issue #1942 / PR
    #1971): private snapshot + validated write-back inside MCP, unchanged
    direct path outside it.

    Inside MCP's ``prebuilt_backend_only`` dynamic scope this yields a
    :class:`MutatingRomSnapshot` bound to a private, backend-writable copy —
    see ``mutating_rom_snapshot``, which this delegates to. Outside that
    scope this yields a :class:`_DirectRomHandle` whose ``path`` is
    *rom_path* itself and whose ``commit()`` is a no-op, because the backend
    already wrote directly to the caller's original file: the historic,
    direct-path Click behavior ``data_import``/``palette_import`` had before
    this fix, preserved byte-for-byte.
    """
    if not is_prebuilt_backend_only():
        yield _DirectRomHandle(rom_path)
        return
    with mutating_rom_snapshot(rom_path, require_checksum) as mutator:
        yield mutator


def validate_checksum_target(rom_path: str) -> None:
    """Reject non-ROM paths before invoking the checksum backend.

    A mismatched header checksum is intentionally allowed because measuring
    that mismatch is the checksum command's purpose.
    """
    _read_validated_header(rom_path, require_checksum=False)


def checksum_header(rom_path: str) -> dict:
    """Compute the header checksum from one bounded, validated descriptor."""
    header, _rom_size = _read_validated_header(
        rom_path,
        require_checksum=False,
    )
    actual = header[_GBA_CHECKSUM_OFFSET]
    expected = (
        -sum(header[_GBA_CHECKSUM_START:_GBA_CHECKSUM_OFFSET])
        - _GBA_CHECKSUM_BIAS
    ) & 0xFF
    valid = actual == expected
    return {
        "exit_code": 0 if valid else 2,
        "stdout": (
            f"Header checksum: 0x{actual:02X} "
            f"(expected: 0x{expected:02X})"
        ),
        "stderr": "",
        "rom_path": rom_path,
        "valid": valid,
        "actual": f"0x{actual:02X}",
        "expected": f"0x{expected:02X}",
    }


def _detect_version_from_header(header: bytes, force_version: str = "") -> str:
    if force_version:
        return force_version
    if len(header) >= 0xB0:
        game_code = header[0xAC:0xB0].decode("ascii", errors="replace")
        version_map = {
            "AFEJ": "FE6",
            "AE7J": "FE7J",
            "AE7E": "FE7U",
            "BE8J": "FE8J",
            "BE8E": "FE8U",
        }
        return version_map.get(game_code, f"unknown ({game_code})")
    return "unknown"


def rom_info(rom_path: str, force_version: str = "") -> dict:
    """Get ROM metadata from one locally validated ROM descriptor.

    This deliberately does not invoke the backend.  Call ``lint_rom`` for an
    explicit full lint run against a validated temporary snapshot.

    Args:
        rom_path: Path to the .gba ROM file.
        force_version: Optional forced version (FE6, FE7J, FE7U, FE8J, FE8U).

    Returns:
        Dict with ROM metadata.
    """
    header, rom_size = _read_validated_header(rom_path)

    # Detect version from the already validated header (no second path read).
    detected_version = _detect_version_from_header(header, force_version)

    return {
        "rom_path": os.path.abspath(rom_path),
        "rom_size": rom_size,
        "rom_size_hex": f"0x{rom_size:X}",
        "rom_size_mb": round(rom_size / (1024 * 1024), 2),
        "detected_version": detected_version,
        "force_version": force_version,
        # Permanent sentinels: metadata discovery never attempts lint.
        "lint_output": "",
        "lint_exit_code": -1,
    }


def _detect_version(rom_path: str, force_version: str = "") -> str:
    """Detect ROM version from file header."""
    if force_version:
        return force_version

    try:
        header, _rom_size = _read_validated_header(rom_path)
        return _detect_version_from_header(header)
    except (FileNotFoundError, ValueError, OSError, TypeError):
        pass
    return "unknown"


def validate_rom(rom_path: str) -> bool:
    """Check if file looks like a valid GBA ROM.

    Validates:
    - File exists and is between 1 MiB and 32 MiB
    - File is at least 0xC0 bytes (full GBA header)
    - Fixed header byte at 0xB2 is 0x96
    - Header complement checksum at 0xBD matches bytes 0xA0..0xBC
    """
    try:
        _read_validated_header(rom_path)
        return True
    except (FileNotFoundError, ValueError, OSError, TypeError):
        return False


def list_tables() -> list[str]:
    """List all supported struct data tables."""
    return [
        "units", "classes", "items", "portraits", "sound_room",
        "sound_boss_bgm", "support_units", "support_talks",
        "support_attributes", "event_haiku", "event_battle_talk",
        "event_force_sortie", "worldmap_points", "worldmap_paths",
        "worldmap_bgm", "map_settings", "link_arena_deny", "cc_branch",
        "menu_definitions", "item_weapon_triangle", "map_exit_points",
        "ai_map_settings", "ai_perform_items", "ai_perform_staff",
        "ai_steal_items", "ai_targets", "generic_enemy_portraits",
        "status_options", "ed_retreat", "ed_epithet",
        "ed_epilogue_a", "ed_epilogue_b", "ed_epilogue_c",
        "op_class_demo", "op_class_font", "op_prologue",
        "class_alpha_names", "summon_units", "summons_demon_king",
        "monster_probability",
    ]


def rom_header(rom_path: str) -> dict:
    """Dump raw GBA header fields from a ROM file.

    Pure Python — reads the ROM binary directly, no backend needed.

    Args:
        rom_path: Path to the .gba ROM file.

    Returns:
        Dict with header fields: title, game_code, maker_code,
        unit_code, device_type, software_version, header_checksum.
    """
    header, _rom_size = _read_validated_header(rom_path)

    title = header[0xA0:0xAC].decode("ascii", errors="replace").rstrip("\x00")
    game_code = header[0xAC:0xB0].decode("ascii", errors="replace")
    maker_code = header[0xB0:0xB2].decode("ascii", errors="replace")
    unit_code = header[0xB3]
    device_type = header[0xB4]
    software_version = header[0xBC]
    header_checksum = header[0xBD]

    return {
        "rom_path": os.path.abspath(rom_path),
        "title": title,
        "game_code": game_code,
        "maker_code": maker_code,
        "unit_code": unit_code,
        "device_type": device_type,
        "software_version": software_version,
        "header_checksum": header_checksum,
    }


def save_rom(rom_path: str, output_path: str) -> dict:
    """Copy a ROM file to a new location.

    Pure Python — copies the file, no backend needed.

    Args:
        rom_path: Path to the source ROM file.
        output_path: Destination path for the copy.

    Returns:
        Dict with copy results.
    """
    if not os.path.isfile(rom_path):
        raise FileNotFoundError(f"ROM file not found: {rom_path}")

    abs_src = os.path.abspath(rom_path)
    abs_dst = os.path.abspath(output_path)

    if os.path.normcase(abs_src) == os.path.normcase(abs_dst):
        raise ValueError(f"Source and destination are the same file: {abs_src}")

    shutil.copy2(abs_src, abs_dst)
    file_size = os.path.getsize(abs_dst)

    return {
        "source": abs_src,
        "output_path": abs_dst,
        "file_size": file_size,
    }
