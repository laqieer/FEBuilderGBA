"""ROM project management — open, info, save, close."""

import os
import shutil
from typing import Optional

from cli_anything.febuildergba.utils.febuildergba_backend import run_cli


def rom_info(rom_path: str, force_version: str = "") -> dict:
    """Get ROM information by running --lint to validate and load the ROM.

    Uses --lint (which fully loads the ROM) as the primary validation path.
    Falls back to local header detection if the backend is unavailable.

    Args:
        rom_path: Path to the .gba ROM file.
        force_version: Optional forced version (FE6, FE7J, FE7U, FE8J, FE8U).

    Returns:
        Dict with ROM metadata.
    """
    if not os.path.isfile(rom_path):
        raise FileNotFoundError(f"ROM file not found: {rom_path}")

    rom_size = os.path.getsize(rom_path)

    # Try --lint for full ROM validation (loads ROM, checks integrity)
    lint_output = ""
    lint_exit = -1
    try:
        args = ["--rom", rom_path]
        if force_version:
            args += [f"--force-version={force_version}"]
        args.append("--lint")
        result = run_cli(args)
        lint_output = result.stdout.strip()
        lint_exit = result.returncode
    except RuntimeError:
        # Backend not available — fall back to local header detection
        pass

    # Detect version from header (always available, no backend needed)
    detected_version = _detect_version(rom_path, force_version)

    return {
        "rom_path": os.path.abspath(rom_path),
        "rom_size": rom_size,
        "rom_size_hex": f"0x{rom_size:X}",
        "rom_size_mb": round(rom_size / (1024 * 1024), 2),
        "detected_version": detected_version,
        "force_version": force_version,
        "lint_output": lint_output,
        "lint_exit_code": lint_exit,
    }


def _detect_version(rom_path: str, force_version: str = "") -> str:
    """Detect ROM version from file header."""
    if force_version:
        return force_version

    try:
        with open(rom_path, "rb") as f:
            header = f.read(0xC0)

        # Check game code at offset 0xAC (4 bytes)
        if len(header) >= 0xB0:
            game_code = header[0xAC:0xB0].decode("ascii", errors="replace")
            # Known game codes
            version_map = {
                "AFEJ": "FE6",
                "AE7J": "FE7J",
                "AE7E": "FE7U",
                "BE8J": "FE8J",
                "BE8E": "FE8U",
            }
            return version_map.get(game_code, f"unknown ({game_code})")
    except Exception:
        pass
    return "unknown"


def validate_rom(rom_path: str) -> bool:
    """Check if file looks like a valid GBA ROM.

    Validates:
    - File exists and is >= 1 MB
    - File is at least 0xC0 bytes (full GBA header)
    - Fixed header byte at 0xB2 is 0x96 (GBA ROM header complement check)
    """
    if not os.path.isfile(rom_path):
        return False
    try:
        size = os.path.getsize(rom_path)
        if size < 0x100000:
            return False
        with open(rom_path, "rb") as f:
            header = f.read(0xC0)
        if len(header) < 0xC0:
            return False
        # GBA ROMs have a fixed 0x96 at offset 0xB2
        return header[0xB2] == 0x96
    except Exception:
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
    if not os.path.isfile(rom_path):
        raise FileNotFoundError(f"ROM file not found: {rom_path}")

    with open(rom_path, "rb") as f:
        header = f.read(0xC0)

    if len(header) < 0xBE:
        raise ValueError(f"File too small for GBA header: {len(header)} bytes")

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
