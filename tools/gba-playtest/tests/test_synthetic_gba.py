"""Copyright-cleanliness and structural tests for the synthetic GBA ROM."""

import struct

import synthetic_gba


def test_rom_is_deterministic():
    assert synthetic_gba.build_synthetic_rom() == synthetic_gba.build_synthetic_rom()


def test_logo_region_is_zeroed():
    rom = synthetic_gba.build_synthetic_rom()
    region = synthetic_gba.nintendo_logo_region(rom)
    assert len(region) == 0xA0 - 0x04
    assert set(region) == {0}
    assert synthetic_gba.logo_is_zeroed(rom)
    assert synthetic_gba.has_no_copyrighted_block(rom)


def test_header_game_code_roundtrips():
    rom = synthetic_gba.build_synthetic_rom(game_code="FEBT")
    assert synthetic_gba.header_game_code(rom) == "FEBT"


def test_fixed_byte_and_entry_branch():
    rom = synthetic_gba.build_synthetic_rom()
    assert rom[0xB2] == 0x96
    # Entry point is an ARM branch (top byte 0xEA).
    entry = struct.unpack("<I", rom[0x00:0x04])[0]
    assert (entry >> 24) == 0xEA


def test_header_complement_checksum_valid():
    rom = synthetic_gba.build_synthetic_rom()
    total = sum(rom[0xA0:0xBD])
    check = rom[0xBD]
    assert (0x19 + total + check) & 0xFF == 0


def test_marker_appears_in_code_not_logo():
    rom = synthetic_gba.build_synthetic_rom(marker=0xAB)
    # The marker must live in the code region, never in the zeroed logo.
    assert synthetic_gba.logo_is_zeroed(rom)
    assert rom[0xC0:] .count(0xAB) >= 1


def test_rom_size():
    assert len(synthetic_gba.build_synthetic_rom()) == 0x200
