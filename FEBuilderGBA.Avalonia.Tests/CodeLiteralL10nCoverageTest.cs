// SPDX-License-Identifier: GPL-3.0-or-later
// Issue #1635 — Translate code-sourced R._() strings and extend the L10n gate.
//
// L10nCoverageTest guards English AXAML literals. EditorDialogLiteralL10nTests
// guards the SIX #1610-scoped .axaml.cs files. NEITHER sees the ~334 user-facing
// status / error / dialog strings wrapped in R._("...") across ALL Avalonia
// ViewModels and code-behind, so a Japanese / Chinese user saw them in English
// while CI stayed green.
//
// This test is the blocking gate that closes that hole: it enumerates every
// R._("literal") call across FEBuilderGBA.Avalonia/**/*.cs via
// L10nScanner.ScanCodeLiterals and asserts:
//   - 0 PartiallyTranslated  (a literal must have BOTH ja AND zh, or neither),
//   - every Untranslated literal is in an explicit allowlist.
// NonEnglish findings (the literal already contains CJK / Hangul — a WinForms
// Japanese-source string reused verbatim) are out of scope, mirroring the AXAML
// sweep's NonEnglish bucket.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FEBuilderGBA.Avalonia.GapSweep;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    public class CodeLiteralL10nCoverageTest
    {
        /// <summary>
        /// Literals deliberately kept English in every locale. These are NOT
        /// translatable UI content — they are URLs, file-format / extension labels
        /// that double as their own value, or punctuation placeholders. Keep this
        /// list small and justified; a new untranslated UI string must be
        /// translated, not allowlisted.
        /// </summary>
        static readonly HashSet<string> Allowlist = new(StringComparer.Ordinal)
        {
            // Format / brand names that are identical across locales (the existing
            // translate files already key these verbatim where present).
            "Adobe ACT (Photoshop)",
            "JASC-PAL (Aseprite/GIMP)",
            "Road.BIN",
        };

        [Fact]
        public void AvaloniaCodeLiterals_HaveJaAndZhTranslations()
        {
            string repoRoot = FindRepoRoot();

            var findings = L10nScanner.ScanCodeLiterals(repoRoot, new[] { "ja", "zh" });

            // The scan must actually find code literals — guard against a silent
            // "0 findings ⇒ vacuous pass" regression (e.g. a broken regex).
            Assert.NotEmpty(findings);

            int partial = findings.Count(f => f.Verdict == L10nVerdict.PartiallyTranslated);
            Assert.True(
                partial == 0,
                $"Expected 0 PartiallyTranslated R._() code literals; found {partial}. " +
                "Each must have BOTH a ja AND a zh entry in config/translate/{ja,zh}.txt. " +
                "Sample: " + string.Join("; ",
                    findings.Where(f => f.Verdict == L10nVerdict.PartiallyTranslated)
                        .Select(f => $"'{Truncate(f.Literal, 60)}' @ {f.SourcePath}:{f.LineNumber}")
                        .Distinct()
                        .Take(8)));

            var untranslated = findings
                .Where(f => f.Verdict == L10nVerdict.Untranslated)
                .ToList();

            foreach (var f in untranslated)
            {
                Assert.True(
                    Allowlist.Contains(f.Literal),
                    $"Untranslated R._() code literal '{Truncate(f.Literal, 80)}' at " +
                    $"{f.SourcePath}:{f.LineNumber} is not in the allowlist. Add ja+zh " +
                    $"translations to config/translate/{{ja,zh}}.txt, or add this literal " +
                    $"to the Allowlist in CodeLiteralL10nCoverageTest.cs with justification.");
            }
        }

        /// <summary>
        /// Proves the gate would have caught the exact examples cited in #1635 had
        /// they not been translated: a literal absent from ja/zh resolves to
        /// Untranslated (a fail unless allowlisted), and a fully-translated literal
        /// resolves to Translated. Uses in-memory maps so it is independent of the
        /// shipped translate files.
        /// </summary>
        [Fact]
        public void Gate_WouldHaveCaught_Issue1635_Examples()
        {
            // No translations at all → the issue's example literal is Untranslated.
            var emptyMaps = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
            {
                ["ja"] = new Dictionary<string, string>(StringComparer.Ordinal),
                ["zh"] = new Dictionary<string, string>(StringComparer.Ordinal),
            };
            var emptyRev = new Dictionary<string, string>(StringComparer.Ordinal);

            const string src =
                "void M(){ CoreState.Services?.ShowError(R._(\"2-ROM Diff: no ROM loaded.\")); }";
            var missing = L10nScanner.ScanCsString(src, "ToolDiffViewModel.cs", emptyRev, emptyMaps);
            Assert.Single(missing);
            Assert.Equal(L10nVerdict.Untranslated, missing[0].Verdict);
            Assert.Equal("2-ROM Diff: no ROM loaded.", missing[0].Literal);

            // Same literal WITH ja+zh entries → Translated (the gate is satisfied).
            var fullMaps = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
            {
                ["ja"] = new Dictionary<string, string>(StringComparer.Ordinal)
                    { ["2-ROM Diff: no ROM loaded."] = "2-ROM 差分: ROMが読み込まれていません。" },
                ["zh"] = new Dictionary<string, string>(StringComparer.Ordinal)
                    { ["2-ROM Diff: no ROM loaded."] = "2-ROM 差异: 未加载ROM。" },
            };
            var ok = L10nScanner.ScanCsString(src, "ToolDiffViewModel.cs", emptyRev, fullMaps);
            Assert.Single(ok);
            Assert.Equal(L10nVerdict.Translated, ok[0].Verdict);
        }

        static string FindRepoRoot()
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

        static string Truncate(string s, int max) =>
            s.Length <= max ? s : s.Substring(0, max) + "…";
    }
}
