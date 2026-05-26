using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class NameResolverTests
    {
        [Fact]
        public void GetTextById_ZeroReturnsEmpty()
        {
            Assert.Equal("", NameResolver.GetTextById(0));
        }

        [Fact]
        public void GetTextById_NullRom_ReturnsFallback()
        {
            // FETextDecode.Direct will fail if no ROM is loaded
            var saved = CoreState.ROM;
            try
            {
                CoreState.ROM = null!;
                string result = NameResolver.GetTextById(1);
                Assert.Equal("???", result);
            }
            finally
            {
                CoreState.ROM = saved;
            }
        }

        [Fact]
        public void GetUnitName_NullRom_ReturnsFallback()
        {
            var saved = CoreState.ROM;
            try
            {
                CoreState.ROM = null!;
                NameResolver.ClearCache();
                string result = NameResolver.GetUnitName(1);
                Assert.Equal("???", result);
            }
            finally
            {
                CoreState.ROM = saved;
            }
        }

        // -------------------------------------------------------------
        // GetUnitNameByOneBasedId — 1-based unit ID semantics (#652 #653)
        // -------------------------------------------------------------

        [Fact]
        public void GetUnitNameByOneBasedId_Zero_ReturnsEmpty()
        {
            // Matches WinForms UnitForm.GetUnitName(uid) early-return on uid == 0.
            NameResolver.ClearCache();
            Assert.Equal("", NameResolver.GetUnitNameByOneBasedId(0));
        }

        [Fact]
        public void GetUnitNameByOneBasedId_DelegatesToZeroBasedMinusOne()
        {
            // Contract: GetUnitNameByOneBasedId(uid) for uid > 0 must equal
            // GetUnitName(uid - 1). This is the core off-by-one fix — passing a
            // ROM-stored 1-based unit ID directly to GetUnitName mis-indexes the
            // unit table by 1 (issues #652, #653). The new helper subtracts
            // internally so the names align with the displayed hex prefix.
            var saved = CoreState.ROM;
            try
            {
                CoreState.ROM = null!;
                NameResolver.ClearCache();
                // Both helpers should produce the same fallback string when ROM
                // is null (??? from the catch). The point is they take the
                // SAME path through ResolveUnitName(0). Compare across multiple
                // values to pin the subtract-1 contract.
                for (uint uid = 1; uid <= 5; uid++)
                {
                    string viaOneBased = NameResolver.GetUnitNameByOneBasedId(uid);
                    string viaZeroBased = NameResolver.GetUnitName(uid - 1);
                    Assert.Equal(viaZeroBased, viaOneBased);
                }
            }
            finally
            {
                CoreState.ROM = saved;
                NameResolver.ClearCache();
            }
        }

        [Fact]
        public void GetUnitNameByOneBasedId_NullRom_ReturnsFallbackForNonZero()
        {
            var saved = CoreState.ROM;
            try
            {
                CoreState.ROM = null!;
                NameResolver.ClearCache();
                Assert.Equal("???", NameResolver.GetUnitNameByOneBasedId(1));
            }
            finally
            {
                CoreState.ROM = saved;
                NameResolver.ClearCache();
            }
        }

        [Fact]
        public void GetClassName_NullRom_ReturnsFallback()
        {
            var saved = CoreState.ROM;
            try
            {
                CoreState.ROM = null!;
                NameResolver.ClearCache();
                string result = NameResolver.GetClassName(1);
                Assert.Equal("???", result);
            }
            finally
            {
                CoreState.ROM = saved;
            }
        }

        [Fact]
        public void GetItemName_NullRom_ReturnsFallback()
        {
            var saved = CoreState.ROM;
            try
            {
                CoreState.ROM = null!;
                NameResolver.ClearCache();
                string result = NameResolver.GetItemName(1);
                Assert.Equal("???", result);
            }
            finally
            {
                CoreState.ROM = saved;
            }
        }

        [Fact]
        public void GetSongName_ReturnsFormattedString()
        {
            NameResolver.ClearCache();
            string result = NameResolver.GetSongName(0x1A);
            Assert.StartsWith("Song", result);
        }

        [Fact]
        public void ClearCache_DoesNotThrow()
        {
            NameResolver.ClearCache();
            NameResolver.GetSongName(1);
            NameResolver.ClearCache();
        }

        [Theory]
        [InlineData("@0501Lord", "Lord")]
        [InlineData("@0501@0102Knight", "Knight")]
        [InlineData("NormalText", "NormalText")]
        [InlineData("", "")]
        [InlineData(null, null)]
        [InlineData("@000C@0010@0001Name", "Name")]
        [InlineData("@000C@0010@0080@0004", "")]
        public void StripControlCodes_RemovesAtCodes(string? input, string? expected)
        {
            Assert.Equal(expected, NameResolver.StripControlCodes(input!));
        }

        [Fact]
        public void GetSkillName_Zero_ReturnsNone()
        {
            NameResolver.ClearCache();
            string result = NameResolver.GetSkillName(0);
            Assert.Equal("(None)", result);
        }

        [Fact]
        public void GetSkillName_NoResolver_ReturnsHexFallback()
        {
            var savedResolver = CoreState.SkillNameResolver;
            try
            {
                CoreState.SkillNameResolver = null;
                NameResolver.ClearCache();
                string result = NameResolver.GetSkillName(0x1A);
                Assert.Equal("Skill 0x1A", result);
            }
            finally
            {
                CoreState.SkillNameResolver = savedResolver;
            }
        }

        [Fact]
        public void GetSkillName_WithResolver_ReturnsResolvedName()
        {
            var savedResolver = CoreState.SkillNameResolver;
            try
            {
                CoreState.SkillNameResolver = id => id == 5 ? "Adept" : null;
                NameResolver.ClearCache();
                string result = NameResolver.GetSkillName(5);
                Assert.Equal("Adept", result);
            }
            finally
            {
                CoreState.SkillNameResolver = savedResolver;
            }
        }

        [Fact]
        public void GetSkillName_ResolverReturnsNull_FallsBackToHex()
        {
            var savedResolver = CoreState.SkillNameResolver;
            try
            {
                CoreState.SkillNameResolver = id => null;
                NameResolver.ClearCache();
                string result = NameResolver.GetSkillName(0xFF);
                Assert.Equal("Skill 0xFF", result);
            }
            finally
            {
                CoreState.SkillNameResolver = savedResolver;
            }
        }

        [Fact]
        public void GetSkillName_CacheWorksAcrossCalls()
        {
            var savedResolver = CoreState.SkillNameResolver;
            try
            {
                int callCount = 0;
                CoreState.SkillNameResolver = id => { callCount++; return "TestSkill"; };
                NameResolver.ClearCache();
                NameResolver.GetSkillName(10);
                NameResolver.GetSkillName(10); // should use cache
                Assert.Equal(1, callCount);
            }
            finally
            {
                CoreState.SkillNameResolver = savedResolver;
            }
        }

        [Fact]
        public void GetPortraitName_ReturnsEmptyWhenNoRom()
        {
            NameResolver.ClearCache();
            var savedRom = CoreState.ROM;
            try
            {
                CoreState.ROM = null;
                string name = NameResolver.GetPortraitName(0);
                Assert.Equal("", name);

                string name2 = NameResolver.GetPortraitName(999);
                Assert.Equal("", name2);
            }
            finally
            {
                CoreState.ROM = savedRom;
                NameResolver.ClearCache();
            }
        }

        [Theory]
        [InlineData(" Lord ", "Lord")]
        [InlineData("\r\nKnight\n", "Knight")]
        [InlineData("\x1FName\x1F", "Name")]
        [InlineData("\u3000Name\u3000", "Name")]
        [InlineData("\0Name\0", "Name")]
        [InlineData("Name\x01\x02", "Name")]
        public void StripControlCodes_TrimsWhitespace(string input, string expected)
        {
            Assert.Equal(expected, NameResolver.StripControlCodes(input));
        }
    }
}
