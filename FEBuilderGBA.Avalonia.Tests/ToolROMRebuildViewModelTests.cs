// SPDX-License-Identifier: GPL-3.0-or-later
// #1171 — ROM Rebuild analysis tool (Avalonia port of WinForms ToolROMRebuildForm).
// Tests the VM's address validation + validate-then-make report over Core RebuildCore.
using System;
using System.IO;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class ToolROMRebuildViewModelTests
    {
        [Fact]
        public void ValidateRebuildAddress_NotAligned_ReturnsNotAligned()
        {
            var prevRom = CoreState.ROM;
            try
            {
                CoreState.ROM = null;   // no RomInfo -> only alignment/safety apply
                var vm = new ToolROMRebuildViewModel();
                Assert.Equal(ToolROMRebuildViewModel.AddressCheck.NotAligned, vm.ValidateRebuildAddress(0x09000001));
            }
            finally { CoreState.ROM = prevRom; }
        }

        [Fact]
        public void ValidateRebuildAddress_NullRom_ReturnsUnsafe()
        {
            var prevRom = CoreState.ROM;
            try
            {
                CoreState.ROM = null;   // no loaded ROM -> cannot judge safety
                var vm = new ToolROMRebuildViewModel();
                Assert.Equal(ToolROMRebuildViewModel.AddressCheck.Unsafe, vm.ValidateRebuildAddress(0x00000400));
            }
            finally { CoreState.ROM = prevRom; }
        }

        [Fact]
        public void ValidateRebuildAddress_Aligned_ReturnsOk_WhenNoRomInfo()
        {
            var prevRom = CoreState.ROM;
            try
            {
                // In-memory ROM (no RomInfo). isSafetyOffset judges against ITS length, so
                // pick an aligned offset that is >= 0x200 and < the ROM size.
                var rom = new ROM();
                rom.SwapNewROMDataDirect(new byte[0x4000]);
                CoreState.ROM = rom;
                var vm = new ToolROMRebuildViewModel();
                Assert.Equal(ToolROMRebuildViewModel.AddressCheck.Ok, vm.ValidateRebuildAddress(0x00001000));
            }
            finally { CoreState.ROM = prevRom; }
        }

        [Fact]
        public void MakeRebuild_NoRom_ReturnsNoRom()
        {
            var prevRom = CoreState.ROM;
            try
            {
                CoreState.ROM = null;
                var vm = new ToolROMRebuildViewModel();
                Assert.Equal(ToolROMRebuildViewModel.MakeResult.NoRom, vm.MakeRebuild("x", 0x09000000, "y"));
            }
            finally { CoreState.ROM = prevRom; }
        }

        [Fact]
        public void MakeRebuild_MissingOriginal_ReturnsOriginalMissing()
        {
            var prevRom = CoreState.ROM;
            try
            {
                var rom = new ROM();
                rom.SwapNewROMDataDirect(new byte[0x100]);
                CoreState.ROM = rom;
                var vm = new ToolROMRebuildViewModel();
                string missing = Path.Combine(Path.GetTempPath(), "feb_rb_missing_" + Guid.NewGuid().ToString("N") + ".gba");
                Assert.Equal(ToolROMRebuildViewModel.MakeResult.OriginalMissing, vm.MakeRebuild(missing, 0x00000000, "out.rebuild"));
            }
            finally { CoreState.ROM = prevRom; }
        }

        [Fact]
        public void MakeRebuild_BadAddress_ReturnsBadAddress()
        {
            var prevRom = CoreState.ROM;
            string origPath = Path.Combine(Path.GetTempPath(), "feb_rb_orig_" + Guid.NewGuid().ToString("N") + ".gba");
            try
            {
                var rom = new ROM();
                rom.SwapNewROMDataDirect(new byte[0x400]);
                CoreState.ROM = rom;
                File.WriteAllBytes(origPath, new byte[0x400]);

                var vm = new ToolROMRebuildViewModel();
                // Misaligned address is a hard failure, checked before reading the original ROM.
                Assert.Equal(ToolROMRebuildViewModel.MakeResult.BadAddress, vm.MakeRebuild(origPath, 0x09000001, "out.rebuild"));
            }
            finally
            {
                CoreState.ROM = prevRom;
                try { File.Delete(origPath); } catch { }
            }
        }

        [Fact]
        public void MakeRebuild_WritesReport_WhenOriginalCrcSkipped()
        {
            // No RomInfo -> orignal_crc32 == 0 -> CRC check skipped (headless path).
            var prevRom = CoreState.ROM;
            string origPath = Path.Combine(Path.GetTempPath(), "feb_rb_orig2_" + Guid.NewGuid().ToString("N") + ".gba");
            string outPath = Path.Combine(Path.GetTempPath(), "feb_rb_out_" + Guid.NewGuid().ToString("N") + ".rebuild");
            try
            {
                byte[] cur = new byte[0x4000];
                for (int i = 0; i < cur.Length; i++) cur[i] = (byte)(i & 0xFF);
                var rom = new ROM();
                rom.SwapNewROMDataDirect(cur);
                CoreState.ROM = rom;

                byte[] orig = (byte[])cur.Clone();
                orig[0x40] ^= 0xFF;
                File.WriteAllBytes(origPath, orig);

                var vm = new ToolROMRebuildViewModel();
                // Aligned, >= 0x200, < ROM length -> a safe offset for this in-memory ROM.
                var r = vm.MakeRebuild(origPath, 0x00001000, outPath);
                Assert.Equal(ToolROMRebuildViewModel.MakeResult.Ok, r);
                Assert.True(File.Exists(outPath));
                Assert.Contains("@_CRC32 ", File.ReadAllText(outPath));
            }
            finally
            {
                CoreState.ROM = prevRom;
                try { File.Delete(origPath); } catch { }
                try { File.Delete(outPath); } catch { }
            }
        }

        [Fact]
        public void SuggestedName_MirrorsWinFormsPrefix()
        {
            Assert.Equal("R.20260616123000.rebuild", ToolROMRebuildViewModel.SuggestedName("20260616123000"));
        }
    }

    // Needs a real ROM so RomInfo.extends_address / orignal_crc32 are set; skips without ROMs.
    [Collection("SharedState")]
    public class ToolROMRebuildRomTests : IClassFixture<RomFixture>
    {
        readonly RomFixture _fixture;
        readonly ITestOutputHelper _output;
        public ToolROMRebuildRomTests(RomFixture fixture, ITestOutputHelper output)
        { _fixture = fixture; _output = output; }

        [Fact]
        public void Load_DefaultsAddressToExtends_ForRealRom()
        {
            if (!_fixture.IsAvailable) { _output.WriteLine("SKIP: no ROM available"); return; }
            var vm = new ToolROMRebuildViewModel();
            bool ok = vm.Load();
            Assert.True(ok);
            uint expected = U.toOffset(CoreState.ROM.RomInfo.extends_address);
            Assert.Equal(expected, vm.RebuildAddress);
            Assert.Equal(expected, vm.DefaultRebuildAddress());
        }

        [Fact]
        public void MakeRebuild_WrongOriginal_ReturnsOriginalNotMatching()
        {
            if (!_fixture.IsAvailable) { _output.WriteLine("SKIP: no ROM available"); return; }
            string wrong = Path.Combine(Path.GetTempPath(), "feb_rb_wrong_" + Guid.NewGuid().ToString("N") + ".gba");
            try
            {
                // Zeros -> CRC32 won't match the loaded game's known-original CRC32.
                File.WriteAllBytes(wrong, new byte[0x1000]);
                var vm = new ToolROMRebuildViewModel();
                uint addr = U.toOffset(CoreState.ROM.RomInfo.extends_address);
                Assert.Equal(ToolROMRebuildViewModel.MakeResult.OriginalNotMatching, vm.MakeRebuild(wrong, addr, "out.rebuild"));
            }
            finally { try { File.Delete(wrong); } catch { } }
        }
    }
}
