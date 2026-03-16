using FEBuilderGBA.Core;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    public class WeaponRankUtilTests
    {
        [Theory]
        [InlineData(0u, "-")]
        [InlineData(1u, "E")]
        [InlineData(30u, "E")]
        [InlineData(31u, "D")]
        [InlineData(70u, "D")]
        [InlineData(71u, "C")]
        [InlineData(120u, "C")]
        [InlineData(121u, "B")]
        [InlineData(150u, "B")]
        [InlineData(151u, "A")]
        [InlineData(180u, "A")]
        [InlineData(181u, "S")]
        [InlineData(255u, "S")]
        public void GetRankLetter_ReturnsCorrectGrade(uint value, string expected)
        {
            Assert.Equal(expected, WeaponRankUtil.GetRankLetter(value));
        }
    }
}
