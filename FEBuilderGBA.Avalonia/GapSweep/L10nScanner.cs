// SPDX-License-Identifier: GPL-3.0-or-later
// FEBuilderGBA gap-sweep tooling (#374) — Phase 6: localisation sweep.
//
// Issue #356 traces the user-visible symptom: when the Avalonia GUI is set to
// Japanese or Chinese, several labels (`Identity`, `Base Stats`, `Weapon
// Levels`, `Growth Rates (%)`, …) stay in English. Most of those literals
// live as plain AXAML attribute values (no `{x:Static c:Strings.Foo}` binding,
// no `R._()` call) so the translation layer never sees them.
//
// This scanner produces a mechanical inventory of every English-looking AXAML
// literal in `FEBuilderGBA.Avalonia/Views/**/*.axaml` and joins it against
// each language's translation table in `config/translate/<lang>.txt`. Each
// literal lands in one of four buckets:
//
//   Translated         — every target language has a translation
//   PartiallyTranslated — some languages have it, others don't
//   Untranslated       — no target language has a translation (the backlog)
//   NonEnglish         — source literal already contains CJK / Hangul (out
//                        of scope: someone wrote it in the target language
//                        directly, which is fine but flagged for sanity)
//
// Translation files have the format (see `MyTranslateResourceLow.LoadResource`):
//
//   :Japanese source string
//   Translated string
//   <blank line>
//
// `config/translate/en.txt` translates Japanese → English. For Avalonia
// (which uses English literals directly) we follow the same reverse-lookup
// chain the runtime uses:
//
//   AXAML literal "Save" → en.txt reverse map → Japanese key "書き込み"
//                       → ja.txt forward map → Japanese translation
//                       → zh.txt forward map → Chinese translation
//
// An AXAML literal counts as "translated" in a target language iff that
// chain produces a non-empty result. The scanner is deliberately a static
// analyzer: it does not modify any AXAML or translation file. The report is
// the per-file backlog for follow-up gap-fix PRs.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace FEBuilderGBA.Avalonia.GapSweep
{
    /// <summary>
    /// How a literal scored against the joined translation tables.
    /// Ordered roughly by "actionability" — the buckets that the report
    /// surfaces in descending order of "you should look at this".
    /// </summary>
    public enum L10nVerdict
    {
        /// <summary>Source literal contains CJK / Hangul characters — already localized in-line.</summary>
        NonEnglish,
        /// <summary>Every target language has a non-empty translation entry.</summary>
        Translated,
        /// <summary>Some languages have a translation, others don't.</summary>
        PartiallyTranslated,
        /// <summary>No target language has a translation entry — the backlog.</summary>
        Untranslated,
    }

    /// <summary>
    /// One AXAML literal finding. The translation-status dictionary maps
    /// language code → has-non-empty-entry (true/false). Original casing /
    /// punctuation of <see cref="Literal"/> is preserved verbatim from the
    /// AXAML attribute value — the report's normalisation key is internal.
    /// </summary>
    /// <param name="AxamlPath">Repo-relative AXAML file path with forward slashes.</param>
    /// <param name="LineNumber">1-based source line of the element carrying the attribute (best-effort via IXmlLineInfo).</param>
    /// <param name="ElementName">Element local-name (e.g. "Button", "TextBox", "TextBlock").</param>
    /// <param name="AttributeName">Attribute local-name (e.g. "Text", "Content", "ToolTip.Tip").</param>
    /// <param name="Literal">Original-cased literal value.</param>
    /// <param name="TranslationStatus">Language code → did the lookup chain produce a non-empty translation.</param>
    /// <param name="Verdict">Bucket classification.</param>
    public record L10nFinding(
        string AxamlPath,
        int LineNumber,
        string ElementName,
        string AttributeName,
        string Literal,
        IReadOnlyDictionary<string, bool> TranslationStatus,
        L10nVerdict Verdict);

    /// <summary>
    /// Phase 6: scan every Avalonia AXAML view for unbound English-looking
    /// literals and join against the project's translation tables.
    /// </summary>
    public static class L10nScanner
    {
        /// <summary>
        /// AXAML attribute local-names that carry user-visible labels.
        /// Mirrors <c>LabelDiffScanner.AvLabelAttributes</c> so the two
        /// scanners stay in sync. `ToolTip.Tip` is the Avalonia attached-
        /// property form (this was a real bug surfaced in PR #377 Phase 2
        /// before Copilot caught it — do NOT regress).
        /// </summary>
        static readonly HashSet<string> CandidateAttributes = new(StringComparer.Ordinal)
        {
            "Text",
            "Content",
            "Header",
            "ToolTip",
            "ToolTip.Tip",
            "Watermark",
        };

        /// <summary>
        /// Template containers — same set as <c>LabelDiffScanner.AvTemplateContainers</c>.
        /// Elements nested inside any of these describe templates rather than the realised
        /// layout; their literals never appear as actual UI text in the running app.
        /// </summary>
        static readonly HashSet<string> TemplateContainers = new(StringComparer.Ordinal)
        {
            "Design.DataContext",
            "Style",
            "Styles",
            "DataTemplate",
            "ControlTemplate",
            "ItemTemplate",
            "ItemsPanelTemplate",
            "HierarchicalDataTemplate",
        };

        /// <summary>
        /// Default language set the Phase 6 sweep joins against. English is the
        /// SOURCE for AXAML literals so it never appears here — these are the
        /// target translations we look up.
        /// </summary>
        public static readonly IReadOnlyList<string> DefaultLanguages = new[] { "ja", "zh", "ko" };

        /// <summary>
        /// Run the full Phase 6 scan over <paramref name="repoRoot"/>. Loads
        /// translation tables for each language in <paramref name="languages"/>
        /// (plus the reverse-English map from en.txt), then walks every
        /// `*.axaml` under `FEBuilderGBA.Avalonia/Views/` looking for literal
        /// attribute values that pass the "English-looking" heuristic.
        ///
        /// Returns findings ordered by (AxamlPath asc, LineNumber asc) so the
        /// report is stable across runs.
        /// </summary>
        public static IReadOnlyList<L10nFinding> Scan(
            string repoRoot,
            IReadOnlyList<string>? languages = null)
        {
            if (string.IsNullOrEmpty(repoRoot))
                throw new ArgumentException("repoRoot must be non-empty", nameof(repoRoot));
            if (!Directory.Exists(repoRoot))
                throw new DirectoryNotFoundException($"repo root not found: {repoRoot}");

            languages ??= DefaultLanguages;

            // Load reverse English → Japanese map from en.txt. en.txt is the
            // pivot: AXAML uses English literals; en.txt maps Japanese key
            // → English value; the reverse map flips it so we can find the
            // Japanese key from an Avalonia literal, then forward-lookup in
            // ja.txt / zh.txt / ko.txt.
            string translateDir = Path.Combine(repoRoot, "config", "translate");
            string enPath = Path.Combine(translateDir, "en.txt");
            var reverseEnMap = LoadReverseEnglishMap(enPath);

            // Load forward maps (Japanese key → translated value) per target
            // language. Languages with no corresponding *.txt simply produce
            // an empty dictionary — every literal is marked "not translated"
            // in that language.
            var languageMaps = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            foreach (string lang in languages)
            {
                string langPath = Path.Combine(translateDir, lang + ".txt");
                languageMaps[lang] = LoadForwardMap(langPath);
            }

            // Walk the views.
            var viewsDir = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views");
            var findings = new List<L10nFinding>();
            if (!Directory.Exists(viewsDir))
                return findings;

            foreach (string axamlPath in Directory.EnumerateFiles(viewsDir, "*.axaml", SearchOption.AllDirectories)
                                                 .OrderBy(p => p, StringComparer.Ordinal))
            {
                string relPath = ToRelative(repoRoot, axamlPath);
                ScanFile(axamlPath, relPath, reverseEnMap, languageMaps, languages, findings);
            }

            // Stable ordering: by file then by line.
            return findings
                .OrderBy(f => f.AxamlPath, StringComparer.Ordinal)
                .ThenBy(f => f.LineNumber)
                .ThenBy(f => f.Literal, StringComparer.Ordinal)
                .ToList();
        }

        // =====================================================================
        // Code-literal sweep (#1635)
        //
        // The AXAML sweep above only sees attribute values. A large class of
        // user-facing strings — status / error / dialog messages — instead live
        // as `R._("literal")` calls inside Avalonia ViewModels and code-behind
        // (`*.cs`). The AXAML scanner is blind to them, so CI stayed green while
        // ~334 such strings rendered in English under Japanese / Chinese UI.
        //
        // `ScanCodeLiterals` closes that gap: it enumerates every distinct
        // `R._("...")` first-string-argument across `FEBuilderGBA.Avalonia/**/*.cs`
        // and joins each against the same reverse-English → forward-map chain the
        // runtime uses (see `MyTranslateResourceLow.str`). Findings reuse the
        // existing verdict buckets so the report / gate logic is identical to the
        // AXAML side.
        // =====================================================================

        /// <summary>
        /// One `R._("literal")` finding from a `.cs` source file. Mirrors
        /// <see cref="L10nFinding"/> but keyed on a source-file path rather than an
        /// AXAML element/attribute, since a code literal has no XML host.
        /// </summary>
        /// <param name="SourcePath">Repo-relative `.cs` path with forward slashes.</param>
        /// <param name="LineNumber">1-based source line of the `R._(` call.</param>
        /// <param name="Literal">Decoded literal value (C# escapes already resolved).</param>
        /// <param name="TranslationStatus">Language code → did the lookup chain produce a non-empty translation.</param>
        /// <param name="Verdict">Bucket classification (same enum as the AXAML sweep).</param>
        public record CodeLiteralFinding(
            string SourcePath,
            int LineNumber,
            string Literal,
            IReadOnlyDictionary<string, bool> TranslationStatus,
            L10nVerdict Verdict);

        /// <summary>
        /// Matches the START of an <c>R._("literal"</c> call. The first string
        /// argument is then captured by a dedicated C#-string-literal scanner
        /// (<see cref="ReadCsStringLiteral"/>) so embedded escaped quotes
        /// (<c>\"</c>) never truncate it. Whitespace between <c>R._(</c> and the
        /// opening quote is tolerated.
        /// </summary>
        static readonly Regex RUnderscoreCallStart = new(
            @"\bR\._\(\s*@?""", RegexOptions.Compiled);

        /// <summary>
        /// Scan every `*.cs` under `FEBuilderGBA.Avalonia/` for `R._("literal")`
        /// calls and join each distinct literal against the translation tables.
        ///
        /// Returns one finding per (file, line) occurrence (so the report can show
        /// where a literal is used), ordered by (SourcePath asc, LineNumber asc,
        /// Literal asc). Files under `bin/` and `obj/` are skipped.
        /// </summary>
        public static IReadOnlyList<CodeLiteralFinding> ScanCodeLiterals(
            string repoRoot,
            IReadOnlyList<string>? languages = null)
        {
            if (string.IsNullOrEmpty(repoRoot))
                throw new ArgumentException("repoRoot must be non-empty", nameof(repoRoot));
            if (!Directory.Exists(repoRoot))
                throw new DirectoryNotFoundException($"repo root not found: {repoRoot}");

            languages ??= DefaultLanguages;

            string translateDir = Path.Combine(repoRoot, "config", "translate");
            var reverseEnMap = LoadReverseEnglishMap(Path.Combine(translateDir, "en.txt"));
            var languageMaps = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            foreach (string lang in languages)
                languageMaps[lang] = LoadForwardMap(Path.Combine(translateDir, lang + ".txt"));

            var avaloniaDir = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia");
            var findings = new List<CodeLiteralFinding>();
            if (!Directory.Exists(avaloniaDir))
                return findings;

            foreach (string csPath in Directory.EnumerateFiles(avaloniaDir, "*.cs", SearchOption.AllDirectories)
                                               .OrderBy(p => p, StringComparer.Ordinal))
            {
                if (IsInBinOrObj(repoRoot, csPath))
                    continue;
                string relPath = ToRelative(repoRoot, csPath);
                ScanCsFile(csPath, relPath, reverseEnMap, languageMaps, languages, findings);
            }

            return findings
                .OrderBy(f => f.SourcePath, StringComparer.Ordinal)
                .ThenBy(f => f.LineNumber)
                .ThenBy(f => f.Literal, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>
        /// In-memory variant for unit tests: scan an arbitrary C# source string
        /// for `R._("literal")` calls and classify each against the supplied maps.
        /// Avoids disk I/O so tests stay hermetic.
        /// </summary>
        public static IReadOnlyList<CodeLiteralFinding> ScanCsString(
            string csContent,
            string sourceRelPath,
            IReadOnlyDictionary<string, string> reverseEnglishMap,
            IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> languageMaps)
        {
            var findings = new List<CodeLiteralFinding>();
            var maps = languageMaps.ToDictionary(
                kv => kv.Key,
                kv => kv.Value.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal),
                StringComparer.Ordinal);
            ScanCsContent(
                csContent,
                sourceRelPath,
                reverseEnglishMap,
                maps,
                languageMaps.Keys.ToList(),
                findings);
            return findings;
        }

        static void ScanCsFile(
            string csPath,
            string sourceRelPath,
            IReadOnlyDictionary<string, string> reverseEnMap,
            IReadOnlyDictionary<string, Dictionary<string, string>> languageMaps,
            IReadOnlyList<string> languages,
            List<CodeLiteralFinding> findings)
        {
            string content;
            try
            {
                content = File.ReadAllText(csPath);
            }
            catch
            {
                return;
            }
            ScanCsContent(content, sourceRelPath, reverseEnMap, languageMaps, languages, findings);
        }

        static void ScanCsContent(
            string content,
            string sourceRelPath,
            IReadOnlyDictionary<string, string> reverseEnMap,
            IReadOnlyDictionary<string, Dictionary<string, string>> languageMaps,
            IReadOnlyList<string> languages,
            List<CodeLiteralFinding> findings)
        {
            // Blank out `//`, `///` and `/* */` comments (preserving offsets and
            // newlines so line numbers stay accurate). Without this, doc-comment
            // examples like the `R._("literal")` in THIS file's own XML docs would
            // be reported as untranslated UI strings — a false positive. String
            // literals are NOT blanked, so the comment-detector never trips on a
            // `//` that lives inside a string.
            content = StripCommentsPreservingLayout(content);

            // Track the line number incrementally. Regex.Matches yields matches in
            // ascending index order, so we only ever count the newlines BETWEEN the
            // previous match index and the current one — keeping the whole scan O(N)
            // instead of the O(N*M) a from-the-start re-count per match would cost.
            int lineNumber = 1;
            int scannedUpTo = 0;

            foreach (Match m in RUnderscoreCallStart.Matches(content))
            {
                // Advance the running line counter across the gap since the last match.
                for (int p = scannedUpTo; p < m.Index; p++)
                    if (content[p] == '\n') lineNumber++;
                scannedUpTo = m.Index;

                // The match ends just past the opening quote. `@?"` — verbatim
                // (`@"..."`) string literals double their quotes to escape them;
                // detect which form we matched so the literal scanner uses the
                // right escaping rule.
                bool verbatim = m.Value.IndexOf('@') >= 0;
                int quoteIndex = m.Index + m.Length - 1; // index of the opening '"'
                if (!ReadCsStringLiteral(content, quoteIndex, verbatim, out string literal))
                    continue;
                if (string.IsNullOrWhiteSpace(literal))
                    continue;

                var status = new Dictionary<string, bool>(StringComparer.Ordinal);
                int hit = 0;
                bool isNonEnglish = ContainsCjkOrHangul(literal);
                foreach (string lang in languages)
                {
                    bool ok = false;
                    if (languageMaps.TryGetValue(lang, out var map))
                        ok = HasTranslation(literal, reverseEnMap, map);
                    status[lang] = ok;
                    if (ok) hit++;
                }

                L10nVerdict verdict;
                if (isNonEnglish)
                    verdict = L10nVerdict.NonEnglish;
                else if (languages.Count == 0)
                    verdict = L10nVerdict.Untranslated;
                else if (hit == languages.Count)
                    verdict = L10nVerdict.Translated;
                else if (hit == 0)
                    verdict = L10nVerdict.Untranslated;
                else
                    verdict = L10nVerdict.PartiallyTranslated;

                findings.Add(new CodeLiteralFinding(
                    SourcePath: sourceRelPath,
                    LineNumber: lineNumber,
                    Literal: literal,
                    TranslationStatus: status,
                    Verdict: verdict));
            }
        }

        /// <summary>
        /// Read a C# string literal starting at <paramref name="quoteIndex"/> (the
        /// index of the opening <c>"</c>) and decode its escape sequences. Handles
        /// both regular literals (backslash escapes) and verbatim <c>@"..."</c>
        /// literals (doubled quotes). Returns false if the literal is unterminated.
        /// </summary>
        public static bool ReadCsStringLiteral(string src, int quoteIndex, bool verbatim, out string value)
        {
            value = string.Empty;
            if (quoteIndex < 0 || quoteIndex >= src.Length || src[quoteIndex] != '"')
                return false;

            var sb = new StringBuilder();
            int i = quoteIndex + 1;
            while (i < src.Length)
            {
                char c = src[i];
                if (verbatim)
                {
                    if (c == '"')
                    {
                        if (i + 1 < src.Length && src[i + 1] == '"')
                        {
                            sb.Append('"');
                            i += 2;
                            continue;
                        }
                        value = sb.ToString();
                        return true;
                    }
                    sb.Append(c);
                    i++;
                }
                else
                {
                    if (c == '\\' && i + 1 < src.Length)
                    {
                        char n = src[i + 1];
                        switch (n)
                        {
                            case 'r': sb.Append('\r'); break;
                            case 'n': sb.Append('\n'); break;
                            case 't': sb.Append('\t'); break;
                            case '0': sb.Append('\0'); break;
                            case '"': sb.Append('"'); break;
                            case '\\': sb.Append('\\'); break;
                            default: sb.Append(n); break; // best-effort for \uXXXX etc.
                        }
                        i += 2;
                        continue;
                    }
                    if (c == '"')
                    {
                        value = sb.ToString();
                        return true;
                    }
                    sb.Append(c);
                    i++;
                }
            }
            return false; // unterminated
        }

        /// <summary>
        /// True if <paramref name="s"/> contains any CJK / Hangul character — the
        /// same "already localised in-line" heuristic the AXAML sweep uses, exposed
        /// here for the code-literal path (and reusable by the gate test).
        /// </summary>
        public static bool ContainsCjkOrHangul(string s)
        {
            if (string.IsNullOrEmpty(s))
                return false;
            foreach (char c in s)
                if (IsCjkOrHangul(c))
                    return true;
            return false;
        }

        /// <summary>
        /// Blank every C# comment (`//`, `///`, `/* */`) — including its delimiters
        /// (`//`, `/*`, `*/`) AND body — by replacing each comment character with a
        /// space, while leaving newlines and overall length untouched, so subsequent
        /// regex matching keeps accurate offsets / line numbers but never sees a
        /// `R._("...")` that lives inside a comment. String and char literals are
        /// preserved verbatim (and skipped over) so a `//` or `/*` inside a string
        /// doesn't start a phantom comment. Handles verbatim (`@"..."`) and
        /// interpolated (`$"..."`, `$@"..."`) string prefixes well enough for this
        /// blanking purpose — the goal is comment removal, not a full C# lexer.
        /// </summary>
        public static string StripCommentsPreservingLayout(string src)
        {
            if (string.IsNullOrEmpty(src))
                return src ?? string.Empty;

            var sb = new StringBuilder(src.Length);
            int i = 0;
            int n = src.Length;
            while (i < n)
            {
                char c = src[i];

                // Line comment: // ... (and ///). Blank to end of line.
                if (c == '/' && i + 1 < n && src[i + 1] == '/')
                {
                    while (i < n && src[i] != '\n') { sb.Append(' '); i++; }
                    continue;
                }
                // Block comment: /* ... */. Blank everything but keep newlines.
                if (c == '/' && i + 1 < n && src[i + 1] == '*')
                {
                    sb.Append(' '); sb.Append(' '); i += 2;
                    while (i < n && !(src[i] == '*' && i + 1 < n && src[i + 1] == '/'))
                    {
                        sb.Append(src[i] == '\n' ? '\n' : ' ');
                        i++;
                    }
                    if (i < n) { sb.Append(' '); sb.Append(' '); i += 2; } // consume the closing */
                    continue;
                }
                // Char literal: '...'. Copy verbatim so an embedded // or /* is safe.
                if (c == '\'')
                {
                    sb.Append(c); i++;
                    while (i < n)
                    {
                        sb.Append(src[i]);
                        if (src[i] == '\\' && i + 1 < n) { i++; sb.Append(src[i]); i++; continue; }
                        if (src[i] == '\'') { i++; break; }
                        i++;
                    }
                    continue;
                }
                // String literal — verbatim (@"...") or regular ("...").
                if (c == '"' || (c == '@' && i + 1 < n && src[i + 1] == '"')
                             || (c == '$' && i + 1 < n && (src[i + 1] == '"' ||
                                  (src[i + 1] == '@' && i + 2 < n && src[i + 2] == '"'))))
                {
                    bool verbatim = false;
                    // Copy any $ / @ prefix and locate the opening quote.
                    while (i < n && src[i] != '"')
                    {
                        if (src[i] == '@') verbatim = true;
                        sb.Append(src[i]); i++;
                    }
                    if (i >= n) break;
                    sb.Append(src[i]); i++; // opening quote
                    while (i < n)
                    {
                        char d = src[i];
                        if (verbatim)
                        {
                            if (d == '"')
                            {
                                if (i + 1 < n && src[i + 1] == '"') { sb.Append('"'); sb.Append('"'); i += 2; continue; }
                                sb.Append('"'); i++; break;
                            }
                            sb.Append(d); i++;
                        }
                        else
                        {
                            if (d == '\\' && i + 1 < n) { sb.Append(d); sb.Append(src[i + 1]); i += 2; continue; }
                            sb.Append(d); i++;
                            if (d == '"') break;
                        }
                    }
                    continue;
                }

                sb.Append(c); i++;
            }
            return sb.ToString();
        }

        static bool IsInBinOrObj(string repoRoot, string fullPath)
        {
            string rel = Path.GetRelativePath(repoRoot, fullPath).Replace('\\', '/');
            return rel.Contains("/bin/", StringComparison.Ordinal)
                || rel.Contains("/obj/", StringComparison.Ordinal)
                || rel.StartsWith("bin/", StringComparison.Ordinal)
                || rel.StartsWith("obj/", StringComparison.Ordinal);
        }

        /// <summary>
        /// Scan a single AXAML file in-place. Internal entry point used by
        /// <see cref="Scan"/>; exposed only for unit tests that want to drive
        /// the per-file path without round-tripping through Scan's directory
        /// glob.
        /// </summary>
        public static IReadOnlyList<L10nFinding> ScanAxaml(
            string axamlPath,
            string axamlRelPath,
            IReadOnlyDictionary<string, string> reverseEnglishMap,
            IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> languageMaps)
        {
            var findings = new List<L10nFinding>();
            var maps = languageMaps.ToDictionary(
                kv => kv.Key,
                kv => kv.Value.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal),
                StringComparer.Ordinal);
            ScanFile(
                axamlPath,
                axamlRelPath,
                reverseEnglishMap.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal),
                maps,
                languageMaps.Keys.ToList(),
                findings);
            return findings;
        }

        /// <summary>
        /// In-memory variant for unit tests: takes the AXAML as an XML string
        /// (or arbitrary path label) plus already-loaded translation maps.
        /// Avoids any disk I/O so tests run hermetic.
        /// </summary>
        public static IReadOnlyList<L10nFinding> ScanXmlString(
            string xmlContent,
            string axamlRelPath,
            IReadOnlyDictionary<string, string> reverseEnglishMap,
            IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> languageMaps)
        {
            var findings = new List<L10nFinding>();
            XDocument doc;
            try
            {
                doc = XDocument.Parse(xmlContent, LoadOptions.SetLineInfo);
            }
            catch (XmlException)
            {
                return findings;
            }
            ScanDocument(
                doc,
                axamlRelPath,
                reverseEnglishMap,
                languageMaps,
                languageMaps.Keys.ToList(),
                findings);
            return findings;
        }

        static void ScanFile(
            string axamlPath,
            string axamlRelPath,
            IReadOnlyDictionary<string, string> reverseEnMap,
            IReadOnlyDictionary<string, Dictionary<string, string>> languageMaps,
            IReadOnlyList<string> languages,
            List<L10nFinding> findings)
        {
            XDocument doc;
            try
            {
                doc = XDocument.Load(axamlPath, LoadOptions.SetLineInfo);
            }
            catch
            {
                // Malformed AXAML — skip silently. Phase 7 will surface
                // parse failures explicitly; for Phase 6 we just want a
                // best-effort literal inventory.
                return;
            }

            // Re-key languageMaps as IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>
            // so ScanDocument's signature works for both internal callers and the test seam.
            var asReadOnly = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
            foreach (var kv in languageMaps)
                asReadOnly[kv.Key] = kv.Value;
            ScanDocument(doc, axamlRelPath, reverseEnMap, asReadOnly, languages, findings);
        }

        static void ScanDocument(
            XDocument doc,
            string axamlRelPath,
            IReadOnlyDictionary<string, string> reverseEnMap,
            IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> languageMaps,
            IReadOnlyList<string> languages,
            List<L10nFinding> findings)
        {
            if (doc.Root == null)
                return;

            foreach (XElement el in doc.Descendants())
            {
                if (IsInsideTemplate(el))
                    continue;

                foreach (XAttribute attr in el.Attributes())
                {
                    if (!CandidateAttributes.Contains(attr.Name.LocalName))
                        continue;

                    string raw = attr.Value;
                    if (!IsCandidateLiteral(raw, out bool isNonEnglish))
                        continue;

                    var status = new Dictionary<string, bool>(StringComparer.Ordinal);
                    int hit = 0;
                    foreach (string lang in languages)
                    {
                        bool ok = false;
                        if (languageMaps.TryGetValue(lang, out var map))
                            ok = HasTranslation(raw, reverseEnMap, map);
                        status[lang] = ok;
                        if (ok) hit++;
                    }

                    L10nVerdict verdict;
                    if (isNonEnglish)
                    {
                        verdict = L10nVerdict.NonEnglish;
                    }
                    else if (languages.Count == 0)
                    {
                        // No target languages requested — treat every literal
                        // as Untranslated (defensive: avoids the "0/0 = 100 %"
                        // false positive that would otherwise classify
                        // everything as Translated).
                        verdict = L10nVerdict.Untranslated;
                    }
                    else if (hit == languages.Count)
                    {
                        verdict = L10nVerdict.Translated;
                    }
                    else if (hit == 0)
                    {
                        verdict = L10nVerdict.Untranslated;
                    }
                    else
                    {
                        verdict = L10nVerdict.PartiallyTranslated;
                    }

                    int line = 0;
                    if (attr is IXmlLineInfo lineInfo && lineInfo.HasLineInfo())
                        line = lineInfo.LineNumber;
                    else if (el is IXmlLineInfo elLineInfo && elLineInfo.HasLineInfo())
                        line = elLineInfo.LineNumber;

                    findings.Add(new L10nFinding(
                        AxamlPath: axamlRelPath,
                        LineNumber: line,
                        ElementName: el.Name.LocalName,
                        AttributeName: attr.Name.LocalName,
                        Literal: raw,
                        TranslationStatus: status,
                        Verdict: verdict));
                }
            }
        }

        /// <summary>
        /// Walk up the ancestor chain looking for a known template container.
        /// Same logic as <c>LabelDiffScanner.IsInsideTemplate</c>.
        /// </summary>
        static bool IsInsideTemplate(XElement el)
        {
            for (XElement? cur = el.Parent; cur != null; cur = cur.Parent)
            {
                if (TemplateContainers.Contains(cur.Name.LocalName))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Quick classification: is <paramref name="value"/> a candidate
        /// English-looking literal that we should record? Sets
        /// <paramref name="isNonEnglish"/> true when the value contains any
        /// CJK / Hangul character (caller will tag it as <see cref="L10nVerdict.NonEnglish"/>
        /// and skip the translation join).
        ///
        /// Excludes:
        ///   - empty / whitespace-only
        ///   - markup extensions (starts with `{`)
        ///   - pure numeric / pure punctuation
        ///   - single-char values
        ///   - short pseudo-words that aren't real labels (length &lt; 2)
        ///
        /// Accepts:
        ///   - any value with at least 1 letter AND a space (multi-word labels)
        ///   - any value ≥ 4 chars and ≥ 80% ASCII letters (single-word labels)
        ///   - non-English values (caller flags them NonEnglish via the out-param)
        /// </summary>
        public static bool IsCandidateLiteral(string value, out bool isNonEnglish)
        {
            isNonEnglish = false;
            if (string.IsNullOrEmpty(value))
                return false;
            string trimmed = value.Trim();
            if (trimmed.Length == 0)
                return false;
            // Markup extension — skip.
            if (trimmed[0] == '{')
                return false;
            // Single character: too noisy (think `?`, `:`, `…`).
            if (trimmed.Length < 2)
                return false;

            // CJK / Hangul detection. If ANY character in the literal lies in
            // these blocks, treat it as already-localised source (NonEnglish).
            // We accept it as a candidate (so it shows up in the report's
            // NonEnglish section for sanity) but skip the translation join.
            foreach (char c in trimmed)
            {
                if (IsCjkOrHangul(c))
                {
                    isNonEnglish = true;
                    return true;
                }
            }

            // Pure-digit / pure-punct: not a label.
            bool hasLetter = false;
            int letterCount = 0;
            int asciiLetterCount = 0;
            foreach (char c in trimmed)
            {
                if (char.IsLetter(c))
                {
                    hasLetter = true;
                    letterCount++;
                    if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z'))
                        asciiLetterCount++;
                }
            }
            if (!hasLetter)
                return false;

            // Must start with an ASCII letter (proper English labels do; this
            // filters out `:Status`, `(Optional)`, etc. that would otherwise
            // sneak in as Untranslated noise).
            if (!((trimmed[0] >= 'A' && trimmed[0] <= 'Z') || (trimmed[0] >= 'a' && trimmed[0] <= 'z')))
                return false;

            // Multi-word labels: at least one space and at least one letter.
            bool hasSpace = trimmed.IndexOf(' ') >= 0;
            if (hasSpace)
                return true;

            // Single-word labels: at least 4 chars AND ≥ 80 % ASCII-letter
            // density. The density check kills things like `0x10`, `FF00`,
            // `RGB16` that are technical strings, not localizable labels.
            if (trimmed.Length >= 4 && (double)asciiLetterCount / trimmed.Length >= 0.8)
                return true;

            return false;
        }

        /// <summary>
        /// True if <paramref name="c"/> is in a CJK / Hangul / Hiragana /
        /// Katakana Unicode block. Used as the "already localised in-line"
        /// heuristic — if the source AXAML literal already contains any of
        /// these characters, it's not a translation gap.
        /// </summary>
        static bool IsCjkOrHangul(char c)
        {
            // Hiragana 3040-309F, Katakana 30A0-30FF, CJK Unified Ideographs
            // 4E00-9FFF, CJK Compatibility 3400-4DBF, Hangul Syllables AC00-D7AF,
            // Hangul Jamo 1100-11FF, Halfwidth/Fullwidth FF00-FFEF.
            if (c >= 0x3040 && c <= 0x309F) return true;
            if (c >= 0x30A0 && c <= 0x30FF) return true;
            if (c >= 0x4E00 && c <= 0x9FFF) return true;
            if (c >= 0x3400 && c <= 0x4DBF) return true;
            if (c >= 0xAC00 && c <= 0xD7AF) return true;
            if (c >= 0x1100 && c <= 0x11FF) return true;
            if (c >= 0xFF00 && c <= 0xFFEF) return true;
            return false;
        }

        /// <summary>
        /// Lookup chain: AXAML English literal → en.txt reverse-map → Japanese key
        /// → forward map → translated value. Returns true iff the chain yields a
        /// non-empty target-language string.
        ///
        /// Falls back to a forward-map direct lookup using the English literal
        /// itself as the key — some translation files (like ja.txt) use English
        /// keys directly for menu items, so the direct lookup catches those too.
        /// </summary>
        static bool HasTranslation(
            string englishLiteral,
            IReadOnlyDictionary<string, string> reverseEnMap,
            IReadOnlyDictionary<string, string> forwardMap)
        {
            if (HasTranslationOne(englishLiteral, reverseEnMap, forwardMap))
                return true;

            // Issue #356: Avalonia AXAML literals decoded from `&#x0a;` XML
            // entities use bare LF, while translation files use literal `\r\n`
            // (CRLF after parsing). Try CRLF-normalised and LF-normalised
            // variants before giving up — mirrors the runtime fallback added
            // to `MyTranslateResourceLow.str()`.
            if (englishLiteral.IndexOf('\n') >= 0 || englishLiteral.IndexOf('\r') >= 0)
            {
                string crlfForm = englishLiteral
                    .Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n");
                if (!string.Equals(crlfForm, englishLiteral, StringComparison.Ordinal) &&
                    HasTranslationOne(crlfForm, reverseEnMap, forwardMap))
                    return true;
                string lfForm = englishLiteral.Replace("\r\n", "\n").Replace("\r", "\n");
                if (!string.Equals(lfForm, englishLiteral, StringComparison.Ordinal) &&
                    HasTranslationOne(lfForm, reverseEnMap, forwardMap))
                    return true;
            }

            return false;
        }

        static bool HasTranslationOne(
            string englishLiteral,
            IReadOnlyDictionary<string, string> reverseEnMap,
            IReadOnlyDictionary<string, string> forwardMap)
        {
            // 1. Direct lookup — some entries are keyed by English directly.
            string normalised = NormalizeKey(englishLiteral);
            if (TryLookup(forwardMap, englishLiteral, normalised, out var direct) &&
                !string.IsNullOrWhiteSpace(direct))
                return true;

            // 2. Reverse chain via en.txt.
            if (TryLookup(reverseEnMap, englishLiteral, normalised, out var jpKey))
            {
                if (forwardMap.TryGetValue(jpKey, out var translated) &&
                    !string.IsNullOrWhiteSpace(translated))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Two-step lookup: try the raw key first, then the trim-trailing-colon
        /// normalised key. Returns true if either succeeded.
        /// </summary>
        static bool TryLookup(
            IReadOnlyDictionary<string, string> map,
            string raw,
            string normalised,
            out string value)
        {
            if (map.TryGetValue(raw, out value!))
                return true;
            // Skip the normalised retry when the normalisation was a no-op
            // (raw and normalised carry the same content) — saves a second
            // dictionary probe in the common case where the AXAML literal
            // had no trailing whitespace / colon. Use ordinal content
            // comparison: most callers DO pass `NormalizeKey(raw)` so the
            // common path has DIFFERENT references with EQUAL content, and
            // a `ReferenceEquals` skip would always be false (Copilot review
            // PR #381 caught this).
            if (!string.Equals(raw, normalised, StringComparison.Ordinal) &&
                map.TryGetValue(normalised, out value!))
                return true;
            value = string.Empty;
            return false;
        }

        /// <summary>
        /// Trim outer whitespace and any trailing colon — AXAML labels use the
        /// short form ("Save") while WinForms designer literals use the colon
        /// form ("Save:"). Translation tables historically reflect the WinForms
        /// convention.
        /// </summary>
        public static string NormalizeKey(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return string.Empty;
            string s = raw.Trim();
            while (s.EndsWith(":", StringComparison.Ordinal))
                s = s.Substring(0, s.Length - 1).TrimEnd();
            return s;
        }

        // ---------------------------------------------------------------------
        // Translation table loaders
        //
        // The file format mirrors `MyTranslateResourceLow.LoadResource`:
        //   :Japanese key
        //   Translated value
        //   <blank>
        //
        // Lines that don't start with `:` are translation values; the line
        // immediately before them was the key. Blank lines reset the parser.
        // ---------------------------------------------------------------------

        /// <summary>
        /// Parse `config/translate/&lt;lang&gt;.txt` into a Japanese-key →
        /// translated-value dictionary. Adds both the raw key AND the
        /// trim-trailing-colon normalised form so callers can look up either.
        /// Returns an empty dictionary if the file does not exist.
        /// </summary>
        public static Dictionary<string, string> LoadForwardMap(string path)
        {
            var dict = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return dict;

            ParseTranslateFile(path, (key, value) =>
            {
                if (string.IsNullOrEmpty(key))
                    return;
                if (!dict.ContainsKey(key))
                    dict[key] = value;
                string norm = NormalizeKey(key);
                if (norm.Length > 0 && !dict.ContainsKey(norm))
                    dict[norm] = value;
            });

            return dict;
        }

        /// <summary>
        /// Parse `config/translate/en.txt` into a reverse English-value →
        /// Japanese-key dictionary. This is the pivot map: en.txt's forward
        /// direction is Japanese → English, but Avalonia uses English literals
        /// directly so we need the reverse direction to find the Japanese key.
        /// Adds both raw and trim-trailing-colon normalised forms.
        /// </summary>
        public static Dictionary<string, string> LoadReverseEnglishMap(string enPath)
        {
            var dict = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(enPath) || !File.Exists(enPath))
                return dict;

            ParseTranslateFile(enPath, (jaKey, enValue) =>
            {
                if (string.IsNullOrEmpty(enValue))
                    return;
                string trimmed = enValue.TrimEnd();
                if (!dict.ContainsKey(trimmed))
                    dict[trimmed] = jaKey;
                string norm = NormalizeKey(trimmed);
                if (norm.Length > 0 && !dict.ContainsKey(norm))
                    dict[norm] = jaKey;
            });

            return dict;
        }

        /// <summary>
        /// Stream a translate-format text file and invoke <paramref name="onPair"/>
        /// for every (key, value) pair. The parser is forgiving: it tolerates
        /// missing blank-line separators, extra whitespace, and skips lines that
        /// don't look like keys or follow-up values.
        ///
        /// Format (literal):
        ///
        ///     :SourceKey1
        ///     Translation1
        ///     &lt;blank&gt;
        ///     :SourceKey2
        ///     Translation2
        ///     &lt;blank&gt;
        ///
        /// `\r\n` escape sequences in the file are translated to real CRLF
        /// (matching the runtime parser at `MyTranslateResourceLow.LoadResource`).
        /// </summary>
        static void ParseTranslateFile(string path, Action<string, string> onPair)
        {
            try
            {
                using var reader = new StreamReader(path);
                string? src = null;
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.Length == 0)
                    {
                        src = null;
                        continue;
                    }
                    if (src == null)
                    {
                        if (line[0] != ':')
                            continue;
                        // Decode the literal `\r\n` escape AND XML char/entity refs
                        // (`&#10;`, `&#x0a;`, `&amp;`, `&lt;`, …) so the audit map is
                        // keyed by the same decoded form the AXAML parser emits at
                        // runtime — matching MyTranslateResourceLow.LoadResource and
                        // avoiding false "untranslated" verdicts for entity-keyed rows
                        // that some translate files still ship (issue #1636).
                        src = MyTranslateResourceLow.DecodeXmlEntities(
                            line.Substring(1).Replace("\\r\\n", "\r\n"));
                    }
                    else
                    {
                        string value = MyTranslateResourceLow.DecodeXmlEntities(
                            line.Replace("\\r\\n", "\r\n"));
                        onPair(src, value);
                        src = null;
                    }
                }
            }
            catch
            {
                // Best-effort parsing — corrupt files produce a partial map.
            }
        }

        /// <summary>
        /// Convert an absolute path into a repo-relative path with forward
        /// slashes. Falls back to the original path if the rel-path calculation
        /// fails (rare — happens when the file isn't under the repo root, which
        /// shouldn't occur in practice but we don't want to crash the report).
        /// </summary>
        static string ToRelative(string repoRoot, string absolutePath)
        {
            try
            {
                string rel = Path.GetRelativePath(repoRoot, absolutePath);
                return rel.Replace('\\', '/');
            }
            catch
            {
                return absolutePath.Replace('\\', '/');
            }
        }

        /// <summary>
        /// Format the markdown body of the localisation report (sans front-matter
        /// — that is added by <see cref="ReportWriter"/>). Layout:
        ///
        ///  1. Methodology header
        ///  2. Summary table with per-verdict counts
        ///  3. Per-language coverage table (% translated)
        ///  4. "Untranslated literals" section grouped by file
        ///  5. "Partially translated literals" with per-language tick columns
        ///  6. "Already translated" collapsed summary
        ///  7. "Non-English source literals" small sanity section
        ///
        /// LF newlines only (matches other GapSweep reports).
        /// </summary>
        public static string FormatReport(
            IReadOnlyList<L10nFinding> findings,
            IReadOnlyList<string> languages)
        {
            if (findings == null) throw new ArgumentNullException(nameof(findings));
            if (languages == null) throw new ArgumentNullException(nameof(languages));

            var sb = new StringBuilder();
            sb.Append("# Avalonia — Localisation Sweep\n\n");
            sb.Append("Phase 6 of the gap-sweep methodology surfaces every English-looking AXAML\n");
            sb.Append("literal in `FEBuilderGBA.Avalonia/Views/` that ISN'T bound to a localized\n");
            sb.Append("resource AND isn't already present in the project's translation tables.\n\n");
            sb.Append("Issue [#356](https://github.com/laqieer/FEBuilderGBA/issues/356) reports the\n");
            sb.Append("user-visible symptom: when the Avalonia GUI is set to Japanese or Chinese,\n");
            sb.Append("several labels stay in English. This report inventories every such literal\n");
            sb.Append("so follow-up gap-fix PRs can add the missing translation entries one file at\n");
            sb.Append("a time.\n\n");
            sb.Append("**Methodology:**\n\n");
            sb.Append("- Glob `FEBuilderGBA.Avalonia/Views/**/*.axaml`; parse each with `XDocument`\n");
            sb.Append("  using `LoadOptions.SetLineInfo` so we get per-attribute line numbers.\n");
            sb.Append("- Inspect attribute values for `Text` / `Content` / `Header` / `ToolTip` /\n");
            sb.Append("  `Watermark` / `ToolTip.Tip` (the Avalonia attached-property form).\n");
            sb.Append("- Skip markup extensions (`{Binding ...}`, `{StaticResource ...}`,\n");
            sb.Append("  `{x:Static ...}`, `{DynamicResource ...}`, `{TemplateBinding ...}`) and\n");
            sb.Append("  elements nested under `Style` / `DataTemplate` / `ControlTemplate` /\n");
            sb.Append("  `ItemTemplate` / `Design.DataContext`.\n");
            sb.Append("- Heuristic accepts: any value starting with an ASCII letter, length ≥ 2,\n");
            sb.Append("  containing at least one space OR ≥ 4 chars with ≥ 80 % ASCII-letter\n");
            sb.Append("  density. Values containing CJK / Hangul characters are recorded as\n");
            sb.Append("  `NonEnglish` (out-of-scope sanity row, not a gap).\n");
            sb.Append("- Translation join: for each candidate literal, look it up in each target\n");
            sb.Append("  language's `config/translate/<lang>.txt` table via the same reverse-English\n");
            sb.Append("  chain the runtime uses (English value → Japanese key → translated value).\n\n");
            sb.Append("**Verdict tiers:**\n\n");
            sb.Append("- `Untranslated` — no target language has a translation. The backlog.\n");
            sb.Append("- `PartiallyTranslated` — some languages have it, others don't.\n");
            sb.Append("- `Translated` — every target language has a translation.\n");
            sb.Append("- `NonEnglish` — source literal already contains CJK / Hangul (out of scope).\n\n");
            sb.Append("Methodology lives in `FEBuilderGBA.Avalonia/GapSweep/L10nScanner.cs`.\n");
            sb.Append("Regenerate with `FEBuilderGBA.Avalonia --gap-sweep-l10n --out=<path>`.\n\n");

            // ---- Summary table ----
            int total = findings.Count;
            int translated = findings.Count(f => f.Verdict == L10nVerdict.Translated);
            int partial = findings.Count(f => f.Verdict == L10nVerdict.PartiallyTranslated);
            int untranslated = findings.Count(f => f.Verdict == L10nVerdict.Untranslated);
            int nonEnglish = findings.Count(f => f.Verdict == L10nVerdict.NonEnglish);

            sb.Append("## Summary\n\n");
            sb.Append("| Verdict | Count | % of total |\n");
            sb.Append("|---|---:|---:|\n");
            AppendSummaryRow(sb, "Untranslated", untranslated, total);
            AppendSummaryRow(sb, "PartiallyTranslated", partial, total);
            AppendSummaryRow(sb, "Translated", translated, total);
            AppendSummaryRow(sb, "NonEnglish", nonEnglish, total);
            AppendSummaryRow(sb, "**Total**", total, total);
            sb.Append('\n');

            // ---- Per-language coverage ----
            // For each language, count how many "English-source" findings (i.e.
            // excluding the NonEnglish ones, which are already localised) have a
            // translation in that language. Denominator excludes NonEnglish so
            // the percentage measures "real coverage of in-scope literals".
            int englishOnly = total - nonEnglish;
            sb.Append("## Per-language Coverage\n\n");
            sb.Append("Coverage is computed over the English-source literals only ");
            sb.Append("(excludes the `NonEnglish` rows, which are out of scope).\n\n");
            sb.Append("| Language | Translated | English-source total | % |\n");
            sb.Append("|---|---:|---:|---:|\n");
            foreach (string lang in languages)
            {
                int langTranslated = findings.Count(f =>
                    f.Verdict != L10nVerdict.NonEnglish &&
                    f.TranslationStatus.TryGetValue(lang, out bool ok) && ok);
                string pct = englishOnly > 0
                    ? ((100.0 * langTranslated) / englishOnly).ToString("F1", CultureInfo.InvariantCulture)
                    : "—";
                sb.Append("| `").Append(lang)
                  .Append("` | ").Append(langTranslated.ToString(CultureInfo.InvariantCulture))
                  .Append(" | ").Append(englishOnly.ToString(CultureInfo.InvariantCulture))
                  .Append(" | ").Append(pct).Append(" |\n");
            }
            sb.Append('\n');

            // ---- Top 20 files by Untranslated count ----
            var untranslatedByFile = findings
                .Where(f => f.Verdict == L10nVerdict.Untranslated)
                .GroupBy(f => f.AxamlPath, StringComparer.Ordinal)
                .Select(g => new { File = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .ThenBy(x => x.File, StringComparer.Ordinal)
                .ToList();

            sb.Append("## Top 20 Files by Untranslated Literal Count\n\n");
            sb.Append("Each row's count is the upper bound on the localisation backlog for that\n");
            sb.Append("file. Files are sorted by Untranslated count descending.\n\n");
            if (untranslatedByFile.Count == 0)
            {
                sb.Append("_No untranslated literals — every English-looking AXAML literal has a translation entry._\n\n");
            }
            else
            {
                sb.Append("| Rank | File | Untranslated |\n");
                sb.Append("|---:|---|---:|\n");
                int rank = 0;
                foreach (var f in untranslatedByFile.Take(20))
                {
                    rank++;
                    sb.Append("| ").Append(rank.ToString(CultureInfo.InvariantCulture))
                      .Append(" | `").Append(f.File).Append("` | ")
                      .Append(f.Count.ToString(CultureInfo.InvariantCulture)).Append(" |\n");
                }
                sb.Append('\n');
            }

            // ---- Per-file untranslated section ----
            sb.Append("## Untranslated Literals (No Language Has Them)\n\n");
            if (untranslatedByFile.Count == 0)
            {
                sb.Append("_None._\n\n");
            }
            else
            {
                sb.Append("Each row is a literal that has no translation in ANY of the target\n");
                sb.Append("languages. Fix these by adding entries to `config/translate/<lang>.txt`.\n\n");
                foreach (var fileGroup in findings
                    .Where(f => f.Verdict == L10nVerdict.Untranslated)
                    .GroupBy(f => f.AxamlPath, StringComparer.Ordinal)
                    .OrderByDescending(g => g.Count())
                    .ThenBy(g => g.Key, StringComparer.Ordinal))
                {
                    sb.Append("### `").Append(fileGroup.Key).Append("`\n\n");
                    sb.Append("| Line | Element | Attribute | Literal |\n");
                    sb.Append("|---:|---|---|---|\n");
                    foreach (var f in fileGroup.OrderBy(x => x.LineNumber).ThenBy(x => x.Literal, StringComparer.Ordinal))
                    {
                        sb.Append("| ").Append(f.LineNumber.ToString(CultureInfo.InvariantCulture))
                          .Append(" | `").Append(f.ElementName).Append("`")
                          .Append(" | `").Append(f.AttributeName).Append("`")
                          .Append(" | ").Append(RenderLiteral(f.Literal)).Append(" |\n");
                    }
                    sb.Append('\n');
                }
            }

            // ---- Partially translated section ----
            sb.Append("## Partially Translated Literals\n\n");
            var partialList = findings.Where(f => f.Verdict == L10nVerdict.PartiallyTranslated).ToList();
            if (partialList.Count == 0)
            {
                sb.Append("_None._\n\n");
            }
            else
            {
                sb.Append("Some target languages have a translation, others don't. The tick columns\n");
                sb.Append("show which languages cover the literal.\n\n");
                foreach (var fileGroup in partialList
                    .GroupBy(f => f.AxamlPath, StringComparer.Ordinal)
                    .OrderByDescending(g => g.Count())
                    .ThenBy(g => g.Key, StringComparer.Ordinal))
                {
                    sb.Append("### `").Append(fileGroup.Key).Append("`\n\n");
                    sb.Append("| Line | Literal |");
                    foreach (string lang in languages)
                        sb.Append(' ').Append(lang).Append(" |");
                    sb.Append('\n');
                    sb.Append("|---:|---|");
                    foreach (var _ in languages)
                        sb.Append(":---:|");
                    sb.Append('\n');
                    foreach (var f in fileGroup.OrderBy(x => x.LineNumber).ThenBy(x => x.Literal, StringComparer.Ordinal))
                    {
                        sb.Append("| ").Append(f.LineNumber.ToString(CultureInfo.InvariantCulture))
                          .Append(" | ").Append(RenderLiteral(f.Literal)).Append(" |");
                        foreach (string lang in languages)
                        {
                            bool ok = f.TranslationStatus.TryGetValue(lang, out var v) && v;
                            sb.Append(' ').Append(ok ? "✓" : "✗").Append(" |");
                        }
                        sb.Append('\n');
                    }
                    sb.Append('\n');
                }
            }

            // ---- Already-translated counts ----
            sb.Append("## Already Translated (Summary)\n\n");
            sb.Append("Literals that have a translation in every target language. Surfaced as a\n");
            sb.Append("count per file so the report stays scannable; the per-literal detail is\n");
            sb.Append("intentionally not listed (they're already covered).\n\n");
            var translatedByFile = findings
                .Where(f => f.Verdict == L10nVerdict.Translated)
                .GroupBy(f => f.AxamlPath, StringComparer.Ordinal)
                .Select(g => new { File = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .ThenBy(x => x.File, StringComparer.Ordinal)
                .ToList();
            if (translatedByFile.Count == 0)
            {
                sb.Append("_No fully-translated literals._\n\n");
            }
            else
            {
                sb.Append("| File | Translated |\n");
                sb.Append("|---|---:|\n");
                foreach (var t in translatedByFile)
                {
                    sb.Append("| `").Append(t.File).Append("` | ")
                      .Append(t.Count.ToString(CultureInfo.InvariantCulture)).Append(" |\n");
                }
                sb.Append('\n');
            }

            // ---- Non-English literals ----
            sb.Append("## Non-English Source Literals (Out of Scope)\n\n");
            sb.Append("AXAML literals that already contain CJK / Hangul characters — someone wrote\n");
            sb.Append("them directly in the target language instead of routing them through the\n");
            sb.Append("translation tables. This is fine when the literal IS the canonical form\n");
            sb.Append("(matches the user's locale), but flagged for sanity in case any are stray\n");
            sb.Append("leftovers from a partial localisation attempt.\n\n");
            var nonEnglishList = findings.Where(f => f.Verdict == L10nVerdict.NonEnglish).ToList();
            if (nonEnglishList.Count == 0)
            {
                sb.Append("_None._\n\n");
            }
            else
            {
                sb.Append("| File | Line | Element | Attribute | Literal |\n");
                sb.Append("|---|---:|---|---|---|\n");
                foreach (var f in nonEnglishList
                    .OrderBy(x => x.AxamlPath, StringComparer.Ordinal)
                    .ThenBy(x => x.LineNumber))
                {
                    sb.Append("| `").Append(f.AxamlPath).Append("` | ")
                      .Append(f.LineNumber.ToString(CultureInfo.InvariantCulture))
                      .Append(" | `").Append(f.ElementName).Append("`")
                      .Append(" | `").Append(f.AttributeName).Append("`")
                      .Append(" | ").Append(RenderLiteral(f.Literal)).Append(" |\n");
                }
                sb.Append('\n');
            }

            // Trim trailing blank lines so the body ends in a single `\n`
            // (same discipline as the other GapSweep scanners).
            while (sb.Length >= 2 && sb[sb.Length - 1] == '\n' && sb[sb.Length - 2] == '\n')
                sb.Length--;
            return sb.ToString();
        }

        static void AppendSummaryRow(StringBuilder sb, string label, int count, int total)
        {
            string pct = total > 0
                ? ((100.0 * count) / total).ToString("F1", CultureInfo.InvariantCulture)
                : "—";
            sb.Append("| ").Append(label).Append(" | ")
              .Append(count.ToString(CultureInfo.InvariantCulture)).Append(" | ")
              .Append(pct).Append(" |\n");
        }

        /// <summary>
        /// Render a literal as an inline-code markdown span safely. Mirrors
        /// <c>LabelDiffScanner.RenderLabelLiteral</c>'s discipline: escape
        /// embedded CR/LF, truncate at 200 chars, choose a backtick fence that
        /// won't collide with embedded backticks. Also escapes pipe chars (`|`)
        /// because we're rendering into a markdown table cell.
        /// </summary>
        static string RenderLiteral(string literal)
        {
            string escaped = literal
                .Replace("\r", "\\r")
                .Replace("\n", "\\n");

            const int MaxLen = 200;
            if (escaped.Length > MaxLen)
                escaped = escaped.Substring(0, MaxLen) + "… (truncated)";

            // Replace pipe with a Unicode lookalike INSIDE the code-span so the
            // markdown table cell doesn't break. CommonMark doesn't escape `|`
            // inside `` `code` `` spans inside table cells, so we substitute
            // U+FF5C FULLWIDTH VERTICAL LINE instead.
            escaped = escaped.Replace("|", "｜");

            if (escaped.IndexOf('`') < 0)
                return "`" + escaped + "`";
            return "`` " + escaped + " ``";
        }
    }
}
