using Xunit;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class PatchDetectionServiceTests : IDisposable
    {
        readonly ROM? _savedRom;
        readonly ISystemTextEncoder _savedSystemTextEncoder;

        public PatchDetectionServiceTests()
        {
            _savedRom = CoreState.ROM;
            _savedSystemTextEncoder = CoreState.SystemTextEncoder;
        }

        public void Dispose()
        {
            CoreState.ROM = _savedRom;
            CoreState.SystemTextEncoder = _savedSystemTextEncoder;
            NameResolver.ClearCache();
            PatchDetection.ClearAllCaches();
            MagicSplitUtil.ClearCache();
            ResetSkillSystemTextBase(PatchDetectionService.Instance);
            PatchDetectionService.Instance.Refresh();
        }

        [Fact]
        public void Refresh_NullROM_AllDefaults()
        {
            CoreState.ROM = null;
            var svc = PatchDetectionService.Instance;
            svc.Refresh();

            Assert.Equal(PatchDetectionService.SkillSystemType.None, svc.SkillSystem);
            Assert.Equal(PatchDetectionService.MagicSplitType.None, svc.MagicSplit);
            Assert.False(svc.VennouWeaponLock);
            Assert.False(svc.ItemEffectRange);
            Assert.Equal(PatchDetectionService.PortraitExtendsType.None, svc.PortraitExtends);
            Assert.False(svc.BG256Color);
            Assert.False(svc.AntiHuffman);
            Assert.False(svc.SkillSystemsClassTypeRework);
            Assert.Equal(PatchDetection.draw_font_enum.NO, svc.DrawFont);
            Assert.Equal(PatchDetection.TextEngineRework_enum.NO, svc.TextEngineRework);
        }

        [Fact]
        public void Refresh_NullROM_ConvenienceHelpers_False()
        {
            CoreState.ROM = null;
            var svc = PatchDetectionService.Instance;
            svc.Refresh();

            Assert.False(svc.HasSkillSystem);
            Assert.False(svc.IsCSkillSys);
            Assert.False(svc.HasMagicSplit);
            Assert.False(svc.HasPortraitExtends);
            Assert.False(svc.IsHalfBody);
        }

        [Fact]
        public void ResolveSkillName_UnresolvedIds_ReturnNull()
        {
            var svc = PatchDetectionService.Instance;

            CoreState.ROM = null;
            svc.Refresh();

            Assert.Null(svc.ResolveSkillName(0));
            Assert.Null(svc.ResolveSkillName(1));

            CoreState.ROM = MakeFE8USkillSystemRomWithAliasedOverflowText();
            ResetSkillSystemTextBase(svc);
            svc.Refresh();
            Assert.Equal(PatchDetectionService.SkillSystemType.SkillSystem, svc.SkillSystem);
            Assert.Null(svc.ResolveSkillName(1));
            Assert.Null(svc.ResolveSkillName(0x80000000u));

            CoreState.ROM = MakeFE8UCSkillSysRomWithAliasedOverflowText();
            svc.Refresh();
            Assert.Equal(PatchDetectionService.SkillSystemType.CSkillSys300, svc.SkillSystem);
            Assert.Null(svc.ResolveSkillName(1));
            Assert.Null(svc.ResolveSkillName(0x20000000u));

            CoreState.ROM = MakeFE8NRomWithAliasedOverflowText();
            svc.Refresh();
            Assert.NotEqual(PatchDetectionService.SkillSystemType.None, svc.SkillSystem);
            Assert.Null(svc.ResolveSkillName(1));
            Assert.Null(svc.ResolveSkillName(0x80000000u));
        }

        [Fact]
        public void Refresh_CleanFE8U_NoPatches()
        {
            CoreState.ROM = MakeFE8URom();
            var svc = PatchDetectionService.Instance;
            svc.Refresh();

            Assert.Equal(PatchDetectionService.SkillSystemType.None, svc.SkillSystem);
            Assert.Equal(PatchDetectionService.MagicSplitType.None, svc.MagicSplit);
            Assert.False(svc.VennouWeaponLock);
            Assert.False(svc.ItemEffectRange);
            Assert.Equal(PatchDetectionService.PortraitExtendsType.None, svc.PortraitExtends);
            Assert.False(svc.BG256Color);
            Assert.False(svc.AntiHuffman);
        }

        [Fact]
        public void Refresh_FE8U_WithSkillSystem_Detected()
        {
            var rom = MakeFE8URom();
            // Write SkillSystem signature at 0x2ACF8: { 0x70, 0x47 }
            rom.Data[0x2ACF8] = 0x70;
            rom.Data[0x2ACF9] = 0x47;
            CoreState.ROM = rom;

            var svc = PatchDetectionService.Instance;
            svc.Refresh();

            Assert.Equal(PatchDetectionService.SkillSystemType.SkillSystem, svc.SkillSystem);
            Assert.True(svc.HasSkillSystem);
            Assert.False(svc.IsCSkillSys);
        }

        [Fact]
        public void Refresh_FE8U_WithAntiHuffman_Detected()
        {
            var rom = MakeFE8URom();
            // AntiHuffman at 0x2BA4: { 0x00, 0xB5, 0xC2, 0x0F }
            rom.Data[0x2BA4] = 0x00;
            rom.Data[0x2BA5] = 0xB5;
            rom.Data[0x2BA6] = 0xC2;
            rom.Data[0x2BA7] = 0x0F;
            CoreState.ROM = rom;

            var svc = PatchDetectionService.Instance;
            svc.Refresh();

            Assert.True(svc.AntiHuffman);
        }

        [Fact]
        public void Refresh_FE8U_WithVennouWeaponLock_Detected()
        {
            var rom = MakeFE8URom();
            // Vennou hook: u32 at 0x16DD8 == 0xFF3D3C00 (little-endian)
            rom.Data[0x16DD8] = 0x00;
            rom.Data[0x16DD9] = 0x3C;
            rom.Data[0x16DDA] = 0x3D;
            rom.Data[0x16DDB] = 0xFF;
            CoreState.ROM = rom;

            var svc = PatchDetectionService.Instance;
            svc.Refresh();

            Assert.True(svc.VennouWeaponLock);
        }

        [Fact]
        public void Refresh_FE8U_WithIER_Detected()
        {
            var rom = MakeFE8URom();
            byte[] ier = { 0x03, 0x4B, 0x14, 0x22, 0x50, 0x43, 0x40, 0x18, 0xC0, 0x18, 0x00, 0x68, 0x70, 0x47, 0x00, 0x00 };
            Array.Copy(ier, 0, rom.Data, 0x28E80, ier.Length);
            CoreState.ROM = rom;

            var svc = PatchDetectionService.Instance;
            svc.Refresh();

            Assert.True(svc.ItemEffectRange);
        }

        [Fact]
        public void Refresh_FE8U_WithHalfBody_Detected()
        {
            var rom = MakeFE8URom();
            // HALFBODY at 0x8540: { 0x0A, 0x1C }
            rom.Data[0x8540] = 0x0A;
            rom.Data[0x8541] = 0x1C;
            CoreState.ROM = rom;

            var svc = PatchDetectionService.Instance;
            svc.Refresh();

            Assert.Equal(PatchDetectionService.PortraitExtendsType.HalfBody, svc.PortraitExtends);
            Assert.True(svc.HasPortraitExtends);
            Assert.True(svc.IsHalfBody);
        }

        [Fact]
        public void Refresh_FE8U_WithBG256Color_Detected()
        {
            var rom = MakeFE8URom();
            // BG256Color at 0xE2DA: { 0xC0, 0x46, 0xC0, 0x46 }
            rom.Data[0xE2DA] = 0xC0;
            rom.Data[0xE2DB] = 0x46;
            rom.Data[0xE2DC] = 0xC0;
            rom.Data[0xE2DD] = 0x46;
            CoreState.ROM = rom;

            var svc = PatchDetectionService.Instance;
            svc.Refresh();

            Assert.True(svc.BG256Color);
        }

        [Fact]
        public void Refresh_FE8U_WithClassTypeRework_Detected()
        {
            var rom = MakeFE8URom();
            byte[] sig = { 0x00, 0x25, 0x00, 0x28, 0x00, 0xD0, 0x05, 0x1C };
            Array.Copy(sig, 0, rom.Data, 0x2AAEC, sig.Length);
            CoreState.ROM = rom;

            var svc = PatchDetectionService.Instance;
            svc.Refresh();

            Assert.True(svc.SkillSystemsClassTypeRework);
        }

        [Fact]
        public void Refresh_FE8U_WithMagicSplit_Detected()
        {
            var rom = MakeFE8URom();
            // FE8U magic split at 0x2BB44: { 0x01, 0x4B, 0xA5, 0xF0, 0xC1, 0xFE }
            rom.Data[0x2BB44] = 0x01;
            rom.Data[0x2BB45] = 0x4B;
            rom.Data[0x2BB46] = 0xA5;
            rom.Data[0x2BB47] = 0xF0;
            rom.Data[0x2BB48] = 0xC1;
            rom.Data[0x2BB49] = 0xFE;
            CoreState.ROM = rom;

            var svc = PatchDetectionService.Instance;
            svc.Refresh();

            Assert.Equal(PatchDetectionService.MagicSplitType.FE8U, svc.MagicSplit);
            Assert.True(svc.HasMagicSplit);
        }

        [Fact]
        public void Refresh_ClearsAfterROMUnload()
        {
            var rom = MakeFE8URom();
            rom.Data[0x2ACF8] = 0x70;
            rom.Data[0x2ACF9] = 0x47;
            CoreState.ROM = rom;

            var svc = PatchDetectionService.Instance;
            svc.Refresh();
            Assert.True(svc.HasSkillSystem);

            // Now unload ROM and refresh
            CoreState.ROM = null;
            svc.Refresh();
            Assert.False(svc.HasSkillSystem);
        }

        [Fact]
        public void Singleton_ReturnsSameInstance()
        {
            var a = PatchDetectionService.Instance;
            var b = PatchDetectionService.Instance;
            Assert.Same(a, b);
        }

        [Fact]
        public void Refresh_FE8U_WithMugExceed_Detected()
        {
            var rom = MakeFE8URom();
            byte[] sig = { 0xC0, 0x46, 0x01, 0xB0, 0x03, 0x4B };
            Array.Copy(sig, 0, rom.Data, 0x55D2, sig.Length);
            CoreState.ROM = rom;

            var svc = PatchDetectionService.Instance;
            svc.Refresh();

            Assert.Equal(PatchDetectionService.PortraitExtendsType.MugExceed, svc.PortraitExtends);
            Assert.True(svc.HasPortraitExtends);
            Assert.False(svc.IsHalfBody);
        }

        // ---- Helper: create a minimal FE8U ROM ----

        static ROM MakeFE8URom()
        {
            byte[] data = new byte[0x1000000]; // 16 MB
            // Write version string "BE8E01" at offset 0xAC (GBA header game code)
            byte[] versionBytes = System.Text.Encoding.ASCII.GetBytes("BE8E01");
            Array.Copy(versionBytes, 0, data, 0xAC, versionBytes.Length);

            var rom = new ROM();
            rom.LoadLow("test.gba", data, "BE8E01");
            return rom;
        }

        static ROM MakeFE8JRom()
        {
            byte[] data = new byte[0x1000000]; // 16 MB
            byte[] versionBytes = System.Text.Encoding.ASCII.GetBytes("BE8J01");
            Array.Copy(versionBytes, 0, data, 0xAC, versionBytes.Length);

            var rom = new ROM();
            rom.LoadLow("test-fe8j.gba", data, "BE8J01");
            return rom;
        }

        static ROM MakeFE8USkillSystemRomWithAliasedOverflowText()
        {
            var rom = MakeFE8URom();
            rom.Data[0x2ACF8] = 0x70;
            rom.Data[0x2ACF9] = 0x47;

            byte[] pattern = { 0x07, 0x49, 0x40, 0x00, 0x40, 0x18, 0x00, 0x88, 0x00, 0x28, 0x00, 0xD1, 0x06, 0x48, 0x21, 0x1C };
            Array.Copy(pattern, 0, rom.Data, 0xB00000, pattern.Length);
            uint skillTextBase = 0x2000;
            WriteU32(rom.Data, 0xB00000 + (uint)pattern.Length + 16, U.toPointer(skillTextBase));
            rom.Data[skillTextBase] = 5;
            rom.Data[skillTextBase + 1] = 0;
            InstallPlainText(rom, 5, "Aliased Skill");
            return rom;
        }

        static ROM MakeFE8UCSkillSysRomWithAliasedOverflowText()
        {
            var rom = MakeFE8URom();
            byte[] sig = { 0x43, 0x53, 0x4B, 0x49, 0x4C, 0x4C, 0x53, 0x59, 0x53, 0x5F, 0x4B, 0x2D, 0x33 };
            Array.Copy(sig, 0, rom.Data, 0xB2A604, sig.Length);
            uint skillInfoBase = 0x2000;
            WriteU32(rom.Data, 0xB2A614, U.toPointer(skillInfoBase));
            rom.Data[skillInfoBase + 4] = 5;
            rom.Data[skillInfoBase + 5] = 0;
            InstallPlainText(rom, 5, "Aliased Skill");
            return rom;
        }

        static ROM MakeFE8NRomWithAliasedOverflowText()
        {
            var rom = MakeFE8JRom();
            byte[] sig = { 0x00, 0x4B, 0x9F, 0x46 };
            Array.Copy(sig, 0, rom.Data, 0x89268, sig.Length);
            uint skillTextBase = 0x2000;
            WriteU32(rom.Data, 0x8927C, U.toPointer(skillTextBase));
            rom.Data[skillTextBase] = 5;
            rom.Data[skillTextBase + 1] = 0;
            InstallPlainText(rom, 5, "Aliased Skill");
            return rom;
        }

        static void InstallPlainText(ROM rom, uint textId, string text)
        {
            const uint textTable = 0x4000;
            const uint textData = 0x5000;
            WriteU32(rom.Data, rom.RomInfo.text_pointer, U.toPointer(textTable));
            WriteU32(rom.Data, textTable + textId * 4, FETextEncode.ConvertPointerToUnHuffmanPatchPointer(U.toPointer(textData)));
            byte[] bytes = System.Text.Encoding.ASCII.GetBytes(text);
            Array.Copy(bytes, 0, rom.Data, textData, bytes.Length);
            rom.Data[textData + (uint)bytes.Length] = 0;
            CoreState.SystemTextEncoder = new HeadlessSystemTextEncoder(rom);
            NameResolver.ClearCache();
        }

        static void WriteU32(byte[] data, uint offset, uint value)
        {
            data[offset + 0] = (byte)(value & 0xFF);
            data[offset + 1] = (byte)((value >> 8) & 0xFF);
            data[offset + 2] = (byte)((value >> 16) & 0xFF);
            data[offset + 3] = (byte)((value >> 24) & 0xFF);
        }

        static void ResetSkillSystemTextBase(PatchDetectionService svc)
        {
            typeof(PatchDetectionService)
                .GetField("_skillSystemTextBase", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(svc, 0u);
        }
    }
}
