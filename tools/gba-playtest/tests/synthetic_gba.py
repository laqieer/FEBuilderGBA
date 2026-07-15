"""Repository-authored synthetic GBA ROM helper.

This builds a *tiny, hand-assembled* homebrew GBA image from known bytes. It
contains no copyrighted material: the Nintendo logo header region (0x04..0x9F)
is deliberately left all-zero, and the cartridge code is a short ARM routine
authored here.

The routine reads ``KEYINPUT`` each frame and writes a marker byte into EWRAM
when the A button is held, so the same image can drive a real-emulator
determinism proof (WU4). WU1's dependency-free tests use it only for header /
SHA-256 / game-code guard coverage and the copyright-cleanliness check.

No external GBA toolchain is required or used.
"""

from __future__ import annotations

import hashlib
import struct

# Header layout constants.
_LOGO_START = 0x04
_LOGO_END = 0xA0        # exclusive
_TITLE_OFFSET = 0xA0
_GAME_CODE_OFFSET = 0xAC
_MAKER_OFFSET = 0xB0
_FIXED_96_OFFSET = 0xB2
_COMPLEMENT_OFFSET = 0xBD
_ROM_SIZE = 0x200
_EWRAM_MARKER_OFFSET = 0x02000000  # informational; code targets EWRAM base

# EWRAM base and KEYINPUT register, kept in the ARM literal pool.
_KEYINPUT_ADDR = 0x04000130
_EWRAM_ADDR = 0x02000000


def _arm_code(marker: int) -> bytes:
    """Hand-assembled ARM routine (see module docstring).

    Layout (ROM offsets):
        0xC0 ldr  r0,[pc,#0x1C]   ; r0 = KEYINPUT
        0xC4 ldr  r1,[pc,#0x1C]   ; r1 = EWRAM base
        0xC8 mov  r3,#0
        0xCC strb r3,[r1]         ; clear marker
        0xD0 ldrh r2,[r0]         ; KEYINPUT (active low)
        0xD4 tst  r2,#1           ; A is bit 0
        0xD8 moveq r3,#marker     ; A pressed -> marker
        0xDC strb r3,[r1]
        0xE0 b    0xD0
        0xE4 .word KEYINPUT
        0xE8 .word EWRAM
    """
    moveq_marker = 0x03A03000 | (marker & 0xFF)
    words = [
        0xE59F001C,  # ldr r0,[pc,#0x1C]
        0xE59F101C,  # ldr r1,[pc,#0x1C]
        0xE3A03000,  # mov r3,#0
        0xE5C13000,  # strb r3,[r1]
        0xE1D020B0,  # ldrh r2,[r0]
        0xE3120001,  # tst r2,#1
        moveq_marker,  # moveq r3,#marker
        0xE5C13000,  # strb r3,[r1]
        0xEAFFFFFA,  # b -0x18 (back to ldrh)
        _KEYINPUT_ADDR,  # literal pool
        _EWRAM_ADDR,     # literal pool
    ]
    return b"".join(struct.pack("<I", w) for w in words)


def _header_complement(rom: bytearray) -> int:
    total = 0
    for offset in range(_TITLE_OFFSET, _COMPLEMENT_OFFSET):
        total += rom[offset]
    return (-(0x19 + total)) & 0xFF


def build_synthetic_rom(game_code: str = "FEBT", marker: int = 0xAB) -> bytes:
    """Assemble a deterministic, copyright-clean homebrew GBA ROM."""
    if len(game_code) != 4 or any(not (32 <= ord(c) < 127) for c in game_code):
        raise ValueError("game_code must be exactly 4 printable ASCII characters")
    if not (0 <= marker <= 0xFF):
        raise ValueError("marker must be a byte value")

    rom = bytearray(_ROM_SIZE)

    # 0x00: ARM branch to the routine at 0xC0.
    rom[0x00:0x04] = struct.pack("<I", 0xEA00002E)

    # 0x04..0x9F: Nintendo logo region, left ALL ZERO (no copyrighted logo).

    # 0xA0: 12-byte ASCII title (repository-authored, not a game name).
    title = b"FEBUILDTEST\x00"[:12].ljust(12, b"\x00")
    rom[_TITLE_OFFSET:_TITLE_OFFSET + 12] = title

    # 0xAC: game code (exactly 4 bytes), 0xB0: maker code.
    rom[_GAME_CODE_OFFSET:_GAME_CODE_OFFSET + 4] = game_code.encode("ascii")
    rom[_MAKER_OFFSET:_MAKER_OFFSET + 2] = b"00"

    # 0xB2: fixed value 0x96 required by real GBA hardware.
    rom[_FIXED_96_OFFSET] = 0x96

    # 0xBD: header complement checksum.
    rom[_COMPLEMENT_OFFSET] = _header_complement(rom)

    # 0xC0: cartridge code.
    code = _arm_code(marker)
    rom[0xC0:0xC0 + len(code)] = code

    return bytes(rom)


def sha256_hex(rom: bytes) -> str:
    return hashlib.sha256(rom).hexdigest()


def header_game_code(rom: bytes) -> str:
    return rom[_GAME_CODE_OFFSET:_GAME_CODE_OFFSET + 4].decode("ascii")


def nintendo_logo_region(rom: bytes) -> bytes:
    return rom[_LOGO_START:_LOGO_END]


def logo_is_zeroed(rom: bytes) -> bool:
    """True when the logo header region contains no data (copyright-clean)."""
    return all(b == 0 for b in nintendo_logo_region(rom))


def has_no_copyrighted_block(rom: bytes) -> bool:
    """Heuristic copyright guard: the logo region must be entirely zero.

    The real Nintendo boot logo is a large, dense, non-zero bitmap. An all-zero
    logo region guarantees this synthetic image embeds none of it.
    """
    return logo_is_zeroed(rom)
