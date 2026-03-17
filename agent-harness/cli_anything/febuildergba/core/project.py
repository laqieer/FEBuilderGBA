"""ROM project management — open, info, save, close."""

import os
from typing import Optional

from cli_anything.febuildergba.utils.febuildergba_backend import run_cli


def rom_info(rom_path: str, force_version: str = "") -> dict:
    """Get ROM information by running --version with --rom.

    Args:
        rom_path: Path to the .gba ROM file.
        force_version: Optional forced version (FE6, FE7J, FE7U, FE8J, FE8U).

    Returns:
        Dict with ROM metadata.
    """
    if not os.path.isfile(rom_path):
        raise FileNotFoundError(f"ROM file not found: {rom_path}")

    args = ["--rom", rom_path]
    if force_version:
        args += [f"--force-version={force_version}"]
    args.append("--version")

    result = run_cli(args)
    version_text = result.stdout.strip()

    rom_size = os.path.getsize(rom_path)

    # Parse version output to detect ROM version
    detected_version = _detect_version(rom_path, force_version)

    return {
        "rom_path": os.path.abspath(rom_path),
        "rom_size": rom_size,
        "rom_size_hex": f"0x{rom_size:X}",
        "rom_size_mb": round(rom_size / (1024 * 1024), 2),
        "detected_version": detected_version,
        "force_version": force_version,
        "version_output": version_text,
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
    """Check if file looks like a valid GBA ROM."""
    if not os.path.isfile(rom_path):
        return False
    try:
        with open(rom_path, "rb") as f:
            header = f.read(4)
        # GBA ROMs start with a branch instruction (typically 0x2E000000EA)
        return len(header) >= 4 and os.path.getsize(rom_path) >= 0x100000
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
