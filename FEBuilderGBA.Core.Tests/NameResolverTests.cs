using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class NameResolverTests
    {
        // ============================================================
        // GetCustomBattleAnimeName — #1412 FE7 custom-battle-anime label
        // ============================================================

        static void W32(byte[] d, uint o, uint v)
        {
            d[o + 0] = (byte)(v & 0xFF); d[o + 1] = (byte)((v >> 8) & 0xFF);
            d[o + 2] = (byte)((v >> 16) & 0xFF); d[o + 3] = (byte)((v >> 24) & 0xFF);
        }
        static void W16(byte[] d, uint o, ushort v) { d[o + 0] = (byte)(v & 0xFF); d[o + 1] = (byte)((v >> 8) & 0xFF); }

        [Fact]
        public void GetCustomBattleAnimeName_FE7_DerefsUnitPointer_ReturnsLowerUpperLabel()
        {
            var saved = CoreState.ROM;
            try
            {
                var rom = new ROM();
                rom.LoadLow("synth.gba", new byte[0x1000000], "AE7J01");
                CoreState.ROM = rom;
                NameResolver.ClearCache();
                var d = rom.Data;
                var info = rom.RomInfo;

                // unit_pointer is a POINTER FIELD — plant a pointer to a unit table base, and put
                // GARBAGE at the pointer slot region so the OLD (no-deref) code would read nonsense.
                const uint unitBase = 0x300000;
                W32(d, info.unit_pointer, 0x08000000u | unitBase);

                uint ds = info.unit_datasize; // 52 for FE7
                // Unit 0: text id 1, lower-class custom-anime id (+37) = 0x05.
                W16(d, unitBase + 0 * ds, 0x0001);
                d[unitBase + 0 * ds + 37] = 0x05;
                d[unitBase + 0 * ds + 38] = 0x00;
                // Unit 1: text id 2, upper-class custom-anime id (+38) = 0x06.
                W16(d, unitBase + 1 * ds, 0x0002);
                d[unitBase + 1 * ds + 37] = 0x00;
                d[unitBase + 1 * ds + 38] = 0x06;

                // id 0x05 → unit 0's lower-class label (text 1 + 下級職 marker).
                string lower = NameResolver.GetCustomBattleAnimeName(rom, 0x05);
                Assert.Contains(R._("下級職"), lower);
                Assert.Equal(NameResolver.GetTextById(1) + " " + R._("下級職"), lower);

                // id 0x06 → unit 1's upper-class label (text 2 + 上級職 marker).
                string upper = NameResolver.GetCustomBattleAnimeName(rom, 0x06);
                Assert.Contains(R._("上級職"), upper);
                Assert.Equal(NameResolver.GetTextById(2) + " " + R._("上級職"), upper);

                // No owner → empty (not garbage from the pointer-slot region the old bug read).
                Assert.Equal("", NameResolver.GetCustomBattleAnimeName(rom, 0x77));
                // id 0 → empty.
                Assert.Equal("", NameResolver.GetCustomBattleAnimeName(rom, 0));
            }
            finally { CoreState.ROM = saved; NameResolver.ClearCache(); }
        }

        [Fact]
        public void GetCustomBattleAnimeName_NonFE7_ReturnsEmpty()
        {
            var saved = CoreState.ROM;
            try
            {
                var rom = new ROM();
                rom.LoadLow("fe8.gba", new byte[0x1000000], "BE8E01");
                CoreState.ROM = rom;
                NameResolver.ClearCache();
                // version != 7 → empty, no throw (FE6/FE8 short-circuit).
                Assert.Equal("", NameResolver.GetCustomBattleAnimeName(rom, 0x05));
                Assert.Equal("", NameResolver.GetCustomBattleAnimeName(null, 0x05));
            }
            finally { CoreState.ROM = saved; NameResolver.ClearCache(); }
        }

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

        static ROM MakeFE8URom()
        {
            var rom = new ROM();
            rom.LoadLow("test.gba", new byte[0x1000000], "BE8E01");
            Assert.NotNull(rom.RomInfo);
            Assert.Equal(8, rom.RomInfo.version);
            return rom;
        }

        // -------------------------------------------------------------
        // GetUnitNameByOneBasedId — FE8 sentinel and bounds-check coverage
        // -------------------------------------------------------------

        /// <summary>
        /// FE8 reserves three u16 unit-ID sentinels (0xFFFF / 0xFFFE / 0xFFFD)
        /// for camera-controlled unit / memory-slot B coords / memory-slot 2
        /// unit ID. WinForms UnitForm.GetUnitName(uid) maps each to a localized
        /// string instead of resolving as a table index — the Core resolver
        /// must mirror that, otherwise the sentinel u16 wraps the (uid-1) *
        /// unit_datasize multiplication and indexes some unrelated row.
        /// </summary>
        [Fact]
        public void GetUnitNameByOneBasedId_FE8_Sentinels_ReturnLocalizedStrings()
        {
            var savedRom = CoreState.ROM;
            try
            {
                CoreState.ROM = MakeFE8URom();
                NameResolver.ClearCache();

                string n1 = NameResolver.GetUnitNameByOneBasedId(0xFFFF);
                string n2 = NameResolver.GetUnitNameByOneBasedId(0xFFFE);
                string n3 = NameResolver.GetUnitNameByOneBasedId(0xFFFD);

                // Whatever the active translation, the sentinels MUST NOT be
                // empty, "???", or "#N" (the un-mapped fallback shapes) — they
                // are reserved logical values, not table rows.
                Assert.False(string.IsNullOrEmpty(n1));
                Assert.False(string.IsNullOrEmpty(n2));
                Assert.False(string.IsNullOrEmpty(n3));
                Assert.NotEqual("???", n1);
                Assert.NotEqual("???", n2);
                Assert.NotEqual("???", n3);
                Assert.DoesNotContain("#", n1);
                Assert.DoesNotContain("#", n2);
                Assert.DoesNotContain("#", n3);

                // The three sentinels also have distinct meanings, so their
                // localized strings must differ from each other.
                Assert.NotEqual(n1, n2);
                Assert.NotEqual(n2, n3);
                Assert.NotEqual(n1, n3);
            }
            finally
            {
                CoreState.ROM = savedRom;
                NameResolver.ClearCache();
            }
        }

        /// <summary>
        /// On FE6 (which does NOT define the 0xFFFF sentinel), an out-of-range
        /// 1-based ID must return "" rather than wrapping the address math and
        /// returning a stale or fabricated name.
        /// </summary>
        [Fact]
        public void GetUnitNameByOneBasedId_FE6_AboveMaxCount_ReturnsEmpty()
        {
            var savedRom = CoreState.ROM;
            try
            {
                CoreState.ROM = MakeFE6Rom();
                NameResolver.ClearCache();

                uint maxCount = CoreState.ROM.RomInfo.unit_maxcount;
                Assert.True(maxCount > 0);

                // First definitely-out-of-range 1-based id and a stray u16 value
                // both must produce empty (matches WinForms IDToAddr/isSafetyOffset
                // path which returns "" for out-of-table IDs).
                Assert.Equal("", NameResolver.GetUnitNameByOneBasedId(maxCount + 1));
                Assert.Equal("", NameResolver.GetUnitNameByOneBasedId(0xFFFF));
            }
            finally
            {
                CoreState.ROM = savedRom;
                NameResolver.ClearCache();
            }
        }

        // -------------------------------------------------------------
        // GetUnitNameAndANYByOneBasedId — WinForms GetUnitNameAndANY parity
        // -------------------------------------------------------------

        /// <summary>
        /// Event tables (battle-talk, force-sortie, haiku) treat unit ID 0 as
        /// "ANY" — the row applies to any unit, not "no unit". WinForms
        /// UnitForm.GetUnitNameAndANY returns R._("ANY") for 0; the Core
        /// resolver must mirror that, otherwise list labels render as
        /// "0x00  vs Eirika" with a blank attacker.
        /// </summary>
        [Fact]
        public void GetUnitNameAndANYByOneBasedId_Zero_ReturnsANY()
        {
            var savedRom = CoreState.ROM;
            try
            {
                CoreState.ROM = MakeFE8URom();
                NameResolver.ClearCache();

                string result = NameResolver.GetUnitNameAndANYByOneBasedId(0);
                Assert.False(string.IsNullOrEmpty(result));
                // The ANY label must differ from the GetUnitNameByOneBasedId(0)
                // contract (which returns ""), proving the call routed through
                // the ANY-aware override.
                Assert.NotEqual("", result);
                Assert.NotEqual(NameResolver.GetUnitNameByOneBasedId(0), result);
            }
            finally
            {
                CoreState.ROM = savedRom;
                NameResolver.ClearCache();
            }
        }

        /// <summary>
        /// For non-zero IDs the ANY resolver must produce the same string as
        /// the non-ANY variant — the only difference between the two helpers
        /// is the 0-handling. This pins that no extra divergence creeps in
        /// (e.g. sentinel reinterpretation).
        /// </summary>
        [Fact]
        public void GetUnitNameAndANYByOneBasedId_NonZero_MatchesPlainResolver()
        {
            var savedRom = CoreState.ROM;
            try
            {
                CoreState.ROM = MakeFE8URom();
                NameResolver.ClearCache();

                for (uint uid = 1; uid <= 5; uid++)
                {
                    string a = NameResolver.GetUnitNameAndANYByOneBasedId(uid);
                    string b = NameResolver.GetUnitNameByOneBasedId(uid);
                    Assert.Equal(b, a);
                }
            }
            finally
            {
                CoreState.ROM = savedRom;
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
        public void GetSongName_NullRom_ReturnsFormattedFallback()
        {
            // With no ROM loaded, GetSongName must fall back to the safe
            // "Song 0x{id:X}" placeholder (the real-name resolver needs a ROM).
            var saved = CoreState.ROM;
            try
            {
                CoreState.ROM = null!;
                NameResolver.ClearCache();
                string result = NameResolver.GetSongName(0x1A);
                Assert.Equal("Song 0x1A", result);
            }
            finally
            {
                CoreState.ROM = saved;
                NameResolver.ClearCache();
            }
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
