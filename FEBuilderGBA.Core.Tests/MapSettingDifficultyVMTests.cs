using Xunit;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Cross-platform VM behavior tests for the batch and dialog Difficulty
    /// Settings editors (#659). Uses synthetic ROMs with valid Nintendo
    /// product codes so the right <c>ROMFEINFO</c> subclass is picked.
    /// </summary>
    [Collection("SharedState")]
    public class MapSettingDifficultyVMTests
    {
        // ---- Batch editor (MapSettingDifficultyViewModel) -----------------

        [Theory]
        [InlineData("AE7J01")]
        [InlineData("AE7E01")]
        [InlineData("BE8E01")]
        public void Batch_LoadEntry_ReadsAndDecodesW20(string productCode)
        {
            var origRom = CoreState.ROM;
            try
            {
                var rom = new ROM();
                rom.LoadLow("test.gba", new byte[0x1000000], productCode);
                CoreState.ROM = rom;

                // Map setting struct at addr 0x100: write packed 0x0123 at offset 20.
                uint addr = 0x100;
                rom.write_u16(addr + 20, 0x0123);

                var vm = new MapSettingDifficultyViewModel();
                vm.LoadEntry(addr);

                Assert.True(vm.IsSupported);
                Assert.True(vm.IsLoaded);
                Assert.Equal(addr, vm.CurrentAddr);
                Assert.Equal(2, vm.HardBoost);
                Assert.Equal(1, vm.NormalPenalty);
                Assert.Equal(3, vm.EasyPenalty);
                Assert.Equal((ushort)0x0123, vm.DifficultyValue);
            }
            finally { CoreState.ROM = origRom; }
        }

        [Theory]
        [InlineData("AE7J01")]
        [InlineData("AE7E01")]
        [InlineData("BE8E01")]
        public void Batch_Write_EncodesNibblesIntoROMAtOffset20(string productCode)
        {
            var origRom = CoreState.ROM;
            try
            {
                var rom = new ROM();
                rom.LoadLow("test.gba", new byte[0x1000000], productCode);
                CoreState.ROM = rom;

                uint addr = 0x200;
                var vm = new MapSettingDifficultyViewModel();
                vm.LoadEntry(addr);

                vm.HardBoost = 4;
                vm.NormalPenalty = 5;
                vm.EasyPenalty = 6;

                bool ok = vm.Write();
                Assert.True(ok);

                // Expected packed: Hard<<4|Normal<<8|Easy = 0x0540 + 0x06 = 0x0546
                ushort expected = DifficultyValueCore.Pack(4, 5, 6);
                Assert.Equal(expected, (ushort)rom.u16(addr + 20));
            }
            finally { CoreState.ROM = origRom; }
        }

        [Theory]
        [InlineData("AE7J01")]
        [InlineData("AE7E01")]
        [InlineData("BE8E01")]
        public void Batch_Write_PreservesHighNibbleReservedBits(string productCode)
        {
            var origRom = CoreState.ROM;
            try
            {
                var rom = new ROM();
                rom.LoadLow("test.gba", new byte[0x1000000], productCode);
                CoreState.ROM = rom;

                uint addr = 0x300;
                // Original ROM value has reserved bits in high nibble.
                rom.write_u16(addr + 20, 0xA123);

                var vm = new MapSettingDifficultyViewModel();
                vm.LoadEntry(addr);

                vm.HardBoost = 2;
                vm.NormalPenalty = 1;
                vm.EasyPenalty = 3;
                Assert.True(vm.Write());

                // High nibble 0xA should be preserved; low 12 bits = 0x123.
                ushort written = (ushort)rom.u16(addr + 20);
                Assert.Equal((ushort)0xA123, written);
            }
            finally { CoreState.ROM = origRom; }
        }

        [Theory]
        [InlineData("AE7J01")]
        [InlineData("AE7E01")]
        public void IsSupported_TrueForFE7Family(string productCode)
        {
            var origRom = CoreState.ROM;
            try
            {
                var rom = new ROM();
                rom.LoadLow("test.gba", new byte[0x1000000], productCode);
                CoreState.ROM = rom;

                Assert.True(DifficultyValueCore.IsSupported(rom));
            }
            finally { CoreState.ROM = origRom; }
        }

        [Fact]
        public void Batch_Write_NoOpOnFE6()
        {
            var origRom = CoreState.ROM;
            try
            {
                var rom = new ROM();
                rom.LoadLow("test.gba", new byte[0x1000000], "AFEJ01");
                CoreState.ROM = rom;
                Assert.Equal(6, rom.RomInfo.version);

                uint addr = 0x400;
                // Seed bytes at addr+20/21 with a sentinel value. On FE6 these
                // bytes are NOT a difficulty word, so the editor must not touch them.
                rom.write_u8(addr + 20, 0xCD);
                rom.write_u8(addr + 21, 0xAB);

                var vm = new MapSettingDifficultyViewModel();
                vm.LoadEntry(addr);

                Assert.False(vm.IsSupported);

                // Try to flip nibbles and write — must NOT modify ROM.
                vm.HardBoost = 0xF;
                vm.NormalPenalty = 0xF;
                vm.EasyPenalty = 0xF;
                bool ok = vm.Write();

                Assert.False(ok);
                Assert.Equal(0xCDu, rom.u8(addr + 20));
                Assert.Equal(0xABu, rom.u8(addr + 21));
            }
            finally { CoreState.ROM = origRom; }
        }

        [Fact]
        public void Batch_LoadList_EmptyOnFE6()
        {
            var origRom = CoreState.ROM;
            try
            {
                var rom = new ROM();
                rom.LoadLow("test.gba", new byte[0x1000000], "AFEJ01");
                CoreState.ROM = rom;

                var vm = new MapSettingDifficultyViewModel();
                var list = vm.LoadList();
                Assert.NotNull(list);
                Assert.Empty(list);
            }
            finally { CoreState.ROM = origRom; }
        }

        [Fact]
        public void Batch_LoadList_EmptyWhenNoRom()
        {
            var origRom = CoreState.ROM;
            try
            {
                CoreState.ROM = null;
                var vm = new MapSettingDifficultyViewModel();
                var list = vm.LoadList();
                Assert.NotNull(list);
                Assert.Empty(list);
            }
            finally { CoreState.ROM = origRom; }
        }

        [Fact]
        public void Batch_LoadEntry_ClearsStateForUnsupportedROM()
        {
            var origRom = CoreState.ROM;
            try
            {
                var rom = new ROM();
                rom.LoadLow("test.gba", new byte[0x1000000], "AFEJ01");
                CoreState.ROM = rom;

                var vm = new MapSettingDifficultyViewModel();
                vm.LoadEntry(0x500);

                Assert.False(vm.IsSupported);
                Assert.False(vm.IsLoaded);
                Assert.Equal(0, vm.HardBoost);
                Assert.Equal(0, vm.NormalPenalty);
                Assert.Equal(0, vm.EasyPenalty);
            }
            finally { CoreState.ROM = origRom; }
        }

        [Theory]
        [InlineData("AE7J01")]
        [InlineData("AE7E01")]
        [InlineData("BE8E01")]
        public void Batch_RoundTrip_PreservesValueAcrossWriteAndReload(string productCode)
        {
            var origRom = CoreState.ROM;
            try
            {
                var rom = new ROM();
                rom.LoadLow("test.gba", new byte[0x1000000], productCode);
                CoreState.ROM = rom;

                uint addr = 0x600;
                var vm = new MapSettingDifficultyViewModel();
                vm.LoadEntry(addr);

                vm.HardBoost = 7;
                vm.NormalPenalty = 3;
                vm.EasyPenalty = 5;
                Assert.True(vm.Write());

                // New VM, reload from same address — should get the same nibbles back.
                var vm2 = new MapSettingDifficultyViewModel();
                vm2.LoadEntry(addr);
                Assert.Equal(7, vm2.HardBoost);
                Assert.Equal(3, vm2.NormalPenalty);
                Assert.Equal(5, vm2.EasyPenalty);
            }
            finally { CoreState.ROM = origRom; }
        }

        // ---- Dialog VM regression (existing nibble swap bug fix) -----------

        [Fact]
        public void Dialog_LoadFromValue_NibbleMappingMatchesWinForms()
        {
            // Regression: prior to using DifficultyValueCore the dialog VM
            // swapped HardBoost and NormalPenalty. WinForms encoding:
            // 0x0123 should decode to HardBoost=2, NormalPenalty=1, EasyPenalty=3.
            var dialog = new MapSettingDifficultyDialogViewModel();
            dialog.LoadFromValue(0x0123);
            Assert.Equal(2, dialog.HardBoost);
            Assert.Equal(1, dialog.NormalPenalty);
            Assert.Equal(3, dialog.EasyPenalty);
        }

        [Fact]
        public void Dialog_SettingNibbles_RepacksCorrectly()
        {
            var dialog = new MapSettingDifficultyDialogViewModel();
            dialog.HardBoost = 2;
            dialog.NormalPenalty = 1;
            dialog.EasyPenalty = 3;
            // Expected packed = 0x0123 (Hard<<4 | Normal<<8 | Easy)
            Assert.Equal(0x0123u, dialog.DifficultyValue);
        }

        [Fact]
        public void Dialog_LoadFromValue_PreservesReservedHighNibble()
        {
            var dialog = new MapSettingDifficultyDialogViewModel();
            dialog.LoadFromValue(0xA123);
            Assert.Equal(2, dialog.HardBoost);
            Assert.Equal(1, dialog.NormalPenalty);
            Assert.Equal(3, dialog.EasyPenalty);

            // Reassigning a nibble must keep the reserved 0xA high nibble.
            dialog.HardBoost = 4;
            Assert.Equal(0xA143u, dialog.DifficultyValue);
        }
    }
}
