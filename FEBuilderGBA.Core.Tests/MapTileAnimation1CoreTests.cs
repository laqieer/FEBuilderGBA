// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for MapTileAnimation1Core (#955, #957 W1c).
//
// Headless mirror of WinForms MapTileAnimation1Form's anime1 PLIST-filter
// structure (MakeTileAnimation1) + the 8-byte data-table record scanned for the
// SELECTED PLIST (Init: isPointer(u32(addr+4)); wait@0, length@2, imgPtr@4).
// Mirrors MapTileAnimation2CoreTests one-for-one with the anime1 schema
// differences: anime1_plist at +9 (not anime2_plist at +10), PlistType.ANIMATION
// (map_tileanime1_pointer), and the image pointer at +4 (not +0).
using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class MapTileAnimation1CoreTests
    {
        // -----------------------------------------------------------------
        // ScanEntries - stops at first non-pointer P4 (mirrors WF
        // InputFormRef predicate: isPointer(u32(addr+4)))
        // -----------------------------------------------------------------

        [Fact]
        public void ScanEntries_StopsAtFirstNonPointer()
        {
            var rom = new ROM();
            rom.LoadLow("synth.gba", new byte[0x1000000], "BE8E01");

            // Plant 3 valid 8-byte entries at base 0x200, then a zero P4 at row 3.
            // Schema: wait@0 (u16), length@2 (u16), imgPtr@4 (u32).
            uint baseAddr = 0x200;
            for (int i = 0; i < 3; i++)
            {
                uint addr = baseAddr + (uint)(i * 8);
                WriteU16(rom.Data, (int)addr + 0, (ushort)(0x10 + i)); // wait
                WriteU16(rom.Data, (int)addr + 2, (ushort)0x1000);     // length
                WriteU32(rom.Data, (int)addr + 4, 0x08800000u + (uint)(i * 0x100)); // imgPtr
            }
            // Row 3: zero P4 - scan should stop here.
            WriteU32(rom.Data, (int)(baseAddr + 3 * 8) + 4, 0u);

            var entries = MapTileAnimation1Core.ScanEntries(rom, baseAddr, 256);
            Assert.Equal(3, entries.Count);
            Assert.Equal(baseAddr, entries[0].Addr);
            Assert.Equal(0x10u, entries[0].Wait);
            Assert.Equal(0x1000u, entries[0].Length);
            Assert.Equal(0x08800000u, entries[0].P4);
        }

        [Fact]
        public void ScanEntries_HonorsMaxRows()
        {
            var rom = new ROM();
            rom.LoadLow("synth.gba", new byte[0x1000000], "BE8E01");

            uint baseAddr = 0x200;
            for (int i = 0; i < 10; i++)
            {
                uint addr = baseAddr + (uint)(i * 8);
                WriteU32(rom.Data, (int)addr + 4, 0x08800000u + (uint)(i * 0x100));
            }
            var entries = MapTileAnimation1Core.ScanEntries(rom, baseAddr, maxRows: 4);
            Assert.Equal(4, entries.Count);
        }

        [Fact]
        public void ScanEntries_NullRom_ReturnsEmpty()
        {
            var entries = MapTileAnimation1Core.ScanEntries(null, 0x200, 256);
            Assert.NotNull(entries);
            Assert.Empty(entries);
        }

        // -----------------------------------------------------------------
        // BuildPlistList - mirrors WF MakeTileAnimation1 semantics
        // -----------------------------------------------------------------

        [Fact]
        public void BuildPlistList_WithNullRom_ReturnsEmpty()
        {
            var rows = MapTileAnimation1Core.BuildPlistList(null);
            Assert.NotNull(rows);
            Assert.Empty(rows);
        }

        [Fact]
        public void BuildPlistList_WithZeroPointer_ReturnsEmpty()
        {
            var rom = new ROM();
            rom.LoadLow("synth.gba", new byte[0x1000000], "BE8E01");
            WriteU32(rom.Data, (int)rom.RomInfo.map_tileanime1_pointer, 0u);

            var rows = MapTileAnimation1Core.BuildPlistList(rom);
            Assert.NotNull(rows);
            Assert.Empty(rows);
        }

        [Fact]
        public void BuildPlistList_OmitsZeroAnime1Plist()
        {
            // Build a single map setting with anime1_plist == 0 (offset +9) - it
            // must not appear in the result.
            var rom = MakeSyntheticFE8URom(out uint mapBase, out uint plistTableBase, out uint mapPtrBase);
            int mapBaseI = (int)mapBase;
            rom.Data[mapBaseI + 9] = 0; // anime1_plist == 0
            WriteU32(rom.Data, (int)mapBase + 0, 0x08800000u); // D0=pointer => valid map

            var rows = MapTileAnimation1Core.BuildPlistList(rom);
            Assert.Empty(rows);
        }

        [Fact]
        public void BuildPlistList_DedupesDuplicateMapReferences()
        {
            // Two map settings BOTH pointing at anime1_plist == 1 - only one row.
            var rom = MakeSyntheticFE8URom(out uint mapBase, out uint plistTableBase, out uint mapPtrBase);
            int mapBaseI = (int)mapBase;
            int dataSizeI = (int)rom.RomInfo.map_setting_datasize;

            // map[0]: D0=pointer, anime1_plist=1 (offset +9)
            WriteU32(rom.Data, mapBaseI + 0, 0x08800000u);
            rom.Data[mapBaseI + 9] = 1;
            // map[1]: D0=pointer, anime1_plist=1 (duplicate)
            WriteU32(rom.Data, mapBaseI + dataSizeI + 0, 0x08800100u);
            rom.Data[mapBaseI + dataSizeI + 9] = 1;
            // map[2]: D0=0 (non-pointer) terminator that fails validation
            WriteU32(rom.Data, mapBaseI + 2 * dataSizeI + 0, 0u);

            // PLIST table entry for plist=1 -> a valid data block.
            uint plist1Slot = plistTableBase + 1 * 4;
            WriteU32(rom.Data, (int)plist1Slot, 0x08900000u);
            // Plant a valid 8-byte entry at 0x900000 (imgPtr at +4) so the block
            // dereferences cleanly.
            WriteU32(rom.Data, (int)0x900000 + 4, 0x08A00000u);

            var rows = MapTileAnimation1Core.BuildPlistList(rom);
            Assert.Single(rows);
            Assert.Equal(1u, rows[0].Plist);
            Assert.False(rows[0].IsBroken);
        }

        [Fact]
        public void BuildPlistList_MarksBrokenPlistAddress()
        {
            // PLIST table entry for plist=2 dereferences to ZERO - row is
            // included with isBroken=true.
            var rom = MakeSyntheticFE8URom(out uint mapBase, out uint plistTableBase, out uint mapPtrBase);
            int mapBaseI = (int)mapBase;
            int dataSizeI = (int)rom.RomInfo.map_setting_datasize;

            WriteU32(rom.Data, mapBaseI + 0, 0x08800000u);
            rom.Data[mapBaseI + 9] = 2; // anime1_plist = 2
            WriteU32(rom.Data, mapBaseI + dataSizeI + 0, 0u); // terminator

            // PLIST table entry for plist=2 -> zero pointer (broken).
            WriteU32(rom.Data, (int)(plistTableBase + 2 * 4), 0u);

            var rows = MapTileAnimation1Core.BuildPlistList(rom);
            Assert.Single(rows);
            Assert.Equal(2u, rows[0].Plist);
            Assert.True(rows[0].IsBroken);
            // Broken suffix (R._("(破損)")) appended - assert the open paren.
            Assert.True(rows[0].Display.Contains("(") || rows[0].Display.Contains("破損"),
                $"Display should contain WF broken suffix: '{rows[0].Display}'");
        }

        // -----------------------------------------------------------------
        // BuildPlistList resolved-label semantics (#952, #955)
        // -----------------------------------------------------------------

        [Fact]
        public void BuildPlistList_ResolvesFilterLabelToAnime1MapName()
        {
            var rom = MakeSyntheticFE8URom(out uint mapBase, out uint plistTableBase, out uint mapPtrBase);
            int mapBaseI = (int)mapBase;
            int dataSizeI = (int)rom.RomInfo.map_setting_datasize;

            // map[0]: D0=pointer (valid), anime1_plist=1
            WriteU32(rom.Data, mapBaseI + 0, 0x08800000u);
            rom.Data[mapBaseI + 9] = 1;
            // map[1]: terminator
            WriteU32(rom.Data, mapBaseI + dataSizeI + 0, 0u);

            // PLIST table entry for plist=1 -> valid data block.
            WriteU32(rom.Data, (int)(plistTableBase + 1 * 4), 0x08900000u);
            WriteU32(rom.Data, (int)0x900000 + 4, 0x08A00000u);

            var rows = MapTileAnimation1Core.BuildPlistList(rom);
            Assert.Single(rows);
            Assert.Equal(1u, rows[0].Plist);
            Assert.False(rows[0].IsBroken);

            // Must be the resolved ANIME1 label (split layout: anime1_plist==1
            // matches under ANIMATION; non-split: first matching field is
            // anime1_plist so still "ANIME1 ...").
            Assert.StartsWith("ANIME1 ", rows[0].Display);
            // Must NOT be a raw 0x pointer label.
            Assert.DoesNotContain("0x08", rows[0].Display);
        }

        [Fact]
        public void BuildPlistList_BrokenRow_KeepsResolvedLabelPlusBrokenSuffix()
        {
            var rom = MakeSyntheticFE8URom(out uint mapBase, out uint plistTableBase, out uint mapPtrBase);
            int mapBaseI = (int)mapBase;
            int dataSizeI = (int)rom.RomInfo.map_setting_datasize;

            WriteU32(rom.Data, mapBaseI + 0, 0x08800000u);
            rom.Data[mapBaseI + 9] = 2;
            WriteU32(rom.Data, mapBaseI + dataSizeI + 0, 0u);
            // plist=2 slot -> zero (broken).
            WriteU32(rom.Data, (int)(plistTableBase + 2 * 4), 0u);

            var rows = MapTileAnimation1Core.BuildPlistList(rom);
            Assert.Single(rows);
            Assert.True(rows[0].IsBroken);
            // Resolved prefix + broken suffix paren.
            Assert.Contains("(", rows[0].Display);
        }

        // -----------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------

        /// <summary>
        /// Build a tiny synthetic FE8U ROM. Sets the map_setting_pointer to
        /// 0x08800000 (map data at 0x800000) and the map_tileanime1_pointer to
        /// dereference to a PLIST table at 0x900000.
        /// </summary>
        static ROM MakeSyntheticFE8URom(out uint mapBase, out uint plistTableBase, out uint mapPtrBase)
        {
            var rom = new ROM();
            rom.LoadLow("synth.gba", new byte[0x1000000], "BE8E01");

            mapPtrBase = rom.RomInfo.map_setting_pointer;
            mapBase = 0x800000;
            plistTableBase = 0x900000;

            WriteU32(rom.Data, (int)mapPtrBase, 0x08000000u | mapBase);
            WriteU32(rom.Data, (int)rom.RomInfo.map_tileanime1_pointer, 0x08000000u | plistTableBase);

            return rom;
        }

        static void WriteU32(byte[] data, int offset, uint value)
        {
            data[offset + 0] = (byte)(value & 0xFF);
            data[offset + 1] = (byte)((value >> 8) & 0xFF);
            data[offset + 2] = (byte)((value >> 16) & 0xFF);
            data[offset + 3] = (byte)((value >> 24) & 0xFF);
        }

        static void WriteU16(byte[] data, int offset, ushort value)
        {
            data[offset + 0] = (byte)(value & 0xFF);
            data[offset + 1] = (byte)((value >> 8) & 0xFF);
        }
    }
}
