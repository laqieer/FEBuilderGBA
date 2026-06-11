using System;
using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Tests for the #1028 Slice C AI-translation hint feature:
    /// <see cref="NameResolver.GetFaceTranslateInfo"/> (faithful WF
    /// <c>UnitForm.GetTranslateInfoByFaceID</c> port) and
    /// <see cref="ToolTranslateROMCore.AppendAIHintMessage"/> (faithful WF
    /// <c>ToolTranslateROM.AppendAIHintMessage</c> port).
    ///
    /// Text decode is not deterministic on a synthetic ROM (no Huffman tree), so
    /// these tests assert on the deterministic structure: the face-id string, the
    /// flag descriptors (localized via R._ — deterministic for the active lang),
    /// the mob fallback, the visitor skip, dedup, and both escape forms. The
    /// descriptor strings themselves are the SAME WF source strings.
    /// </summary>
    [Collection("SharedState")]
    public class AIHintCoreTests : IDisposable
    {
        readonly ROM? _savedRom;
        readonly string _savedLang;

        public AIHintCoreTests()
        {
            _savedRom = CoreState.ROM;
            _savedLang = CoreState.Language;
        }

        public void Dispose()
        {
            CoreState.ROM = _savedRom;
            CoreState.Language = _savedLang;
            NameResolver.ClearCache();
        }

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

        /// <summary>
        /// Build an FE8U ROM whose unit table contains the given (faceId, flagByte)
        /// rows starting at index 0. Unit datasize on FE8U is 52, face at +6,
        /// flags at +41. The face value stored at +6 is the SMALL portrait id
        /// (WITHOUT the +0x100 LoadFace offset) — exactly how WF reads it. The
        /// pointer slot at rom.RomInfo.unit_pointer is repointed to a fresh table
        /// base inside free space.
        /// </summary>
        static ROM MakeFE8URomWithUnits((ushort face, byte flags)[] units)
        {
            var rom = new ROM();
            var data = new byte[0x1000000];
            rom.LoadLow("aihint-fe8u.gba", data, "BE8E01");
            Assert.NotNull(rom.RomInfo);
            Assert.Equal(8, rom.RomInfo.version);

            uint dataSize = rom.RomInfo.unit_datasize; // 52
            uint tableBase = 0x300000;                 // well inside the 0x1000000 ROM
            uint unitPtr = rom.RomInfo.unit_pointer;   // FindROMPointer -> 0x10108 on a zeroed ROM
            Assert.True(U.isSafetyOffset(unitPtr, rom));

            WriteU32(rom.Data, unitPtr, tableBase | 0x08000000);

            for (uint i = 0; i < units.Length; i++)
            {
                uint addr = tableBase + i * dataSize;
                WriteU16(rom.Data, addr + 0, 1);            // name text id (decodes to "???")
                WriteU16(rom.Data, addr + 2, 2);            // info text id
                WriteU16(rom.Data, addr + 6, units[i].face); // face at +6
                rom.Data[addr + 41] = units[i].flags;        // flags at +41
            }
            return rom;
        }

        // -------------------------------------------------------------
        // GetFaceTranslateInfo
        // -------------------------------------------------------------

        [Fact]
        public void GetFaceTranslateInfo_NullRom_ReturnsEmpty()
        {
            Assert.Equal("", NameResolver.GetFaceTranslateInfo(null!, 0x100));
        }

        [Fact]
        public void GetFaceTranslateInfo_VisitorSelf_ReturnsEmpty()
        {
            // WF: faceId == 0xFFFF - 0x100 (the visitor self) -> "".
            var rom = MakeFE8URomWithUnits(new (ushort, byte)[] { (0x00, 0x00) });
            Assert.Equal("", NameResolver.GetFaceTranslateInfo(rom, 0xFFFF - 0x100));
        }

        [Fact]
        public void GetFaceTranslateInfo_UnmatchedFace_ReturnsMobFallback()
        {
            // Unit table holds small face 0x00 only. Query a small face (0x55) not
            // present anywhere in the table -> the mob-character fallback string.
            var rom = MakeFE8URomWithUnits(new (ushort, byte)[] { (0x00, 0x00) });
            string r = NameResolver.GetFaceTranslateInfo(rom, 0x55);
            Assert.Contains(R._("モブキャラ"), r);
            Assert.Contains(R._("未参照の人物。兵士か村人のモブキャラだと思われる。"), r);
            // The faceId string uses (face + 0x100) — here 0x155.
            Assert.Contains("0x155", r);
        }

        [Fact]
        public void GetFaceTranslateInfo_Index0_AppendsProtagonist()
        {
            // i == 0 always appends the protagonist descriptor (WF UnitForm.cs:370).
            // Small face 0x00 at +6 -> queried with 0x00; faceId string is 0x100.
            var rom = MakeFE8URomWithUnits(new (ushort, byte)[] { (0x00, 0x00) });
            string r = NameResolver.GetFaceTranslateInfo(rom, 0x00);
            Assert.Contains(R._(" 主人公"), r);
            // Format: Name(0x100) info...
            Assert.Contains("(0x100)", r);
        }

        [Fact]
        public void GetFaceTranslateInfo_EnemyLeaderFlag_AppendsEnemyLeader()
        {
            // Non-index-0 row with flag 0x80 -> enemy-leader descriptor.
            var rom = MakeFE8URomWithUnits(new (ushort, byte)[]
            {
                (0x00, 0x00), // index 0 (protagonist), small face 0x00
                (0x10, 0x80), // index 1 -> enemy leader, small face 0x10
            });
            string r = NameResolver.GetFaceTranslateInfo(rom, 0x10);
            Assert.Contains(R._(" 敵将"), r);
            Assert.Contains("(0x110)", r); // faceId string = small + 0x100
            // The protagonist descriptor must NOT appear (this is index 1).
            Assert.DoesNotContain(R._(" 主人公"), r);
        }

        [Fact]
        public void GetFaceTranslateInfo_FemaleFlag_AppendsFemale()
        {
            var rom = MakeFE8URomWithUnits(new (ushort, byte)[]
            {
                (0x00, 0x00),
                (0x11, 0x40), // index 1 -> female
            });
            string r = NameResolver.GetFaceTranslateInfo(rom, 0x11);
            Assert.Contains(R._(" 女性"), r);
        }

        [Fact]
        public void GetFaceTranslateInfo_PromotedFlag_AppendsPromoted()
        {
            var rom = MakeFE8URomWithUnits(new (ushort, byte)[]
            {
                (0x00, 0x00),
                (0x12, 0x01), // index 1 -> promoted (上級職)
            });
            string r = NameResolver.GetFaceTranslateInfo(rom, 0x12);
            Assert.Contains(R._(" 上級職"), r);
        }

        [Fact]
        public void GetFaceTranslateInfo_ImpossibleLordFlagBranch_NeverEmits()
        {
            // Faithful WF quirk: `else if ((f2 & 0x80) == 0x20)` can never be true
            // ((f2 & 0x80) is only 0x00 or 0x80). Even with flag 0x20 set, the
            // 主人公格 descriptor must NEVER appear — preserving WF parity exactly.
            var rom = MakeFE8URomWithUnits(new (ushort, byte)[]
            {
                (0x00, 0x00),
                (0x20, 0x20), // index 1, flag 0x20 set
            });
            string r = NameResolver.GetFaceTranslateInfo(rom, 0x20);
            Assert.DoesNotContain(R._(" 主人公格"), r);
        }

        // -------------------------------------------------------------
        // AppendAIHintMessage
        // -------------------------------------------------------------

        [Fact]
        public void AppendAIHintMessage_NullRom_ReturnsEmpty()
        {
            Assert.Equal("", ToolTranslateROMCore.AppendAIHintMessage(null!, "[LoadFace][0x100]"));
        }

        [Fact]
        public void AppendAIHintMessage_NoFaceLoad_ReturnsEmpty()
        {
            var rom = MakeFE8URomWithUnits(new (ushort, byte)[] { (0x00, 0x00) });
            Assert.Equal("", ToolTranslateROMCore.AppendAIHintMessage(rom, "Hello world, no faces here."));
        }

        [Fact]
        public void AppendAIHintMessage_FEditorAdvForm_ParsesLoadFace()
        {
            // Default text-escape mode (func_text_escape unset -> 1 = FEditorAdv).
            var rom = MakeFE8URomWithUnits(new (ushort, byte)[] { (0x00, 0x00) });
            string hint = ToolTranslateROMCore.AppendAIHintMessage(rom, "[LoadFace][0x100] hello");
            Assert.NotEqual("", hint);
            // Index-0 protagonist descriptor proves the face resolved.
            Assert.Contains(R._(" 主人公"), hint);
            // WF format: two leading blank lines (AppendLine("") twice) then the
            // info line — the hint always begins with blank line(s) before content.
            Assert.StartsWith(Environment.NewLine, hint);
        }

        [Fact]
        public void AppendAIHintMessage_EngineEscapeForm_ParsesLoadFace()
        {
            // Force engine-escape mode (func_text_escape = 0).
            var savedConfig = CoreState.Config;
            try
            {
                EnsureConfig();
                CoreState.Config["func_text_escape"] = "0";

                var rom = MakeFE8URomWithUnits(new (ushort, byte)[] { (0x00, 0x00) });
                // Engine form: the WF regex is @0010@([0-9A-F]...)] — the capture
                // group is 1 hex digit + 3 any-chars (4 chars) then a literal ']'.
                // So @0010@0100] captures "0100" -> faceID 0x100 -> -0x100 = 0,
                // matching the small face 0x00 at unit index 0.
                string hint = ToolTranslateROMCore.AppendAIHintMessage(rom, "@0010@0100] hello");
                Assert.NotEqual("", hint);
                Assert.Contains(R._(" 主人公"), hint);
            }
            finally
            {
                CoreState.Config = savedConfig;
            }
        }

        [Fact]
        public void AppendAIHintMessage_DuplicateFaceIds_Deduped()
        {
            var rom = MakeFE8URomWithUnits(new (ushort, byte)[] { (0x00, 0x00) });
            // Same face referenced twice -> emitted once.
            string hint = ToolTranslateROMCore.AppendAIHintMessage(rom,
                "[LoadFace][0x100] foo [LoadFace][0x100] bar");
            int first = hint.IndexOf("(0x100)", StringComparison.Ordinal);
            int second = hint.IndexOf("(0x100)", first + 1, StringComparison.Ordinal);
            Assert.True(first >= 0, "face line missing");
            Assert.True(second < 0, "duplicate face id was not deduped");
        }

        [Fact]
        public void AppendAIHintMessage_FaceIdBelow0x100_Skipped()
        {
            // WF: faceID must be >= 0x100; a [LoadFace][0x0FF] is skipped entirely.
            var rom = MakeFE8URomWithUnits(new (ushort, byte)[] { (0x00, 0x00) });
            string hint = ToolTranslateROMCore.AppendAIHintMessage(rom, "[LoadFace][0x0FF]");
            Assert.Equal("", hint);
        }

        [Fact]
        public void AppendAIHintMessage_VisitorFace_EmitsNoInfoLine()
        {
            // [LoadFace][0xFFFF] -> faceID 0xFFFF -> faceID-0x100 = 0xFEFF =
            // 0xFFFF-0x100, the visitor self, which GetFaceTranslateInfo returns
            // "" for. Faithful WF: the id is still added to `dup` (so dup.Count=1,
            // the block is NOT the empty-string early return) but its "" result is
            // `continue`d, so the hint block has the two blank lines and NO info
            // line. Assert it contains neither a mob fallback nor a descriptor.
            var rom = MakeFE8URomWithUnits(new (ushort, byte)[] { (0x00, 0x00) });
            string hint = ToolTranslateROMCore.AppendAIHintMessage(rom, "[LoadFace][0xFFFF]");
            Assert.DoesNotContain(R._("モブキャラ"), hint);
            Assert.DoesNotContain("(0x", hint); // no resolved face line emitted
            Assert.Equal(hint.Trim(), ""); // only the WF leading blank lines
        }

        static void EnsureConfig()
        {
            if (CoreState.Config == null)
            {
                CoreState.Config = new Config();
            }
        }
    }
}
