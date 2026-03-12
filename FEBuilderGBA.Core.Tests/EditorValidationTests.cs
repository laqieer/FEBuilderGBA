using Xunit;
using System.Collections.Generic;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Tests for the validation logic used in Unit/Item/Class editor ViewModels.
    /// Tests the pure validation rules without requiring Avalonia dependency.
    /// </summary>
    public class EditorValidationTests
    {
        // ---- Unit Validation Logic ----

        static List<string> ValidateUnit(
            uint selectedId, uint classId, uint portraitId, uint level,
            int hp, int str, int skl, int spd, int def, int res, int lck, int con,
            uint growHP, uint growStr, uint growSkl, uint growSpd, uint growDef, uint growRes, uint growLck)
        {
            var warnings = new List<string>();
            if (selectedId > 0)
            {
                if (classId == 0)
                    warnings.Add("No class assigned (ClassId is 0)");
                if (portraitId == 0)
                    warnings.Add("No portrait assigned (PortraitId is 0)");
            }
            if (level == 0)
                warnings.Add("Level is 0");
            if (hp == 0 && str == 0 && skl == 0 && spd == 0 && def == 0 && res == 0 && lck == 0 && con == 0)
                warnings.Add("All base stats are zero");
            if (growHP == 0 && growStr == 0 && growSkl == 0 && growSpd == 0 && growDef == 0 && growRes == 0 && growLck == 0)
                warnings.Add("No growth rates set (all zero)");
            return warnings;
        }

        [Fact]
        public void Unit_ZeroIndex_SkipsClassAndPortraitCheck()
        {
            var w = ValidateUnit(0, 0, 0, 1, 10, 5, 3, 4, 2, 1, 3, 5, 50, 40, 30, 35, 20, 25, 30);
            Assert.DoesNotContain("No class assigned", w);
            Assert.DoesNotContain("No portrait assigned", w);
        }

        [Fact]
        public void Unit_NonZeroIndex_WarnsOnMissingClassAndPortrait()
        {
            var w = ValidateUnit(1, 0, 0, 1, 10, 5, 3, 4, 2, 1, 3, 5, 50, 40, 30, 35, 20, 25, 30);
            Assert.Contains(w, x => x.Contains("No class assigned"));
            Assert.Contains(w, x => x.Contains("No portrait assigned"));
        }

        [Fact]
        public void Unit_LevelZero_Warns()
        {
            var w = ValidateUnit(1, 1, 1, 0, 10, 5, 3, 4, 2, 1, 3, 5, 50, 40, 30, 35, 20, 25, 30);
            Assert.Contains(w, x => x.Contains("Level is 0"));
        }

        [Fact]
        public void Unit_AllBaseStatsZero_Warns()
        {
            var w = ValidateUnit(1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 50, 40, 30, 35, 20, 25, 30);
            Assert.Contains(w, x => x.Contains("All base stats are zero"));
        }

        [Fact]
        public void Unit_AllGrowthsZero_Warns()
        {
            var w = ValidateUnit(1, 1, 1, 1, 10, 5, 3, 4, 2, 1, 3, 5, 0, 0, 0, 0, 0, 0, 0);
            Assert.Contains(w, x => x.Contains("No growth rates set"));
        }

        [Fact]
        public void Unit_ValidData_NoWarnings()
        {
            var w = ValidateUnit(1, 5, 3, 1, 18, 5, 3, 7, 4, 1, 3, 5, 80, 45, 40, 50, 25, 30, 45);
            Assert.Empty(w);
        }

        // ---- Item Validation Logic ----

        static List<string> ValidateItem(
            uint itemIndex, uint nameId, uint uses, uint price,
            uint trait1, uint weaponType, uint might, uint hit, uint range)
        {
            var warnings = new List<string>();
            if (itemIndex > 0 && nameId == 0)
                warnings.Add("No name assigned (NameId is 0)");

            bool isUnbreakable = (trait1 & 0x08) != 0;
            if (uses == 0 && !isUnbreakable && itemIndex > 0)
                warnings.Add("Uses is 0 (item is not unbreakable)");
            if (price > 0 && uses == 0 && !isUnbreakable)
                warnings.Add("Has price but no uses");
            if (weaponType <= 7 && itemIndex > 0 && might == 0 && hit == 0 && range == 0)
                warnings.Add("Weapon type set but no weapon stats (might, hit, range all 0)");

            return warnings;
        }

        [Fact]
        public void Item_ZeroIndex_SkipsNameAndUsesCheck()
        {
            var w = ValidateItem(0, 0, 0, 0, 0, 0, 0, 0, 0);
            Assert.DoesNotContain("No name assigned", w);
            Assert.DoesNotContain("Uses is 0", w);
        }

        [Fact]
        public void Item_NonZeroIndex_WarnsOnMissingName()
        {
            var w = ValidateItem(1, 0, 30, 100, 0, 0, 5, 80, 1);
            Assert.Contains(w, x => x.Contains("No name assigned"));
        }

        [Fact]
        public void Item_ZeroUses_NotUnbreakable_Warns()
        {
            var w = ValidateItem(1, 100, 0, 0, 0, 9, 0, 0, 0);
            Assert.Contains(w, x => x.Contains("Uses is 0"));
        }

        [Fact]
        public void Item_ZeroUses_Unbreakable_NoWarning()
        {
            // Trait1 bit 3 = 0x08 means unbreakable
            var w = ValidateItem(1, 100, 0, 0, 0x08, 9, 0, 0, 0);
            Assert.DoesNotContain("Uses is 0", w);
        }

        [Fact]
        public void Item_PriceButNoUses_Warns()
        {
            var w = ValidateItem(1, 100, 0, 500, 0, 9, 0, 0, 0);
            Assert.Contains(w, x => x.Contains("Has price but no uses"));
        }

        [Fact]
        public void Item_WeaponTypeNoStats_Warns()
        {
            var w = ValidateItem(1, 100, 30, 100, 0, 0, 0, 0, 0);
            Assert.Contains(w, x => x.Contains("Weapon type set but no weapon stats"));
        }

        [Fact]
        public void Item_WeaponTypeWithStats_NoWarning()
        {
            var w = ValidateItem(1, 100, 30, 100, 0, 0, 5, 80, 1);
            Assert.DoesNotContain("Weapon type set but no weapon stats", w);
        }

        [Fact]
        public void Item_ValidData_NoWarnings()
        {
            var w = ValidateItem(1, 100, 30, 500, 0, 0, 8, 90, 1);
            Assert.Empty(w);
        }

        // ---- Class Validation Logic ----

        static List<string> ValidateClass(
            uint classIndex, uint nameId, uint mov,
            uint baseHp, uint baseStr, uint baseSkl, uint baseSpd, uint baseDef, uint baseRes)
        {
            var warnings = new List<string>();
            if (classIndex > 0 && nameId == 0)
                warnings.Add("No name assigned (NameId is 0)");
            if (mov == 0)
                warnings.Add("Movement is 0");
            if (baseHp == 0 && baseStr == 0 && baseSkl == 0 && baseSpd == 0 && baseDef == 0 && baseRes == 0)
                warnings.Add("All base stats are zero");
            return warnings;
        }

        [Fact]
        public void Class_ZeroIndex_SkipsNameCheck()
        {
            var w = ValidateClass(0, 0, 5, 18, 4, 3, 5, 2, 1);
            Assert.DoesNotContain("No name assigned", w);
        }

        [Fact]
        public void Class_NonZeroIndex_WarnsOnMissingName()
        {
            var w = ValidateClass(1, 0, 5, 18, 4, 3, 5, 2, 1);
            Assert.Contains(w, x => x.Contains("No name assigned"));
        }

        [Fact]
        public void Class_ZeroMovement_Warns()
        {
            var w = ValidateClass(1, 100, 0, 18, 4, 3, 5, 2, 1);
            Assert.Contains(w, x => x.Contains("Movement is 0"));
        }

        [Fact]
        public void Class_AllBaseStatsZero_Warns()
        {
            var w = ValidateClass(1, 100, 5, 0, 0, 0, 0, 0, 0);
            Assert.Contains(w, x => x.Contains("All base stats are zero"));
        }

        [Fact]
        public void Class_ValidData_NoWarnings()
        {
            var w = ValidateClass(1, 100, 5, 18, 4, 3, 5, 2, 1);
            Assert.Empty(w);
        }
    }
}
