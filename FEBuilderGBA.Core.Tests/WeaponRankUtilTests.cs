using FEBuilderGBA.Core;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    public class WeaponRankUtilTests
    {
        // Default (FE7/FE8) thresholds: 1-30=E, 31-70=D, 71-120=C, 121-180=B, 181-250=A, 251+=S
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
        [InlineData(151u, "B")]
        [InlineData(180u, "B")]
        [InlineData(181u, "A")]
        [InlineData(250u, "A")]
        [InlineData(251u, "S")]
        [InlineData(255u, "S")]
        public void GetRankLetter_ReturnsCorrectGrade(uint value, string expected)
        {
            Assert.Equal(expected, WeaponRankUtil.GetRankLetter(value));
        }

        // FE7/FE8 (romVersion=7 or 8): same thresholds as default (1-30=E, 31-70=D, 71-120=C, 121-180=B, 181-250=A, 251+=S)
        [Theory]
        [InlineData(0u, 7, "-")]
        [InlineData(1u, 7, "E")]
        [InlineData(30u, 7, "E")]
        [InlineData(31u, 7, "D")]
        [InlineData(70u, 7, "D")]
        [InlineData(71u, 7, "C")]
        [InlineData(120u, 7, "C")]
        [InlineData(121u, 7, "B")]
        [InlineData(180u, 7, "B")]
        [InlineData(181u, 7, "A")]
        [InlineData(250u, 7, "A")]
        [InlineData(251u, 7, "S")]
        [InlineData(255u, 7, "S")]
        [InlineData(0u, 8, "-")]
        [InlineData(31u, 8, "D")]
        [InlineData(251u, 8, "S")]
        public void GetRankLetter_FE7AndFE8_UsesDefaultThresholds(uint value, int romVersion, string expected)
        {
            Assert.Equal(expected, WeaponRankUtil.GetRankLetter(value, romVersion));
        }

        // FE6 (romVersion=6): different thresholds — 1-50=E, 51-100=D, 101-150=C, 151-200=B, 201-250=A, 251+=S
        [Theory]
        [InlineData(0u, "-")]
        [InlineData(1u, "E")]
        [InlineData(50u, "E")]
        [InlineData(51u, "D")]
        [InlineData(100u, "D")]
        [InlineData(101u, "C")]
        [InlineData(150u, "C")]
        [InlineData(151u, "B")]
        [InlineData(200u, "B")]
        [InlineData(201u, "A")]
        [InlineData(250u, "A")]
        [InlineData(251u, "S")]
        [InlineData(255u, "S")]
        public void GetRankLetter_FE6_UsesFE6Thresholds(uint value, string expected)
        {
            Assert.Equal(expected, WeaponRankUtil.GetRankLetter(value, 6));
        }

        // Boundary values around FE6/FE7 difference: WEXP=40 -> FE6=E, FE7=D
        [Fact]
        public void GetRankLetter_BoundaryDivergence_FE6vsFE7_AtWEXP40()
        {
            Assert.Equal("E", WeaponRankUtil.GetRankLetter(40u, 6));
            Assert.Equal("D", WeaponRankUtil.GetRankLetter(40u, 7));
            Assert.Equal("D", WeaponRankUtil.GetRankLetter(40u, 8));
        }

        // Boundary value where FE6 differs from FE7: WEXP=75 -> FE6=D (51-100), FE7=C (71-120)
        [Fact]
        public void GetRankLetter_BoundaryDivergence_FE6vsFE7_AtWEXP75()
        {
            Assert.Equal("D", WeaponRankUtil.GetRankLetter(75u, 6));
            Assert.Equal("C", WeaponRankUtil.GetRankLetter(75u, 7));
        }

        // Another divergence: WEXP=130 -> FE6=C (101-150), FE7=B (121-180)
        [Fact]
        public void GetRankLetter_BoundaryDivergence_FE6vsFE7_AtWEXP130()
        {
            Assert.Equal("C", WeaponRankUtil.GetRankLetter(130u, 6));
            Assert.Equal("B", WeaponRankUtil.GetRankLetter(130u, 7));
        }
    }
}
