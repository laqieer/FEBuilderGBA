using System;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Services;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// Issue #364 — synthetic-ROM regression tests for the Item Editor stop predicate.
    /// These tests do NOT require a real ROM file. They construct an in-memory byte[]
    /// containing crafted item entries and exercise <see cref="ItemListPredicate"/> directly.
    ///
    /// The predicate mirrors WinForms ItemForm.Init exactly:
    ///   FE8 single-byte (version == 8 && is_multibyte == false):
    ///                                  +12 must be pointer-or-null
    ///   FE6 / FE7 either / FE8 multibyte:
    ///                                  +12 AND +16 must each be pointer-or-null
    ///   i > 0xFF                       always stops (hard cap of 256 entries)
    /// </summary>
    public class ItemEditorListCountPredicateTests
    {
        const uint BASE_ADDR = 0x1000;
        const uint ENTRY_SIZE = 36;
        const int ROM_SIZE = 0x100000; // 1 MB — plenty for 256 entries

        /// <summary>Build an empty ROM (1 MB, all zeros). Each item entry is filled with
        /// zeros so <c>u32 @ +12</c> and <c>u32 @ +16</c> are NULL (pointer-or-null).
        /// Tests then overwrite specific bytes to mark entries invalid.</summary>
        static ROM BuildValidRom()
        {
            byte[] data = new byte[ROM_SIZE];
            // byte[] is already zero-initialised — 0 is treated as pointer-or-null
            // by U.isPointerOrNULL, so every entry is "valid" until a test mutates it.
            var rom = new ROM();
            rom.SwapNewROMDataDirect(data);
            return rom;
        }

        /// <summary>Write a u32 little-endian at the given offset.</summary>
        static void WriteU32(byte[] data, uint addr, uint value)
        {
            data[addr + 0] = (byte)(value & 0xFF);
            data[addr + 1] = (byte)((value >> 8) & 0xFF);
            data[addr + 2] = (byte)((value >> 16) & 0xFF);
            data[addr + 3] = (byte)((value >> 24) & 0xFF);
        }

        [Fact]
        public void Predicate_StopsOnFirst_PlusSixteenNotPointer_NonFE8U()
        {
            // FE6 / FE7 either / FE8 multibyte path: +12 AND +16 both required.
            var rom = BuildValidRom();
            byte[] data = rom.Data;

            // Entries 0..9: +12 = 0 (null), +16 = 0 (null) — valid.
            // Entry 10: +12 = 0 (null), +16 = 0x12345678 (not a pointer) — invalid, should stop here.
            for (uint i = 0; i <= 9; i++)
            {
                WriteU32(data, BASE_ADDR + i * ENTRY_SIZE + 12, 0);
                WriteU32(data, BASE_ADDR + i * ENTRY_SIZE + 16, 0);
            }
            // Entry 10: +12 valid, +16 NOT a pointer-or-null (some junk value in WRAM range).
            WriteU32(data, BASE_ADDR + 10 * ENTRY_SIZE + 12, 0);
            WriteU32(data, BASE_ADDR + 10 * ENTRY_SIZE + 16, 0x12345678);

            int count = ItemListPredicate.CountValidEntries(
                rom, BASE_ADDR, ENTRY_SIZE, fe8SingleByte: false);

            Assert.Equal(10, count);
        }

        [Fact]
        public void Predicate_StopsOnFirst_PlusTwelveNotPointer_FE8USingleByte()
        {
            // FE8 single-byte (e.g. FE8U): only +12 required; +16 is ignored.
            var rom = BuildValidRom();
            byte[] data = rom.Data;

            // Entries 0..4: +12 = 0 — valid.
            // Entry 5: +12 = 0xDEADBEEF (junk, not pointer-or-null) — stop here.
            for (uint i = 0; i <= 4; i++)
            {
                WriteU32(data, BASE_ADDR + i * ENTRY_SIZE + 12, 0);
                WriteU32(data, BASE_ADDR + i * ENTRY_SIZE + 16, 0xDEADBEEF); // ignored
            }
            WriteU32(data, BASE_ADDR + 5 * ENTRY_SIZE + 12, 0xDEADBEEF);

            int count = ItemListPredicate.CountValidEntries(
                rom, BASE_ADDR, ENTRY_SIZE, fe8SingleByte: true);

            Assert.Equal(5, count);
        }

        [Fact]
        public void Predicate_StopsAt256_OnAllPointersValid()
        {
            // All zeros → every entry is pointer-or-null. Hard cap of 256 entries kicks in.
            var rom = BuildValidRom();
            // No edits — entire ROM is 0.

            int count = ItemListPredicate.CountValidEntries(
                rom, BASE_ADDR, ENTRY_SIZE, fe8SingleByte: false);

            Assert.Equal(256, count);
        }

        [Fact]
        public void Predicate_FE8USingleByte_StopsAt256_OnAllValid()
        {
            // Same hard-cap behaviour under FE8 single-byte branch.
            var rom = BuildValidRom();

            int count = ItemListPredicate.CountValidEntries(
                rom, BASE_ADDR, ENTRY_SIZE, fe8SingleByte: true);

            Assert.Equal(256, count);
        }

        [Fact]
        public void Predicate_NonFE8U_StopsWhenPlusSixteenInvalid_NotJustPlusTwelve()
        {
            // Regression guard: ensures the +16 check is actually applied in the
            // non-FE8U branch. If a future refactor accidentally drops it, the
            // count would be 256 (full cap) and this test would fail.
            var rom = BuildValidRom();
            byte[] data = rom.Data;

            // Entry 50: +12 = 0 (valid pointer-or-null), +16 = 0xFFFF_FFFF (NOT pointer-or-null).
            // The +16 check must reject this and stop.
            WriteU32(data, BASE_ADDR + 50 * ENTRY_SIZE + 12, 0);
            WriteU32(data, BASE_ADDR + 50 * ENTRY_SIZE + 16, 0xFFFFFFFF);

            int count = ItemListPredicate.CountValidEntries(
                rom, BASE_ADDR, ENTRY_SIZE, fe8SingleByte: false);

            Assert.Equal(50, count);
        }

        [Fact]
        public void Predicate_AcceptsRomPointerOrNull_AtPlusTwelveAndPlusSixteen()
        {
            // 0x08000000 is a valid ROM pointer → predicate accepts it.
            var rom = BuildValidRom();
            byte[] data = rom.Data;

            // Entries 0..2: realistic ROM pointers.
            for (uint i = 0; i <= 2; i++)
            {
                WriteU32(data, BASE_ADDR + i * ENTRY_SIZE + 12, 0x08123456);
                WriteU32(data, BASE_ADDR + i * ENTRY_SIZE + 16, 0x08234567);
            }
            // Entry 3: NULL at both (allowed).
            WriteU32(data, BASE_ADDR + 3 * ENTRY_SIZE + 12, 0);
            WriteU32(data, BASE_ADDR + 3 * ENTRY_SIZE + 16, 0);
            // Entry 4: invalid +16 to terminate at 4.
            WriteU32(data, BASE_ADDR + 4 * ENTRY_SIZE + 12, 0);
            WriteU32(data, BASE_ADDR + 4 * ENTRY_SIZE + 16, 0x03001000); // RAM pointer is NOT ROM pointer

            int count = ItemListPredicate.CountValidEntries(
                rom, BASE_ADDR, ENTRY_SIZE, fe8SingleByte: false);

            Assert.Equal(4, count);
        }

        [Fact]
        public void Predicate_StopsAtRomEndOverflow()
        {
            // ROM is only 0x600 bytes; with ENTRY_SIZE=36 and BASE=0x100,
            // we can fit (0x600 - 0x100) / 36 = 35 entries before overflow.
            byte[] data = new byte[0x600];
            var rom = new ROM();
            rom.SwapNewROMDataDirect(data);

            int count = ItemListPredicate.CountValidEntries(
                rom, 0x100, ENTRY_SIZE, fe8SingleByte: false);

            // All entries valid (zeros) until ROM ends.
            Assert.Equal(35, count);
        }
    }
}
