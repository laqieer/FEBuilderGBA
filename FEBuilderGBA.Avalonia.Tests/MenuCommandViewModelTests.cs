// SPDX-License-Identifier: GPL-3.0-or-later
// #1404 — the Avalonia Menu Command editor must list ONLY 36-byte MenuCommand
// records. The two usability FUNCTION addresses (MenuCommand_UsabilityAlways /
// MenuCommand_UsabilityNever) are ARM Thumb ROM code, NOT records, and used to
// be injected as list rows 0/1; selecting one read 36 bytes of code as a struct
// and Write would overwrite ROM code. These tests lock in:
//   1. the list contains no usability code addresses (only real 36-byte records),
//   2. LoadMenuCommand refuses a code address and clears any stale writable state,
//   3. WriteMenuCommand refuses a code address (returns false, no ROM mutation),
//   4. the VM list and the golden ListParityHelper builder stay in lockstep.
using System;
using System.Linq;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Core;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// [Collection("SharedState")] because every test mutates the ambient
    /// CoreState.ROM. Each synthetic-ROM test restores CoreState.ROM in a
    /// finally block so it cannot leak into sibling tests.
    /// </summary>
    [Collection("SharedState")]
    public class MenuCommandViewModelTests : IClassFixture<RomFixture>
    {
        readonly RomFixture _rom;

        public MenuCommandViewModelTests(RomFixture rom)
        {
            _rom = rom;
        }

        // ----------------------------------------------------------------
        // Synthetic FE8U ROM with a planted menu-definition chain so the
        // VM list builds with REAL 36-byte records. No commercial ROM needed.
        // ----------------------------------------------------------------

        const uint DefBase = 0x500000;       // menu-definition table base
        const uint MenuCmdBase = 0x600000;   // menu command record table base
        const uint DefCandidate = 0x1C02C;   // 2nd FindROMPointer candidate for FE8U

        static void WritePtr(byte[] data, uint at, uint offset)
        {
            uint ptr = offset + 0x08000000;
            int i = (int)at;
            data[i + 0] = (byte)(ptr & 0xFF);
            data[i + 1] = (byte)((ptr >> 8) & 0xFF);
            data[i + 2] = (byte)((ptr >> 16) & 0xFF);
            data[i + 3] = (byte)((ptr >> 24) & 0xFF);
        }

        /// <summary>
        /// Build a 16 MB FE8U ROM with a valid one-entry menu-definition table
        /// and exactly one 36-byte MenuCommand record. menu_definiton_pointer
        /// resolves to <see cref="DefCandidate"/> via U.FindROMPointer.
        /// </summary>
        static ROM MakeMenuRom()
        {
            byte[] data = new byte[0x1000000]; // 16 MB
            byte[] versionBytes = System.Text.Encoding.ASCII.GetBytes("BE8E01");
            Array.Copy(versionBytes, 0, data, 0xAC, versionBytes.Length);

            // FindROMPointer(rom, 8, {submenu_pointer, 0x1C02C, ...}) chooses the
            // first candidate whose u32 is a safety pointer AND whose target+8 is a
            // safety pointer. submenu_pointer is computed dynamically (invalid in a
            // synthetic ROM), so plant the chain at 0x1C02C.
            WritePtr(data, DefCandidate, DefBase);     // candidate -> defBase

            // Menu definition entry 0 (36 bytes). Only +8 (the menu command handler
            // pointer) is read by the list builder. Entry 1's +8 is left 0 so the
            // def loop stops after one entry.
            WritePtr(data, DefBase + 8, MenuCmdBase);  // def[0].+8 -> menuCmdBase

            // One valid 36-byte MenuCommand record at MenuCmdBase. The existence
            // check is "u32(+0xC) is a pointer". Record 1's +0xC is left 0 so the
            // command loop stops after one record.
            WritePtr(data, MenuCmdBase + 0xC, 0x100);  // record[0].+0xC = any pointer

            var rom = new ROM();
            rom.LoadLow("test.gba", data, "BE8E01");
            return rom;
        }

        /// <summary>Set CoreState.ROM to a synthetic ROM, run body, restore.</summary>
        static void WithRom(ROM rom, Action body)
        {
            ROM prev = CoreState.ROM;
            try
            {
                CoreState.ROM = rom;
                body();
            }
            finally
            {
                CoreState.ROM = prev;
            }
        }

        [Fact]
        public void SyntheticRom_ResolvesMenuDefinitionPointer()
        {
            var rom = MakeMenuRom();
            // Sanity: the planted chain must make the list non-empty, otherwise the
            // "no code address" assertions below would be vacuously true.
            WithRom(rom, () =>
            {
                Assert.NotEqual(0u, rom.RomInfo.menu_definiton_pointer);
                var vm = new MenuCommandViewModel();
                var list = vm.LoadMenuCommandList();
                Assert.NotEmpty(list);
            });
        }

        [Fact]
        public void LoadMenuCommandList_ContainsNoUsabilityCodeAddresses()
        {
            var rom = MakeMenuRom();
            WithRom(rom, () =>
            {
                uint always = rom.RomInfo.MenuCommand_UsabilityAlways;
                uint never = rom.RomInfo.MenuCommand_UsabilityNever;
                Assert.NotEqual(0u, always);
                Assert.NotEqual(0u, never);

                var vm = new MenuCommandViewModel();
                var list = vm.LoadMenuCommandList();

                foreach (var row in list)
                {
                    Assert.NotEqual(always, row.addr);
                    Assert.NotEqual(never, row.addr);
                    // Every listed row must be a real 36-byte record: in-bounds and
                    // a pointer at +0xC (the WinForms/VM existence check).
                    Assert.True(row.addr + 36 <= (uint)rom.Data.Length);
                    Assert.True(U.isPointer(rom.u32(row.addr + 0xC)));
                }
            });
        }

        [Fact]
        public void LoadMenuCommand_RefusesUsabilityAddress_LeavesCanWriteFalse()
        {
            var rom = MakeMenuRom();
            WithRom(rom, () =>
            {
                var vm = new MenuCommandViewModel();
                vm.LoadMenuCommand(rom.RomInfo.MenuCommand_UsabilityAlways);
                Assert.False(vm.CanWrite);
                Assert.Equal(0u, vm.CurrentAddr);

                vm.LoadMenuCommand(rom.RomInfo.MenuCommand_UsabilityNever);
                Assert.False(vm.CanWrite);
                Assert.Equal(0u, vm.CurrentAddr);
            });
        }

        [Fact]
        public void LoadMenuCommand_ValidThenUsabilityAddress_ClearsStaleWritableState()
        {
            var rom = MakeMenuRom();
            WithRom(rom, () =>
            {
                var vm = new MenuCommandViewModel();

                // Load a real record first → CanWrite becomes true.
                vm.LoadMenuCommand(MenuCmdBase);
                Assert.True(vm.CanWrite);
                Assert.Equal(MenuCmdBase, vm.CurrentAddr);

                // Now a stale/alternate path selects a usability code address. The
                // previous record must NOT remain writable (#1404 review gap 1).
                vm.LoadMenuCommand(rom.RomInfo.MenuCommand_UsabilityAlways);
                Assert.False(vm.CanWrite);
                Assert.Equal(0u, vm.CurrentAddr);
            });
        }

        [Fact]
        public void WriteMenuCommand_RefusesUsabilityAddress_ReturnsFalse_NoMutation()
        {
            var rom = MakeMenuRom();
            WithRom(rom, () =>
            {
                uint always = rom.RomInfo.MenuCommand_UsabilityAlways;

                var vm = new MenuCommandViewModel();
                // Force the current address to the code address (simulating any path
                // that could land Write on it) and snapshot the 36 code bytes.
                vm.CurrentAddr = always;
                byte[] before = rom.getBinaryData(always, 36);

                bool wrote = vm.WriteMenuCommand();

                Assert.False(wrote);
                byte[] after = rom.getBinaryData(always, 36);
                Assert.Equal(before, after);
            });
        }

        [Fact]
        public void WriteMenuCommand_ValidRecord_ReturnsTrue()
        {
            var rom = MakeMenuRom();
            WithRom(rom, () =>
            {
                var vm = new MenuCommandViewModel();
                vm.LoadMenuCommand(MenuCmdBase);
                Assert.True(vm.CanWrite);

                using (FE8UMagicSplitTestRom.BeginUndoScope(rom))
                {
                    bool wrote = vm.WriteMenuCommand();
                    Assert.True(wrote);
                }
            });
        }

        [Fact]
        public void IsUsabilityFunctionAddress_MatchesAlwaysAndNever_RejectsOthers()
        {
            var rom = MakeMenuRom();
            uint always = rom.RomInfo.MenuCommand_UsabilityAlways;
            uint never = rom.RomInfo.MenuCommand_UsabilityNever;

            Assert.True(MenuCommandViewModel.IsUsabilityFunctionAddress(rom, always));
            Assert.True(MenuCommandViewModel.IsUsabilityFunctionAddress(rom, never));
            Assert.False(MenuCommandViewModel.IsUsabilityFunctionAddress(rom, MenuCmdBase));
            Assert.False(MenuCommandViewModel.IsUsabilityFunctionAddress(rom, 0));
            Assert.False(MenuCommandViewModel.IsUsabilityFunctionAddress(null, always));
        }

        // ----------------------------------------------------------------
        // Lockstep: VM list == golden ListParityHelper builder, and neither
        // contains the usability code addresses. (Synthetic ROM = always runs.)
        // ----------------------------------------------------------------

        [Fact]
        public void Golden_And_ViewModel_MatchAndContainNoCodeAddresses()
        {
            var rom = MakeMenuRom();
            WithRom(rom, () =>
            {
                uint always = rom.RomInfo.MenuCommand_UsabilityAlways;
                uint never = rom.RomInfo.MenuCommand_UsabilityNever;

                var vm = new MenuCommandViewModel();
                var vmList = vm.LoadMenuCommandList();
                var refList = ListParityHelper.BuildReferenceList("MenuCommandView");

                Assert.NotNull(refList);
                Assert.Equal(vmList.Count, refList.Count);
                for (int i = 0; i < vmList.Count; i++)
                {
                    Assert.Equal(vmList[i].addr, refList[i].addr);
                    Assert.Equal(vmList[i].name, refList[i].name);
                }

                Assert.DoesNotContain(refList, r => r.addr == always);
                Assert.DoesNotContain(refList, r => r.addr == never);
                Assert.DoesNotContain(vmList, r => r.addr == always);
                Assert.DoesNotContain(vmList, r => r.addr == never);
            });
        }

        // ----------------------------------------------------------------
        // ROM-backed lockstep (skips when no commercial ROM is present), in
        // the same style as ListParityHelperTests.
        // ----------------------------------------------------------------

        [Fact]
        public void BuildReferenceList_MenuCommand_MatchesViewModel_RealRom()
        {
            if (!_rom.IsAvailable) return; // skip if no ROM

            var vm = new MenuCommandViewModel();
            var vmList = vm.LoadMenuCommandList();
            var refList = ListParityHelper.BuildReferenceList("MenuCommandView");

            Assert.NotNull(refList);
            Assert.Equal(vmList.Count, refList.Count);

            uint always = _rom.ROM!.RomInfo.MenuCommand_UsabilityAlways;
            uint never = _rom.ROM!.RomInfo.MenuCommand_UsabilityNever;
            for (int i = 0; i < vmList.Count; i++)
            {
                Assert.Equal(vmList[i].addr, refList[i].addr);
                Assert.Equal(vmList[i].name, refList[i].name);
                Assert.NotEqual(always, vmList[i].addr);
                Assert.NotEqual(never, vmList[i].addr);
            }
        }
    }
}
