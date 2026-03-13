using FEBuilderGBA.Avalonia.Services;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    public class PatchDetectionServiceDefaultTests
    {
        [Fact]
        public void Instance_IsSingleton()
        {
            var a = PatchDetectionService.Instance;
            var b = PatchDetectionService.Instance;
            Assert.Same(a, b);
        }

        [Fact]
        public void DefaultState_NoSkillSystem()
        {
            // Without ROM loaded and Refresh called, defaults should be None
            Assert.Equal(PatchDetectionService.SkillSystemType.None,
                PatchDetectionService.Instance.SkillSystem);
        }

        [Fact]
        public void DefaultState_NoMagicSplit()
        {
            Assert.Equal(PatchDetectionService.MagicSplitType.None,
                PatchDetectionService.Instance.MagicSplit);
        }

        [Fact]
        public void HasSkillSystem_FalseByDefault()
        {
            Assert.False(PatchDetectionService.Instance.HasSkillSystem);
        }

        [Fact]
        public void VennouWeaponLock_FalseByDefault()
        {
            Assert.False(PatchDetectionService.Instance.VennouWeaponLock);
        }

        [Fact]
        public void SkillSystemsClassTypeRework_FalseByDefault()
        {
            Assert.False(PatchDetectionService.Instance.SkillSystemsClassTypeRework);
        }

        [Fact]
        public void ItemEffectRange_FalseByDefault()
        {
            Assert.False(PatchDetectionService.Instance.ItemEffectRange);
        }

        [Fact]
        public void BG256Color_FalseByDefault()
        {
            Assert.False(PatchDetectionService.Instance.BG256Color);
        }

        [Fact]
        public void AntiHuffman_FalseByDefault()
        {
            Assert.False(PatchDetectionService.Instance.AntiHuffman);
        }

        [Fact]
        public void PortraitExtends_NoneByDefault()
        {
            Assert.Equal(PatchDetectionService.PortraitExtendsType.None,
                PatchDetectionService.Instance.PortraitExtends);
        }

        [Fact]
        public void DrawFont_NoByDefault()
        {
            Assert.Equal(PatchDetection.draw_font_enum.NO,
                PatchDetectionService.Instance.DrawFont);
        }

        [Fact]
        public void TextEngineRework_NoByDefault()
        {
            Assert.Equal(PatchDetection.TextEngineRework_enum.NO,
                PatchDetectionService.Instance.TextEngineRework);
        }
    }
}
