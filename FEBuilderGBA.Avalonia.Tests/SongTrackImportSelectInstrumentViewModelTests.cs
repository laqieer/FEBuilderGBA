// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for the Avalonia SongTrack instrument-set (voicegroup) browser VM,
// which populates its list from InstrumentSetCore.SearchInstrumentSet. (#787)
using System;
using System.IO;
using System.Linq;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// Verifies SongTrackImportSelectInstrumentViewModel.LoadList() drives the
    /// browser from InstrumentSetCore and auto-selects the first non-"Current"
    /// entry (mirroring WinForms PickupInstrument()). Uses a synthetic FE8U ROM
    /// with the NIMAP2 / NIMAP / AllInstrument signatures injected above the
    /// compressed-image borderline so discovery actually returns instrument
    /// sets (a vanilla ROM has none — see InstrumentSetCoreTests).
    ///
    /// [Collection("SharedState")] because it mutates CoreState.ROM /
    /// CoreState.BaseDirectory.
    /// </summary>
    [Collection("SharedState")]
    public class SongTrackImportSelectInstrumentViewModelTests
    {
        static byte[] Sig(string hex) => hex.Split(' ').Select(x => (byte)U.atoh(x)).ToArray();

        static readonly byte[] FE8U_NIMAP2 = Sig("00 3C 00 00 B8 2A 51 08 FF FA 00 CC 00 3C 00 00 68 80 2A 08 FF FA 00 CC 00 3C 00 00 8C 91 29 08 FF F9 00 A5 01 3C 00 00 02 00 00 00 00 00 0F 00 00 3C 00 00 F4 B7 2B 08 FF FD 00 CC 01 3C 00 00 02 00 00 00 00 00 0F 00 00 3C 00 00 24 F5 28 08 FF F9 00 A5 00 3C 00 00 24 F5 28 08 FF F5 96 96");
        static readonly byte[] ALL_INSTRUMENT = Sig("00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 01 01 01 02 02 03 03 04 05 05 05 05 06 06 06 07 07 07 07 08 08 09 09 09 0A 0A 0A 0A 0B 0C 0C 0C 0D 0D 0D 0D 0E 0E 0E 0F 0F 0F 0F 10 10 10 11 11 11 11 12 12 12 13 13 13 13 14 14 15 15 15 16 16 16 16 17 17 17 17 17 17 17 17 17 17 17 17 17 17 17 17 17 17 17 17 17 17 17");

        static string FindRepoRoot()
        {
            string dir = AppContext.BaseDirectory;
            while (dir != null && !Directory.Exists(Path.Combine(dir, "config", "data")))
                dir = Path.GetDirectoryName(dir);
            return dir;
        }

        static ROM MakeFE8URomWithInstrumentSets(out uint nimap2Ptr, out uint allInstrumentPtr)
        {
            var rom = new ROM();
            rom.LoadLow("synth.gba", new byte[0x1000000], "BE8E01"); // FE8U (needs 16MB for RomInfo)
            uint borderline = rom.RomInfo.compress_image_borderline_address; // 0xDB000

            uint nimap2Off = 0x00200000;
            uint allOff = 0x00300000;
            uint allPtrOff = 0x00380000;
            Assert.True(nimap2Off >= borderline);

            for (uint i = 0; i < FE8U_NIMAP2.Length; i++) rom.write_u8(nimap2Off + i, FE8U_NIMAP2[i]);
            for (uint i = 0; i < ALL_INSTRUMENT.Length; i++) rom.write_u8(allOff + i, ALL_INSTRUMENT[i]);
            rom.write_u32(allPtrOff, U.toPointer(allOff));

            nimap2Ptr = U.toPointer(nimap2Off);
            allInstrumentPtr = U.toPointer(allPtrOff - 8);
            return rom;
        }

        [Fact]
        public void LoadList_NoRom_ReturnsEmpty_DefaultIndexZero()
        {
            var savedRom = CoreState.ROM;
            try
            {
                CoreState.ROM = null;
                var vm = new SongTrackImportSelectInstrumentViewModel();
                var items = vm.LoadList();
                Assert.Empty(items);
                Assert.Equal(0, vm.DefaultSelectionIndex);
            }
            finally
            {
                CoreState.ROM = savedRom;
            }
        }

        [Fact]
        public void LoadList_PopulatesFromInstrumentSetCore_AndAutoSelectsFirstNonCurrent()
        {
            string root = FindRepoRoot();
            if (root == null) return; // no config/data — cannot resolve signature list

            var savedRom = CoreState.ROM;
            var savedBase = CoreState.BaseDirectory;
            try
            {
                CoreState.BaseDirectory = root;
                var rom = MakeFE8URomWithInstrumentSets(out uint nimap2Ptr, out uint allInstrumentPtr);
                CoreState.ROM = rom;
                InstrumentSetCore.ClearCache();

                var vm = new SongTrackImportSelectInstrumentViewModel { CurrentAddr = 0 };
                var items = vm.LoadList();

                // Row 0 is the "Current" seed; the rest are discovered sets.
                Assert.True(items.Count >= 2, "should discover at least one instrument set besides the seed");
                Assert.EndsWith("=Current", items[0].name);

                // Same set InstrumentSetCore returns (single source of truth).
                Assert.Contains(items, e => e.name.EndsWith("=NatveInstrumentMap2(NIMAP2)") && e.addr == nimap2Ptr);
                Assert.Contains(items, e => e.name.EndsWith("=AllInstrument") && e.addr == allInstrumentPtr);

                // WF PickupInstrument auto-selects index 1 (first non-Current).
                Assert.Equal(1, vm.DefaultSelectionIndex);
            }
            finally
            {
                CoreState.ROM = savedRom;
                CoreState.BaseDirectory = savedBase;
                InstrumentSetCore.ClearCache();
            }
        }

        [Fact]
        public void LoadEntry_SetsAddressAndInfoText()
        {
            var savedRom = CoreState.ROM;
            try
            {
                var rom = new ROM();
                rom.LoadLow("synth.gba", new byte[0x1000000], "BE8E01"); // FE8U (needs 16MB for RomInfo)
                CoreState.ROM = rom;

                var vm = new SongTrackImportSelectInstrumentViewModel();
                vm.LoadEntry(0x08200000);

                Assert.True(vm.IsLoaded);
                Assert.Equal(0x08200000u, vm.CurrentAddr);
                Assert.Contains("08200000", vm.InstrumentInfoText);
            }
            finally
            {
                CoreState.ROM = savedRom;
            }
        }
    }
}
