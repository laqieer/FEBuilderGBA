// SPDX-License-Identifier: GPL-3.0-or-later
// #1460 — Apply-UPS tool (Avalonia port of WinForms ToolUPSOpenSimpleForm).
// Tests the VM's validate-then-apply over Core UPSUtilCore (no ROM init / config needed
// for the headless cases; one ROM-fixture case for the clean-original CRC32 check).
using System;
using System.IO;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class ToolUPSOpenSimpleViewModelTests
    {
        // ---- helpers --------------------------------------------------------

        // We cannot synthesize a real game ROM's CRC32 cheaply, so the headless cases here
        // assert the validation gates (UpsMissing/UpsInvalid/OriginalMissing/OriginalNotClean)
        // and the CRC32-based auto-detect — none of which need a clean ROM. The full VM-level
        // apply round-trip (which must pass the clean-original gate) uses the ROM fixture below.

        static (string upsPath, string origPath, byte[] dst) MakeUpsPair(byte[] src, byte[] dst)
        {
            string upsPath = Path.Combine(Path.GetTempPath(), "feb_apply_ups_" + Guid.NewGuid().ToString("N") + ".ups");
            string origPath = Path.Combine(Path.GetTempPath(), "feb_apply_orig_" + Guid.NewGuid().ToString("N") + ".gba");
            UPSUtilCore.MakeUPS(src, dst, upsPath);
            File.WriteAllBytes(origPath, src);
            return (upsPath, origPath, dst);
        }

        // ---- headless cases (no ROM) ---------------------------------------

        [Fact]
        public void ApplyUps_MissingUps_ReturnsUpsMissing()
        {
            var vm = new ToolUPSOpenSimpleViewModel();
            string missing = Path.Combine(Path.GetTempPath(), "feb_no_ups_" + Guid.NewGuid().ToString("N") + ".ups");
            Assert.Equal(ToolUPSOpenSimpleViewModel.ApplyResult.UpsMissing, vm.ApplyUps(missing, "x.gba", out _, out _));
        }

        [Fact]
        public void ApplyUps_NonUpsFile_ReturnsUpsInvalid()
        {
            var vm = new ToolUPSOpenSimpleViewModel();
            string notUps = Path.Combine(Path.GetTempPath(), "feb_not_ups_" + Guid.NewGuid().ToString("N") + ".ups");
            try
            {
                File.WriteAllBytes(notUps, new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05 }); // no "UPS1" magic
                Assert.Equal(ToolUPSOpenSimpleViewModel.ApplyResult.UpsInvalid, vm.ApplyUps(notUps, "x.gba", out _, out _));
            }
            finally { try { File.Delete(notUps); } catch { } }
        }

        [Fact]
        public void ApplyUps_MissingOriginal_ReturnsOriginalMissing()
        {
            var vm = new ToolUPSOpenSimpleViewModel();
            byte[] src = MakeBuffer(0x200, 1);
            byte[] dst = MakeBuffer(0x200, 2);
            var (upsPath, origPath, _) = MakeUpsPair(src, dst);
            try
            {
                string missingOrig = Path.Combine(Path.GetTempPath(), "feb_no_orig_" + Guid.NewGuid().ToString("N") + ".gba");
                Assert.Equal(ToolUPSOpenSimpleViewModel.ApplyResult.OriginalMissing, vm.ApplyUps(upsPath, missingOrig, out _, out _));
            }
            finally { Cleanup(upsPath, origPath); }
        }

        [Fact]
        public void ApplyUps_OriginalNotClean_ReturnsOriginalNotClean()
        {
            // A real-on-disk original that is NOT a known clean ROM (its CRC32 isn't in the
            // ROMBaseTable) must be rejected before applying (mirrors WF CheckOrignalROM).
            var vm = new ToolUPSOpenSimpleViewModel();
            byte[] src = MakeBuffer(0x200, 1);
            byte[] dst = MakeBuffer(0x200, 2);
            var (upsPath, origPath, _) = MakeUpsPair(src, dst);
            try
            {
                Assert.Equal(ToolUPSOpenSimpleViewModel.ApplyResult.OriginalNotClean, vm.ApplyUps(upsPath, origPath, out _, out _));
            }
            finally { Cleanup(upsPath, origPath); }
        }

        [Fact]
        public void IsCleanOriginal_RejectsArbitraryBytes()
        {
            string junk = Path.Combine(Path.GetTempPath(), "feb_junk_" + Guid.NewGuid().ToString("N") + ".gba");
            try
            {
                File.WriteAllBytes(junk, new byte[0x1000]);
                Assert.False(ToolUPSOpenSimpleViewModel.IsCleanOriginal(junk));
            }
            finally { try { File.Delete(junk); } catch { } }
        }

        // NOTE: a positive "auto-detect finds the clean ROM by CRC32" test lives in the
        // ROM-fixture class below — Core FindOrignalROMByCRC32 only resolves CRC32s present in
        // ROMBaseTable (real game ROMs), so it cannot find a synthetic candidate.

        [Fact]
        public void FindOriginalForUps_InvalidUps_ReturnsEmpty()
        {
            var vm = new ToolUPSOpenSimpleViewModel();
            string notUps = Path.Combine(Path.GetTempPath(), "feb_bad_ups_" + Guid.NewGuid().ToString("N") + ".ups");
            try
            {
                File.WriteAllBytes(notUps, new byte[] { 0, 1, 2, 3 });
                Assert.Equal("", vm.FindOriginalForUps(notUps));
            }
            finally { try { File.Delete(notUps); } catch { } }
        }

        static byte[] MakeBuffer(int len, byte seed)
        {
            byte[] b = new byte[len];
            for (int i = 0; i < len; i++) b[i] = (byte)((i + seed) & 0xFF);
            return b;
        }

        static void Cleanup(params string[] paths)
        {
            foreach (var p in paths) { try { File.Delete(p); } catch { } }
        }
    }

    // ROM-fixture cases: need a real clean ROM so IsCleanOriginal accepts it and the full
    // VM-level apply round-trip can run end-to-end. Skips when no ROM is available.
    [Collection("SharedState")]
    public class ToolUPSOpenSimpleRomApplyTests : IClassFixture<RomFixture>
    {
        readonly RomFixture _fixture;
        readonly ITestOutputHelper _output;
        public ToolUPSOpenSimpleRomApplyTests(RomFixture fixture, ITestOutputHelper output)
        { _fixture = fixture; _output = output; }

        [Fact]
        public void ApplyUps_CleanOriginalPlusUps_RoundTripsToModifiedRom()
        {
            if (!_fixture.IsAvailable) { _output.WriteLine("SKIP: no ROM available"); return; }

            // Use the fixture's clean ROM as the original; build a small modification (dst),
            // make a UPS, then prove ApplyUps reproduces dst byte-for-byte.
            string cleanPath = _fixture.RomPath;
            byte[] src = File.ReadAllBytes(cleanPath);

            // IsCleanOriginal must accept the fixture ROM (it is an official clean ROM).
            Assert.True(ToolUPSOpenSimpleViewModel.IsCleanOriginal(cleanPath),
                "fixture ROM should be a known clean original");

            byte[] dst = (byte[])src.Clone();
            dst[0x100] ^= 0xFF; dst[0x2000] ^= 0x55; dst[src.Length - 1] ^= 0xAA;

            string upsPath = Path.Combine(Path.GetTempPath(), "feb_apply_rt_" + Guid.NewGuid().ToString("N") + ".ups");
            try
            {
                UPSUtilCore.MakeUPS(src, dst, upsPath);

                var vm = new ToolUPSOpenSimpleViewModel();
                var r = vm.ApplyUps(upsPath, cleanPath, out byte[] patched, out _);

                Assert.Equal(ToolUPSOpenSimpleViewModel.ApplyResult.Ok, r);
                Assert.NotNull(patched);
                Assert.Equal(dst.Length, patched.Length);
                Assert.True(((ReadOnlySpan<byte>)dst).SequenceEqual(patched),
                    "patched bytes must equal the target modified ROM");
            }
            finally { try { File.Delete(upsPath); } catch { } }
        }

        [Fact]
        public void ApplyUps_WrongUpsForOriginal_ReturnsSourceCrcMismatch()
        {
            if (!_fixture.IsAvailable) { _output.WriteLine("SKIP: no ROM available"); return; }

            // A UPS built from a DIFFERENT source than the chosen (clean) original ⇒ the
            // recorded source CRC32 won't match ⇒ Core ApplyUPS returns null ⇒ SourceCrcMismatch.
            string cleanPath = _fixture.RomPath;

            byte[] otherSrc = new byte[0x400];
            for (int i = 0; i < otherSrc.Length; i++) otherSrc[i] = (byte)(i & 0xFF);
            byte[] otherDst = (byte[])otherSrc.Clone();
            otherDst[0x10] ^= 0xFF;

            string upsPath = Path.Combine(Path.GetTempPath(), "feb_apply_wrong_" + Guid.NewGuid().ToString("N") + ".ups");
            try
            {
                UPSUtilCore.MakeUPS(otherSrc, otherDst, upsPath); // records CRC32(otherSrc)

                var vm = new ToolUPSOpenSimpleViewModel();
                var r = vm.ApplyUps(upsPath, cleanPath, out byte[] patched, out _);

                Assert.Equal(ToolUPSOpenSimpleViewModel.ApplyResult.SourceCrcMismatch, r);
                Assert.Null(patched);
            }
            finally { try { File.Delete(upsPath); } catch { } }
        }

        [Fact]
        public void ApplyUps_TamperedResultCrc_ReturnsOkWithWarning()
        {
            if (!_fixture.IsAvailable) { _output.WriteLine("SKIP: no ROM available"); return; }

            // Build a valid UPS from the clean ROM, then corrupt only the recorded DEST CRC32
            // (the 8..4-byte-from-end u32). Source CRC stays valid ⇒ Core ApplyUPS still returns
            // patched bytes but with a non-empty "Result CRC mismatch" warning ⇒ OkWithWarning,
            // and the warning text must reach the caller (Copilot review finding #2).
            string cleanPath = _fixture.RomPath;
            byte[] src = File.ReadAllBytes(cleanPath);
            byte[] dst = (byte[])src.Clone();
            dst[0x40] ^= 0xFF;

            string upsPath = Path.Combine(Path.GetTempPath(), "feb_apply_warn_" + Guid.NewGuid().ToString("N") + ".ups");
            try
            {
                UPSUtilCore.MakeUPS(src, dst, upsPath);
                byte[] ups = File.ReadAllBytes(upsPath);
                // Dest CRC32 lives at (len-8 .. len-4). Flip a bit so the result CRC won't match,
                // but DON'T touch the trailing patch CRC (len-4 .. len) or the source CRC (len-12).
                ups[ups.Length - 8] ^= 0xFF;
                File.WriteAllBytes(upsPath, ups);

                var vm = new ToolUPSOpenSimpleViewModel();
                var r = vm.ApplyUps(upsPath, cleanPath, out byte[] patched, out string warning);

                Assert.Equal(ToolUPSOpenSimpleViewModel.ApplyResult.OkWithWarning, r);
                Assert.NotNull(patched);
                Assert.False(string.IsNullOrEmpty(warning), "warning text must propagate to the caller");
                // The patched bytes are still the correctly-applied result (WF applies anyway).
                Assert.True(((ReadOnlySpan<byte>)dst).SequenceEqual(patched));
            }
            finally { try { File.Delete(upsPath); } catch { } }
        }

        [Fact]
        public void ApplyUps_TruncatedUpsPastMagic_ReturnsUpsInvalidNotSourceCrcMismatch()
        {
            if (!_fixture.IsAvailable) { _output.WriteLine("SKIP: no ROM available"); return; }

            // A file that PASSES IsUPSFile (has the 4-byte "UPS1" magic) but is too short for a
            // real UPS (< 16 bytes) makes Core ApplyUPS return null with a "corrupted: below
            // minimum size" message. The original is the real clean ROM (passes the clean gate),
            // so the failure is the UPS itself — must report UpsInvalid, NOT SourceCrcMismatch
            // (Copilot review thread #1).
            string cleanPath = _fixture.RomPath;
            string upsPath = Path.Combine(Path.GetTempPath(), "feb_trunc_ups_" + Guid.NewGuid().ToString("N") + ".ups");
            try
            {
                File.WriteAllBytes(upsPath, new byte[] { (byte)'U', (byte)'P', (byte)'S', (byte)'1', 0x00, 0x00 });
                var vm = new ToolUPSOpenSimpleViewModel();
                var r = vm.ApplyUps(upsPath, cleanPath, out byte[] patched, out _);
                Assert.Equal(ToolUPSOpenSimpleViewModel.ApplyResult.UpsInvalid, r);
                Assert.Null(patched);
            }
            finally { try { File.Delete(upsPath); } catch { } }
        }

        [Fact]
        public void FindOriginalForUps_AutoDetectsCleanRomByCrc32_InUpsDir()
        {
            if (!_fixture.IsAvailable) { _output.WriteLine("SKIP: no ROM available"); return; }

            // Copy the real clean ROM into a temp dir, build a UPS from it there, and confirm
            // the auto-detect resolves the clean ROM by its recorded source CRC32 (WF parity:
            // GetUPSSrcCRC32 → FindOrignalROMByCRC32). Core only matches ROMBaseTable CRC32s, so
            // this needs a real ROM.
            string dir = Path.Combine(Path.GetTempPath(), "feb_find_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string candidate = Path.Combine(dir, "clean.gba");
            string upsPath = Path.Combine(dir, "patch.ups");
            try
            {
                byte[] src = File.ReadAllBytes(_fixture.RomPath);
                File.WriteAllBytes(candidate, src);
                byte[] dst = (byte[])src.Clone();
                dst[0x80] ^= 0x33;
                UPSUtilCore.MakeUPS(src, dst, upsPath); // records CRC32(src) = the clean ROM's CRC

                var vm = new ToolUPSOpenSimpleViewModel();
                string found = vm.FindOriginalForUps(upsPath);

                Assert.False(string.IsNullOrEmpty(found), "auto-detect should find the clean ROM");
                Assert.Equal(Path.GetFullPath(candidate), Path.GetFullPath(found));
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }
    }
}
