// SPDX-License-Identifier: GPL-3.0-or-later
// Minimal, scoped Core decode helpers backing the Avalonia Text Editor's two
// rich-text outgoing jumps (#1108):
//
//   * The portrait char-jump (WF TextForm.MakePortait / TextListSpShowCharLabel_Click)
//     — find the first portrait face id displayed by a decoded dialogue script.
//   * The escape-code insert dialog (WF TextScriptFormCategorySelectForm) — load
//     the shipped text-escape + text-category tables so the Avalonia category
//     picker shows REAL escape codes instead of hardcoded stubs.
//
// This intentionally does NOT port EventScript.DisAssemble. The portrait decode
// reuses the existing ConversationScriptParser (a faithful WF parser port) and
// the escape/category loaders reuse the same config-data files WF reads.
//
// Everything here is READ-ONLY (no ROM writes), bounds-guarded, and never throws
// — every public entry point wraps its work in try/catch and degrades to a safe
// empty/null result so a headless or early call can't crash the caller.
using System;
using System.Collections.Generic;
using System.IO;

namespace FEBuilderGBA
{
    /// <summary>
    /// A single text-escape mapping row (WF
    /// <c>TextScriptFormCategorySelectForm.TextEscape</c>): the <c>@XXXX</c> code,
    /// its human-readable info string, and the category bucket it belongs to.
    /// </summary>
    public readonly record struct TextEscapeEntry(string Code, string Info, string Category);

    /// <summary>
    /// Scoped Core helpers for the Avalonia Text Editor rich-text jumps (#1108).
    /// </summary>
    public static class TextRichControlDecode
    {
        /// <summary>
        /// The visitor sentinel face id. When a Display code's argument is
        /// <c>0xFFFF</c> the dialogue shows "the visited character" (resolved at
        /// runtime by the game), so there is no concrete portrait to jump to. The
        /// caller treats this as "do not navigate".
        /// </summary>
        public const uint VisitorSentinel = 0xFFFF;

        /// <summary>
        /// Find the FIRST portrait face id displayed by a decoded dialogue script.
        /// Mirrors WinForms <c>TextForm.MakePortait</c>:
        /// <list type="bullet">
        ///   <item>If the first Display step (<c>Code2 == 0x10</c>) has
        ///   <c>Code3 == 0xFFFF</c>, returns <see cref="VisitorSentinel"/> — the
        ///   caller does NOT navigate.</item>
        ///   <item>Else if <c>Code3 &gt;= 0x100</c>, returns <c>Code3 - 0x100</c>
        ///   (the face id; WF stores it as face-id + 0x100).</item>
        ///   <item>Returns <c>null</c> when no Display code is present.</item>
        /// </list>
        /// Pure string decode — no ROM access. The TextEngineRework flag is passed
        /// as <c>false</c>: the portrait display code is position-independent so
        /// the variable-length opcode table is not needed for this lookup, and
        /// keeping it off keeps the decode simple and total. Never throws.
        ///
        /// <para>The face-id-0 boundary <c>@0010@0100</c> is handled by a minimal
        /// post-loop fallback, NOT the parser: <see cref="ConversationScriptParser"/>
        /// is a verbatim port of WF <c>TextForm.ParseTextList</c> which splits a
        /// Display step only when <c>code2 &gt; 0x100</c> (STRICT), so the exact
        /// boundary <c>@0010@0100</c> (face id 0) never produces a Display step and
        /// would otherwise be dropped. The fallback fires ONLY when the parser found
        /// no portrait at all, and matches ONLY this single boundary sequence.</para>
        /// </summary>
        /// <param name="decodedText">Decoded dialogue text with escape codes in
        /// the <c>@XXXX</c> hex-literal form.</param>
        /// <returns>The face id, <see cref="VisitorSentinel"/>, or <c>null</c>.</returns>
        public static uint? FindFirstPortraitFaceId(string decodedText)
        {
            if (string.IsNullOrEmpty(decodedText))
            {
                return null;
            }

            try
            {
                List<ConversationStep> steps = ConversationScriptParser.ParseScript(decodedText, false);
                if (steps == null)
                {
                    return null;
                }

                foreach (ConversationStep step in steps)
                {
                    if (step == null)
                    {
                        continue;
                    }
                    if (step.Code2 != 0x10)
                    {
                        continue;
                    }

                    // First Display code wins (WF MakePortait reads the selected
                    // step's Code3; here we surface the first displayed face).
                    if (step.Code3 == VisitorSentinel)
                    {
                        return VisitorSentinel;
                    }
                    if (step.Code3 >= 0x100)
                    {
                        return step.Code3 - 0x100;
                    }

                    // A Display code with an out-of-range argument is malformed;
                    // skip it and keep scanning for a well-formed one.
                }

                // Parser found no portrait. The strict WF-faithful split
                // (`code2 > 0x100`) drops the single face-id-0 boundary
                // `@0010@0100`. Recover ONLY that exact sequence here (bounded,
                // case-insensitive on the hex digits, optional \r\n between the
                // two codes to mirror the parser's skip_linebreak). Match -> 0u.
                if (HasFaceIdZeroDisplaySequence(decodedText))
                {
                    return 0u;
                }

                return null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Bounded, never-throws scan for the literal face-id-0 Display sequence
        /// <c>@0010</c> immediately followed by <c>@0100</c> (an optional
        /// <c>\r\n</c> allowed between them, matching the parser's
        /// <c>skip_linebreak</c>). Hex digits are matched case-insensitively. This
        /// is the ONLY boundary the strict <c>code2 &gt; 0x100</c> parser split
        /// drops; it does NOT match the visitor (<c>@FFFF</c>) or any other code.
        /// </summary>
        static bool HasFaceIdZeroDisplaySequence(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            int from = 0;
            while (true)
            {
                int idx = text.IndexOf("@0010", from, StringComparison.OrdinalIgnoreCase);
                if (idx < 0)
                {
                    return false;
                }

                int next = idx + 5; // length of "@0010"
                // Optional CR/LF between the display code and its argument.
                if (next + 1 < text.Length && text[next] == '\r' && text[next + 1] == '\n')
                {
                    next += 2;
                }

                if (next + 5 <= text.Length &&
                    string.Compare(text, next, "@0100", 0, 5, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    return true;
                }

                from = idx + 5;
            }
        }

        /// <summary>
        /// Load the shipped text-escape table (WF
        /// <c>TextScriptFormCategorySelectForm.LoadTextEscapeList</c>). Reads
        /// <c>config/data/text_escape_*.txt</c> (skipping comment / other-language
        /// lines), then appends any patch-added escapes from the active
        /// <see cref="TextEscape"/>. When <paramref name="isDetail"/> is false the
        /// move/load + position escapes are filtered out (WF <c>IsDetailOnly</c>).
        /// Returns an empty list on any failure. Never throws.
        /// </summary>
        public static List<TextEscapeEntry> LoadEscapeEntries(bool isDetail)
        {
            var result = new List<TextEscapeEntry>();
            try
            {
                string[] lines;
                try
                {
                    lines = File.ReadAllLines(U.ConfigDataFilename("text_escape_"));
                }
                catch (Exception)
                {
                    lines = Array.Empty<string>();
                }

                foreach (string raw in lines)
                {
                    string line = raw;
                    if (U.IsComment(line) || U.OtherLangLine(line))
                    {
                        continue;
                    }
                    line = U.ClipComment(line);
                    if (line == "")
                    {
                        continue;
                    }
                    string[] sp = line.Split('\t');
                    if (sp.Length < 3)
                    {
                        continue;
                    }

                    string code = sp[0];
                    string info = sp[1];
                    string category = sp[2];

                    if (!isDetail && IsDetailOnly(category))
                    {
                        continue;
                    }

                    result.Add(new TextEscapeEntry(code, info, category));
                }

                // Patch-added escape sequences (WF appends Program.TextEscape's
                // snapshot). Guard the cache being unwired (headless / early call).
                // Info order is info + feditorAdv — exact WF parity with
                // TextScriptFormCategorySelectForm.cs:76 (te.Info = t.Value.info +
                // t.Value.feditorAdv).
                var snapshot = CoreState.TextEscape?.GetAddEscapeMappingSnapshot();
                if (snapshot != null)
                {
                    foreach (var kv in snapshot)
                    {
                        result.Add(new TextEscapeEntry(kv.Key, kv.Value.info + kv.Value.feditorAdv, ""));
                    }
                }
            }
            catch (Exception)
            {
                // Best-effort: return whatever we collected so far.
            }
            return result;
        }

        /// <summary>
        /// Load the text-category table (WF reads
        /// <c>config/data/text_category_*.txt</c> via <c>U.LoadTSVResourcePair</c>).
        /// Each pair is (category-key like <c>{DISPLAY}</c>, human label). When
        /// <paramref name="isDetail"/> is false the move/load + position categories
        /// are filtered out (WF <c>IsDetailOnly</c>). Returns an empty list on any
        /// failure. Never throws.
        /// </summary>
        public static List<(string Category, string Label)> LoadEscapeCategories(bool isDetail)
        {
            var result = new List<(string, string)>();
            try
            {
                // isRequired:false so a missing file degrades to an empty dict
                // (no IAppServices error dialog) — keeps this loader headless-safe.
                Dictionary<string, string> dic =
                    U.LoadTSVResourcePair2(U.ConfigDataFilename("text_category_"), isRequired: false);
                foreach (var pair in dic)
                {
                    if (!isDetail && IsDetailOnly(pair.Key))
                    {
                        continue;
                    }
                    result.Add((pair.Key, pair.Value));
                }
            }
            catch (Exception)
            {
                // Best-effort.
            }
            return result;
        }

        /// <summary>
        /// WF <c>TextScriptFormCategorySelectForm.IsDetailOnly</c>: the move/load
        /// and position categories are only shown in detail mode.
        /// </summary>
        static bool IsDetailOnly(string category)
        {
            return category == "{MOVE_LOAD}" || category == "{POSITION}";
        }
    }
}
