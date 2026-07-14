"""ROM project management — open, info, save, close."""

import os
import shutil
import stat
import tempfile
from contextlib import contextmanager


_MIN_ROM_SIZE = 0x100000
_MAX_ROM_SIZE = 0x2000000
_GBA_HEADER_SIZE = 0xC0
_GBA_FIXED_VALUE_OFFSET = 0xB2
_GBA_FIXED_VALUE = 0x96
_GBA_CHECKSUM_START = 0xA0
_GBA_CHECKSUM_OFFSET = 0xBD
_GBA_CHECKSUM_BIAS = 0x19
_SNAPSHOT_CHUNK_BYTES = 64 * 1024


def _rom_open_flags() -> int:
    flags = os.O_RDONLY
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
def _open_validated_rom(rom_path: str, require_checksum: bool = True):
    """Yield one validated, rewound ROM descriptor with its trusted header."""
    fd = os.open(rom_path, _rom_open_flags())
    try:
        opened_stat = os.fstat(fd)
        rom_size = _validate_opened_rom_size(rom_path, opened_stat)
        with os.fdopen(fd, "rb", closefd=True) as stream:
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
                written = snapshot.write(header)
                if written != len(header):
                    raise OSError("Failed to write complete ROM snapshot")
                copied = written
                source.seek(len(header))
                while copied < rom_size:
                    chunk = source.read(min(_SNAPSHOT_CHUNK_BYTES, rom_size - copied))
                    if not chunk:
                        raise ValueError(
                            f"Invalid GBA ROM (changed while snapshotting): {rom_path}",
                        )
                    written = snapshot.write(chunk)
                    if written != len(chunk):
                        raise OSError("Failed to write complete ROM snapshot")
                    copied += written
                if source.read(1) or os.fstat(source.fileno()).st_size != rom_size:
                    raise ValueError(
                        f"Invalid GBA ROM (changed while snapshotting): {rom_path}",
                    )
                snapshot.flush()
                if os.fstat(snapshot.fileno()).st_size != rom_size:
                    raise OSError("ROM snapshot length verification failed")
        yield snapshot_path
    finally:
        if snapshot_fd is not None:
            os.close(snapshot_fd)
        if snapshot_path is not None:
            try:
                os.unlink(snapshot_path)
            except FileNotFoundError:
                pass


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
