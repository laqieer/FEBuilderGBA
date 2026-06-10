// SPDX-License-Identifier: GPL-3.0-or-later
// #1029 Core tests for OPClassFontListCore.MakeList — the cross-platform port of WF
// OPClassFontForm.MakeList() / OPClassFontFE8UForm.MakeList() (the 4-byte glyph-
// pointer slot table walk). JP = contiguous pointer run; FE8U = fixed i <= 0x7a.
using System;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class OPClassFontListCoreTests
    {
        const uint TableBase = 0x00500000u;
        const uint Glyph = 0x00510000u;

        static ROM MakeRom(string gameCode, int contiguousPointers)
        {
            var rom = new ROM();
            byte[] data = new byte[0x1000000];
            rom.LoadLow("x.gba", data, gameCode);

            for (uint i = 0; i < (uint)contiguousPointers; i++)
            {
                U.write_u32(rom.Data, TableBase + i * OPClassFontListCore.EntrySize,
                    U.toPointer(Glyph + i * 0x100));
            }
            // Terminator: a 0 (non-pointer) just past the run.
            U.write_u32(rom.Data, TableBase + (uint)contiguousPointers * OPClassFontListCore.EntrySize, 0);

            U.write_u32(rom.Data, rom.RomInfo.op_class_font_pointer, U.toPointer(TableBase));
            return rom;
        }

        [Fact]
        public void MakeList_JP_ReturnsContiguousPointerRun()
        {
            var prev = CoreState.ROM;
            try
            {
                ROM rom = MakeRom("BE8J01", 5);
                CoreState.ROM = rom;
                var list = OPClassFontListCore.MakeList(rom);
                Assert.Equal(5, list.Count);
                Assert.Equal(U.toOffset(TableBase + 0), list[0].addr);
                Assert.Equal(U.toOffset(TableBase + 4 * OPClassFontListCore.EntrySize), list[4].addr);
            }
            finally { CoreState.ROM = prev; }
        }

        [Fact]
        public void MakeList_FE8U_ReturnsFixed0x7bSlots()
        {
            var prev = CoreState.ROM;
            try
            {
                // FE8U uses the fixed i <= 0x7a run (0..0x7a inclusive = 0x7b slots)
                // regardless of the pointer contents.
                ROM rom = MakeRom("BE8E01", 3);
                CoreState.ROM = rom;
                var list = OPClassFontListCore.MakeList(rom);
                Assert.Equal(0x7b, list.Count);
            }
            finally { CoreState.ROM = prev; }
        }

        [Fact]
        public void MakeList_JP_StopsAtNonPointer()
        {
            var prev = CoreState.ROM;
            try
            {
                ROM rom = MakeRom("BE8J01", 5);
                CoreState.ROM = rom;
                // Break the run at slot 2.
                U.write_u32(rom.Data, TableBase + 2 * OPClassFontListCore.EntrySize, 0);
                var list = OPClassFontListCore.MakeList(rom);
                Assert.Equal(2, list.Count);
            }
            finally { CoreState.ROM = prev; }
        }

        [Fact]
        public void MakeList_NullRom_ReturnsEmpty_NoThrow()
        {
            Assert.Empty(OPClassFontListCore.MakeList(null));
        }

        [Fact]
        public void MakeList_UnsafeTablePointer_ReturnsEmpty()
        {
            var prev = CoreState.ROM;
            try
            {
                ROM rom = MakeRom("BE8J01", 5);
                CoreState.ROM = rom;
                U.write_u32(rom.Data, rom.RomInfo.op_class_font_pointer, 0);
                Assert.Empty(OPClassFontListCore.MakeList(rom));
            }
            finally { CoreState.ROM = prev; }
        }
    }
}
