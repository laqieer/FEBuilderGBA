// SPDX-License-Identifier: GPL-3.0-or-later
// #1452: regression tests proving the FE8N skill-patch DETECTION used by the
// Avalonia SkillAssignmentUnitFE8NView works on a (synthetic) patched FE8J ROM.
//
// The known "skill editor ROM detection wall" (no FE8N skill ROM ships in
// roms/) is sidestepped by planting the exact FE8N byte signature
// ({0x00,0x4B,0x9F,0x46} @ 0x89268 on FE8J) the same way
// PatchDetectionServiceTests plants the FE8U SkillSystem signature.
using Xunit;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class SkillAssignmentUnitFE8NDetectionTests : IDisposable
    {
        readonly ROM? _savedRom;

        public SkillAssignmentUnitFE8NDetectionTests()
        {
            _savedRom = CoreState.ROM;
        }

        public void Dispose()
        {
            CoreState.ROM = _savedRom;
            PatchDetection.ClearAllCaches();
            MagicSplitUtil.ClearCache();
            // Reset the PatchDetectionService singleton so a patched scan result
            // can't leak into order-dependent siblings (e.g.
            // PatchDetectionServiceDefaultTests assert default values).
            PatchDetectionService.Instance.Refresh();
        }

        // ---- FE8J ROM helper (multibyte; game code BE8J01) ----
        static ROM MakeFE8JRom()
        {
            byte[] data = new byte[0x1000000]; // 16 MB
            byte[] versionBytes = System.Text.Encoding.ASCII.GetBytes("BE8J01");
            Array.Copy(versionBytes, 0, data, 0xAC, versionBytes.Length);

            var rom = new ROM();
            rom.LoadLow("test-fe8j.gba", data, "BE8J01");
            return rom;
        }

        // ---- The FE8N base-variant signature lives at 0x89268. ----
        static void PlantFE8NSignature(ROM rom)
        {
            byte[] sig = { 0x00, 0x4B, 0x9F, 0x46 };
            Array.Copy(sig, 0, rom.Data, 0x89268, sig.Length);
        }

        [Fact]
        public void RomHelper_IsFE8J_Multibyte()
        {
            var rom = MakeFE8JRom();
            Assert.Equal("FE8J", rom.RomInfo.VersionToFilename);
            Assert.True(rom.RomInfo.is_multibyte);
            Assert.Equal(8, rom.RomInfo.version);
        }

        [Fact]
        public void Refresh_CleanFE8J_NoSkillSystem()
        {
            CoreState.ROM = MakeFE8JRom();
            var svc = PatchDetectionService.Instance;
            svc.Refresh();

            Assert.Equal(PatchDetectionService.SkillSystemType.None, svc.SkillSystem);
            Assert.False(svc.HasSkillSystem);
        }

        [Fact]
        public void Refresh_FE8J_WithFE8NPatch_DetectedAsFE8NFamily()
        {
            var rom = MakeFE8JRom();
            PlantFE8NSignature(rom);
            CoreState.ROM = rom;

            var svc = PatchDetectionService.Instance;
            svc.Refresh();

            // The base FE8N variant (no Ver2/Ver3 icon-table extension planted).
            Assert.Equal(PatchDetectionService.SkillSystemType.FE8N, svc.SkillSystem);
            Assert.True(svc.HasSkillSystem);
            // Not a CSkillSys (FE8U) variant.
            Assert.False(svc.IsCSkillSys);
        }

        [Fact]
        public void Refresh_FE8J_WithYugudoraPatch_Detected()
        {
            var rom = MakeFE8JRom();
            // yugudora @ 0xEE594: { 0x4B, 0xFA, 0x2F, 0x59 }
            byte[] sig = { 0x4B, 0xFA, 0x2F, 0x59 };
            Array.Copy(sig, 0, rom.Data, 0xEE594, sig.Length);
            CoreState.ROM = rom;

            var svc = PatchDetectionService.Instance;
            svc.Refresh();

            Assert.Equal(PatchDetectionService.SkillSystemType.Yugudora, svc.SkillSystem);
            Assert.True(svc.HasSkillSystem);
        }

        [Fact]
        public void Refresh_ClearsFE8NAfterRomUnload()
        {
            var rom = MakeFE8JRom();
            PlantFE8NSignature(rom);
            CoreState.ROM = rom;

            var svc = PatchDetectionService.Instance;
            svc.Refresh();
            Assert.True(svc.HasSkillSystem);

            CoreState.ROM = null;
            svc.Refresh();
            Assert.False(svc.HasSkillSystem);
        }
    }
}
