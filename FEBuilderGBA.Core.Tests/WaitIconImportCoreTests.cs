// SPDX-License-Identifier: GPL-3.0-or-later
// #991 Core tests for WaitIconImportCore — the static PNG/BMP wait-icon sheet
// import seam (validate dims -> b2, encode + LZ77 write-back + repoint, with a
// defensive length-aware snapshot restore on fault).
//
// Uses a synthetic ROM (no RomInfo needed — the seam only touches entryAddr+2 /
// entryAddr+4 and ROM free space). Validates:
//   * 16x48 import sets W2=0 and repoints P4 to a fresh pointer
//   * a wrong-size image mutates ZERO bytes (byte-identical ROM + error string)
//   * a forced fault (free-space exhaustion) restores byte-identical
using System;
using FEBuilderGBA;
using FEBuilderGBA.Core;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class WaitIconImportCoreTests
    {
        // A wait-icon entry near the ROM START, with plenty of free 0xFF space
        // after the midpoint for WriteCompressedToROM to land in.
        const uint ENTRY_ADDR = 0x400;
        const uint ROM_LEN = 0x20000; // 128 KiB

        static ROM MakeRom()
        {
            var rom = new ROM();
            byte[] data = new byte[ROM_LEN];
            // Fill with 0xFF (free-space marker) so FindFreeSpace can place data.
            Array.Fill(data, (byte)0xFF);
            // Zero the entry region + header so reads are deterministic.
            for (uint i = 0; i < 0x1000; i++) data[i] = 0x00;
            rom.LoadLow("synth.gba", data, "BE8E01");
            return rom;
        }

        static byte[] MakeIndexed(int w, int h)
        {
            byte[] px = new byte[w * h];
            for (int i = 0; i < px.Length; i++) px[i] = (byte)(i % 16);
            return px;
        }

        [Fact]
        public void Import_16x48_SetsB2_0_RepointsP4()
        {
            var savedRom = CoreState.ROM;
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;

                uint p4Before = rom.u32(ENTRY_ADDR + 4);
                string err = WaitIconImportCore.Import(rom, ENTRY_ADDR, MakeIndexed(16, 48), 16, 48);

                Assert.Equal("", err);
                // W2 (animType byte) set to 0 for 16x48.
                Assert.Equal(0u, rom.u16(ENTRY_ADDR + 2));
                // P4 repointed to a real GBA pointer (changed from the old value).
                uint p4After = rom.u32(ENTRY_ADDR + 4);
                Assert.True(U.isPointer(p4After), $"P4 not a pointer: 0x{p4After:X08}");
                Assert.NotEqual(p4Before, p4After);
            }
            finally { CoreState.ROM = savedRom; }
        }

        [Theory]
        [InlineData(16, 96, 1)]
        [InlineData(32, 96, 2)]
        public void Import_ValidSizes_SetCorrectB2(int w, int h, int expectedB2)
        {
            var savedRom = CoreState.ROM;
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;
                string err = WaitIconImportCore.Import(rom, ENTRY_ADDR, MakeIndexed(w, h), w, h);
                Assert.Equal("", err);
                Assert.Equal((uint)expectedB2, rom.u16(ENTRY_ADDR + 2));
                Assert.True(U.isPointer(rom.u32(ENTRY_ADDR + 4)));
            }
            finally { CoreState.ROM = savedRom; }
        }

        [Fact]
        public void Import_WrongSize_NoMutation()
        {
            var savedRom = CoreState.ROM;
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;
                byte[] before = (byte[])rom.Data.Clone();

                string err = WaitIconImportCore.Import(rom, ENTRY_ADDR, MakeIndexed(24, 24), 24, 24);

                Assert.False(string.IsNullOrEmpty(err));
                Assert.Equal(before.Length, rom.Data.Length);
                Assert.Equal(before, rom.Data); // byte-identical
            }
            finally { CoreState.ROM = savedRom; }
        }

        [Fact]
        public void Import_ForcedFault_RestoresByteIdentical()
        {
            var savedRom = CoreState.ROM;
            try
            {
                // Force WriteCompressedToROM -> U.NOT_FOUND: a ROM already at the
                // 32MB max with NO free space at all. FindFreeSpace treats both
                // 0x00 AND 0xFF runs as free, so the ROM is filled with 0x01
                // (a non-free byte) to break every run; AppendToRomEnd then
                // refuses (newEnd > 0x02000000). The import must restore
                // byte-identical (length unchanged, no +2/+4 mutation) and
                // return a non-empty error.
                const uint MAX = 0x02000000;
                var rom = new ROM();
                byte[] data = new byte[MAX];
                Array.Fill(data, (byte)0x01);    // no 0x00/0xFF free runs anywhere
                rom.LoadLow("synth.gba", data, "BE8E01");
                CoreState.ROM = rom;

                byte[] before = (byte[])rom.Data.Clone();
                string err = WaitIconImportCore.Import(rom, ENTRY_ADDR, MakeIndexed(16, 48), 16, 48);

                Assert.False(string.IsNullOrEmpty(err));
                Assert.Equal(before.Length, rom.Data.Length);
                Assert.Equal(before, rom.Data); // byte-identical, ZERO mutation
            }
            finally { CoreState.ROM = savedRom; }
        }

        [Fact]
        public void Import_NullRom_ReturnsError_NoThrow()
        {
            string err = WaitIconImportCore.Import(null, ENTRY_ADDR, MakeIndexed(16, 48), 16, 48);
            Assert.False(string.IsNullOrEmpty(err));
        }
    }
}
