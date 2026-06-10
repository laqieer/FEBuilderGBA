// SPDX-License-Identifier: GPL-3.0-or-later
// #1029 Core tests for ChapterTitleCore.MakeList — the cross-platform port of WF
// ImageChapterTitleForm.MakeList() (the 12-byte chapter-title struct table walk).
//
// The synthetic FE8J ROM plants a contiguous run of N chapter-title rows at the
// table base, terminated by a non-pointer +0 entry, and points
// image_chapter_title_pointer at that base.
using System;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class ChapterTitleCoreTests
    {
        const uint TableBase = 0x00500000u;
        const uint Img0 = 0x00510000u;
        const uint Img1 = 0x00520000u;
        const uint Img2 = 0x00530000u;

        // Build a synthetic FE8J ROM with a 3-row chapter-title table.
        static ROM MakeRom(int rows = 3)
        {
            var rom = new ROM();
            byte[] data = new byte[0x1000000];
            rom.LoadLow("x.gba", data, "BE8J01");

            for (uint i = 0; i < (uint)rows; i++)
            {
                uint row = TableBase + i * ChapterTitleCore.EntrySize;
                // +0 Save image pointer (must be a valid pointer for the run).
                U.write_u32(rom.Data, row + 0, U.toPointer(Img0 + i * 0x1000));
                U.write_u32(rom.Data, row + 4, U.toPointer(Img1 + i * 0x1000));
                U.write_u32(rom.Data, row + 8, U.toPointer(Img2 + i * 0x1000));
            }
            // Terminator row: +0 is a non-pointer (0).
            U.write_u32(rom.Data, TableBase + (uint)rows * ChapterTitleCore.EntrySize, 0);

            U.write_u32(rom.Data, rom.RomInfo.image_chapter_title_pointer, U.toPointer(TableBase));
            return rom;
        }

        [Fact]
        public void MakeList_ReturnsContiguousRun()
        {
            var prev = CoreState.ROM;
            try
            {
                ROM rom = MakeRom(3);
                CoreState.ROM = rom;
                var list = ChapterTitleCore.MakeList(rom);
                Assert.Equal(3, list.Count);
                Assert.Equal(U.toOffset(TableBase + 0), list[0].addr);
                Assert.Equal(U.toOffset(TableBase + 1 * ChapterTitleCore.EntrySize), list[1].addr);
                Assert.Equal(U.toOffset(TableBase + 2 * ChapterTitleCore.EntrySize), list[2].addr);
            }
            finally { CoreState.ROM = prev; }
        }

        [Fact]
        public void MakeList_StopsAtNonPointerTerminator()
        {
            var prev = CoreState.ROM;
            try
            {
                ROM rom = MakeRom(3);
                CoreState.ROM = rom;
                // Wipe row 1's +0 pointer -> run truncates at 1.
                U.write_u32(rom.Data, TableBase + 1 * ChapterTitleCore.EntrySize, 0);
                var list = ChapterTitleCore.MakeList(rom);
                Assert.Single(list);
            }
            finally { CoreState.ROM = prev; }
        }

        [Fact]
        public void MakeList_NullRom_ReturnsEmpty_NoThrow()
        {
            Assert.Empty(ChapterTitleCore.MakeList(null));
        }

        [Fact]
        public void MakeList_UnsafeTablePointer_ReturnsEmpty()
        {
            var prev = CoreState.ROM;
            try
            {
                ROM rom = MakeRom(3);
                CoreState.ROM = rom;
                U.write_u32(rom.Data, rom.RomInfo.image_chapter_title_pointer, 0);
                Assert.Empty(ChapterTitleCore.MakeList(rom));
            }
            finally { CoreState.ROM = prev; }
        }
    }
}
