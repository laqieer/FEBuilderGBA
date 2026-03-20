using FEBuilderGBA.Avalonia.Services;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Tests that ability flag label arrays match the config/data/unitclass_checkbox_FE*.en.txt
    /// definitions. The config format is {byte}{bit}=Label where byte=1-4 and bit=1-8
    /// map to bit 0 (0x01) through bit 7 (0x80).
    /// </summary>
    public class AbilityFlagNamesTests
    {
        [Fact]
        public void Ability1_Has8Entries()
        {
            Assert.Equal(8, AbilityFlagNames.Ability1.Length);
        }

        [Fact]
        public void Ability2_Has8Entries()
        {
            Assert.Equal(8, AbilityFlagNames.Ability2.Length);
        }

        [Fact]
        public void Ability3_FE6_Has8Entries()
        {
            Assert.Equal(8, AbilityFlagNames.Ability3_FE6.Length);
        }

        [Fact]
        public void Ability3_FE7_Has8Entries()
        {
            Assert.Equal(8, AbilityFlagNames.Ability3_FE7.Length);
        }

        [Fact]
        public void Ability3_FE8_Has8Entries()
        {
            Assert.Equal(8, AbilityFlagNames.Ability3_FE8.Length);
        }

        [Fact]
        public void Ability4_FE6_Has8Entries()
        {
            Assert.Equal(8, AbilityFlagNames.Ability4_FE6.Length);
        }

        [Fact]
        public void Ability4_FE7_Has8Entries()
        {
            Assert.Equal(8, AbilityFlagNames.Ability4_FE7.Length);
        }

        [Fact]
        public void Ability4_FE8_Has8Entries()
        {
            Assert.Equal(8, AbilityFlagNames.Ability4_FE8.Length);
        }

        // Byte 1 labels match config (same for all versions):
        // 01=Mounted Aid Calc, 02=Canto, 03=Steal, 04=Thief Skill,
        // 05=Dance (Dancer), 06=Play (Bard), 07=Critical Boost, 08=Ballista Access
        [Theory]
        [InlineData(0, "Mounted Aid Calc")]
        [InlineData(1, "Canto")]
        [InlineData(2, "Steal")]
        [InlineData(3, "Thief Skill")]
        [InlineData(4, "Dance (Dancer)")]
        [InlineData(5, "Play (Bard)")]
        [InlineData(6, "Critical Boost")]
        [InlineData(7, "Ballista Access")]
        public void Ability1_LabelsMatchConfig(int bit, string expected)
        {
            Assert.Equal(expected, AbilityFlagNames.Ability1[bit]);
        }

        // Byte 2 labels match config (same for all versions):
        // 11=Promoted, 12=Supply, 13=Cavalry Icon, 14=Wyvern Icon,
        // 15=Pegasus Icon, 16=Lord, 17=Female, 18=Boss
        [Theory]
        [InlineData(0, "Promoted")]
        [InlineData(1, "Supply")]
        [InlineData(2, "Cavalry Icon")]
        [InlineData(3, "Wyvern Icon")]
        [InlineData(4, "Pegasus Icon")]
        [InlineData(5, "Lord")]
        [InlineData(6, "Female")]
        [InlineData(7, "Boss")]
        public void Ability2_LabelsMatchConfig(int bit, string expected)
        {
            Assert.Equal(expected, AbilityFlagNames.Ability2[bit]);
        }

        // FE8 byte 3 labels:
        // 21=Weapon Lock 1, 22=Myrmidon/Swordmaster, 23=Monster Weapons,
        // 24=Max Level 10, 25=Disable Select, 26=Tri Attack 1, 27=Tri Attack 2, 28=NPC
        [Theory]
        [InlineData(0, "Weapon Lock 1")]
        [InlineData(1, "Myrmidon/Swordmaster")]
        [InlineData(2, "Monster Weapons")]
        [InlineData(3, "Max Level 10")]
        [InlineData(4, "Disable Unit Select")]
        [InlineData(5, "Triangle Attack 1")]
        [InlineData(6, "Triangle Attack 2")]
        [InlineData(7, "NPC")]
        public void Ability3_FE8_LabelsMatchConfig(int bit, string expected)
        {
            Assert.Equal(expected, AbilityFlagNames.Ability3_FE8[bit]);
        }

        // FE6 byte 3 labels:
        // 21=Roy Lock, 22=Myrmidon/SM, 23=Manakete Lock, 24=Zephiel Lock,
        // 25=Disable Select, 26=Tri Attack 1, 27=Tri Attack 2, 28=NPC
        [Theory]
        [InlineData(0, "Roy Lock")]
        [InlineData(1, "Myrmidon/Swordmaster")]
        [InlineData(2, "Manakete Lock")]
        [InlineData(3, "Zephiel Lock")]
        public void Ability3_FE6_LabelsMatchConfig(int bit, string expected)
        {
            Assert.Equal(expected, AbilityFlagNames.Ability3_FE6[bit]);
        }

        // FE7 byte 3 labels:
        // 21=Weapon Lock 1, 22=Myrmidon/SM, 23=Manakete Lock, 24=Morphs
        [Theory]
        [InlineData(0, "Weapon Lock 1")]
        [InlineData(2, "Manakete Lock")]
        [InlineData(3, "Morphs")]
        public void Ability3_FE7_LabelsMatchConfig(int bit, string expected)
        {
            Assert.Equal(expected, AbilityFlagNames.Ability3_FE7[bit]);
        }

        // FE8 byte 4 labels:
        // 31=Do Not Grant Exp, 32=Silencer/Lethality, 33=Magic Seal,
        // 34=Summoning, 35=Eirika Lock, 36=Ephraim Lock
        [Theory]
        [InlineData(0, "Do Not Grant Exp")]
        [InlineData(1, "Silencer/Lethality")]
        [InlineData(2, "Magic Seal")]
        [InlineData(3, "Summoning")]
        [InlineData(4, "Eirika Lock")]
        [InlineData(5, "Ephraim Lock")]
        public void Ability4_FE8_LabelsMatchConfig(int bit, string expected)
        {
            Assert.Equal(expected, AbilityFlagNames.Ability4_FE8[bit]);
        }

        // FE7 byte 4 labels:
        // 31=Disable Exp Gain, 32=Silencer/Lethality, 33=Magic Seal,
        // 34=Droppable Item, 35=Eliwood Lock, 36=Hector Lock, 37=Lyndis Lock, 38=Athos Lock
        [Theory]
        [InlineData(0, "Disable Exp Gain")]
        [InlineData(3, "Droppable Item")]
        [InlineData(4, "Eliwood Lock")]
        [InlineData(5, "Hector Lock")]
        [InlineData(6, "Lyndis Lock")]
        [InlineData(7, "Athos Lock")]
        public void Ability4_FE7_LabelsMatchConfig(int bit, string expected)
        {
            Assert.Equal(expected, AbilityFlagNames.Ability4_FE7[bit]);
        }

        // FE6 byte 4: only bit 0 is known
        [Fact]
        public void Ability4_FE6_Bit0_IsDisableExpGain()
        {
            Assert.Equal("Disable Exp Gain", AbilityFlagNames.Ability4_FE6[0]);
        }

        // GetAbilityNames version dispatch
        [Theory]
        [InlineData(6, 1)]
        [InlineData(7, 1)]
        [InlineData(8, 1)]
        public void GetAbilityNames_Byte1_ReturnsSameForAllVersions(int version, int byteIndex)
        {
            var names = AbilityFlagNames.GetAbilityNames(version, byteIndex);
            Assert.Same(AbilityFlagNames.Ability1, names);
        }

        [Theory]
        [InlineData(6, 2)]
        [InlineData(7, 2)]
        [InlineData(8, 2)]
        public void GetAbilityNames_Byte2_ReturnsSameForAllVersions(int version, int byteIndex)
        {
            var names = AbilityFlagNames.GetAbilityNames(version, byteIndex);
            Assert.Same(AbilityFlagNames.Ability2, names);
        }

        [Fact]
        public void GetAbilityNames_Byte3_FE6_ReturnsCorrectArray()
        {
            Assert.Same(AbilityFlagNames.Ability3_FE6, AbilityFlagNames.GetAbilityNames(6, 3));
        }

        [Fact]
        public void GetAbilityNames_Byte3_FE7_ReturnsCorrectArray()
        {
            Assert.Same(AbilityFlagNames.Ability3_FE7, AbilityFlagNames.GetAbilityNames(7, 3));
        }

        [Fact]
        public void GetAbilityNames_Byte3_FE8_ReturnsCorrectArray()
        {
            Assert.Same(AbilityFlagNames.Ability3_FE8, AbilityFlagNames.GetAbilityNames(8, 3));
        }

        [Fact]
        public void GetAbilityNames_Byte4_FE6_ReturnsCorrectArray()
        {
            Assert.Same(AbilityFlagNames.Ability4_FE6, AbilityFlagNames.GetAbilityNames(6, 4));
        }

        [Fact]
        public void GetAbilityNames_Byte4_FE7_ReturnsCorrectArray()
        {
            Assert.Same(AbilityFlagNames.Ability4_FE7, AbilityFlagNames.GetAbilityNames(7, 4));
        }

        [Fact]
        public void GetAbilityNames_Byte4_FE8_ReturnsCorrectArray()
        {
            Assert.Same(AbilityFlagNames.Ability4_FE8, AbilityFlagNames.GetAbilityNames(8, 4));
        }

        // Backward-compatibility aliases
        [Fact]
        public void UnitAbility1_SameAsAbility1()
        {
            Assert.Same(AbilityFlagNames.Ability1, AbilityFlagNames.UnitAbility1);
        }

        [Fact]
        public void UnitAbility2_SameAsAbility2()
        {
            Assert.Same(AbilityFlagNames.Ability2, AbilityFlagNames.UnitAbility2);
        }

        [Fact]
        public void ClassAbility1_SameAsAbility1()
        {
            Assert.Same(AbilityFlagNames.Ability1, AbilityFlagNames.ClassAbility1);
        }

        [Fact]
        public void ClassAbility2_SameAsAbility2()
        {
            Assert.Same(AbilityFlagNames.Ability2, AbilityFlagNames.ClassAbility2);
        }
    }
}
