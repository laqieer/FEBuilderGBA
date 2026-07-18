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


def test_game_code_must_be_exactly_four_chars():
    import pytest

    with pytest.raises(ValueError, match="exactly 4 printable"):
        synthetic_gba.build_synthetic_rom(game_code="FEB")
    with pytest.raises(ValueError, match="exactly 4 printable"):
        synthetic_gba.build_synthetic_rom(game_code="FEBTX")
    with pytest.raises(ValueError, match="exactly 4 printable"):
        synthetic_gba.build_synthetic_rom(game_code="FE\x00B")


def test_header_game_code_has_no_nul_padding():
    rom = synthetic_gba.build_synthetic_rom(game_code="AZ90")
    code = synthetic_gba.header_game_code(rom)
    assert code == "AZ90"
    assert "\x00" not in code
    assert len(code) == 4


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
    assert len(synthetic_gba.build_synthetic_rom()) == 0x80000


def test_rom_exceeds_multiboot_limit():
    rom = synthetic_gba.build_synthetic_rom()
    # Strictly above the 256 KiB multiboot cutoff so a real emulator loads it as
    # a cartridge ROM (GBAIsMB rejects only sizes > SIZE_WORKING_RAM).
    assert len(rom) > 0x40000
    assert synthetic_gba.exceeds_multiboot_limit(rom)
    # Power-of-two size keeps the image on a natural GBA cart boundary.
    size = len(rom)
    assert size & (size - 1) == 0


def test_padding_beyond_code_is_zero_and_copyright_clean():
    rom = synthetic_gba.build_synthetic_rom()
    # Everything past the hand-assembled code is deterministic zero padding, so
    # enlarging the image to the cartridge size cannot smuggle in any data.
    assert set(rom[0x200:]) == {0}
    assert synthetic_gba.logo_is_zeroed(rom)
    assert synthetic_gba.has_no_copyrighted_block(rom)


def test_loop_clears_marker_each_iteration():
    # The per-iteration release default (mov r3,#0 = 0xE3A03000) must sit inside
    # the loop, between the loop head (ldrh) and the marker store, so releasing
    # A clears the byte instead of latching it.
    rom = synthetic_gba.build_synthetic_rom()
    ldrh = struct.pack("<I", 0xE1D020B0)      # 0xC8 loop head
    clear = struct.pack("<I", 0xE3A03000)     # 0xCC mov r3,#0
    strb = struct.pack("<I", 0xE5C13000)      # 0xD8 strb r3,[r1]
    ldrh_pos = rom.index(ldrh)
    clear_pos = rom.index(clear)
    strb_pos = rom.index(strb)
    assert ldrh_pos < clear_pos < strb_pos


def test_marker_reflects_held_state_not_latch():
    marker = 0xAB
    # Press A, then release: the marker must appear while held and clear on
    # release (proving both transitions, not a one-way latch).
    result = synthetic_gba.simulate_marker_sequence([True, False], marker=marker)
    assert result == [marker, 0]


def test_marker_full_press_release_sequence():
    marker = 0x5A
    states = [False, True, True, False, True, False]
    result = synthetic_gba.simulate_marker_sequence(states, marker=marker)
    assert result == [synthetic_gba.expected_marker(s, marker) for s in states]
    assert result == [0, marker, marker, 0, marker, 0]


def test_expected_marker_contract():
    assert synthetic_gba.expected_marker(True) == synthetic_gba.DEFAULT_MARKER
    assert synthetic_gba.expected_marker(False) == 0
    assert synthetic_gba.expected_marker(True, 0x42) == 0x42


def test_key_transition_sequence_constant_covers_both_edges():
    seq = synthetic_gba.KEY_TRANSITION_SEQUENCE
    result = synthetic_gba.simulate_marker_sequence(seq)
    assert result == [synthetic_gba.expected_marker(s) for s in seq]
    # Both a rising (press) and falling (release) edge are represented.
    assert synthetic_gba.DEFAULT_MARKER in result
    assert 0 in result


def test_public_marker_constants():
    assert synthetic_gba.EWRAM_MARKER_ADDR == 0x02000000
    assert synthetic_gba.KEYINPUT_ADDR == 0x04000130
    assert synthetic_gba.A_KEYINPUT_BIT == 0x0001
