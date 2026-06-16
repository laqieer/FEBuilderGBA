// SPDX-License-Identifier: GPL-3.0-or-later
// #1194 — Save-as-UPS tool (Avalonia port of WinForms ToolUPSPatchSimpleForm).
// Tests the VM's validate-then-make over Core UPSUtilCore (no ROM init / config needed).
using System;
using System.IO;
using System.Text;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class ToolUPSPatchSimpleViewModelTests
    {
        [Fact]
        public void MakeUps_CreatesValidUpsPatch_FromOriginalVsCurrentRom()
        {
            var prevRom = CoreState.ROM;
            string origPath = Path.Combine(Path.GetTempPath(), "feb_ups_orig_" + Guid.NewGuid().ToString("N") + ".gba");
            string outPath = Path.Combine(Path.GetTempPath(), "feb_ups_out_" + Guid.NewGuid().ToString("N") + ".ups");
            try
            {
                // Current (modified) ROM held in memory.
                byte[] cur = new byte[0x400];
                for (int i = 0; i < cur.Length; i++) cur[i] = (byte)(i & 0xFF);
                var rom = new ROM();
                rom.SwapNewROMDataDirect(cur);
                CoreState.ROM = rom;

                // Clean original on disk (differs in a couple of bytes).
                byte[] orig = (byte[])cur.Clone();
                orig[0x10] ^= 0xFF; orig[0x100] ^= 0x55;
                File.WriteAllBytes(origPath, orig);

                var vm = new ToolUPSPatchSimpleViewModel();
                var r = vm.MakeUps(origPath, outPath);

                Assert.Equal(ToolUPSPatchSimpleViewModel.MakeResult.Ok, r);
                Assert.True(File.Exists(outPath));
                byte[] ups = File.ReadAllBytes(outPath);
                Assert.True(ups.Length >= 4);
                Assert.Equal("UPS1", Encoding.ASCII.GetString(ups, 0, 4));   // UPS file magic
            }
            finally
            {
                CoreState.ROM = prevRom;
                try { File.Delete(origPath); } catch { }
                try { File.Delete(outPath); } catch { }
            }
        }

        [Fact]
        public void MakeUps_NoRom_ReturnsNoRom()
        {
            var prevRom = CoreState.ROM;
            try
            {
                CoreState.ROM = null;
                var vm = new ToolUPSPatchSimpleViewModel();
                Assert.Equal(ToolUPSPatchSimpleViewModel.MakeResult.NoRom, vm.MakeUps("x", "y"));
            }
            finally { CoreState.ROM = prevRom; }
        }

        [Fact]
        public void MakeUps_MissingOriginal_ReturnsOriginalMissing()
        {
            var prevRom = CoreState.ROM;
            try
            {
                var rom = new ROM();
                rom.SwapNewROMDataDirect(new byte[0x100]);
                CoreState.ROM = rom;
                var vm = new ToolUPSPatchSimpleViewModel();
                string missing = Path.Combine(Path.GetTempPath(), "feb_ups_missing_" + Guid.NewGuid().ToString("N") + ".gba");
                Assert.Equal(ToolUPSPatchSimpleViewModel.MakeResult.OriginalMissing, vm.MakeUps(missing, "out.ups"));
            }
            finally { CoreState.ROM = prevRom; }
        }

        [Fact]
        public void SuggestedName_MirrorsWinFormsFormat()
        {
            Assert.Equal("PATCH.20260616123000.ups", ToolUPSPatchSimpleViewModel.SuggestedName("20260616123000"));
        }
    }

    // Needs a real ROM so RomInfo.orignal_crc32 is set; skips without ROMs.
    [Collection("SharedState")]
    public class ToolUPSPatchSimpleRomValidationTests : IClassFixture<RomFixture>
    {
        readonly RomFixture _fixture;
        readonly ITestOutputHelper _output;
        public ToolUPSPatchSimpleRomValidationTests(RomFixture fixture, ITestOutputHelper output)
        { _fixture = fixture; _output = output; }

        [Fact]
        public void MakeUps_WrongOriginal_ReturnsOriginalNotMatching()
        {
            if (!_fixture.IsAvailable) { _output.WriteLine("SKIP: no ROM available"); return; }
            string wrong = Path.Combine(Path.GetTempPath(), "feb_ups_wrong_" + Guid.NewGuid().ToString("N") + ".gba");
            try
            {
                // Zeros -> CRC32 won't match the loaded game's known-original CRC32.
                File.WriteAllBytes(wrong, new byte[0x1000]);
                var vm = new ToolUPSPatchSimpleViewModel();
                Assert.Equal(ToolUPSPatchSimpleViewModel.MakeResult.OriginalNotMatching, vm.MakeUps(wrong, "out.ups"));
            }
            finally { try { File.Delete(wrong); } catch { } }
        }
    }
}
