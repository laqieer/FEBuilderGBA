using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Tests.Unit
{
    public class GrowSimulatorTests
    {
        #region Setter Tests

        [Fact]
        public void SetUnitBase_SetsAllProperties()
        {
            // Arrange
            var sim = new GrowSimulator();

            // Act
            sim.SetUnitBase(5, 20, 8, 7, 9, 5, 3, 6, 4);

            // Assert
            Assert.Equal(5, sim.unit_lv);
            Assert.Equal(20, sim.unit_hp);
            Assert.Equal(8, sim.unit_str);
            Assert.Equal(7, sim.unit_skill);
            Assert.Equal(9, sim.unit_spd);
            Assert.Equal(5, sim.unit_def);
            Assert.Equal(3, sim.unit_res);
            Assert.Equal(6, sim.unit_luck);
            Assert.Equal(4, sim.unit_ext_magic);
        }

        [Fact]
        public void SetClassBase_SetsAllProperties()
        {
            // Arrange
            var sim = new GrowSimulator();

            // Act
            sim.SetClassBase(18, 5, 3, 4, 2, 1, 2);

            // Assert
            Assert.Equal(18, sim.class_hp);
            Assert.Equal(5, sim.class_str);
            Assert.Equal(3, sim.class_skill);
            Assert.Equal(4, sim.class_spd);
            Assert.Equal(2, sim.class_def);
            Assert.Equal(1, sim.class_res);
            Assert.Equal(2, sim.class_ext_magic);
        }

        [Fact]
        public void SetUnitGrow_SetsAllGrowthRates()
        {
            // Arrange
            var sim = new GrowSimulator();

            // Act
            sim.SetUnitGrow(80, 50, 40, 60, 30, 25, 45, 20);

            // Assert
            Assert.Equal(80, sim.unit_grow_hp);
            Assert.Equal(50, sim.unit_grow_str);
            Assert.Equal(40, sim.unit_grow_skill);
            Assert.Equal(60, sim.unit_grow_spd);
            Assert.Equal(30, sim.unit_grow_def);
            Assert.Equal(25, sim.unit_grow_res);
            Assert.Equal(45, sim.unit_grow_luck);
            Assert.Equal(20, sim.unit_grow_ext_magic);
        }

        [Fact]
        public void SetClassGrow_SetsAllGrowthRates()
        {
            // Arrange
            var sim = new GrowSimulator();

            // Act
            sim.SetClassGrow(15, 10, 10, 10, 5, 5, 0, 15);

            // Assert
            Assert.Equal(15, sim.class_grow_hp);
            Assert.Equal(10, sim.class_grow_str);
            Assert.Equal(10, sim.class_grow_skill);
            Assert.Equal(10, sim.class_grow_spd);
            Assert.Equal(5, sim.class_grow_def);
            Assert.Equal(5, sim.class_grow_res);
            Assert.Equal(0, sim.class_grow_luck);
            Assert.Equal(15, sim.class_grow_ext_magic);
        }

        [Fact]
        public void SetUnitLv1_SetsLevelToOne()
        {
            // Arrange
            var sim = new GrowSimulator();
            sim.SetUnitBase(10, 30, 10, 10, 10, 10, 10, 10, 10);

            // Act
            sim.SetUnitLv1();

            // Assert
            Assert.Equal(1, sim.unit_lv);
        }

        #endregion

        #region Grow Method - Basic Tests

        [Fact]
        public void Grow_WithNoGrowth_CombinesUnitAndClassBases()
        {
            // Arrange
            var sim = new GrowSimulator();
            sim.SetUnitBase(1, 18, 5, 6, 7, 3, 2, 4, 1);
            sim.SetClassBase(0, 2, 1, 1, 2, 0, 0);
            sim.SetUnitGrow(0, 0, 0, 0, 0, 0, 0, 0);

            // Act
            sim.Grow(1, GrowSimulator.GrowOptionEnum.UnitGrow);

            // Assert - Should be unit + class bases (no growth applied)
            Assert.Equal(1, sim.sim_lv);
            Assert.Equal(18, sim.sim_hp);
            Assert.Equal(7, sim.sim_str);  // 5 + 2
            Assert.Equal(7, sim.sim_skill); // 6 + 1
            Assert.Equal(8, sim.sim_spd);   // 7 + 1
            Assert.Equal(5, sim.sim_def);   // 3 + 2
            Assert.Equal(2, sim.sim_res);   // 2 + 0
            Assert.Equal(4, sim.sim_luck);  // 4 + 0 (luck doesn't get class bonus)
            Assert.Equal(1, sim.sim_ext_magic); // 1 + 0
        }

        [Fact]
        public void Grow_WithUnitGrow_AppliesGrowthFormula()
        {
            // Arrange
            var sim = new GrowSimulator();
            sim.SetUnitBase(1, 18, 5, 6, 7, 3, 2, 4, 1);
            sim.SetClassBase(0, 0, 0, 0, 0, 0, 0);
            sim.SetUnitGrow(80, 50, 40, 60, 30, 25, 45, 20); // 80% HP growth, etc.

            // Act - Grow from level 1 to level 10 (9 level-ups)
            sim.Grow(10, GrowSimulator.GrowOptionEnum.UnitGrow);

            // Assert
            Assert.Equal(10, sim.sim_lv);
            // Formula: base + round((growth_rate / 100) * level_ups)
            // Note: C# uses banker's rounding (MidpointRounding.ToEven)
            // HP: 18 + round((80/100) * 9) = 18 + round(7.2) = 18 + 7 = 25
            Assert.Equal(25, sim.sim_hp);
            // STR: 5 + round((50/100) * 9) = 5 + round(4.5) = 5 + 4 = 9 (banker's rounding: 4.5 → 4)
            Assert.Equal(9, sim.sim_str);
            // SKILL: 6 + round((40/100) * 9) = 6 + round(3.6) = 6 + 4 = 10
            Assert.Equal(10, sim.sim_skill);
            // SPD: 7 + round((60/100) * 9) = 7 + round(5.4) = 7 + 5 = 12
            Assert.Equal(12, sim.sim_spd);
        }

        [Fact]
        public void Grow_WithClassGrow_UsesClassGrowthRates()
        {
            // Arrange
            var sim = new GrowSimulator();
            sim.SetUnitBase(1, 18, 5, 6, 7, 3, 2, 4, 1);
            sim.SetClassBase(0, 0, 0, 0, 0, 0, 0);
            sim.SetUnitGrow(80, 50, 40, 60, 30, 25, 45, 20); // Unit growths (ignored)
            sim.SetClassGrow(100, 100, 100, 100, 100, 100, 100, 100); // Class growths (used)

            // Act - Grow from level 1 to level 11 (10 level-ups)
            sim.Grow(11, GrowSimulator.GrowOptionEnum.ClassGrow);

            // Assert
            Assert.Equal(11, sim.sim_lv);
            // With 100% growth rates and 10 level-ups: base + round((100/100) * 10) = base + 10
            Assert.Equal(28, sim.sim_hp);     // 18 + 10
            Assert.Equal(15, sim.sim_str);    // 5 + 10
            Assert.Equal(16, sim.sim_skill);  // 6 + 10
            Assert.Equal(17, sim.sim_spd);    // 7 + 10
            Assert.Equal(13, sim.sim_def);    // 3 + 10
            Assert.Equal(12, sim.sim_res);    // 2 + 10
            Assert.Equal(14, sim.sim_luck);   // 4 + 10
            Assert.Equal(11, sim.sim_ext_magic); // 1 + 10
        }

        [Fact]
        public void Grow_WithNoneOption_NoGrowthApplied()
        {
            // Arrange
            var sim = new GrowSimulator();
            sim.SetUnitBase(1, 18, 5, 6, 7, 3, 2, 4, 1);
            sim.SetClassBase(0, 0, 0, 0, 0, 0, 0);
            sim.SetUnitGrow(80, 50, 40, 60, 30, 25, 45, 20);

            // Act - Grow to level 20 with no growth
            sim.Grow(20, GrowSimulator.GrowOptionEnum.None);

            // Assert - Should remain at base values
            Assert.Equal(20, sim.sim_lv);
            Assert.Equal(18, sim.sim_hp);
            Assert.Equal(5, sim.sim_str);
            Assert.Equal(6, sim.sim_skill);
        }

        #endregion

        #region Stat Capping Tests

        [Fact]
        public void Grow_CapsStatsAt255()
        {
            // Arrange - Use extreme values to guarantee capping
            var sim = new GrowSimulator();
            sim.SetUnitBase(1, 10, 10, 10, 10, 10, 10, 10, 10);
            sim.SetClassBase(0, 0, 0, 0, 0, 0, 0);
            sim.SetUnitGrow(255, 255, 255, 255, 255, 255, 255, 255); // Max growth rates

            // Act - Grow to level 255 (254 level-ups)
            // With 255% growth and 254 level-ups: 10 + round((255/100) * 254) = 10 + round(647.7) = 10 + 648 = 658
            sim.Grow(255, GrowSimulator.GrowOptionEnum.UnitGrow);

            // Assert - All stats should be capped at 255
            Assert.True(sim.sim_hp <= 255);
            Assert.True(sim.sim_str <= 255);
            Assert.True(sim.sim_skill <= 255);
            Assert.True(sim.sim_spd <= 255);
            Assert.True(sim.sim_def <= 255);
            Assert.True(sim.sim_res <= 255);
            Assert.True(sim.sim_luck <= 255);
            Assert.True(sim.sim_ext_magic <= 255);
        }

        [Fact]
        public void Grow_WithExactly256_DoesNotCap()
        {
            // Arrange - Carefully crafted to reach exactly 256
            var sim = new GrowSimulator();
            sim.SetUnitBase(1, 156, 0, 0, 0, 0, 0, 0, 0);
            sim.SetClassBase(0, 0, 0, 0, 0, 0, 0);
            sim.SetUnitGrow(100, 0, 0, 0, 0, 0, 0, 0); // 100% HP growth

            // Act - 100 level-ups: 156 + 100 = 256
            sim.Grow(101, GrowSimulator.GrowOptionEnum.UnitGrow);

            // Assert - Code checks "if (sim_hp > 256)" so 256 is NOT capped
            Assert.Equal(256, sim.sim_hp);
        }

        [Fact]
        public void Grow_WithNegativeStats_CapsToZero()
        {
            // Arrange - Start with negative values (shouldn't happen in practice)
            var sim = new GrowSimulator();
            sim.SetUnitBase(1, -10, -5, -3, -2, -1, -4, -6, -2);
            sim.SetClassBase(0, 0, 0, 0, 0, 0, 0);
            sim.SetUnitGrow(0, 0, 0, 0, 0, 0, 0, 0);

            // Act
            sim.Grow(1, GrowSimulator.GrowOptionEnum.None);

            // Assert - All negative stats should be capped to 0
            Assert.Equal(0, sim.sim_hp);
            Assert.Equal(0, sim.sim_str);
            Assert.Equal(0, sim.sim_skill);
            Assert.Equal(0, sim.sim_spd);
            Assert.Equal(0, sim.sim_def);
            Assert.Equal(0, sim.sim_res);
            Assert.Equal(0, sim.sim_luck);
            Assert.Equal(0, sim.sim_ext_magic);
        }

        #endregion

        #region Growth Rate Calculation Tests

        [Fact]
        public void Grow_WithZeroGrowthRates_NoStatsIncrease()
        {
            // Arrange
            var sim = new GrowSimulator();
            sim.SetUnitBase(1, 18, 5, 6, 7, 3, 2, 4, 1);
            sim.SetClassBase(0, 0, 0, 0, 0, 0, 0);
            sim.SetUnitGrow(0, 0, 0, 0, 0, 0, 0, 0);

            // Act
            sim.Grow(20, GrowSimulator.GrowOptionEnum.UnitGrow);

            // Assert - No growth should occur
            Assert.Equal(18, sim.sim_hp);
            Assert.Equal(5, sim.sim_str);
            Assert.Equal(6, sim.sim_skill);
        }

        [Fact]
        public void Grow_With50PercentGrowth_RoundsCorrectly()
        {
            // Arrange
            var sim = new GrowSimulator();
            sim.SetUnitBase(1, 10, 10, 10, 10, 10, 10, 10, 10);
            sim.SetClassBase(0, 0, 0, 0, 0, 0, 0);
            sim.SetUnitGrow(50, 50, 50, 50, 50, 50, 50, 50); // 50% growth

            // Act - 10 level-ups: round((50/100) * 10) = round(5.0) = 5
            sim.Grow(11, GrowSimulator.GrowOptionEnum.UnitGrow);

            // Assert
            Assert.Equal(15, sim.sim_hp);  // 10 + 5
            Assert.Equal(15, sim.sim_str); // 10 + 5
        }

        [Fact]
        public void Grow_WithOddNumberOfLevels_RoundsCorrectly()
        {
            // Arrange
            var sim = new GrowSimulator();
            sim.SetUnitBase(1, 10, 10, 10, 10, 10, 10, 10, 10);
            sim.SetClassBase(0, 0, 0, 0, 0, 0, 0);
            sim.SetUnitGrow(80, 80, 80, 80, 80, 80, 80, 80); // 80% growth

            // Act - 5 level-ups: round((80/100) * 5) = round(4.0) = 4
            sim.Grow(6, GrowSimulator.GrowOptionEnum.UnitGrow);

            // Assert
            Assert.Equal(14, sim.sim_hp); // 10 + 4
        }

        [Fact]
        public void Grow_CalculatesSumGrowthRate_WithUnitGrow()
        {
            // Arrange
            var sim = new GrowSimulator();
            sim.SetUnitBase(1, 10, 10, 10, 10, 10, 10, 10, 10);
            sim.SetClassBase(0, 0, 0, 0, 0, 0, 0);
            sim.SetUnitGrow(80, 50, 40, 60, 30, 25, 45, 20); // Sum = 350

            // Act
            sim.Grow(5, GrowSimulator.GrowOptionEnum.UnitGrow);

            // Assert
            Assert.Equal(350, sim.sim_sum_grow_rate);
        }

        [Fact]
        public void Grow_CalculatesSumGrowthRate_WithClassGrow()
        {
            // Arrange
            var sim = new GrowSimulator();
            sim.SetUnitBase(1, 10, 10, 10, 10, 10, 10, 10, 10);
            sim.SetClassBase(0, 0, 0, 0, 0, 0, 0);
            sim.SetUnitGrow(80, 50, 40, 60, 30, 25, 45, 20);
            sim.SetClassGrow(15, 10, 10, 10, 5, 5, 0, 15); // Sum = 70

            // Act
            sim.Grow(5, GrowSimulator.GrowOptionEnum.ClassGrow);

            // Assert - Should use class growth sum
            Assert.Equal(70, sim.sim_sum_grow_rate);
        }

        [Fact]
        public void Grow_CalculatesSumGrowthRate_WithNone()
        {
            // Arrange
            var sim = new GrowSimulator();
            sim.SetUnitBase(1, 10, 10, 10, 10, 10, 10, 10, 10);
            sim.SetClassBase(0, 0, 0, 0, 0, 0, 0);
            sim.SetUnitGrow(80, 50, 40, 60, 30, 25, 45, 20);

            // Act
            sim.Grow(5, GrowSimulator.GrowOptionEnum.None);

            // Assert - Should be 0 when no growth option
            Assert.Equal(0, sim.sim_sum_grow_rate);
        }

        #endregion

        #region Level Progression Tests

        [Fact]
        public void Grow_ToSameLevel_NoAdditionalGrowth()
        {
            // Arrange
            var sim = new GrowSimulator();
            sim.SetUnitBase(5, 25, 10, 9, 11, 7, 5, 8, 6);
            sim.SetClassBase(0, 0, 0, 0, 0, 0, 0);
            sim.SetUnitGrow(80, 50, 40, 60, 30, 25, 45, 20);

            // Act - Grow to same level
            sim.Grow(5, GrowSimulator.GrowOptionEnum.UnitGrow);

            // Assert - Should remain at base stats
            Assert.Equal(5, sim.sim_lv);
            Assert.Equal(25, sim.sim_hp);
            Assert.Equal(10, sim.sim_str);
        }

        [Fact]
        public void Grow_ToLowerLevel_NoGrowth()
        {
            // Arrange
            var sim = new GrowSimulator();
            sim.SetUnitBase(10, 30, 15, 12, 14, 10, 8, 12, 9);
            sim.SetClassBase(0, 0, 0, 0, 0, 0, 0);
            sim.SetUnitGrow(80, 50, 40, 60, 30, 25, 45, 20);

            // Act - Try to grow to lower level
            sim.Grow(5, GrowSimulator.GrowOptionEnum.UnitGrow);

            // Assert - No growth applied, but sim_lv is still set to target (5 in this case is lower than unit_lv 10)
            // Code: if (this.sim_lv < lv) applies growth, then sim_lv = lv at end
            Assert.Equal(10, sim.sim_lv); // sim_lv starts at unit_lv (10), doesn't change if target < current
            Assert.Equal(30, sim.sim_hp);
            Assert.Equal(15, sim.sim_str);
        }

        [Fact]
        public void Grow_FromLevel1ToLevel20_CalculatesCorrectly()
        {
            // Arrange
            var sim = new GrowSimulator();
            sim.SetUnitBase(1, 18, 5, 6, 7, 3, 2, 4, 1);
            sim.SetClassBase(0, 0, 0, 0, 0, 0, 0);
            sim.SetUnitGrow(100, 100, 100, 100, 100, 100, 100, 100); // 100% growth

            // Act - 19 level-ups
            sim.Grow(20, GrowSimulator.GrowOptionEnum.UnitGrow);

            // Assert
            Assert.Equal(20, sim.sim_lv);
            // Each stat gains exactly 19 points (100% growth, 19 level-ups)
            Assert.Equal(37, sim.sim_hp);     // 18 + 19
            Assert.Equal(24, sim.sim_str);    // 5 + 19
            Assert.Equal(25, sim.sim_skill);  // 6 + 19
            Assert.Equal(26, sim.sim_spd);    // 7 + 19
            Assert.Equal(22, sim.sim_def);    // 3 + 19
            Assert.Equal(21, sim.sim_res);    // 2 + 19
            Assert.Equal(23, sim.sim_luck);   // 4 + 19
            Assert.Equal(20, sim.sim_ext_magic); // 1 + 19
        }

        #endregion

        #region Edge Cases

        [Fact]
        public void Grow_WithMaxGrowthRates_DoesNotOverflow()
        {
            // Arrange - Extreme growth rates
            var sim = new GrowSimulator();
            sim.SetUnitBase(1, 10, 10, 10, 10, 10, 10, 10, 10);
            sim.SetClassBase(0, 0, 0, 0, 0, 0, 0);
            sim.SetUnitGrow(255, 255, 255, 255, 255, 255, 255, 255); // Max growth

            // Act - Grow to level 100 (99 level-ups)
            sim.Grow(100, GrowSimulator.GrowOptionEnum.UnitGrow);

            // Assert - Should be capped at 255
            Assert.Equal(255, sim.sim_hp);
            Assert.Equal(255, sim.sim_str);
            Assert.Equal(255, sim.sim_skill);
        }

        [Fact]
        public void Grow_WithClassAndUnitBases_CombinesCorrectly()
        {
            // Arrange
            var sim = new GrowSimulator();
            sim.SetUnitBase(1, 18, 5, 6, 7, 3, 2, 4, 1);
            sim.SetClassBase(2, 3, 2, 3, 5, 3, 4);
            sim.SetUnitGrow(0, 0, 0, 0, 0, 0, 0, 0); // No growth

            // Act
            sim.Grow(1, GrowSimulator.GrowOptionEnum.None);

            // Assert - Unit + Class bases
            Assert.Equal(20, sim.sim_hp);     // 18 + 2
            Assert.Equal(8, sim.sim_str);     // 5 + 3
            Assert.Equal(8, sim.sim_skill);   // 6 + 2
            Assert.Equal(10, sim.sim_spd);    // 7 + 3
            Assert.Equal(8, sim.sim_def);     // 3 + 5
            Assert.Equal(5, sim.sim_res);     // 2 + 3
            Assert.Equal(4, sim.sim_luck);    // 4 + 0 (luck doesn't get class bonus)
            Assert.Equal(5, sim.sim_ext_magic); // 1 + 4
        }

        [Fact]
        public void Grow_MultipleTimesWithDifferentOptions_WorksCorrectly()
        {
            // Arrange
            var sim = new GrowSimulator();
            sim.SetUnitBase(1, 18, 5, 6, 7, 3, 2, 4, 1);
            sim.SetClassBase(0, 0, 0, 0, 0, 0, 0);
            sim.SetUnitGrow(80, 50, 40, 60, 30, 25, 45, 20);
            sim.SetClassGrow(100, 100, 100, 100, 100, 100, 100, 100);

            // Act - First grow with unit growth
            sim.Grow(10, GrowSimulator.GrowOptionEnum.UnitGrow);
            int firstHP = sim.sim_hp;

            // Act - Second grow with class growth (should recalculate from base)
            sim.Grow(10, GrowSimulator.GrowOptionEnum.ClassGrow);

            // Assert - Second calculation should be independent
            Assert.NotEqual(firstHP, sim.sim_hp); // Different growth rates
        }

        [Fact]
        public void Grow_WithZeroBaseStats_WorksCorrectly()
        {
            // Arrange
            var sim = new GrowSimulator();
            sim.SetUnitBase(1, 0, 0, 0, 0, 0, 0, 0, 0);
            sim.SetClassBase(0, 0, 0, 0, 0, 0, 0);
            sim.SetUnitGrow(100, 100, 100, 100, 100, 100, 100, 100);

            // Act - 10 level-ups
            sim.Grow(11, GrowSimulator.GrowOptionEnum.UnitGrow);

            // Assert - Should gain 10 points per stat
            Assert.Equal(10, sim.sim_hp);
            Assert.Equal(10, sim.sim_str);
            Assert.Equal(10, sim.sim_skill);
        }

        #endregion

        #region Rounding Tests

        [Theory]
        [InlineData(33, 3, 1)]  // round((33/100) * 3) = round(0.99) = 1
        [InlineData(33, 6, 2)]  // round((33/100) * 6) = round(1.98) = 2
        [InlineData(67, 3, 2)]  // round((67/100) * 3) = round(2.01) = 2
        [InlineData(75, 4, 3)]  // round((75/100) * 4) = round(3.0) = 3
        public void Grow_RoundsGrowthCorrectly(int growthRate, int levelUps, int expectedGain)
        {
            // Arrange
            var sim = new GrowSimulator();
            sim.SetUnitBase(1, 10, 0, 0, 0, 0, 0, 0, 0);
            sim.SetClassBase(0, 0, 0, 0, 0, 0, 0);
            sim.SetUnitGrow(growthRate, 0, 0, 0, 0, 0, 0, 0);

            // Act
            sim.Grow(1 + levelUps, GrowSimulator.GrowOptionEnum.UnitGrow);

            // Assert
            Assert.Equal(10 + expectedGain, sim.sim_hp);
        }

        [Fact]
        public void Grow_WithFractionalGrowth_Rounds()
        {
            // Arrange
            var sim = new GrowSimulator();
            sim.SetUnitBase(1, 10, 10, 10, 10, 10, 10, 10, 10);
            sim.SetClassBase(0, 0, 0, 0, 0, 0, 0);
            // Growth rates that create fractional results
            sim.SetUnitGrow(45, 55, 65, 35, 85, 95, 25, 15);

            // Act - 7 level-ups
            sim.Grow(8, GrowSimulator.GrowOptionEnum.UnitGrow);

            // Assert - Verify rounding
            // HP: 10 + round((45/100) * 7) = 10 + round(3.15) = 10 + 3 = 13
            Assert.Equal(13, sim.sim_hp);
            // STR: 10 + round((55/100) * 7) = 10 + round(3.85) = 10 + 4 = 14
            Assert.Equal(14, sim.sim_str);
            // SKILL: 10 + round((65/100) * 7) = 10 + round(4.55) = 10 + 5 = 15
            Assert.Equal(15, sim.sim_skill);
        }

        #endregion
    }
}
