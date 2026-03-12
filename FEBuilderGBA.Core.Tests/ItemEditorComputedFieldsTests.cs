using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Tests for the computed fields logic used in ItemEditorViewModel.
    /// Tests the pure calculation logic without requiring Avalonia dependency.
    /// </summary>
    public class ItemEditorComputedFieldsTests
    {
        [Theory]
        [InlineData(0, 0, 0, 0, 0)]
        [InlineData(30, 100, 3000, 1500, 4500)]
        [InlineData(1, 1, 1, 0, 1)]       // 1*1=1, 1/2=0, (uint)(1*1.5)=1
        [InlineData(45, 600, 27000, 13500, 40500)]
        [InlineData(255, 65535, 16711425, 8355712, 25067137)] // max byte * max u16
        public void ShopPrice_Calculations(uint uses, uint price, uint expectedBuy, uint expectedSell, uint expectedForge)
        {
            uint total = uses * price;
            uint buy = total;
            uint sell = total / 2;
            uint forge = (uint)(total * 1.5);

            Assert.Equal(expectedBuy, buy);
            Assert.Equal(expectedSell, sell);
            Assert.Equal(expectedForge, forge);
        }

        [Fact]
        public void NullPointerWarning_ShownWhenPtrZeroAndIndexPositive()
        {
            uint statBonusesPtr = 0;
            uint effectivenessPtr = 0;
            uint itemIndex = 5;

            bool showAllocStatBonuses = statBonusesPtr == 0 && itemIndex > 0;
            bool showAllocEffectiveness = effectivenessPtr == 0 && itemIndex > 0;

            Assert.True(showAllocStatBonuses);
            Assert.True(showAllocEffectiveness);
        }

        [Fact]
        public void NullPointerWarning_HiddenForItemZero()
        {
            uint statBonusesPtr = 0;
            uint effectivenessPtr = 0;
            uint itemIndex = 0;

            bool showAllocStatBonuses = statBonusesPtr == 0 && itemIndex > 0;
            bool showAllocEffectiveness = effectivenessPtr == 0 && itemIndex > 0;

            Assert.False(showAllocStatBonuses);
            Assert.False(showAllocEffectiveness);
        }

        [Fact]
        public void NullPointerWarning_HiddenWhenPtrNonZero()
        {
            uint statBonusesPtr = 0x08123456;
            uint effectivenessPtr = 0x08234567;
            uint itemIndex = 5;

            bool showAllocStatBonuses = statBonusesPtr == 0 && itemIndex > 0;
            bool showAllocEffectiveness = effectivenessPtr == 0 && itemIndex > 0;

            Assert.False(showAllocStatBonuses);
            Assert.False(showAllocEffectiveness);
        }

        [Fact]
        public void StatBonusPreview_SByteCast()
        {
            // Simulate ROM bytes: 0xFF = -1 as sbyte, 0x05 = +5
            byte[] romBytes = { 0xFF, 0x05, 0x00, 0xFE, 0x03, 0x80, 0x01, 0x02, 0xFD };
            int[] expected  = { -1,   5,    0,    -2,   3,    -128, 1,    2,    -3 };

            for (int i = 0; i < 9; i++)
            {
                int value = (sbyte)romBytes[i];
                Assert.Equal(expected[i], value);
            }
        }
    }
}
