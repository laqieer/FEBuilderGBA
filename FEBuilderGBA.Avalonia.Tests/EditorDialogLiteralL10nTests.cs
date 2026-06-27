// SPDX-License-Identifier: GPL-3.0-or-later
// Issue #1610 — Untranslated dialog literals across editor views.
//
// These tests are the CI gate for the #1610 localisation work. They guard two
// independent invariants for the six scoped editor views:
//
//   1. CODE-BEHIND GATE (Copilot plan-review finding): every user-facing
//      CoreState.Services?.ShowInfo/ShowError/ShowWarning/ShowYesNo call in the
//      six scoped *.axaml.cs files passes a LOCALIZED message — i.e. an
//      R._(...) call or a plain identifier (a Core-returned / pre-localized
//      variable) — NOT a bare English string literal, an interpolated $"..."
//      string, or a "a" + b string concatenation. This proves the wrapping
//      actually happened (L10nCoverageTest only scans AXAML, never code-behind).
//
//   2. TRANSLATION-COVERAGE GATE: every English string newly wrapped in R._()
//      across the six files has a non-empty Japanese AND Chinese entry in
//      config/translate/{ja,zh}.txt, so a ja/zh user actually sees a translated
//      dialog (mirrors L10nCoverageTest's "0 PartiallyTranslated" success
//      metric, but for these code-behind literals).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FEBuilderGBA.Avalonia.GapSweep;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    public class EditorDialogLiteralL10nTests
    {
        /// <summary>
        /// The six editor views in #1610 scope. Paths are relative to
        /// <c>FEBuilderGBA.Avalonia/Views/</c>.
        /// </summary>
        static readonly string[] ScopedViews =
        {
            "MapStyleEditorView.axaml.cs",
            "EventCondView.axaml.cs",
            "AIScriptView.axaml.cs",
            "MapSettingDifficultyView.axaml.cs",
            "ImageTSAEditorView.axaml.cs",
            "ImageUnitPaletteView.axaml.cs",
        };

        // Locates the START of a CoreState.Services(?)?.Show{Info,Error,Warning,
        // YesNo}( call. The FULL argument expression is then captured by a
        // balanced-paren scanner (CaptureShowArgs) — NOT by the regex — so a
        // comma inside the expression (e.g. R._("{0}", x) or a trailing
        // + " literal") never truncates the captured argument (Copilot bot
        // review on PR #1627). The Avalonia IAppServices Show* methods all take
        // exactly one string argument, so the entire `( ... )` is the message.
        static readonly Regex ShowCallStart = new(
            @"CoreState\.Services\??\.Show(?:Info|Error|Warning|YesNo)\s*\(",
            RegexOptions.Compiled);

        [Fact]
        public void ScopedViews_HaveNoBareDialogLiterals()
        {
            string viewsDir = Path.Combine(RepoRoot(), "FEBuilderGBA.Avalonia", "Views");
            var offenders = new List<string>();

            foreach (string view in ScopedViews)
            {
                string path = Path.Combine(viewsDir, view);
                Assert.True(File.Exists(path), $"Scoped view not found: {path}");
                string src = File.ReadAllText(path);

                foreach (Match m in ShowCallStart.Matches(src))
                {
                    int openParen = m.Index + m.Length - 1; // index of the '('
                    string arg = CaptureCallArgument(src, openParen).Trim();
                    if (arg.Length == 0) continue;
                    if (IsLocalized(arg)) continue;

                    int line = src.Take(m.Index).Count(c => c == '\n') + 1;
                    offenders.Add($"{view}:{line} -> {Truncate(arg, 80)}");
                }
            }

            Assert.True(
                offenders.Count == 0,
                "Bare (un-R._()) dialog message argument(s) found in scoped editor views. " +
                "Wrap each user-facing message in R._(\"...\"):\n  " +
                string.Join("\n  ", offenders));
        }

        /// <summary>
        /// Capture the ENTIRE argument expression of a call, given the index of
        /// its opening '('. Scans to the matching close-paren, honouring nested
        /// parentheses and skipping over string / interpolated-string literals so
        /// a '(' or ')' inside a string never miscounts depth. Returns the text
        /// between the outer parens (the full message expression, commas and all).
        /// </summary>
        static string CaptureCallArgument(string src, int openParenIndex)
        {
            int i = openParenIndex + 1;
            int start = i;
            int depth = 1;
            bool inStr = false;
            while (i < src.Length && depth > 0)
            {
                char c = src[i];
                if (inStr)
                {
                    if (c == '\\') { i += 2; continue; }
                    if (c == '"') inStr = false;
                }
                else
                {
                    if (c == '"') inStr = true;
                    else if (c == '(') depth++;
                    else if (c == ')') { depth--; if (depth == 0) break; }
                }
                i++;
            }
            return i <= src.Length && i > start ? src.Substring(start, i - start) : string.Empty;
        }

        // Strips every R._( "literal" [, args] ) call from an expression by
        // blanking the whole call (open-paren to its matching close-paren). What
        // remains lets us detect any English string literal that escaped the
        // localization wrap.
        static readonly Regex RWrapCall = new(@"R\._\(", RegexOptions.Compiled);

        /// <summary>
        /// Is the captured Show* first-argument expression fully localized — i.e.
        /// no English string literal reaches the dialog UN-wrapped?
        ///
        /// Accepted:
        ///   - a plain identifier / member access (a Core-returned or
        ///     pre-localized value such as <c>err</c>, <c>refuseMessage</c>),
        ///   - an R._("...") call,
        ///   - an interpolated <c>$"{R._("...")} {value}"</c> where EVERY string
        ///     literal segment is itself an R._(...) argument (the localized
        ///     text is wrapped; only runtime values like a path / count / message
        ///     are interpolated raw).
        /// Rejected:
        ///   - a bare "literal",
        ///   - an interpolated <c>$"raw English {x}"</c>,
        ///   - a <c>"a" + b</c> concatenation whose literal is English.
        /// </summary>
        static bool IsLocalized(string arg)
        {
            // Remove every R._( ... ) call (balanced-paren aware) so we can see
            // whether any ENGLISH literal text survives outside the wrap.
            string stripped = StripRWrapCalls(arg);

            // After stripping all R._(...) calls, the only remaining quotes belong
            // to the outer expression: either a bare/concatenated literal, or the
            // delimiters + literal segments of an interpolated $"...". A violation
            // is any literal RUN that contains an ASCII letter (English text). A
            // run with only spaces / punctuation (e.g. the " " / ". " between
            // interpolation holes in `$"{R._(...)} {x}. {R._(...)}"`) is benign —
            // every English word in such a string was inside an R._() wrap.
            return !HasEnglishLetterInStringLiteral(stripped);
        }

        /// <summary>
        /// True if <paramref name="expr"/> contains a double-quoted run that
        /// includes an ASCII letter. Interpolation holes (<c>{ ... }</c>) inside a
        /// quoted run are skipped — their contents are runtime values, not
        /// translatable text.
        /// </summary>
        static bool HasEnglishLetterInStringLiteral(string expr)
        {
            bool inStr = false;
            int braceDepth = 0;
            for (int k = 0; k < expr.Length; k++)
            {
                char c = expr[k];
                if (!inStr)
                {
                    if (c == '"') { inStr = true; braceDepth = 0; }
                    continue;
                }
                // inside a string literal
                if (c == '\\') { k++; continue; }
                if (braceDepth == 0 && c == '"') { inStr = false; continue; }
                if (c == '{') { braceDepth++; continue; }
                if (c == '}') { if (braceDepth > 0) braceDepth--; continue; }
                if (braceDepth == 0 && ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z')))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Replace each <c>R._( ... )</c> call (including its arguments) with a
        /// neutral placeholder, honouring balanced parentheses and skipping over
        /// string literals so a `)` inside a string doesn't end the call early.
        /// </summary>
        static string StripRWrapCalls(string expr)
        {
            var sb = new System.Text.StringBuilder(expr.Length);
            int i = 0;
            while (i < expr.Length)
            {
                Match m = RWrapCall.Match(expr, i);
                if (!m.Success)
                {
                    sb.Append(expr, i, expr.Length - i);
                    break;
                }
                // Append text before the R._( match.
                sb.Append(expr, i, m.Index - i);
                // Skip the whole R._( ... ) call by scanning to the matching ')'.
                int j = m.Index + m.Length; // just after "R._("
                int depth = 1;
                bool inStr = false;
                while (j < expr.Length && depth > 0)
                {
                    char c = expr[j];
                    if (inStr)
                    {
                        if (c == '\\') { j += 2; continue; }
                        if (c == '"') inStr = false;
                    }
                    else
                    {
                        if (c == '"') inStr = true;
                        else if (c == '(') depth++;
                        else if (c == ')') depth--;
                    }
                    j++;
                }
                sb.Append("__LOC__"); // placeholder for the stripped call
                i = j;
            }
            return sb.ToString();
        }

        // Guard the detector itself so ScopedViews_HaveNoBareDialogLiterals can
        // never pass vacuously (a broken IsLocalized that accepts everything).
        [Theory]
        [InlineData("\"Map style data written.\"", false)]                       // bare literal
        [InlineData("$\"Imported {count} bytes.\"", false)]                       // interpolation w/ raw English
        [InlineData("\"a\" + b", false)]                                          // concatenation
        [InlineData("R._(\"Map style data written.\")", true)]                    // wrapped
        [InlineData("R._(\"Imported {0} bytes.\", count)", true)]                 // wrapped w/ format arg
        [InlineData("$\"{R._(\"Import failed:\")} {ex.Message}\"", true)]         // every word wrapped
        [InlineData("R._(\"{0}\", x) + \" extra\"", false)]                       // unwrapped tail after R._ (Copilot #1627)
        [InlineData("err", true)]                                                 // pre-localized variable
        [InlineData("refuseMessage", true)]                                       // pre-localized variable
        public void IsLocalized_ClassifiesExpressions(string expr, bool expected)
        {
            Assert.Equal(expected, IsLocalized(expr));
        }

        // Proves the full-argument capture (CaptureCallArgument) does NOT
        // truncate at a comma inside the Show* expression, so an unlocalized tail
        // appended after an R._(...) call is still detected (Copilot bot review,
        // PR #1627).
        [Fact]
        public void CaptureCallArgument_CapturesFullExpressionPastCommas()
        {
            const string src = "CoreState.Services.ShowError(R._(\"{0}\", x) + \" extra\");";
            int open = src.IndexOf('(');
            string arg = CaptureCallArgument(src, open);
            Assert.Equal("R._(\"{0}\", x) + \" extra\"", arg);
            Assert.False(IsLocalized(arg)); // the " extra" tail is unlocalized
        }

        [Fact]
        public void NewlyWrappedLiterals_HaveJaAndZhTranslations()
        {
            string repoRoot = RepoRoot();
            string viewsDir = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views");

            // Collect every R._("literal"...) key used in the scoped views.
            var keys = new HashSet<string>(StringComparer.Ordinal);
            var keyRegex = new Regex(@"R\._\(\s*""(?<lit>(?:[^""\\]|\\.)*)""", RegexOptions.Compiled);
            foreach (string view in ScopedViews)
            {
                string src = File.ReadAllText(Path.Combine(viewsDir, view));
                foreach (Match m in keyRegex.Matches(src))
                {
                    string lit = Unescape(m.Groups["lit"].Value);
                    if (lit.Length > 0) keys.Add(lit);
                }
            }

            Assert.NotEmpty(keys);

            string translateDir = Path.Combine(repoRoot, "config", "translate");
            var ja = L10nScanner.LoadForwardMap(Path.Combine(translateDir, "ja.txt"));
            var zh = L10nScanner.LoadForwardMap(Path.Combine(translateDir, "zh.txt"));

            var missing = new List<string>();
            foreach (string key in keys.OrderBy(k => k, StringComparer.Ordinal))
            {
                bool jaOk = ja.TryGetValue(key, out var jv) && !string.IsNullOrWhiteSpace(jv);
                bool zhOk = zh.TryGetValue(key, out var zv) && !string.IsNullOrWhiteSpace(zv);
                if (!jaOk || !zhOk)
                    missing.Add($"'{Truncate(key, 70)}' (ja={(jaOk ? "ok" : "MISSING")}, zh={(zhOk ? "ok" : "MISSING")})");
            }

            Assert.True(
                missing.Count == 0,
                "Editor dialog literals wrapped in R._() but missing a ja/zh translation entry " +
                "in config/translate/{ja,zh}.txt:\n  " + string.Join("\n  ", missing));
        }

        // ----------------------------------------------------------------------

        static string Unescape(string s) =>
            s.Replace("\\\"", "\"").Replace("\\\\", "\\");

        static string Truncate(string s, int max) =>
            s.Length <= max ? s : s.Substring(0, max) + "…";

        static string RepoRoot()
        {
            string start = AppDomain.CurrentDomain.BaseDirectory;
            for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "FEBuilderGBA.sln")))
                    return dir.FullName;
            }
            throw new InvalidOperationException(
                $"Could not locate FEBuilderGBA.sln starting from {start}");
        }
    }
}
