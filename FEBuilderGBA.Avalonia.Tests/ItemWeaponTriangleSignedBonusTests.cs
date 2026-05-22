using System;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// Headless UI tests for the Avalonia Weapon Triangle Editor signed-byte
    /// fix (issue #370 bug 2). These verify:
    ///   * The NumericUpDown boxes for Bonus/Penalty accept negative values
    ///     (Minimum=-128, Maximum=127) rather than the broken Minimum=0
    ///     clamping that caused 0xF1 to display as 241.
    ///   * Loading a ROM entry whose bytes 2/3 are 0xF1/0xFF produces
    ///     Bonus=-15 / Penalty=-1 (sign-extended) in the ViewModel.
    ///   * Writing -15/-1 produces ROM bytes 0xF1/0xFF (round-trip).
    ///   * GetDataReport masks the byte for hex display to avoid 32-bit
    ///     sign-extended values like 0xFFFFFFF1 (Copilot review item 2).
    /// </summary>
    [Collection("SharedState")]
    public class ItemWeaponTriangleSignedBonusTests : IClassFixture<RomFixture>
    {
        private readonly RomFixture _fixture;
        private readonly ITestOutputHelper _output;

        public ItemWeaponTriangleSignedBonusTests(RomFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        // -------------------------------------------------- View-level tests

        [AvaloniaFact]
        public void View_BonusBox_AllowsSignedRange()
        {
            var view = new ItemWeaponTriangleViewerView();
            var bonusBox = view.FindControl<NumericUpDown>("BonusBox");
            Assert.NotNull(bonusBox);
            // Catches regressions where someone re-clamps to 0..255.
            Assert.Equal(-128m, bonusBox!.Minimum);
            Assert.Equal(127m, bonusBox.Maximum);
        }

        [AvaloniaFact]
        public void View_PenaltyBox_AllowsSignedRange()
        {
            var view = new ItemWeaponTriangleViewerView();
            var penaltyBox = view.FindControl<NumericUpDown>("PenaltyBox");
            Assert.NotNull(penaltyBox);
            Assert.Equal(-128m, penaltyBox!.Minimum);
            Assert.Equal(127m, penaltyBox.Maximum);
        }

        [AvaloniaFact]
        public void View_WeaponTypeBoxes_Unsigned_StillAllowFullByte()
        {
            // Weapon-type IDs (bytes 0+1) remain unsigned. Verify the XAML
            // didn't get cargo-culted into signed ranges when only bytes 2/3
            // should be signed.
            var view = new ItemWeaponTriangleViewerView();
            var w1 = view.FindControl<NumericUpDown>("WeaponType1Box");
            var w2 = view.FindControl<NumericUpDown>("WeaponType2Box");
            Assert.NotNull(w1);
            Assert.NotNull(w2);
            Assert.Equal(0m, w1!.Minimum);
            Assert.Equal(255m, w1.Maximum);
            Assert.Equal(0m, w2!.Minimum);
            Assert.Equal(255m, w2.Maximum);
        }

        // -------------------------------------------------- ViewModel tests

        [Fact]
        public void ViewModel_LoadsNegativeBonusFromROM()
        {
            if (!_fixture.IsAvailable)
            {
                _output.WriteLine("SKIP: no ROM available");
                return;
            }

            ROM rom = CoreState.ROM!;
            // Find a free scratch region and plant a known 4-byte struct
            // using the same accessors as production code (rom.u8 /
            // rom.write_u8) so the test exercises the public API.
            uint scratch = FindScratchAddr(rom, 4);
            Assert.NotEqual(0u, scratch);

            byte[] saved = SaveBytes(rom, scratch, 4);
            try
            {
                rom.write_u8(scratch + 0, 0x00);  // weapon1
                rom.write_u8(scratch + 1, 0x01);  // weapon2
                rom.write_u8(scratch + 2, 0xF1);  // bonus = -15 (sbyte)
                rom.write_u8(scratch + 3, 0xFF);  // penalty = -1 (sbyte)

                var vm = new ItemWeaponTriangleViewerViewModel();
                vm.LoadItemWeaponTriangle(scratch);

                Assert.Equal(0u, vm.WeaponType1);
                Assert.Equal(1u, vm.WeaponType2);
                Assert.Equal(-15, vm.Bonus);
                Assert.Equal(-1, vm.Penalty);
            }
            finally
            {
                RestoreBytes(rom, scratch, saved);
            }
        }

        [Fact]
        public void ViewModel_WriteRoundTrip_PreservesNegativeValues()
        {
            if (!_fixture.IsAvailable)
            {
                _output.WriteLine("SKIP: no ROM available");
                return;
            }

            ROM rom = CoreState.ROM!;
            uint scratch = FindScratchAddr(rom, 4);
            Assert.NotEqual(0u, scratch);

            byte[] saved = SaveBytes(rom, scratch, 4);
            try
            {
                var vm = new ItemWeaponTriangleViewerViewModel();
                vm.CurrentAddr = scratch;
                vm.WeaponType1 = 0x00;
                vm.WeaponType2 = 0x01;
                vm.Bonus = -15;
                vm.Penalty = -1;

                vm.WriteItemWeaponTriangle();

                // Verify ROM bytes are exactly 0xF1 / 0xFF (NOT 0x00 / 0x00).
                Assert.Equal(0x00u, rom.u8(scratch + 0));
                Assert.Equal(0x01u, rom.u8(scratch + 1));
                Assert.Equal(0xF1u, rom.u8(scratch + 2));
                Assert.Equal(0xFFu, rom.u8(scratch + 3));

                // Read back to confirm round-trip
                var vm2 = new ItemWeaponTriangleViewerViewModel();
                vm2.LoadItemWeaponTriangle(scratch);
                Assert.Equal(-15, vm2.Bonus);
                Assert.Equal(-1, vm2.Penalty);
            }
            finally
            {
                RestoreBytes(rom, scratch, saved);
            }
        }

        [Fact]
        public void ViewModel_WriteRoundTrip_PreservesPositiveBoundary()
        {
            if (!_fixture.IsAvailable)
            {
                _output.WriteLine("SKIP: no ROM available");
                return;
            }

            ROM rom = CoreState.ROM!;
            uint scratch = FindScratchAddr(rom, 4);
            Assert.NotEqual(0u, scratch);

            byte[] saved = SaveBytes(rom, scratch, 4);
            try
            {
                var vm = new ItemWeaponTriangleViewerViewModel();
                vm.CurrentAddr = scratch;
                vm.WeaponType1 = 0x02;
                vm.WeaponType2 = 0x03;
                vm.Bonus = 127;
                vm.Penalty = -128;
                vm.WriteItemWeaponTriangle();

                Assert.Equal(0x7Fu, rom.u8(scratch + 2));  // 127
                Assert.Equal(0x80u, rom.u8(scratch + 3));  // -128

                var vm2 = new ItemWeaponTriangleViewerViewModel();
                vm2.LoadItemWeaponTriangle(scratch);
                Assert.Equal(127, vm2.Bonus);
                Assert.Equal(-128, vm2.Penalty);
            }
            finally
            {
                RestoreBytes(rom, scratch, saved);
            }
        }

        [Fact]
        public void GetDataReport_MasksByteForHex_NotSignExtended()
        {
            // Test in isolation — no ROM access needed.
            var vm = new ItemWeaponTriangleViewerViewModel();
            vm.CurrentAddr = 0x12345678u;
            vm.WeaponType1 = 0;
            vm.WeaponType2 = 1;
            vm.Bonus = -15;
            vm.Penalty = -1;

            var report = vm.GetDataReport();

            // Decimal must show -15 / -1
            Assert.Contains("-15", report["Bonus"]);
            Assert.Contains("-1", report["Penalty"]);

            // Hex must be byte-masked (0xF1 / 0xFF), NOT 32-bit (0xFFFFFFF1)
            Assert.Contains("0xF1", report["Bonus"]);
            Assert.Contains("0xFF", report["Penalty"]);
            Assert.DoesNotContain("FFFFFF", report["Bonus"]);
            Assert.DoesNotContain("FFFFFF", report["Penalty"]);
        }

        [Fact]
        public void GetDataReport_PositiveValuesStillCorrect()
        {
            var vm = new ItemWeaponTriangleViewerViewModel();
            vm.CurrentAddr = 0x12345678u;
            vm.Bonus = 15;
            vm.Penalty = 0;

            var report = vm.GetDataReport();
            Assert.Contains("15", report["Bonus"]);
            Assert.Contains("0x0F", report["Bonus"]);
            Assert.Contains("0", report["Penalty"]);
            Assert.Contains("0x00", report["Penalty"]);
        }

        [Fact]
        public void GetFieldOffsetMap_ReportsBonusAndPenaltyAsSignedByte()
        {
            // Regression: the field offset map should advertise signed-byte
            // semantics (s8) for the bonus/penalty fields, not unsigned (u8).
            var vm = new ItemWeaponTriangleViewerViewModel();
            var map = vm.GetFieldOffsetMap();
            Assert.Equal("u8@0x00", map["WeaponType1"]);
            Assert.Equal("u8@0x01", map["WeaponType2"]);
            Assert.Equal("s8@0x02", map["Bonus"]);
            Assert.Equal("s8@0x03", map["Penalty"]);
        }

        // -------------------------------------------------- Helpers

        /// <summary>
        /// Find a writable scratch region of the requested size in the ROM
        /// free-space area (consecutive 0xFF bytes). Tests restore the bytes
        /// they overwrite to leave the fixture clean for subsequent tests.
        /// Uses <c>rom.u8</c> for reads (same accessor as production code).
        /// </summary>
        static uint FindScratchAddr(ROM rom, int size)
        {
            // Search the back ~64KB which usually contains freespace padding.
            int dataLen = rom.Data.Length;
            int startInt = Math.Max(0, dataLen - 0x10000);
            for (int aInt = startInt; aInt < dataLen - size; aInt += size)
            {
                uint a = (uint)aInt;
                bool allFF = true;
                for (int i = 0; i < size + 4; i++)
                {
                    if (aInt + i >= dataLen) { allFF = false; break; }
                    if (rom.u8(a + (uint)i) != 0xFF) { allFF = false; break; }
                }
                if (allFF) return a;
            }
            return 0;
        }

        /// <summary>
        /// Capture <paramref name="size"/> bytes starting at <paramref name="addr"/>
        /// so they can be restored after a destructive test. Uses
        /// <c>rom.u8</c> for reads.
        /// </summary>
        static byte[] SaveBytes(ROM rom, uint addr, int size)
        {
            byte[] buf = new byte[size];
            for (int i = 0; i < size; i++) buf[i] = (byte)rom.u8(addr + (uint)i);
            return buf;
        }

        /// <summary>
        /// Restore bytes previously captured with <see cref="SaveBytes"/> using
        /// <c>rom.write_u8</c> (same accessor as production code).
        /// </summary>
        static void RestoreBytes(ROM rom, uint addr, byte[] saved)
        {
            for (int i = 0; i < saved.Length; i++) rom.write_u8(addr + (uint)i, saved[i]);
        }
    }
}
