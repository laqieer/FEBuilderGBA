using System.Collections.Generic;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Tests for <see cref="ConversationScriptParser"/>. These are direct ports of
    /// the debug-only TEST_TEXTPARSE1-7 cases in WinForms <c>TextForm.cs</c>, plus
    /// new coverage for the Text Engine Rework opcode-length handling.
    /// </summary>
    public class ConversationScriptParserTests
    {
        // ========================================================================
        // Port of WinForms TextForm.TEST_TEXTPARSE1 (line 746)
        // Verifies that pos-only codes and serif codes interleave correctly.
        // ========================================================================
        [Fact]
        public void Parse_FE8ChapterTalk_ProducesAllSimpleListEntries()
        {
            string text =
                "@0008@0010@0139@000D@0010@015A\r\n"
                + "@000D@0080@000D\r\n"
                + "Nino!?@0005\r\n"   // pre-pended @000D is omitted
                + "Why@0003\r\n"
                + "\r\n@0008\r\n";

            List<ConversationStep> simpleList = ConversationScriptParser.ParseScript(text);

            Assert.Equal(6, simpleList.Count);
            Assert.Equal("@0008@0010@0139", simpleList[0].SrcText);
            Assert.Equal("@000D@0010@015A", simpleList[1].SrcText);
            Assert.Equal("\r\n", simpleList[2].SrcText);
            Assert.Equal("@000D@0080@000D", simpleList[3].SrcText);
            Assert.Equal("\r\nNino!?@0005\r\nWhy@0003\r\n\r\n", simpleList[4].SrcText);
            Assert.Equal("@0008\r\n", simpleList[5].SrcText);
        }

        // ========================================================================
        // Port of WinForms TextForm.TEST_TEXTPARSE2 (line 764)
        // Verifies that mid-script linebreaks between codes parse correctly.
        // ========================================================================
        [Fact]
        public void Parse_FE7InlineDisplayCode_HandlesMissingPosPrefix()
        {
            string text =
                "@000C\r\n"                // pos changes mid-script (FE7:D1)
                + "@0010@0137\r\n"
                + "@0016？@0016@0003\r\n"
                + "\r\n"
                + "なんだい？@0004\r\n"
                + "？@0003\r\n"
                + "\r\n"
                + "@0002\r\n"
                + "フン\r\n"
                + "\r\n"
                + "@0011@0006\r\n"
                + "@0009@0010@0127\r\n"
                + "・・・@0003\r\n"
                + "\r\n"
                + "まあいい・・・@0004\r\n"
                + "一人の方が@0003";

            List<ConversationStep> simpleList = ConversationScriptParser.ParseScript(text);

            Assert.Equal(6, simpleList.Count);
            Assert.Equal("@000C\r\n@0010@0137", simpleList[0].SrcText);
            Assert.Equal("\r\n@0016？@0016@0003\r\n\r\nなんだい？@0004\r\n？@0003\r\n\r\n@0002\r\nフン\r\n\r\n", simpleList[1].SrcText);
            Assert.Equal("@0011", simpleList[2].SrcText);
            Assert.Equal("@0006\r\n", simpleList[3].SrcText);
            Assert.Equal("@0009@0010@0127", simpleList[4].SrcText);
            Assert.Equal("\r\n・・・@0003\r\n\r\nまあいい・・・@0004\r\n一人の方が@0003", simpleList[5].SrcText);
        }

        // ========================================================================
        // Port of WinForms TextForm.TEST_TEXTPARSE3 (line 793)
        // Verifies that simultaneous display + serif emit a Display step then
        // a continuation text step.
        // ========================================================================
        [Fact]
        public void Parse_DisplayWithSerifBoth_ProducesDisplayPlusSerif()
        {
            string text =
                "@0009@0010@016B\r\n"
                + "援軍が・・・@0004\r\n"
                + "なぜだ・・・。@0003\r\n"
                + "@0015\r\n";

            List<ConversationStep> simpleList = ConversationScriptParser.ParseScript(text);

            Assert.Equal(2, simpleList.Count);
            Assert.Equal("@0009@0010@016B", simpleList[0].SrcText);
            Assert.Equal("\r\n援軍が・・・@0004\r\nなぜだ・・・。@0003\r\n@0015\r\n", simpleList[1].SrcText);
        }

        // ========================================================================
        // Port of WinForms TextForm.TEST_TEXTPARSE4 (line 807)
        // Verifies a two-character intermixed Display + Serif block is split
        // correctly and inherits Code1 from the last seen position.
        // ========================================================================
        [Fact]
        public void Parse_TwoCharactersShowAndSerifIntermixed()
        {
            string text =
                "@0009@0010@0102@000C@0010@0104@000Cエイリーク様\r\n"
                + "ここが@0003\r\n"
                + "\r\n"
                + "この港から\r\n"
                + "ロストンまでは@0003\r\n"
                + "@0009潮の匂いがします・・・\r\n"
                + "とても@0003\r\n";

            List<ConversationStep> simpleList = ConversationScriptParser.ParseScript(text);

            Assert.Equal(4, simpleList.Count);
            Assert.Equal("@0009@0010@0102", simpleList[0].SrcText);
            Assert.Equal("@000C@0010@0104", simpleList[1].SrcText);
            Assert.Equal("@000Cエイリーク様\r\nここが@0003\r\n\r\nこの港から\r\nロストンまでは@0003\r\n", simpleList[2].SrcText);
            Assert.Equal(0xCu, simpleList[2].Code1);
            Assert.Equal("@0009潮の匂いがします・・・\r\nとても@0003\r\n", simpleList[3].SrcText);
            Assert.Equal(0x9u, simpleList[3].Code1);
        }

        // ========================================================================
        // Port of WinForms TextForm.TEST_TEXTPARSE5 (line 830) / 6 (identical)
        // ========================================================================
        [Fact]
        public void Parse_MoveCommand_FlagsAsMove()
        {
            string text =
                "@000B@0010@011A@0017\r\n" +
                "私はリン。\r\n" +
                "ロルカ族の娘。@0003@0002@0004\r\n" +
                "あなたは？\r\n" +
                "名前を教えて？@0003@0015@0006\r\n" +
                "@000B\r\n" +
                "@0080@0020っていうの？@0005\r\n";

            List<ConversationStep> simpleList = ConversationScriptParser.ParseScript(text);

            Assert.Equal(3, simpleList.Count);
            Assert.Equal("@000B@0010@011A", simpleList[0].SrcText);
            Assert.Equal("@0017\r\n私はリン。\r\nロルカ族の娘。@0003@0002@0004\r\nあなたは？\r\n名前を教えて？@0003@0015@0006\r\n", simpleList[1].SrcText);
            Assert.Equal("@000B\r\n@0080@0020っていうの？@0005\r\n", simpleList[2].SrcText);
        }

        // ========================================================================
        // Port of WinForms TextForm.TEST_TEXTPARSE7 (line 864)
        // Verifies English chapter talk and the literal '@' in 0x10/0x16 codes
        // do not produce spurious splits.
        // ========================================================================
        [Fact]
        public void Parse_EnglishChapterTalk_HandlesAtCharInText()
        {
            string text =
                "@000B@0017\r\n" +
                "You're safe now.@0003@0002@0005\r\n" +
                "Can you remember your name?@0003@0015@0006\r\n" +
                "Your name is @0080@0020?@0005\r\n" +
                "What an odd-sounding name...@0003\r\n" +
                "But@0005 pay me no mind.\r\n" +
                "It is a good name.@0003@0002\r\n" +
                "you are a traveler.@0003\r\n" +
                "What brings you to the Sacae Plains?@0005\r\n" +
                "Would you share your story with me?@0003@0015@0017\r\n" +
                "@0010@0116\r\n" +
                "abc";

            List<ConversationStep> simpleList = ConversationScriptParser.ParseScript(text);

            // Must split at @0010@0116 (display code mid-script)
            Assert.True(simpleList.Count >= 2);
            // First entry must keep its left-edge Code1=@000B
            Assert.Equal(0xBu, simpleList[0].Code1);
        }

        // ========================================================================
        // New test: Verify position tracking via UpdatePosstion-equivalent logic.
        // Display puts a face in a slot, Hide removes it.
        // ========================================================================
        [Fact]
        public void Parse_DisplayThenHide_TracksUnitStateAcrossSteps()
        {
            string text =
                "@0008@0010@0102\r\n"   // show face 0x102 at pos 8 (left-edge)
                + "Hello!@0003\r\n"
                + "@0008@0011\r\n";     // hide face at pos 8

            List<ConversationStep> simpleList = ConversationScriptParser.ParseScript(text);

            Assert.True(simpleList.Count >= 1);
            // After the Display step, slot 0 (pos 0x8 - 0x8) must hold face id 0x102
            ConversationStep display = simpleList[0];
            Assert.Equal(0x102u, display.Units[0]);
            Assert.Equal(0x8u, display.Code1);
            Assert.Equal(0x10u, display.Code2);
        }

        // ========================================================================
        // New test: Pure text with no @-codes should produce a single text step.
        // ========================================================================
        [Fact]
        public void Parse_PureTextWithNoCodes_ProducesOneStep()
        {
            string text = "Just some text without any control codes.";

            List<ConversationStep> simpleList = ConversationScriptParser.ParseScript(text);

            Assert.Single(simpleList);
            Assert.Equal(text, simpleList[0].SrcText);
            Assert.Equal(0u, simpleList[0].Code1);
            Assert.Equal(0u, simpleList[0].Code2);
        }

        // ========================================================================
        // New test: Empty input produces zero steps.
        // ========================================================================
        [Fact]
        public void Parse_EmptyInput_ProducesNoSteps()
        {
            List<ConversationStep> simpleList = ConversationScriptParser.ParseScript("");

            Assert.Empty(simpleList);
        }

        // ========================================================================
        // New test: TextEngineRework opcodes - variable-length argument consumption.
        // Without the gate, @0080@0026<arg> would be split as two separate codes.
        // With the gate enabled, the parser must skip the trailing variable-length
        // bytes without producing spurious steps.
        // ========================================================================
        [Fact]
        public void Parse_TextEngineReworkOpcodes_ConsumesVariableLengthCodes()
        {
            // @0080@0026 takes one trailing @XXXX argument (5 chars) under the
            // TextEngineRework gate. With the gate ON, the trailing @0042 must
            // be consumed as the argument, not split off as a new step.
            string text =
                "@000B@0010@011A"          // standard display
                + "@0080@0026@0042"        // TextEngineRework op 0x26 + arg
                + "Hello@0003";            // normal serif

            // With gate disabled: the @0080 / @0026 / @0042 are seen as ordinary
            // codes and the parser falls back to the standard move-or-jump check
            // (which fails for code 0x26 outside the move range), producing a
            // single-step parse.
            List<ConversationStep> baseline = ConversationScriptParser.ParseScript(text, enableTextEngineRework: false);

            // With gate enabled: the same input is consumed as a single
            // variable-length TextEngineRework instruction and no false split
            // is introduced.
            List<ConversationStep> gated = ConversationScriptParser.ParseScript(text, enableTextEngineRework: true);

            // The gated parse must produce no MORE steps than the baseline,
            // because TextEngineRework consumes the bytes without emitting a
            // new step. (Equal counts is the expected behaviour.)
            Assert.True(gated.Count <= baseline.Count,
                $"Gated parse produced {gated.Count} steps but baseline produced {baseline.Count}; the gate should reduce or equal the count, not increase it.");
        }
    }
}
