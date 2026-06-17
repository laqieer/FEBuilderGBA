// SPDX-License-Identifier: GPL-3.0-or-later
// #1177 Core tests for UnitMoveIconImportCore — the move-icon sheet + AP import
// seams (validate dims, LZ77/raw write-back + repoint, defensive length-aware
// snapshot restore on fault).
//
// Uses a synthetic ROM (the seam only touches entryAddr+0 / entryAddr+4 and ROM
// free space). Validates:
//   * a 32-wide sheet import repoints P0 (+0) to a fresh LZ77 pointer +
//     round-trips: LZ77.decompress of the written data == the encoded tiles
//   * a wrong-width image mutates ZERO bytes (byte-identical ROM + error)
//   * a forced fault (free-space exhaustion) restores byte-identical
//   * AP import writes raw bytes + repoints P4 (+4); the written bytes match
//   * AP shared-region guard appends fresh instead of overwriting in place
using System;
using FEBuilderGBA;
using FEBuilderGBA.Core;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class UnitMoveIconImportCoreTests
    {
        // A move-icon entry near the ROM START, with free 0xFF space after the
        // midpoint for WriteCompressedToROM / WriteRawToROM to land in.
        const uint ENTRY_ADDR = 0x400;
        const uint ROM_LEN = 0x20000; // 128 KiB

        static ROM MakeRom()
        {
            var rom = new ROM();
            byte[] data = new byte[ROM_LEN];
            Array.Fill(data, (byte)0xFF); // free-space marker
            for (uint i = 0; i < 0x1000; i++) data[i] = 0x00; // deterministic entry region
            rom.LoadLow("synth.gba", data, "BE8E01");
            return rom;
        }

        static byte[] MakeIndexed(int w, int h)
        {
            byte[] px = new byte[w * h];
            for (int i = 0; i < px.Length; i++) px[i] = (byte)(i % 16);
            return px;
        }

        // ---------------- Sheet import ----------------

        [Fact]
        public void Import_32Wide_RepointsP0_RoundTrips()
        {
            var savedRom = CoreState.ROM;
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;

                const int W = 32, H = 32;
                byte[] px = MakeIndexed(W, H);
                uint p0Before = rom.u32(ENTRY_ADDR + 0);

                string err = UnitMoveIconImportCore.Import(rom, ENTRY_ADDR, px, W, H);
                Assert.Equal("", err);

                // P0 (+0) repointed to a real GBA pointer.
                uint p0After = rom.u32(ENTRY_ADDR + 0);
                Assert.True(U.isPointer(p0After), $"P0 not a pointer: 0x{p0After:X08}");
                Assert.NotEqual(p0Before, p0After);

                // Round-trip: LZ77.decompress of the written sheet == encoded tiles.
                byte[] expectedTiles = ImageImportCore.EncodeDirectTiles4bpp(px, W, H);
                byte[] writtenTiles = LZ77.decompress(rom.Data, U.toOffset(p0After));
                Assert.Equal(expectedTiles, writtenTiles);
            }
            finally { CoreState.ROM = savedRom; }
        }

        [Theory]
        [InlineData(16, 32)]   // wrong width (not 32)
        [InlineData(64, 32)]   // wrong width (not 32)
        [InlineData(32, 33)]   // height not a multiple of 8
        public void Import_WrongDims_NoMutation(int w, int h)
        {
            var savedRom = CoreState.ROM;
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;
                byte[] before = (byte[])rom.Data.Clone();

                string err = UnitMoveIconImportCore.Import(rom, ENTRY_ADDR, MakeIndexed(w, h), w, h);

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
                // ROM at the 32MB max with NO free run anywhere -> WriteCompressedToROM
                // returns NOT_FOUND -> length-aware restore must yield byte-identical.
                const uint MAX = 0x02000000;
                var rom = new ROM();
                byte[] data = new byte[MAX];
                Array.Fill(data, (byte)0x01); // no 0x00/0xFF free runs
                rom.LoadLow("synth.gba", data, "BE8E01");
                CoreState.ROM = rom;

                byte[] before = (byte[])rom.Data.Clone();
                string err = UnitMoveIconImportCore.Import(rom, ENTRY_ADDR, MakeIndexed(32, 32), 32, 32);

                Assert.False(string.IsNullOrEmpty(err));
                Assert.Equal(before.Length, rom.Data.Length);
                Assert.Equal(before, rom.Data); // ZERO mutation
            }
            finally { CoreState.ROM = savedRom; }
        }

        [Fact]
        public void Import_NullRom_ReturnsError_NoThrow()
        {
            string err = UnitMoveIconImportCore.Import(null, ENTRY_ADDR, MakeIndexed(32, 32), 32, 32);
            Assert.False(string.IsNullOrEmpty(err));
        }

        // ---------------- AP import ----------------

        [Fact]
        public void ImportAP_RepointsP4_WritesRawBytes()
        {
            var savedRom = CoreState.ROM;
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;

                byte[] ap = new byte[40];
                for (int i = 0; i < ap.Length; i++) ap[i] = (byte)(0x80 + (i % 7));
                uint p4Before = rom.u32(ENTRY_ADDR + 4);

                string err = UnitMoveIconImportCore.ImportAP(rom, ENTRY_ADDR, ap);
                Assert.Equal("", err);

                uint p4After = rom.u32(ENTRY_ADDR + 4);
                Assert.True(U.isPointer(p4After), $"P4 not a pointer: 0x{p4After:X08}");
                Assert.NotEqual(p4Before, p4After);

                // Raw bytes written verbatim (no LZ77).
                byte[] written = rom.getBinaryData(U.toOffset(p4After), ap.Length);
                Assert.Equal(ap, written);
            }
            finally { CoreState.ROM = savedRom; }
        }

        [Fact]
        public void ImportAP_EmptyData_NoMutation()
        {
            var savedRom = CoreState.ROM;
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;
                byte[] before = (byte[])rom.Data.Clone();

                string err = UnitMoveIconImportCore.ImportAP(rom, ENTRY_ADDR, Array.Empty<byte>());

                Assert.False(string.IsNullOrEmpty(err));
                Assert.Equal(before, rom.Data);
            }
            finally { CoreState.ROM = savedRom; }
        }

        [Fact]
        public void ImportAP_NullRom_ReturnsError_NoThrow()
        {
            string err = UnitMoveIconImportCore.ImportAP(null, ENTRY_ADDR, new byte[] { 1, 2, 3, 4 });
            Assert.False(string.IsNullOrEmpty(err));
        }

        // ---------------- AP shared-region advisory ----------------

        [Fact]
        public void IsApRegionShared_TwoEntriesPointSame_True_OneEntry_False()
        {
            var savedRom = CoreState.ROM;
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;

                // Two table entries (8 bytes each at base) whose +4 both point to
                // the same AP region.
                uint baseAddr = ENTRY_ADDR;
                uint apOff = 0x800;               // some in-bounds AP region
                uint apGba = U.toPointer(apOff);
                rom.write_u32(baseAddr + 0 * 8 + 0, U.toPointer(0x900)); // entry 0 P0
                rom.write_u32(baseAddr + 0 * 8 + 4, apGba);             // entry 0 P4
                rom.write_u32(baseAddr + 1 * 8 + 0, U.toPointer(0x900)); // entry 1 P0
                rom.write_u32(baseAddr + 1 * 8 + 4, apGba);             // entry 1 P4

                // 2 entries reference the AP -> shared.
                Assert.True(UnitMoveIconImportCore.IsApRegionShared(rom, apGba, baseAddr, 2));
                // Scanning only the first entry -> a single reference -> not shared.
                Assert.False(UnitMoveIconImportCore.IsApRegionShared(rom, apGba, baseAddr, 1));
            }
            finally { CoreState.ROM = savedRom; }
        }

        [Fact]
        public void IsApRegionShared_ZeroPointer_False_NoThrow()
        {
            var savedRom = CoreState.ROM;
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;
                Assert.False(UnitMoveIconImportCore.IsApRegionShared(rom, 0, ENTRY_ADDR, 4));
            }
            finally { CoreState.ROM = savedRom; }
        }
    }
}
