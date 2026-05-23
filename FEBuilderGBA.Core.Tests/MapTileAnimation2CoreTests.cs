// SPDX-License-Identifier: GPL-3.0-or-later
// Phase 1 gap-sweep tests for MapTileAnimation2Core (#426).
//
// Headless mirror of WinForms MapTileAnimation2Form's static helpers
// (MakeTileAnimation2, ExportAll/ImportAll palette enumeration) so the
// Avalonia view can drive list population, palette decoding, and round-trip
// validation without WinForms dependencies.
using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Tests proving MapTileAnimation2Core mirrors the WF MakeTileAnimation2 /
    /// per-PLIST scan / RGB-to-GBA conversion semantics. Mutates CoreState.ROM
    /// in several cases (Avalonia view path) and direct ROM in others.
    /// </summary>
    [Collection("SharedState")]
    public class MapTileAnimation2CoreTests
    {
        // -----------------------------------------------------------------
        // GbaToRgb / RgbToGba round-trip
        // -----------------------------------------------------------------

        [Fact]
        public void RgbToGba_Encodes15BitColor()
        {
            // R=0xF8 (top 5 bits), G=0x10 (top 5 bits = 2), B=0x80 (top 5 bits = 16)
            // Expected: r5=31 (0x1F), g5=2 (0x02), b5=16 (0x10)
            // GBA layout: bbbbbgggggrrrrr -> (16 << 10) | (2 << 5) | 31
            //          = 0x4000 | 0x0040 | 0x001F = 0x405F
            ushort encoded = MapTileAnimation2Core.RgbToGba(0xF8, 0x10, 0x80);
            Assert.Equal((ushort)0x405F, encoded);
        }

        [Fact]
        public void GbaToRgb_Decodes15BitColor()
        {
            // From the previous round: 0x405F -> r5=31, g5=2, b5=16
            // Decoded with 3-bit shift: r=248, g=16, b=128
            var (r, g, b) = MapTileAnimation2Core.GbaToRgb(0x405F);
            Assert.Equal((byte)0xF8, r);
            Assert.Equal((byte)0x10, g);
            Assert.Equal((byte)0x80, b);
        }

        [Fact]
        public void RgbToGba_GbaToRgb_RoundTrips_AcrossKnownColors()
        {
            // Truncate-to-3 round-trip: (r,g,b) where each is already a multiple of 8.
            byte[] testValues = { 0, 8, 0x40, 0x80, 0xF8 };
            foreach (byte r in testValues)
                foreach (byte g in testValues)
                    foreach (byte b in testValues)
                    {
                        ushort gba = MapTileAnimation2Core.RgbToGba(r, g, b);
                        var (rb, gb, bb) = MapTileAnimation2Core.GbaToRgb(gba);
                        Assert.Equal(r, rb);
                        Assert.Equal(g, gb);
                        Assert.Equal(b, bb);
                    }
        }

        // -----------------------------------------------------------------
        // ScanEntries - stops at first non-pointer P0 (mirrors WF
        // InputFormRef predicate: isPointer(u32(addr+0)))
        // -----------------------------------------------------------------

        [Fact]
        public void ScanEntries_StopsAtFirstNonPointer()
        {
            var rom = new ROM();
            rom.LoadLow("synth.gba", new byte[0x1000000], "BE8E01");

            // Plant 3 valid 8-byte entries at base 0x200, then a zero P0 at row 3.
            uint baseAddr = 0x200;
            for (int i = 0; i < 3; i++)
            {
                uint addr = baseAddr + (uint)(i * 8);
                WriteU32(rom.Data, (int)addr, 0x08800000u + (uint)(i * 0x100)); // valid GBA pointer
                rom.Data[(int)addr + 4] = 0x10; // wait
                rom.Data[(int)addr + 5] = 0x04; // count
                rom.Data[(int)addr + 6] = 0x00; // startindex
                rom.Data[(int)addr + 7] = 0x00; // padding
            }
            // Row 3: zero P0 - scan should stop here.
            WriteU32(rom.Data, (int)(baseAddr + 3 * 8), 0u);

            var entries = MapTileAnimation2Core.ScanEntries(rom, baseAddr, 256);
            Assert.Equal(3, entries.Count);
            Assert.Equal(0x08800000u, entries[0].P0);
            Assert.Equal(0x10u, entries[0].Wait);
            Assert.Equal(0x04u, entries[0].Count);
        }

        [Fact]
        public void ScanEntries_HonorsMaxRows()
        {
            var rom = new ROM();
            rom.LoadLow("synth.gba", new byte[0x1000000], "BE8E01");

            // Plant 10 valid entries.
            uint baseAddr = 0x200;
            for (int i = 0; i < 10; i++)
            {
                uint addr = baseAddr + (uint)(i * 8);
                WriteU32(rom.Data, (int)addr, 0x08800000u + (uint)(i * 0x100));
            }
            var entries = MapTileAnimation2Core.ScanEntries(rom, baseAddr, maxRows: 4);
            Assert.Equal(4, entries.Count);
        }

        // -----------------------------------------------------------------
        // BuildPlistList - mirrors WF MakeTileAnimation2 semantics
        // -----------------------------------------------------------------

        [Fact]
        public void BuildPlistList_WithNullRom_ReturnsEmpty()
        {
            var rows = MapTileAnimation2Core.BuildPlistList(null);
            Assert.NotNull(rows);
            Assert.Empty(rows);
        }

        [Fact]
        public void BuildPlistList_WithZeroPointer_ReturnsEmpty()
        {
            // ROM where map_tileanime2_pointer dereferences to zero.
            var rom = new ROM();
            rom.LoadLow("synth.gba", new byte[0x1000000], "BE8E01");
            // Leave map_tileanime2_pointer area pointing to zero.
            WriteU32(rom.Data, (int)rom.RomInfo.map_tileanime2_pointer, 0u);

            var rows = MapTileAnimation2Core.BuildPlistList(rom);
            Assert.NotNull(rows);
            Assert.Empty(rows);
        }

        [Fact]
        public void BuildPlistList_OmitsZeroAnime2Plist()
        {
            // Build a single map setting with anime2_plist == 0 - it must not
            // appear in the result.
            var rom = MakeSyntheticFE8URom(out uint mapBase, out uint plistTableBase, out uint mapPtrBase);
            int mapBaseI = (int)mapBase;
            // map[0]: anime2_plist == 0
            rom.Data[mapBaseI + 10] = 0;
            // Plant the rest of the map fields so MapSettingCore validates it
            // and stops enumeration cleanly. Setting D0 to a pointer is the
            // shortcut WinForms uses; subsequent map entries will then fail
            // validation and stop the loop.
            WriteU32(rom.Data, (int)mapBase + 0, 0x08800000u); // D0=pointer => valid

            var rows = MapTileAnimation2Core.BuildPlistList(rom);
            Assert.Empty(rows);
        }

        [Fact]
        public void BuildPlistList_DedupesDuplicateMapReferences()
        {
            // Build two map settings BOTH pointing at anime2_plist == 1 - only
            // one row must appear.
            var rom = MakeSyntheticFE8URom(out uint mapBase, out uint plistTableBase, out uint mapPtrBase);
            int mapBaseI = (int)mapBase;
            int dataSizeI = (int)rom.RomInfo.map_setting_datasize;

            // map[0]: D0=pointer, anime2_plist=1
            WriteU32(rom.Data, mapBaseI + 0, 0x08800000u);
            rom.Data[mapBaseI + 10] = 1;
            // map[1]: D0=pointer, anime2_plist=1 (duplicate)
            WriteU32(rom.Data, mapBaseI + dataSizeI + 0, 0x08800100u);
            rom.Data[mapBaseI + dataSizeI + 10] = 1;
            // map[2]: D0=0 (non-pointer) terminator that fails validation
            WriteU32(rom.Data, mapBaseI + 2 * dataSizeI + 0, 0u);

            // PLIST table entry for plist=1 -> a valid data block.
            uint plist1Slot = plistTableBase + 1 * 4;
            uint plist1DataPtr = 0x08900000u;
            WriteU32(rom.Data, (int)plist1Slot, plist1DataPtr);
            // Plant a valid 8-byte entry at offset 0x900000 so the data block
            // dereferences cleanly. (The dedupe check happens BEFORE the
            // address-resolution check, so even a missing block would still
            // dedupe; this just makes the row not flagged broken.)
            uint dataBlockOffset = 0x900000;
            WriteU32(rom.Data, (int)dataBlockOffset + 0, 0x08A00000u);

            var rows = MapTileAnimation2Core.BuildPlistList(rom);
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

            // map[0]: D0=pointer, anime2_plist=2
            WriteU32(rom.Data, mapBaseI + 0, 0x08800000u);
            rom.Data[mapBaseI + 10] = 2;
            // map[1]: terminator
            WriteU32(rom.Data, mapBaseI + dataSizeI + 0, 0u);

            // PLIST table entry for plist=2 -> zero pointer (broken).
            uint plist2Slot = plistTableBase + 2 * 4;
            WriteU32(rom.Data, (int)plist2Slot, 0u);

            var rows = MapTileAnimation2Core.BuildPlistList(rom);
            Assert.Single(rows);
            Assert.Equal(2u, rows[0].Plist);
            Assert.True(rows[0].IsBroken);
            Assert.Contains("broken", rows[0].Display);
        }

        // -----------------------------------------------------------------
        // BuildPaletteList - decodes per-row RGB
        // -----------------------------------------------------------------

        [Fact]
        public void BuildPaletteList_ConvertsRgb555Rows()
        {
            // Plant 3 u16 GBA colors at 0x800000 and check decoded RGB tuples.
            var rom = new ROM();
            rom.LoadLow("synth.gba", new byte[0x1000000], "BE8E01");
            uint dataOffset = 0x800000;

            // Row 0: white (0x7FFF -> 248, 248, 248)
            WriteU16(rom.Data, (int)dataOffset + 0, 0x7FFF);
            // Row 1: red (0x001F -> 248, 0, 0)
            WriteU16(rom.Data, (int)dataOffset + 2, 0x001F);
            // Row 2: blue (0x7C00 -> 0, 0, 248)
            WriteU16(rom.Data, (int)dataOffset + 4, 0x7C00);

            uint dataPointer = 0x08000000u | dataOffset;
            var rows = MapTileAnimation2Core.BuildPaletteList(rom, dataPointer, count: 3);

            Assert.Equal(3, rows.Count);
            Assert.Equal((byte)0xF8, rows[0].R);
            Assert.Equal((byte)0xF8, rows[0].G);
            Assert.Equal((byte)0xF8, rows[0].B);
            Assert.Equal((byte)0xF8, rows[1].R);
            Assert.Equal((byte)0x00, rows[1].G);
            Assert.Equal((byte)0x00, rows[1].B);
            Assert.Equal((byte)0x00, rows[2].R);
            Assert.Equal((byte)0x00, rows[2].G);
            Assert.Equal((byte)0xF8, rows[2].B);
        }

        [Fact]
        public void BuildPaletteList_WithUnsafePointer_ReturnsEmpty()
        {
            var rom = new ROM();
            rom.LoadLow("synth.gba", new byte[0x1000000], "BE8E01");
            var rows = MapTileAnimation2Core.BuildPaletteList(rom, dataPointer: 0u, count: 4);
            Assert.Empty(rows);
        }

        // -----------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------

        /// <summary>
        /// Build a tiny synthetic FE8U ROM. Sets the map_setting_pointer to
        /// 0x08800000 (so map_setting data starts at 0x800000) and the
        /// map_tileanime2_pointer to dereference to a PLIST table at
        /// 0x900000. Returns the map-data base offset, the PLIST table base
        /// offset, and the raw mapPtrBase address.
        /// </summary>
        static ROM MakeSyntheticFE8URom(out uint mapBase, out uint plistTableBase, out uint mapPtrBase)
        {
            var rom = new ROM();
            rom.LoadLow("synth.gba", new byte[0x1000000], "BE8E01");

            mapPtrBase = rom.RomInfo.map_setting_pointer;
            mapBase = 0x800000;
            plistTableBase = 0x900000;

            // Plant: map_setting_pointer -> 0x08800000 (mapBase + 0x08000000)
            WriteU32(rom.Data, (int)mapPtrBase, 0x08000000u | mapBase);
            // Plant: map_tileanime2_pointer -> 0x08900000 (plistTableBase + 0x08000000)
            WriteU32(rom.Data, (int)rom.RomInfo.map_tileanime2_pointer, 0x08000000u | plistTableBase);

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
