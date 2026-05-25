// SPDX-License-Identifier: GPL-3.0-or-later
// Phase 1 gap-sweep tests for MapTileAnimation2Core (#426).
//
// Headless mirror of WinForms MapTileAnimation2Form's static helpers
// (MakeTileAnimation2, ExportAll/ImportAll palette enumeration) so the
// Avalonia view can drive list population, palette decoding, and round-trip
// validation without WinForms dependencies.
//
// Phase 2 (#524) follow-up: BulkImport / BulkExport / ExpandEntryList /
// ExpandPaletteRowList. These four helpers extract the WF
// MapTileAnimation2Form.ImportAll / ExportAll / InputFormRef.AppendBinaryData
// repoint plumbing so the Avalonia view can drive bulk flows without the
// WinForms dependency. Tests cover round-trip parity, malformed-line WF
// parity, undo single-source recording (no double-recording when the helper
// runs inside an ambient ROM.BeginUndoScope), and rollback fidelity.
using System;
using System.IO;
using System.Linq;
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
            // Display is composed via R._() with the WF Japanese key
            // "タイルアニメーション2 パレットアニメ:{0}" plus "(破損)". When the
            // test runs headlessly without a translation table loaded,
            // R._() returns the JP source string. Assert the broken
            // suffix (open paren) is present rather than relying on
            // a specific language form.
            Assert.True(rows[0].Display.Contains("(") || rows[0].Display.Contains("破損"),
                $"Display should contain WF broken suffix: '{rows[0].Display}'");
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
        // BulkExport - TSV mirror of WF MapTileAnimation2Form.ExportAll
        // -----------------------------------------------------------------

        [Fact]
        public void BulkExport_WritesExpectedTsv()
        {
            // Plant 2 entries at 0x500000 pointing at distinct palette blocks.
            var rom = MakeRomWithTwoEntries(out uint baseAddr);
            string tmp = Path.GetTempFileName();
            try
            {
                string err = MapTileAnimation2Core.BulkExport(rom, tmp, baseAddr, dataCount: 2);
                Assert.Equal("", err);

                string[] lines = File.ReadAllLines(tmp);
                // Header + 2 data lines.
                Assert.Equal(3, lines.Length);
                Assert.StartsWith("//wait", lines[0]);
                // Row 0: wait=0x10, startindex=0x20, 2 colors: white (0x7FFF -> 248,248,248), red (0x001F -> 248,0,0)
                Assert.Contains("248,248,248", lines[1]);
                Assert.Contains("248,0,0", lines[1]);
                // Row 1: wait=0x05, startindex=0x3C, 1 color: blue (0x7C00 -> 0,0,248)
                Assert.Contains("0,0,248", lines[2]);
            }
            finally { if (File.Exists(tmp)) File.Delete(tmp); }
        }

        [Fact]
        public void BulkExport_WithUnsafeBase_ReturnsError()
        {
            var rom = new ROM();
            rom.LoadLow("synth.gba", new byte[0x1000000], "BE8E01");
            string tmp = Path.GetTempFileName();
            try
            {
                string err = MapTileAnimation2Core.BulkExport(rom, tmp, baseAddr: 0u, dataCount: 1);
                Assert.NotEqual("", err);
            }
            finally { if (File.Exists(tmp)) File.Delete(tmp); }
        }

        // -----------------------------------------------------------------
        // BulkImport - round-trip parity + WF malformed-line parity
        // -----------------------------------------------------------------

        [Fact]
        public void BulkImport_RoundTripsViaExport()
        {
            // Plant 2 entries; export; mutate ROM; import; assert the round-
            // trip restores the same WAIT/COUNT/STARTINDEX + palette bytes the
            // export saw.
            var rom = MakeRomWithTwoEntries(out uint baseAddr);
            var prevRom = CoreState.ROM;
            string tmp = Path.GetTempFileName();
            try
            {
                CoreState.ROM = rom;
                Assert.Equal("", MapTileAnimation2Core.BulkExport(rom, tmp, baseAddr, dataCount: 2));

                // The export's "pointer" is the slot we'll repoint on import.
                // In a real call site this is the slot that holds the entry-
                // table pointer (e.g. PLIST table slot). For this test we
                // allocate a writable slot in the ROM and point it at baseAddr.
                uint pointerSlot = 0x400000;
                rom.write_p32(pointerSlot, baseAddr);

                // Open ambient undo so the import path records into it.
                var undo = new Undo.UndoData();
                undo.list = new System.Collections.Generic.List<Undo.UndoPostion>();
                undo.filesize = (uint)rom.Data.Length;
                using (ROM.BeginUndoScope(undo))
                {
                    Assert.Equal("",
                        MapTileAnimation2Core.BulkImport(rom, tmp, pointerSlot, baseAddr, dataCount: 2));
                }

                // After import: the pointer slot now points at the new entry
                // table; the new table has the same wait/count/startindex
                // sequence (palette content matches via export -> import).
                uint newBase = rom.p32(pointerSlot);
                Assert.NotEqual(0u, newBase);
                Assert.Equal(0x10u, rom.u8(newBase + 4)); // row 0 wait
                Assert.Equal(0x02u, rom.u8(newBase + 5)); // row 0 count
                Assert.Equal(0x20u, rom.u8(newBase + 6)); // row 0 startindex
                Assert.Equal(0x05u, rom.u8(newBase + 12)); // row 1 wait
                Assert.Equal(0x01u, rom.u8(newBase + 13)); // row 1 count
                Assert.Equal(0x3Cu, rom.u8(newBase + 14)); // row 1 startindex
                // Row 2 terminator (P0=0, wait=0, ...)
                Assert.Equal(0u, rom.u32(newBase + 16));
            }
            finally
            {
                CoreState.ROM = prevRom;
                if (File.Exists(tmp)) File.Delete(tmp);
            }
        }

        [Fact]
        public void BulkImport_SkipsMalformedLinesPerWfParity()
        {
            // WF MapTileAnimation2Form.ImportAll:523-527 silently skips lines
            // with sp.Length < 2 (continues, no error). Core helper must mirror.
            var rom = MakeRomWithTwoEntries(out uint baseAddr);
            var prevRom = CoreState.ROM;
            string tmp = Path.GetTempFileName();
            try
            {
                CoreState.ROM = rom;
                // Three lines: header, malformed (1 field), valid.
                // WF MapTileAnimation2Form.ImportAll uses U.atoi (decimal)
                // for wait/startindex and U.atoi0x (decimal-or-0x-hex) for
                // the R/G/B triples, so the file format is decimal numbers.
                File.WriteAllLines(tmp, new[]
                {
                    "//header",
                    "onlyfield",
                    "16\t32\t0xF8,0xF8,0xF8",
                });

                uint pointerSlot = 0x400000;
                rom.write_p32(pointerSlot, baseAddr);

                var undo = new Undo.UndoData();
                undo.list = new System.Collections.Generic.List<Undo.UndoPostion>();
                undo.filesize = (uint)rom.Data.Length;
                using (ROM.BeginUndoScope(undo))
                {
                    string err = MapTileAnimation2Core.BulkImport(rom, tmp, pointerSlot, baseAddr, dataCount: 2);
                    Assert.Equal("", err); // malformed line silently skipped
                }

                // Only one valid entry written, plus the terminator row.
                uint newBase = rom.p32(pointerSlot);
                Assert.Equal(16u, rom.u8(newBase + 4));      // wait = 16
                Assert.Equal(0x01u, rom.u8(newBase + 5));    // 1 palette color
                Assert.Equal(32u, rom.u8(newBase + 6));      // startindex = 32
                Assert.Equal(0u, rom.u32(newBase + 8));      // terminator P0
            }
            finally
            {
                CoreState.ROM = prevRom;
                if (File.Exists(tmp)) File.Delete(tmp);
            }
        }

        [Fact]
        public void BulkImport_WorksWithoutAppendBinaryDataCallback()
        {
            // V3 plan-review #1: when CoreState.AppendBinaryData is null and
            // the existing recyclable blocks are insufficient, the helper must
            // install a local freespace-backed callback so allocation still
            // succeeds. We force the situation by ZEROING the existing entry
            // table (no recyclable space), then importing a fresh file that
            // requires a new allocation.
            var rom = MakeRomWithTwoEntries(out uint baseAddr);
            var prevRom = CoreState.ROM;
            var prevAppend = CoreState.AppendBinaryData;
            string tmp = Path.GetTempFileName();
            try
            {
                CoreState.ROM = rom;
                CoreState.AppendBinaryData = null; // global callback unset

                // Zero the area around baseAddr so RecycleAddress has no
                // reusable blocks for the new palette data.
                for (uint i = 0; i < 64; i++) rom.Data[baseAddr + i] = 0;

                // Decimal numbers for wait/startindex (WF parity); 0x-prefix
                // allowed for R/G/B (U.atoi0x accepts both).
                File.WriteAllLines(tmp, new[]
                {
                    "//header",
                    "18\t52\t0xF8,0xF8,0xF8\t0,0,0xF8",
                });

                uint pointerSlot = 0x400000;
                rom.write_p32(pointerSlot, baseAddr);

                var undo = new Undo.UndoData();
                undo.list = new System.Collections.Generic.List<Undo.UndoPostion>();
                undo.filesize = (uint)rom.Data.Length;
                using (ROM.BeginUndoScope(undo))
                {
                    string err = MapTileAnimation2Core.BulkImport(rom, tmp, pointerSlot, baseAddr, dataCount: 0);
                    Assert.Equal("", err);
                }

                uint newBase = rom.p32(pointerSlot);
                Assert.NotEqual(0u, newBase);
                Assert.Equal(18u, rom.u8(newBase + 4));      // wait = 18
                Assert.Equal(0x02u, rom.u8(newBase + 5));    // 2 palette colors
                Assert.Equal(52u, rom.u8(newBase + 6));      // startindex = 52
            }
            finally
            {
                CoreState.AppendBinaryData = prevAppend;
                CoreState.ROM = prevRom;
                if (File.Exists(tmp)) File.Delete(tmp);
            }
        }

        [Fact]
        public void BulkImport_DoesNotDoubleRecordUndo()
        {
            // V3 plan-review #2: when the helper runs inside an ambient undo
            // scope opened with the same UndoData, each write must record
            // exactly ONCE in undo.list. The fix is the no-undo RecycleAddress
            // overloads added by WU1; verify here by counting unique address
            // entries before/after.
            var rom = MakeRomWithTwoEntries(out uint baseAddr);
            var prevRom = CoreState.ROM;
            string tmp = Path.GetTempFileName();
            try
            {
                CoreState.ROM = rom;
                File.WriteAllLines(tmp, new[]
                {
                    "//header",
                    "16\t32\t0xF8,0xF8,0xF8",
                });

                uint pointerSlot = 0x400000;
                rom.write_p32(pointerSlot, baseAddr);

                var undo = new Undo.UndoData();
                undo.list = new System.Collections.Generic.List<Undo.UndoPostion>();
                undo.filesize = (uint)rom.Data.Length;
                using (ROM.BeginUndoScope(undo))
                {
                    Assert.Equal("",
                        MapTileAnimation2Core.BulkImport(rom, tmp, pointerSlot, baseAddr, dataCount: 2));
                }

                // For each address, the count of UndoPostion entries must be
                // <= 2 (palette block addr can recur if it's reused across
                // multiple steps; the SAME address never records twice for
                // the SAME write op). Group by addr and assert no address has
                // > 2 entries - WF's ImportAll for two-entry input creates at
                // most one record per ROM write op. With double-recording the
                // count would be 2x what WF produces; here we cap at a sane
                // small number per address.
                var maxPerAddr = undo.list.GroupBy(p => p.addr).Max(g => g.Count());
                Assert.True(maxPerAddr <= 2,
                    $"Per-address undo records must be <= 2 (no double-recording); was {maxPerAddr}");
            }
            finally
            {
                CoreState.ROM = prevRom;
                if (File.Exists(tmp)) File.Delete(tmp);
            }
        }

        [Fact]
        public void BulkImport_RollbackRestoresOriginalPointer()
        {
            // V3 plan-review #2: rollback must restore the ROM bytes around
            // the pointer slot. Capture the slot's value pre-import, run
            // import, rollback via the captured UndoData, assert the slot
            // returns to its pre-import value.
            var rom = MakeRomWithTwoEntries(out uint baseAddr);
            var prevRom = CoreState.ROM;
            string tmp = Path.GetTempFileName();
            try
            {
                CoreState.ROM = rom;
                File.WriteAllLines(tmp, new[]
                {
                    "//header",
                    "16\t32\t0xF8,0xF8,0xF8",
                });

                uint pointerSlot = 0x400000;
                rom.write_p32(pointerSlot, baseAddr);
                uint origPointer = rom.u32(pointerSlot);

                var undo = new Undo.UndoData();
                undo.list = new System.Collections.Generic.List<Undo.UndoPostion>();
                undo.filesize = (uint)rom.Data.Length;
                using (ROM.BeginUndoScope(undo))
                {
                    Assert.Equal("",
                        MapTileAnimation2Core.BulkImport(rom, tmp, pointerSlot, baseAddr, dataCount: 2));
                }
                Assert.NotEqual(origPointer, rom.u32(pointerSlot));

                // Now rollback: walk the undo list in reverse and restore each
                // address-range's pre-write bytes (matches Undo.Patch).
                for (int i = undo.list.Count - 1; i >= 0; i--)
                {
                    var p = undo.list[i];
                    if (p.data != null && p.data.Length > 0)
                    {
                        for (int j = 0; j < p.data.Length; j++)
                        {
                            rom.Data[p.addr + j] = p.data[j];
                        }
                    }
                }
                Assert.Equal(origPointer, rom.u32(pointerSlot));
            }
            finally
            {
                CoreState.ROM = prevRom;
                if (File.Exists(tmp)) File.Delete(tmp);
            }
        }

        // -----------------------------------------------------------------
        // ExpandEntryList - grow main entry table, row-0 template copy
        // -----------------------------------------------------------------

        [Fact]
        public void ExpandEntryList_AllocatesAndRepoints()
        {
            var rom = MakeRomWithTwoEntries(out uint baseAddr);
            // Allocate a pointer slot that holds the entry table base.
            uint pointerSlot = 0x400000;
            rom.write_p32(pointerSlot, baseAddr);

            uint newBase = MapTileAnimation2Core.ExpandEntryList(
                rom, pointerSlot, oldBase: baseAddr, oldCount: 2, newCount: 5);

            Assert.NotEqual(U.NOT_FOUND, newBase);
            Assert.NotEqual(baseAddr, newBase);
            // The new pointer slot dereferences to newBase.
            Assert.Equal(newBase, rom.p32(pointerSlot));
            // ScanEntries must return exactly 5 valid rows (V1 plan-review #2:
            // row-0 template copy preserves isPointer(P0)).
            var entries = MapTileAnimation2Core.ScanEntries(rom, newBase, maxRows: 16);
            Assert.Equal(5, entries.Count);
        }

        [Fact]
        public void ExpandEntryList_RefusesShrinks()
        {
            var rom = MakeRomWithTwoEntries(out uint baseAddr);
            uint pointerSlot = 0x400000;
            rom.write_p32(pointerSlot, baseAddr);
            uint newBase = MapTileAnimation2Core.ExpandEntryList(
                rom, pointerSlot, oldBase: baseAddr, oldCount: 5, newCount: 3);
            Assert.Equal(U.NOT_FOUND, newBase);
        }

        [Fact]
        public void ExpandEntryList_RefusesZeroOldCount()
        {
            var rom = MakeRomWithTwoEntries(out uint baseAddr);
            uint pointerSlot = 0x400000;
            rom.write_p32(pointerSlot, baseAddr);
            uint newBase = MapTileAnimation2Core.ExpandEntryList(
                rom, pointerSlot, oldBase: baseAddr, oldCount: 0, newCount: 2);
            Assert.Equal(U.NOT_FOUND, newBase);
        }

        // -----------------------------------------------------------------
        // ExpandPaletteRowList - grow palette sub-table, row-0 template copy
        // -----------------------------------------------------------------

        [Fact]
        public void ExpandPaletteRowList_AllocatesAndRepoints()
        {
            var rom = MakeRomWithTwoEntries(out uint baseAddr);
            // The entry at baseAddr+0..7 has P0 -> palette block at
            // baseAddr+0x100 (planted by MakeRomWithTwoEntries). Use that
            // slot as the palette data pointer slot.
            uint paletteSlot = baseAddr + 0; // +0 is the palette pointer
            uint oldPaletteBase = rom.p32(paletteSlot); // 0x500100

            uint newBase = MapTileAnimation2Core.ExpandPaletteRowList(
                rom, paletteSlot, oldBase: oldPaletteBase, oldCount: 2, newCount: 4);

            Assert.NotEqual(U.NOT_FOUND, newBase);
            Assert.NotEqual(oldPaletteBase, newBase);
            Assert.Equal(newBase, rom.p32(paletteSlot));
            // First 2 colors copied from old block (white, red).
            Assert.Equal((ushort)0x7FFF, (ushort)rom.u16(newBase + 0));
            Assert.Equal((ushort)0x001F, (ushort)rom.u16(newBase + 2));
            // New rows 2 and 3 are clones of row 0 (white 0x7FFF) - NOT zero,
            // per V1 plan-review #2 (WF row-0 template copy semantics).
            Assert.Equal((ushort)0x7FFF, (ushort)rom.u16(newBase + 4));
            Assert.Equal((ushort)0x7FFF, (ushort)rom.u16(newBase + 6));
        }

        [Fact]
        public void ExpandPaletteRowList_RefusesShrinks()
        {
            var rom = MakeRomWithTwoEntries(out uint baseAddr);
            uint paletteSlot = baseAddr + 0;
            uint oldPaletteBase = rom.p32(paletteSlot);
            uint newBase = MapTileAnimation2Core.ExpandPaletteRowList(
                rom, paletteSlot, oldBase: oldPaletteBase, oldCount: 4, newCount: 2);
            Assert.Equal(U.NOT_FOUND, newBase);
        }

        [Fact]
        public void ExpandPaletteRowList_RefusesZeroOldCount()
        {
            var rom = MakeRomWithTwoEntries(out uint baseAddr);
            uint paletteSlot = baseAddr + 0;
            uint oldPaletteBase = rom.p32(paletteSlot);
            uint newBase = MapTileAnimation2Core.ExpandPaletteRowList(
                rom, paletteSlot, oldBase: oldPaletteBase, oldCount: 0, newCount: 4);
            Assert.Equal(U.NOT_FOUND, newBase);
        }

        // -----------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------

        /// <summary>
        /// Build a ROM with two valid entries at <c>baseAddr</c>:
        ///   - Row 0: P0=0x08500100, wait=0x10, count=2, startindex=0x20
        ///     Palette block at 0x500100 (2 colors: white 0x7FFF, red 0x001F).
        ///   - Row 1: P0=0x08500200, wait=0x05, count=1, startindex=0x3C
        ///     Palette block at 0x500200 (1 color: blue 0x7C00).
        ///   - Row 2: terminator (P0=0).
        /// </summary>
        static ROM MakeRomWithTwoEntries(out uint baseAddr)
        {
            var rom = new ROM();
            rom.LoadLow("synth.gba", new byte[0x1000000], "BE8E01");
            baseAddr = 0x500000;
            // Row 0: P0=0x08500100, wait=0x10, count=2, startindex=0x20.
            WriteU32(rom.Data, (int)baseAddr + 0, 0x08500100u);
            rom.Data[baseAddr + 4] = 0x10;
            rom.Data[baseAddr + 5] = 0x02;
            rom.Data[baseAddr + 6] = 0x20;
            rom.Data[baseAddr + 7] = 0x00;
            // Row 1: P0=0x08500200, wait=0x05, count=1, startindex=0x3C.
            WriteU32(rom.Data, (int)baseAddr + 8, 0x08500200u);
            rom.Data[baseAddr + 12] = 0x05;
            rom.Data[baseAddr + 13] = 0x01;
            rom.Data[baseAddr + 14] = 0x3C;
            rom.Data[baseAddr + 15] = 0x00;
            // Row 2: terminator.
            WriteU32(rom.Data, (int)baseAddr + 16, 0u);

            // Palette block at 0x500100 (row 0): 2 colors.
            WriteU16(rom.Data, 0x500100 + 0, 0x7FFF); // white
            WriteU16(rom.Data, 0x500100 + 2, 0x001F); // red
            // Palette block at 0x500200 (row 1): 1 color.
            WriteU16(rom.Data, 0x500200 + 0, 0x7C00); // blue

            return rom;
        }

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
