// SPDX-License-Identifier: GPL-3.0-or-later
// #1456: the TSA Animation Editor v2 must expose the FULL per-category 12-byte
// entry list (the WinForms ImageTSAAnime2Form second-level list), not just the
// first entry of each category. WinForms walks 12-byte strides from dataAddr+20,
// counting entry i while isPointer(u32(addr+8)) is true (ROM.getBlockDataCount).
//
// These tests lock:
//  - CountCategoryEntries matches the WinForms stop condition (pointer SHAPE at
//    addr+8, EOF-hard-bound).
//  - LoadList emits one AddrResult per entry, stride 12 == loader record size.
//  - HeaderBase resolves the SHARED category header for entry[i>0] (so the
//    coupled IMAGE/PALETTE pointers don't drift), keyed on CurrentAddr so a
//    stale/raw-navigation address falls back to the entry[0] formula.
using System;
using System.Collections.Generic;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class ImageTSAAnime2EntryListTests
    {
        readonly ITestOutputHelper _output;
        public ImageTSAAnime2EntryListTests(ITestOutputHelper output) { _output = output; }

        const uint SIZE = 12;
        const uint HEADER = ImageTSAAnime2ViewModel.HEADER_SIZE; // 20

        // Build a synthetic ROM with one category header at baseGba (GBA addr),
        // and K consecutive 12-byte entries each carrying a pointer-shaped value
        // at +8, followed by a non-pointer terminator entry.
        static ROM MakeRomWithCategory(uint headerBaseGba, int entryCount, out uint headerBaseOff)
        {
            var bytes = new byte[0x1000000];
            var rom = new ROM();
            rom.LoadLow("test.gba", bytes, "BE8E01"); // FE8U signature
            headerBaseOff = U.toOffset(headerBaseGba);

            // shared header: palette @ +4, image @ +16 (values not exercised here)
            uint entry0Off = headerBaseOff + HEADER;
            for (int i = 0; i < entryCount; i++)
            {
                uint e = entry0Off + (uint)i * SIZE;
                // a pointer-shaped TSA value at +8 keeps the walk going
                rom.write_u32(e + 8, 0x08C00000u + (uint)i * 0x100u);
            }
            // terminator entry: +8 is NOT pointer-shaped
            uint term = entry0Off + (uint)entryCount * SIZE;
            rom.write_u32(term + 8, 0x00000000u);
            return rom;
        }

        [Fact]
        public void CountCategoryEntries_StopsAtFirstNonPointer()
        {
            var rom = MakeRomWithCategory(0x08500000u, 14, out uint baseOff);
            uint entry0 = baseOff + HEADER;
            Assert.Equal(14u, ImageTSAAnime2ViewModel.CountCategoryEntries(rom, entry0));
        }

        [Fact]
        public void CountCategoryEntries_ZeroWhenEntry0NotPointer()
        {
            var rom = MakeRomWithCategory(0x08500000u, 0, out uint baseOff);
            uint entry0 = baseOff + HEADER;
            Assert.Equal(0u, ImageTSAAnime2ViewModel.CountCategoryEntries(rom, entry0));
        }

        [Fact]
        public void CountCategoryEntries_EofHardBound_DoesNotRunPastEnd()
        {
            // Place entry0 within 12 bytes of EOF: the walk must not read past it.
            var bytes = new byte[0x1000000];
            var rom = new ROM();
            rom.LoadLow("test.gba", bytes, "BE8E01");
            uint len = (uint)rom.Data.Length;
            uint entry0 = len - 8; // entry0 + SIZE(12) > len -> immediate stop
            Assert.Equal(0u, ImageTSAAnime2ViewModel.CountCategoryEntries(rom, entry0));
        }

        [Fact]
        public void LoadList_SyntheticCategory_EmitsEveryEntry_AndHeaderBaseStaysShared()
        {
            // Drive the REAL LoadList path on a synthetic ROM + crafted config so
            // the map population and per-entry HeaderBase resolution are proven
            // deterministically (no real ROM required). Without the #1456 fix,
            // LoadList emits one row per category and HeaderBase for entry[i>0]
            // would be (addr-20), pointing INTO the entry list, not the header.
            const int K = 6;                       // entries in the category
            uint headerBaseGba = 0x08500000u;      // category data address (GBA)
            // category pointer slot: pick a stable, in-range offset to hold the
            // GBA pointer to the header base.
            uint catPointerGba = 0x08010000u;

            var rom = MakeRomWithCategory(headerBaseGba, K, out uint headerBaseOff);
            // Seed the category pointer slot so p32(catPointerOff) == headerBaseGba.
            uint catPointerOff = U.toOffset(catPointerGba);
            rom.write_u32(catPointerOff, headerBaseGba);

            var prevRom = CoreState.ROM;
            var prevBase = CoreState.BaseDirectory;
            var prevLang = CoreState.Language;
            string tmp = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "tsaanime2_test_" + Guid.NewGuid().ToString("N"));
            try
            {
                CoreState.ROM = rom;
                CoreState.Language = "en";
                CoreState.BaseDirectory = tmp;

                // Write the config resource LoadList consumes. The filename must
                // match U.ConfigDataFilename("tsaanime2_", rom): tsaanime2_<Title>.en.txt
                string dataDir = System.IO.Path.Combine(tmp, "config", "data");
                System.IO.Directory.CreateDirectory(dataDir);
                string resPath = U.ConfigDataFilename("tsaanime2_", rom);
                System.IO.File.WriteAllText(resPath,
                    U.ToHexString(catPointerGba) + "\tSynthCategory\n");

                var vm = new ImageTSAAnime2ViewModel();
                var list = vm.LoadList();

                // Every one of the K entries is enumerated (the bug emitted 1).
                Assert.Equal(K, list.Count);

                // Stride-12 geometry from entry0 = headerBase + 20.
                uint entry0 = headerBaseOff + HEADER;
                for (int i = 0; i < K; i++)
                    Assert.Equal(entry0 + (uint)i * SIZE, list[i].addr);

                // HeaderBase resolves the SHARED header for EVERY entry — including
                // entry[i>0] where the addr-20 formula would be wrong.
                foreach (var r in list)
                {
                    vm.LoadEntry(r.addr);
                    Assert.Equal(headerBaseOff, vm.HeaderBase);
                    Assert.Equal(headerBaseOff + 16u, vm.ImagePointerAddr);
                    Assert.Equal(headerBaseOff + 4u, vm.PalettePointerAddr);
                    Assert.Equal(r.addr + 8u, vm.TSAPointerAddr);
                }

                // Proof the addr-20 formula would have been wrong for entry[1..]:
                uint entry1 = entry0 + SIZE;
                Assert.NotEqual(headerBaseOff, entry1 - HEADER);
            }
            finally
            {
                CoreState.ROM = prevRom;
                CoreState.BaseDirectory = prevBase;
                CoreState.Language = prevLang;
                try { System.IO.Directory.Delete(tmp, true); } catch { }
            }
        }

        [Fact]
        public void HeaderBase_FallsBackToFormula_ForRawNavigationAddress()
        {
            // No map seeded (raw NavigateTo) -> entry0-style formula.
            uint dataAddr = 0x00500000u;
            var vm = new ImageTSAAnime2ViewModel { CurrentAddr = dataAddr + HEADER };
            Assert.Equal(dataAddr, vm.HeaderBase);
        }

        // ---- FE8U ground-truth (issue #1456 cites 14 entries, ptrs 81C1900..) ----

        [Fact]
        public void FE8U_LoadList_ExposesAllEntries_NotJustOnePerCategory()
        {
            if (TestRomLocator.FindRom("FE8U") == null)
            {
                _output.WriteLine("SKIP: FE8U.gba not found in roms/ or ROMS_DIR");
                return;
            }

            RomTestHelper.WithRom("FE8U", () =>
            {
                var vm = new ImageTSAAnime2ViewModel();
                var list = vm.LoadList();
                Assert.NotEmpty(list);

                // Count distinct category pointers (AddrResult.tag) and total rows.
                var categories = new HashSet<uint>();
                foreach (var r in list) categories.Add(r.tag);
                _output.WriteLine($"FE8U: {list.Count} entries across {categories.Count} categories");

                // The bug surfaced exactly one row per category. With the fix the
                // total must exceed the category count (the active FE8U category
                // alone has 14 entries per the issue).
                Assert.True(list.Count > categories.Count,
                    $"Expected more entries than categories (got {list.Count} entries, {categories.Count} categories)");

                // Geometry: every row in a category is stride-12 from entry0, and
                // HeaderBase resolves the SHARED category header for every entry.
                // Group by category tag.
                var byCat = new Dictionary<uint, List<AddrResult>>();
                foreach (var r in list)
                {
                    if (!byCat.TryGetValue(r.tag, out var l)) { l = new List<AddrResult>(); byCat[r.tag] = l; }
                    l.Add(r);
                }

                bool sawMultiEntryCategory = false;
                foreach (var kv in byCat)
                {
                    var entries = kv.Value;
                    if (entries.Count > 1) sawMultiEntryCategory = true;

                    // entries must be contiguous 12-byte strides
                    for (int i = 1; i < entries.Count; i++)
                        Assert.Equal(entries[0].addr + (uint)i * SIZE, entries[i].addr);

                    // category header base = entry0.addr - 20
                    uint headerBase = entries[0].addr - HEADER;

                    // LoadEntry every entry; HeaderBase must stay the shared base
                    foreach (var e in entries)
                    {
                        vm.LoadEntry(e.addr);
                        Assert.Equal(headerBase, vm.HeaderBase);
                        // image/palette resolve off the SHARED header for every entry
                        Assert.Equal(headerBase + 16u, vm.ImagePointerAddr);
                        Assert.Equal(headerBase + 4u, vm.PalettePointerAddr);
                        // TSA pointer varies per entry (entryAddr + 8)
                        Assert.Equal(e.addr + 8u, vm.TSAPointerAddr);
                    }
                }

                Assert.True(sawMultiEntryCategory,
                    "Expected at least one FE8U category with >1 entry (the bug collapsed these)");
            });
        }
    }
}
