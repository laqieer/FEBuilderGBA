using System.Collections.Generic;

namespace FEBuilderGBA
{
    /// <summary>
    /// A single parsed step in a chapter dialogue script. Each step corresponds
    /// to one card / row in the simple conversation viewer.
    /// </summary>
    public class ConversationStep
    {
        /// <summary>The raw substring of the source text owned by this step.</summary>
        public string SrcText;

        /// <summary>
        /// Position code (last seen position 0x8-0xF). 0 means no position
        /// context yet (pure text). 0x8 = left edge, 0x9 = left mid,
        /// 0xA = left side, 0xB = right side, 0xC = right mid, 0xD = right edge,
        /// 0xE = off-screen left, 0xF = off-screen right.
        /// </summary>
        public uint Code1;

        /// <summary>
        /// Action code: 0x10 = display, 0x11 = hide, 0x80 = move/jump,
        /// 0 = ordinary serif/text continuation.
        /// </summary>
        public uint Code2;

        /// <summary>
        /// Action argument: for Display this is face id + 0x100 (or 0xFFFF for
        /// the "visitor" sentinel), for Move this is the destination position
        /// (0xA-0xF), for Hide it is unused.
        /// </summary>
        public uint Code3;

        /// <summary>
        /// Snapshot of which face id (+0x100) occupies each of the 8 portrait
        /// slots at this step. Index 0 = pos 0x8, index 7 = pos 0xF.
        /// </summary>
        public uint[] Units;

        /// <summary>Validation error message (empty when the step is well-formed).</summary>
        public string Error;

        /// <summary>True when the move command points to the same slot it started in.</summary>
        public bool IsJump;

        public ConversationStep()
        {
            this.SrcText = "";
            this.Error = "";
            this.Units = new uint[8];
        }
    }

    /// <summary>
    /// Cross-platform port of WinForms <c>TextForm.ParseTextList</c> +
    /// <c>UpdatePosstion</c>. Splits a decoded dialogue script into a list of
    /// <see cref="ConversationStep"/>s that can be rendered as a simple
    /// conversation viewer.
    /// </summary>
    /// <remarks>
    /// This is a faithful, line-for-line port of the WinForms parser, keeping
    /// the same edge cases (FE7 mid-script linebreaks, FE7 inline @0010@XXXX
    /// without a leading position code, @0080 move command argument, and the
    /// TextEngineRework variable-length opcode table). The tests in
    /// FEBuilderGBA.Core.Tests/ConversationScriptParserTests.cs port the
    /// WinForms debug-only TEST_TEXTPARSE1-7 assertions verbatim.
    /// </remarks>
    public static class ConversationScriptParser
    {
        /// <summary>
        /// Parse a decoded dialogue source string into a list of
        /// <see cref="ConversationStep"/>s.
        /// </summary>
        /// <param name="srcText">Decoded dialogue text (escape codes as
        /// @XXXX hex literals).</param>
        /// <param name="enableTextEngineRework">When true, consume variable-
        /// length argument bytes following @0080@0026 / 0x27 / 0x28-0x2C /
        /// 0x2D / 0x2E / 0x2F / 0x30-0x38 opcodes per the TextEngineRework
        /// patch. Production callers pass
        /// <c>PatchDetection.SearchTextEngineReworkPatch() == TeqTextEngineRework</c>.
        /// </param>
        /// <returns>Parsed steps. Empty input yields an empty list.</returns>
        public static List<ConversationStep> ParseScript(string srcText, bool enableTextEngineRework = false)
        {
            ParseTextList(srcText, out List<ConversationStep> simpleList, enableTextEngineRework);
            UpdatePosstion(srcText, ref simpleList);
            return simpleList;
        }

        // ====================================================================
        // Private parsing helpers (faithful ports of WinForms TextForm logic).
        // ====================================================================

        static int skip_linebreak(string text, int i)
        {
            if (i >= text.Length)
            {
                return 0;
            }
            if (i + 1 < text.Length && text[i] == '\r' && text[i + 1] == '\n')
            {
                return 2;
            }
            return 0;
        }

        static bool CheckPosCodeOnly(string str)
        {
            return str == "@0008" || str == "@0009" || str == "@000a" || str == "@000b"
                || str == "@000c" || str == "@000d" || str == "@000e" || str == "@000f"
                || str == "@000A" || str == "@000B" || str == "@000C" || str == "@000D"
                || str == "@000E" || str == "@000F";
        }

        static bool IsMoveOrJump(uint code1, uint code2)
        {
            return code1 == 0x0080 && (code2 >= 0xA && code2 <= 0x11);
        }

        /// <summary>
        /// TextEngineRework variable-length opcode consumer. Mirrors WinForms
        /// <c>TextForm.CheckTextEngineRework_ParseTextList</c>.
        /// </summary>
        static bool CheckTextEngineRework_ParseTextList(uint code2, string srctext, ref int next_i, bool enableTextEngineRework)
        {
            if (!enableTextEngineRework)
            {
                return false;
            }

            if (code2 == 0x26 || (code2 >= 0x28 && code2 <= 0x2C) || (code2 >= 0x30 && code2 <= 0x38))
            {
                if (next_i + 5 > srctext.Length || srctext[next_i] != '@')
                {
                    return false;
                }
                next_i += 5;
            }
            else if (code2 == 0x27 || code2 == 0x2E)
            {
                if (next_i + 10 > srctext.Length || srctext[next_i] != '@' || srctext[next_i + 5] != '@')
                {
                    return false;
                }
                next_i += 10;
            }
            else if (code2 == 0x2D)
            {
                if (next_i + 20 > srctext.Length || srctext[next_i] != '@' || srctext[next_i + 5] != '@' || srctext[next_i + 10] != '@' || srctext[next_i + 15] != '@')
                {
                    return false;
                }
                next_i += 20;
            }
            else if (code2 == 0x2F)
            {
                if (next_i + 30 > srctext.Length || srctext[next_i] != '@' || srctext[next_i + 5] != '@' || srctext[next_i + 10] != '@' || srctext[next_i + 15] != '@' || srctext[next_i + 20] != '@' || srctext[next_i + 25] != '@')
                {
                    return false;
                }
                next_i += 30;
            }
            else
            {
                return false;
            }

            return true;
        }

        // --------------------------------------------------------------------
        // Core parser: scans the script for @XXXX codes and splits at position
        // changes and display/hide/move/jump commands.
        // Direct port of WinForms TextForm.ParseTextList (line 599).
        // --------------------------------------------------------------------
        static void ParseTextList(string srctext, out List<ConversationStep> simpleList, bool enableTextEngineRework)
        {
            simpleList = new List<ConversationStep>();
            if (string.IsNullOrEmpty(srctext))
            {
                return;
            }

            uint lastPosstion = 0x0;
            int lastPosstionIndex = 0;

            int textstart = 0;
            int len = srctext.Length;
            for (int i = 0; i < len;)
            {
                if (srctext[i] != '@')
                {
                    i++;
                    continue;
                }
                int codestart = i;

                uint code1 = SubHex4(srctext, i + 1);
                i += 5;

                // Lookahead one code
                int next_i = i;

                uint code2 = 0;
                next_i += skip_linebreak(srctext, next_i); // FE7 sometimes has \r\n between codes
                if (next_i <= len - 5 && srctext[next_i] == '@')
                {
                    code2 = SubHex4(srctext, next_i + 1);
                    next_i += 5;
                }

                if (code1 == 0x80 && CheckTextEngineRework_ParseTextList(code2, srctext, ref next_i, enableTextEngineRework))
                {
                    i = next_i;
                    continue;
                }

                if (code1 >= 8 && code1 <= 0xF)
                {
                    // Position code @0008-@000F. Any serif text before it is emitted as its own step.
                    string seriftext = Substr(srctext, textstart, i - 5 - textstart);
                    if (seriftext != "")
                    {
                        var current = new ConversationStep
                        {
                            SrcText = seriftext,
                            Code1 = lastPosstion,
                            Code2 = 0,
                            Code3 = 0,
                        };
                        simpleList.Add(current);

                        textstart = i - 5;
                    }

                    lastPosstion = code1;
                    if (code2 > 0)
                    {
                        lastPosstionIndex = i - 5;
                    }
                    continue;
                }

                if ((code1 == 0x0010 && code2 > 0x100) || IsMoveOrJump(code1, code2) || code1 == 0x11)
                {
                    i = next_i;

                    string seriftext;
                    if (lastPosstionIndex < textstart)
                    {
                        seriftext = Substr(srctext, textstart, codestart - textstart);
                    }
                    else if (lastPosstionIndex == textstart && codestart - textstart > 7)
                    {
                        // mid-script display code without leading pos prefix
                        seriftext = Substr(srctext, textstart, codestart - textstart);
                    }
                    else
                    {
                        seriftext = Substr(srctext, textstart, lastPosstionIndex - textstart);
                        codestart = lastPosstionIndex;
                    }

                    if (lastPosstion <= 0)
                    {
                        // FE7: @0010 sometimes appears with no preceding position
                        lastPosstion = 0x8;
                    }

                    if (code1 == 0x11)
                    {
                        if (code2 > 0)
                        {
                            i = i - 5;
                            code2 = 0;
                        }
                    }

                    if (seriftext != "" && CheckPosCodeOnly(seriftext) == false)
                    {
                        var pre = new ConversationStep
                        {
                            SrcText = seriftext,
                            Code1 = lastPosstion,
                            Code2 = 0,
                            Code3 = 0,
                        };
                        simpleList.Add(pre);
                    }

                    string codetext = Substr(srctext, codestart, i - codestart);
                    {
                        var current = new ConversationStep
                        {
                            SrcText = codetext,
                            Code1 = lastPosstion,
                            Code2 = code1,
                            Code3 = code2,
                        };
                        simpleList.Add(current);
                    }
                    textstart = i;

                    if (code1 == 0x0080)
                    {
                        // Move command: the position pointer rewinds by 2 since
                        // the destination is now where the unit lives.
                        lastPosstion = lastPosstion - 0x0002;
                    }
                    continue;
                }
            }

            // Trailing remainder
            {
                string seriftext = Substr(srctext, textstart);
                if (seriftext != "")
                {
                    var current = new ConversationStep
                    {
                        SrcText = seriftext,
                        Code1 = lastPosstion,
                        Code2 = 0,
                        Code3 = 0,
                    };
                    simpleList.Add(current);
                }
            }
        }

        // --------------------------------------------------------------------
        // Direct port of WinForms TextForm.UpdatePosstion (line 509).
        // After parsing, this propagates "which face is at which slot" state
        // across the parsed steps so each step's Units[] reflects what would
        // be on screen at that moment.
        // --------------------------------------------------------------------
        static void UpdatePosstion(string srctext, ref List<ConversationStep> simpleList)
        {
            uint[] units = new uint[9];
            // CheckText (line-width validation) is omitted in Core — error
            // checking is a UI-side concern and not needed for read-only
            // playback.

            int len = simpleList.Count;
            for (int i = 0; i < len; i++)
            {
                ConversationStep code = simpleList[i];
                code.Units = SliceFirst8(units);

                if (code.Code1 < 0x8)
                {
                    continue;
                }

                uint pos = code.Code1 - 0x8;
                if (pos >= units.Length)
                {
                    code.Error = "Unit position out of 0x08..0xF range.";
                    continue;
                }

                if (code.Code2 == 0x10)
                {
                    // Display
                    units[pos] = code.Code3;
                }
                code.Units = SliceFirst8(units);

                if (code.Code2 == 0x10)
                {
                    continue;
                }
                else if (code.Code2 == 0x11)
                {
                    // Hide
                    units[pos] = 0;
                }
                else if (IsMoveOrJump(code.Code2, code.Code3))
                {
                    uint newPos = code.Code3 - 0xA;
                    if (newPos >= units.Length)
                    {
                        // Different command
                        continue;
                    }
                    if (pos == newPos)
                    {
                        // Self-target = jump
                        code.IsJump = true;
                        continue;
                    }
                    // If another unit is already at the destination, swap them.
                    (units[pos], units[newPos]) = (units[newPos], units[pos]);
                }
            }
        }

        // Hex parser: WinForms used U.atoh(U.substr(text, i+1, 4)). We inline a
        // tiny equivalent to avoid pulling U.cs internals.
        static uint SubHex4(string s, int start)
        {
            if (start + 4 > s.Length) return 0;
            uint v = 0;
            for (int k = 0; k < 4; k++)
            {
                char c = s[start + k];
                v <<= 4;
                if (c >= '0' && c <= '9') v |= (uint)(c - '0');
                else if (c >= 'a' && c <= 'f') v |= (uint)(c - 'a' + 10);
                else if (c >= 'A' && c <= 'F') v |= (uint)(c - 'A' + 10);
                else return 0;
            }
            return v;
        }

        // Substring helpers matching WinForms U.substr semantics (negative/short
        // requests return "" rather than throwing).
        static string Substr(string s, int start)
        {
            if (start < 0 || start >= s.Length) return "";
            return s.Substring(start);
        }

        static string Substr(string s, int start, int length)
        {
            if (length <= 0 || start < 0 || start >= s.Length) return "";
            if (start + length > s.Length) length = s.Length - start;
            return s.Substring(start, length);
        }

        static uint[] SliceFirst8(uint[] units)
        {
            // Always return a fresh 8-slot copy to avoid aliasing.
            uint[] copy = new uint[8];
            for (int k = 0; k < 8 && k < units.Length; k++) copy[k] = units[k];
            return copy;
        }
    }
}
