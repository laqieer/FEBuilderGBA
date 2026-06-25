// SPDX-License-Identifier: GPL-3.0-or-later
// MapPointerViewModel PLIST Split/Expand wiring (#1432).
//
// The Avalonia Map Pointer editor was missing the WinForms "PLIST分割"
// (PLIST Split) operation entirely. These tests prove the new VM wiring:
//   * CanSplit reflects MapPlistSplitCore.CanSplit on the active ROM;
//   * SplitPlist() runs the atomic Core split and returns null on success;
//   * after a successful split the editor no longer offers Split (CanSplit
//     flips false) and IsPlistSplit is true;
//   * a second SplitPlist() is rejected (already split) without mutating.
//
// The split MUTATES the ROM, so each test snapshots CoreState.ROM.Data and
// restores it byte-for-byte in a finally block to keep the shared fixture
// clean for other tests in the collection.
using System;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    public class MapPointerSplitTests : IClassFixture<RomFixture>
    {
        readonly RomFixture _rom;

        public MapPointerSplitTests(RomFixture rom)
        {
            _rom = rom;
        }

        [Fact]
        public void CanSplit_VanillaFixtureRom_MatchesCore()
        {
            if (!_rom.IsAvailable) return;

            var vm = new MapPointerViewModel();
            // The VM's CanSplit must agree with the Core helper on the active ROM.
            Assert.Equal(MapPlistSplitCore.CanSplit(CoreState.ROM), vm.CanSplit);
        }

        [Fact]
        public void SplitPlist_OnVanillaRom_Succeeds_AndFlipsCanSplit()
        {
            if (!_rom.IsAvailable) return;

            ROM rom = CoreState.ROM!;
            // Only meaningful on a NON-split ROM. A vanilla test ROM is not
            // split; skip gracefully if a future fixture is already split.
            if (!MapPlistSplitCore.CanSplit(rom)) return;

            byte[] snapshot = (byte[])rom.Data.Clone();
            try
            {
                var vm = new MapPointerViewModel();
                Assert.True(vm.CanSplit);

                string? error = vm.SplitPlist();

                Assert.Null(error);                                  // success
                Assert.True(MapChangeCore.IsPlistSplit(rom));        // tables now split
                Assert.False(vm.CanSplit);                           // no longer offered

                // The shared OBJ/PAL and ANIME1/ANIME2 pairs stay shared.
                ROMFEINFO info = rom.RomInfo;
                Assert.Equal(rom.p32(info.map_obj_pointer), rom.p32(info.map_pal_pointer));
                Assert.Equal(rom.p32(info.map_tileanime1_pointer), rom.p32(info.map_tileanime2_pointer));
            }
            finally
            {
                // Restore the shared fixture byte-for-byte (incl. any ROM growth).
                if (rom.Data.Length != snapshot.Length)
                    rom.write_resize_data((uint)snapshot.Length);
                Array.Copy(snapshot, rom.Data, snapshot.Length);
            }
        }

        [Fact]
        public void SplitPlist_AlreadySplit_IsRejected_WithoutMutation()
        {
            if (!_rom.IsAvailable) return;

            ROM rom = CoreState.ROM!;
            if (!MapPlistSplitCore.CanSplit(rom)) return;

            byte[] snapshot = (byte[])rom.Data.Clone();
            try
            {
                var vm = new MapPointerViewModel();
                Assert.Null(vm.SplitPlist()); // first split succeeds

                byte[] afterFirst = (byte[])rom.Data.Clone();
                // A second split must be rejected (already split) and mutate nothing.
                string? error = vm.SplitPlist();
                Assert.NotNull(error);
                Assert.Equal(afterFirst, rom.Data);
            }
            finally
            {
                if (rom.Data.Length != snapshot.Length)
                    rom.write_resize_data((uint)snapshot.Length);
                Array.Copy(snapshot, rom.Data, snapshot.Length);
            }
        }
    }
}
