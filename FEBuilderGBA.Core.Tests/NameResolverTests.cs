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
        public void GetUnitNameByOneBasedId_NullRom_DistinctFromZeroBasedFallback()
        {
            // GetUnitNameByOneBasedId has an explicit null-ROM short-circuit that
            // returns "???" without delegating into SupportUnitNavigation, so the
            // null-ROM contract differs from the raw helper's catch-block path
            // (which also happens to return "???"). The point of this test is
            // that the 1-based helper never throws on null ROM, and that the
            // uid=1 path on a null ROM produces "???" rather than rebinding into
            // the 0-based table.
            var saved = CoreState.ROM;
            try
            {
                CoreState.ROM = null!;
                NameResolver.ClearCache();
                Assert.Equal("???", NameResolver.GetUnitNameByOneBasedId(1));
                Assert.Equal("???", NameResolver.GetUnitNameByOneBasedId(5));
                Assert.Equal("", NameResolver.GetUnitNameByOneBasedId(0));
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

        /// <summary>
        /// FE6 has a dummy entry at unit-table index 0; the real first unit
        /// (Roy) sits at <c>p32(unit_pointer) + unit_datasize</c>. WinForms
        /// <c>UnitForm.GetUnitName(1)</c> on FE6 returns Roy's name (via
        /// <c>UnitFE6Form.Init</c>'s <c>ReInit</c> branch that shifts the
        /// table base). <c>GetUnitNameByOneBasedId(1)</c> must mirror that —
        /// otherwise FE6 Support Talk / Event lists would surface the
        /// dummy entry's empty/junk name (issue #652/#653 FE6 regression).
        /// </summary>
        [Fact]
        public void GetUnitNameByOneBasedId_FE6_SkipsDummyEntry()
        {
            var savedRom = CoreState.ROM;
            try
            {
                var rom = MakeFE6Rom();
                CoreState.ROM = rom;

                uint rawBase = 0x200000;
                uint dataSize = rom.RomInfo.unit_datasize;
                uint unitPtr = rom.RomInfo.unit_pointer;
                WriteU32(rom.Data, unitPtr, rawBase | 0x08000000);
                // Dummy at raw index 0: textId = 0 → "#0" via raw helper.
                WriteU16(rom.Data, rawBase, 0);
                // Real index 0 at raw index 1: textId = 1 → "???" without decoder.
                WriteU16(rom.Data, rawBase + dataSize, 1);

                NameResolver.ClearCache();

                // 1-based: uid=1 must resolve to the REAL row (raw index 1),
                // NOT the dummy row (raw index 0). The two paths must disagree.
                string oneBased = NameResolver.GetUnitNameByOneBasedId(1);
                string rawZeroBased = NameResolver.GetUnitName(0);
                Assert.NotEqual(rawZeroBased, oneBased);
                Assert.Equal("#0", rawZeroBased);          // raw reads dummy → "#0"
                Assert.NotEqual("#0", oneBased);            // FE6-aware skips dummy
            }
            finally
            {
                CoreState.ROM = savedRom;
                NameResolver.ClearCache();
            }
        }

        // ---- helpers for FE6-specific tests ----
        static void WriteU16(byte[] data, uint addr, ushort value)
        {
            int i = checked((int)addr);
            data[i + 0] = (byte)(value & 0xFF);
            data[i + 1] = (byte)((value >> 8) & 0xFF);
        }

        static void WriteU32(byte[] data, uint addr, uint value)
        {
            int i = checked((int)addr);
            data[i + 0] = (byte)(value & 0xFF);
            data[i + 1] = (byte)((value >> 8) & 0xFF);
            data[i + 2] = (byte)((value >> 16) & 0xFF);
            data[i + 3] = (byte)((value >> 24) & 0xFF);
        }

        static ROM MakeFE6Rom()
        {
            var rom = new ROM();
            rom.LoadLow("test.gba", new byte[0x1000000], "AFEJ01");
            Assert.NotNull(rom.RomInfo);
            Assert.Equal(6, rom.RomInfo.version);
            return rom;
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
