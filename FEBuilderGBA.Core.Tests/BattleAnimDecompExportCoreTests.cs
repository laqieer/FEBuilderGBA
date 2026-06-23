// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for BattleAnimDecompExportCore (#1363) — export a FEBuilder/FEditor-decoded
// battle animation as reviewable decomp source macro asm (banim_<TAG>_motion.s).
//
// Coverage (PURE, ROM-free, hand-built AnimeRecords):
//   * banim_code_frame / banim_code_85 / banim_code_nop / banim_code_end_mode lines.
//   * banim_frame_oam / banim_frame_end OAM emission; affine entry -> raw .hword + diagnostic.
//   * unknown command byte -> commented placeholder + diagnostic, NO wrong macro.
//   * the 24-word mode table (12 offsets + 12 trailing zeros); empty mode -> .word 0.
//   * both OAM sides (oam_r/oam_l); shared sides -> alias, differing -> both emitted.
//   * trailing diagnostics block (dedup, never inline between macro args).
//   * BuildManifestJson + BuildPaletteSidecars + SanitizeTag.
//   * Export never throws on a null ROM / out-of-range address.

using System;
using System.Collections.Generic;
using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    public class BattleAnimDecompExportCoreTests
    {
        // ---- helpers -------------------------------------------------------

        static BattleAnimDecompExportCore.AnimeRecord MakeAnime(string tag = "test")
        {
            return new BattleAnimDecompExportCore.AnimeRecord { Tag = tag, RecordAddr = 0xC00028 };
        }

        static BattleAnimDecompExportCore.ModeRecord ModeWith(int index,
            params BattleAnimDecompExportCore.CommandRecord[] cmds)
        {
            var m = new BattleAnimDecompExportCore.ModeRecord { Index = index, HasData = true };
            m.Commands.AddRange(cmds);
            return m;
        }

        static BattleAnimDecompExportCore.CommandRecord Frame(ushort dur, byte frameNo, uint sheet, uint oam, bool valid = true)
        {
            return new BattleAnimDecompExportCore.CommandRecord
            {
                Kind = BattleAnimDecompExportCore.CommandKind.Frame,
                Duration = dur, FrameNumber = frameNo, SheetPointer = sheet,
                SheetPointerValid = valid, OamOffset = oam,
            };
        }

        static BattleAnimDecompExportCore.CommandRecord Control(uint payload24)
        {
            return new BattleAnimDecompExportCore.CommandRecord
            { Kind = BattleAnimDecompExportCore.CommandKind.Control, Payload24 = payload24 };
        }

        static BattleAnimDecompExportCore.CommandRecord EndMode()
        {
            return new BattleAnimDecompExportCore.CommandRecord
            { Kind = BattleAnimDecompExportCore.CommandKind.EndMode };
        }

        static BattleAnimDecompExportCore.CommandRecord Unknown(uint raw)
        {
            return new BattleAnimDecompExportCore.CommandRecord
            { Kind = BattleAnimDecompExportCore.CommandKind.Unknown, RawWord = raw };
        }

        static BattleAnimDecompExportCore.OamEntry Oam(ushort a0, ushort a1, ushort a2, ushort dx, ushort dy, ushort pad = 0)
        {
            return new BattleAnimDecompExportCore.OamEntry { Attr0 = a0, Attr1 = a1, Attr2 = a2, Dx = dx, Dy = dy, Pad = pad };
        }

        // ---- PURE formatter: script -----------------------------------------

        [Fact]
        public void Format_Frame_EmitsBanimCodeFrame()
        {
            var anime = MakeAnime();
            anime.Modes.Add(ModeWith(0, Frame(2, 3, 0x08C0424C, 60), EndMode()));
            anime.OamRight.Add(Oam(0, 0, 0, 0, 0));

            string s = BattleAnimDecompExportCore.FormatBanimSource(anime, out var diags);

            Assert.Contains(".include \"banim_code.inc\"", s);
            Assert.Contains(".include \"banim_code_frame.inc\"", s);
            Assert.Contains(".global banim_test_script", s);
            // banim_code_frame duration, sheet_addr, frame_number, oam_offset
            Assert.Contains("banim_code_frame 2, 0x08C0424C, 3, 60", s);
            Assert.Contains("banim_code_end_mode", s);
        }

        [Fact]
        public void Format_Control_EmitsBanimCode85_WithFull24BitPayload()
        {
            var anime = MakeAnime();
            // 0x48 sound command with a music id in the high bytes: payload 0x1A2348.
            anime.Modes.Add(ModeWith(0, Control(0x1A2348), EndMode()));

            string s = BattleAnimDecompExportCore.FormatBanimSource(anime, out _);

            Assert.Contains("banim_code_85 0x1A2348", s);
        }

        [Fact]
        public void Format_ControlZero_EmitsBanimCodeNop()
        {
            var anime = MakeAnime();
            anime.Modes.Add(ModeWith(0, Control(0), EndMode()));

            string s = BattleAnimDecompExportCore.FormatBanimSource(anime, out _);

            Assert.Contains("banim_code_nop", s);
        }

        [Fact]
        public void Format_MissingEndMode_AppendsEndMode()
        {
            var anime = MakeAnime();
            anime.Modes.Add(ModeWith(0, Frame(1, 0, 0x08000100, 0)));  // no EndMode in stream

            string s = BattleAnimDecompExportCore.FormatBanimSource(anime, out _);

            Assert.Contains("banim_code_end_mode", s);
        }

        [Fact]
        public void Format_UnknownCommand_EmitsCommentedPlaceholder_NoWrongMacro()
        {
            var anime = MakeAnime();
            anime.Modes.Add(ModeWith(0, Unknown(0xDEADBEEF), EndMode()));

            string s = BattleAnimDecompExportCore.FormatBanimSource(anime, out var diags);

            Assert.Contains("@ UNKNOWN command word 0xDEADBEEF", s);
            // No guessed macro for the unknown word.
            Assert.DoesNotContain("banim_code_85 0xDEADBEEF", s);
        }

        [Fact]
        public void Format_UnresolvedSheetPointer_Diagnostic_NotInlineInArgs()
        {
            var anime = MakeAnime();
            anime.Modes.Add(ModeWith(0, Frame(1, 0, 0x08C0424C, 0), EndMode()));

            string s = BattleAnimDecompExportCore.FormatBanimSource(anime, out var diags);

            // The macro line itself has no inline comment between args.
            Assert.Contains("banim_code_frame 1, 0x08C0424C, 0, 0\n", s);
            // The unresolved note lives in the trailing diagnostics block.
            Assert.Contains("unresolved raw ROM address", string.Join("\n", diags));
            Assert.Contains("@ ===== Export diagnostics", s);
        }

        [Fact]
        public void Format_MissingSheetPointer_EmitsZero_AndDiagnostic()
        {
            var anime = MakeAnime();
            anime.Modes.Add(ModeWith(0, Frame(1, 0, 0, 0, valid: false), EndMode()));

            string s = BattleAnimDecompExportCore.FormatBanimSource(anime, out var diags);

            Assert.Contains("banim_code_frame 1, 0, 0, 0", s);
            Assert.Contains("missing/out of range", string.Join("\n", diags));
        }

        // ---- PURE formatter: mode table -------------------------------------

        [Fact]
        public void Format_ModeTable_Has24Words_12OffsetsPlus12Zeros()
        {
            var anime = MakeAnime();
            // mode 0 has data; modes 1..11 absent (empty -> .word 0).
            anime.Modes.Add(ModeWith(0, Frame(1, 0, 0x08000100, 0), EndMode()));
            for (int i = 1; i < 12; i++)
                anime.Modes.Add(new BattleAnimDecompExportCore.ModeRecord { Index = i, HasData = false });

            string s = BattleAnimDecompExportCore.FormatBanimSource(anime, out _);

            Assert.Contains("banim_test_mode_0 - banim_test_script", s);
            // count the total .word lines in the .data.modes section.
            int idx = s.IndexOf(".data.modes", StringComparison.Ordinal);
            Assert.True(idx >= 0);
            string modesSection = s.Substring(idx);
            // 24 .word entries (12 offsets + 12 zero pad). mode_0 is an offset; the rest are 0.
            int wordCount = CountOccurrences(modesSection, "\t.word ");
            Assert.Equal(24, wordCount);
        }

        // ---- PURE formatter: OAM --------------------------------------------

        [Fact]
        public void Format_Oam_EmitsBanimFrameOam_And_FrameEnd()
        {
            var anime = MakeAnime();
            anime.Modes.Add(ModeWith(0, EndMode()));
            anime.OamRight.Add(Oam(0x0000, 0x8000, 0x0025, 0xFFF0, 0xFFE8));
            anime.OamRight.Add(new BattleAnimDecompExportCore.OamEntry { IsTerminator = true });

            string s = BattleAnimDecompExportCore.FormatBanimSource(anime, out _);

            Assert.Contains("banim_frame_oam 0x0000, 0x8000, 0x0025, 0xFFF0, 0xFFE8", s);
            Assert.Contains("banim_frame_end", s);
        }

        [Fact]
        public void Format_AffineOam_EmitsRawHword_AndDiagnostic()
        {
            var anime = MakeAnime();
            anime.Modes.Add(ModeWith(0, EndMode()));
            // affine entry: bytes [2..3] == 0xFFFF -> attr1 == 0xFFFF.
            anime.OamRight.Add(new BattleAnimDecompExportCore.OamEntry
            { Attr0 = 0x0000, Attr1 = 0xFFFF, Attr2 = 0x0100, Dx = 0x0000, Dy = 0x0000, Pad = 0x0100, IsAffine = true });

            string s = BattleAnimDecompExportCore.FormatBanimSource(anime, out var diags);

            Assert.Contains("@ affine matrix", s);
            Assert.DoesNotContain("banim_frame_oam 0x0000, 0xFFFF", s); // not emitted as a normal sprite
            Assert.Contains("affine", string.Join("\n", diags).ToLowerInvariant());
        }

        [Fact]
        public void Format_SharedOamSides_AliasesLeftToRight()
        {
            var anime = MakeAnime();
            anime.OamSidesShared = true;
            anime.Modes.Add(ModeWith(0, EndMode()));
            anime.OamRight.Add(Oam(0, 0, 0, 0, 0));

            string s = BattleAnimDecompExportCore.FormatBanimSource(anime, out _);

            Assert.Contains("banim_test_oam_l = banim_test_oam_r", s);
        }

        [Fact]
        public void Format_DifferingOamSides_EmitsBoth()
        {
            var anime = MakeAnime();
            anime.OamSidesShared = false;
            anime.Modes.Add(ModeWith(0, EndMode()));
            anime.OamRight.Add(Oam(0x0000, 0x8000, 0x0025, 0xFFF0, 0xFFE8));
            anime.OamLeft.Add(Oam(0x0000, 0x9000, 0x0025, 0xFFF0, 0xFFE8));

            string s = BattleAnimDecompExportCore.FormatBanimSource(anime, out _);

            Assert.Contains("banim_test_oam_r:", s);
            Assert.Contains("banim_test_oam_l:", s);
            Assert.Contains("0x8000", s);
            Assert.Contains("0x9000", s);
            // NOT aliased.
            Assert.DoesNotContain("banim_test_oam_l = banim_test_oam_r", s);
        }

        // ---- manifest / palette / tag ---------------------------------------

        [Fact]
        public void BuildManifestJson_IncludesChecklist_OamSymbols_AndSheetPointers()
        {
            var anime = MakeAnime("arcf_ar1");
            anime.Modes.Add(ModeWith(0, Frame(1, 0, 0x08C0424C, 0), EndMode()));
            for (int i = 1; i < 12; i++)
                anime.Modes.Add(new BattleAnimDecompExportCore.ModeRecord { Index = i, HasData = false });
            anime.SheetPointers.Add(0x08C0424C);

            string json = BattleAnimDecompExportCore.BuildManifestJson(anime);

            Assert.Contains("\"tag\": \"arcf_ar1\"", json);
            Assert.Contains("banim_arcf_ar1_oam_r", json);
            Assert.Contains("banim_arcf_ar1_oam_l", json);
            Assert.Contains("0x08C0424C", json);
            Assert.Contains("banim_data[]", json);
            Assert.Contains("BattleAnimDef", json);
            Assert.Contains("\"modeTableWords\": 24", json);
        }

        [Fact]
        public void BuildPaletteSidecars_SplitsInto4SubPalettes()
        {
            // 4 sub-palettes × 16 colors × 2 bytes = 128 bytes.
            byte[] pal = new byte[128];
            var sidecars = BattleAnimDecompExportCore.BuildPaletteSidecars(pal);
            Assert.Equal(4, sidecars.Count);
            Assert.Equal("_pal0.pal", sidecars[0].Suffix);
            Assert.Equal("_pal3.pal", sidecars[3].Suffix);
        }

        [Fact]
        public void BuildPaletteSidecars_EmptyOrNull_ReturnsEmpty()
        {
            Assert.Empty(BattleAnimDecompExportCore.BuildPaletteSidecars(null));
            Assert.Empty(BattleAnimDecompExportCore.BuildPaletteSidecars(new byte[1]));
        }

        [Theory]
        [InlineData("Arcf AR1", "arcf_ar1")]
        [InlineData("", "anim")]
        [InlineData("___", "anim")]
        [InlineData("Anim001", "anim001")]
        public void SanitizeTag_ProducesValidLabel(string input, string expected)
        {
            Assert.Equal(expected, BattleAnimDecompExportCore.SanitizeTag(input));
        }

        // ---- never-throws contract ------------------------------------------

        [Fact]
        public void Export_NullRom_ReturnsNotOk_NoThrow()
        {
            var r = BattleAnimDecompExportCore.Export(null, 0xC00028, "x");
            Assert.False(r.Ok);
            Assert.NotEmpty(r.Diagnostics);
        }

        [Fact]
        public void Export_OutOfRangeAddr_ReturnsNotOk_NoThrow()
        {
            var rom = new ROM();
            rom.SwapNewROMDataDirect(new byte[0x1000]);
            var r = BattleAnimDecompExportCore.Export(rom, 0x7FFFFFFF, "x");
            Assert.False(r.Ok);
            Assert.NotEmpty(r.Diagnostics);
        }

        [Fact]
        public void FormatBanimSource_NullAnime_NoThrow()
        {
            string s = BattleAnimDecompExportCore.FormatBanimSource(null, out var diags);
            Assert.Contains("no animation", s);
        }

        static int CountOccurrences(string haystack, string needle)
        {
            int count = 0, idx = 0;
            while ((idx = haystack.IndexOf(needle, idx, StringComparison.Ordinal)) >= 0)
            { count++; idx += needle.Length; }
            return count;
        }
    }
}
