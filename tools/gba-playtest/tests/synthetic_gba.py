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
# Multiboot cutoff: ``GBAIsMB`` classifies any image <= SIZE_WORKING_RAM
# (256 KiB) as a multiboot payload, which mGBA loads into EWRAM instead of the
# cartridge path. Size the image strictly above that (a power-of-two 512 KiB)
# so a real-emulator proof (WU4) exercises the actual cartridge ROM path rather
# than accidentally testing multiboot. The header/code live below 0x200; the
# remainder is deterministic zero padding.
_MULTIBOOT_MAX_BYTES = 0x40000  # SIZE_WORKING_RAM (256 KiB)
_ROM_SIZE = 0x80000             # 512 KiB, power of two, above the multiboot cutoff
_EWRAM_MARKER_OFFSET = 0x02000000  # informational; code targets EWRAM base

# EWRAM base and KEYINPUT register, kept in the ARM literal pool.
_KEYINPUT_ADDR = 0x04000130
_EWRAM_ADDR = 0x02000000

# Public routine contract (consumed by tests and, later, the WU4 real-emulator
# proof). The routine publishes the held A-button state as a single EWRAM byte.
DEFAULT_MARKER = 0xAB
EWRAM_MARKER_ADDR = _EWRAM_ADDR   # where the routine writes the held-key marker
KEYINPUT_ADDR = _KEYINPUT_ADDR
A_KEYINPUT_BIT = 0x0001           # A button (active low in KEYINPUT)
# Canonical WU4 proof sequence: press A, then release it. A correct routine
# publishes the marker while held and clears it once released (not a one-way
# latch), so both transitions are observable.
KEY_TRANSITION_SEQUENCE = (True, False)


def _arm_code(marker: int) -> bytes:
    """Hand-assembled ARM routine (see module docstring).

    The marker reflects the *held* A-button state every frame: each loop
    iteration first defaults ``r3`` to zero (released) and only sets it to the
    marker while A is held, so releasing A clears the EWRAM byte on the very
    next iteration instead of latching it forever.

    Layout (ROM offsets):
        0xC0 ldr   r0,[pc,#0x18]  ; r0 = KEYINPUT
        0xC4 ldr   r1,[pc,#0x18]  ; r1 = EWRAM base
      loop:
        0xC8 ldrh  r2,[r0]        ; KEYINPUT (active low)
        0xCC mov   r3,#0          ; default released each iteration (clears latch)
        0xD0 tst   r2,#1          ; A is bit 0
        0xD4 moveq r3,#marker     ; A pressed -> marker
        0xD8 strb  r3,[r1]        ; publish held state
        0xDC b     loop (0xC8)
        0xE0 .word KEYINPUT
        0xE4 .word EWRAM
    """
    moveq_marker = 0x03A03000 | (marker & 0xFF)
    words = [
        0xE59F0018,  # 0xC0 ldr r0,[pc,#0x18]  -> literal at 0xE0
        0xE59F1018,  # 0xC4 ldr r1,[pc,#0x18]  -> literal at 0xE4
        0xE1D020B0,  # 0xC8 ldrh r2,[r0]        (loop head)
        0xE3A03000,  # 0xCC mov r3,#0           (per-iteration release default)
        0xE3120001,  # 0xD0 tst r2,#1
        moveq_marker,  # 0xD4 moveq r3,#marker
        0xE5C13000,  # 0xD8 strb r3,[r1]
        0xEAFFFFF9,  # 0xDC b -0x1C (back to 0xC8)
        _KEYINPUT_ADDR,  # 0xE0 literal pool
        _EWRAM_ADDR,     # 0xE4 literal pool
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


def exceeds_multiboot_limit(rom: bytes) -> bool:
    """True when the image is too large for mGBA's multiboot (EWRAM) path.

    ``GBAIsMB`` accepts only images ``<= SIZE_WORKING_RAM`` (256 KiB); anything
    larger is guaranteed to take the cartridge ROM path. Keeping the synthetic
    image above the cutoff ensures WU4 proves cartridge behaviour, not
    multiboot.
    """
    return len(rom) > _MULTIBOOT_MAX_BYTES


def has_no_copyrighted_block(rom: bytes) -> bool:
    """Heuristic copyright guard: the logo region must be entirely zero.

    The real Nintendo boot logo is a large, dense, non-zero bitmap. An all-zero
    logo region guarantees this synthetic image embeds none of it.
    """
    return logo_is_zeroed(rom)


def expected_marker(a_pressed: bool, marker: int = DEFAULT_MARKER) -> int:
    """Routine contract: the EWRAM marker byte for a given held A state.

    A held publishes ``marker``; A released publishes ``0`` (the routine clears
    the byte every iteration instead of latching).
    """
    return (marker & 0xFF) if a_pressed else 0


def simulate_marker_sequence(a_states, marker: int = DEFAULT_MARKER,
                             game_code: str = "FEBT"):
    """Execute the assembled ARM routine over a sequence of A-button states.

    A tiny, dependency-free ARM interpreter runs the *actual assembled bytes*
    from :func:`build_synthetic_rom` (not a re-implementation), reading a fake
    ``KEYINPUT`` and writing the fake EWRAM marker byte. ``a_states`` is an
    iterable of booleans (is A held this iteration?); the returned list gives
    the EWRAM marker byte published after each corresponding loop iteration.

    This proves both key transitions a real emulator must reproduce in WU4:
    press -> marker, release -> 0.
    """
    states = [bool(x) for x in a_states]
    rom = build_synthetic_rom(game_code=game_code, marker=marker)

    regs = [0] * 16
    z = False
    ewram = bytearray(1)
    keyinput = 0x03FF  # active low, all keys released initially
    pc = 0xC0
    results = []
    idx = 0
    steps = 0
    max_steps = 64 * (len(states) + 2)

    def cond_holds(cond: int) -> bool:
        if cond == 0xE:      # AL (always)
            return True
        if cond == 0x0:      # EQ (Z set)
            return z
        if cond == 0x1:      # NE (Z clear)
            return not z
        raise ValueError("unsupported condition code 0x%X" % cond)

    while idx < len(states) and steps < max_steps:
        steps += 1
        word = struct.unpack_from("<I", rom, pc)[0]
        cond = (word >> 28) & 0xF
        klass = (word >> 26) & 0x3
        run = cond_holds(cond)
        next_pc = pc + 4

        if klass == 0x2:  # branch
            if run:
                offset = word & 0x00FFFFFF
                if offset & 0x00800000:
                    offset -= 0x01000000
                next_pc = pc + 8 + (offset << 2)
        elif klass == 0x1:  # single data transfer (ldr / strb)
            load = (word >> 20) & 1
            byte = (word >> 22) & 1
            rn = (word >> 16) & 0xF
            rd = (word >> 12) & 0xF
            imm = word & 0xFFF
            up = (word >> 23) & 1
            if rn == 15:  # PC-relative literal load
                base = pc + 8
            else:
                base = regs[rn]
            addr = base + imm if up else base - imm
            if run:
                if load:
                    regs[rd] = struct.unpack_from("<I", rom, addr)[0]
                else:  # store
                    value = regs[rd] & (0xFF if byte else 0xFFFFFFFF)
                    if addr == _EWRAM_ADDR:
                        ewram[0] = value & 0xFF
                        results.append(ewram[0])
                        idx += 1
        elif klass == 0x0:  # data processing / halfword transfer
            if (word & 0x0E000090) == 0x00000090 and ((word >> 5) & 0x3):
                # ldrh rd,[rn]  (halfword load, immediate offset 0)
                rn = (word >> 16) & 0xF
                rd = (word >> 12) & 0xF
                if run:
                    if regs[rn] == _KEYINPUT_ADDR:
                        # Present this iteration's A state (active low).
                        keyinput = 0x03FF
                        if states[idx]:
                            keyinput &= ~A_KEYINPUT_BIT
                        regs[rd] = keyinput & 0xFFFF
                    else:
                        regs[rd] = struct.unpack_from("<H", rom, regs[rn])[0]
            else:
                # data-processing immediate (mov / tst)
                opcode = (word >> 21) & 0xF
                set_flags = (word >> 20) & 1
                rn = (word >> 16) & 0xF
                rd = (word >> 12) & 0xF
                rot = ((word >> 8) & 0xF) * 2
                imm = word & 0xFF
                operand = ((imm >> rot) | (imm << (32 - rot))) & 0xFFFFFFFF if rot else imm
                if run:
                    if opcode == 0xD:      # mov
                        regs[rd] = operand
                        if set_flags:
                            z = regs[rd] == 0
                    elif opcode == 0x8:    # tst
                        z = (regs[rn] & operand) == 0
                    else:
                        raise ValueError("unsupported data op 0x%X" % opcode)
        else:
            raise ValueError("unsupported instruction class at 0x%X" % pc)

        pc = next_pc

    if idx < len(states):
        raise RuntimeError("interpreter step budget exhausted before completion")
    return results
