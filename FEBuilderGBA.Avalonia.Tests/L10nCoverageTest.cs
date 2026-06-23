using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FEBuilderGBA.Avalonia.GapSweep;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// CI gate for the #356 localisation work: asserts that every English
    /// AXAML literal in <c>FEBuilderGBA.Avalonia/Views/</c> has a Japanese
    /// AND Chinese translation, modulo an explicit allowlist of 10 distinct
    /// URL/hex-example placeholders that are deliberately kept English.
    /// These 10 distinct literals appear in 13 separate AXAML files (some
    /// like <c>e.g. 0x08000000</c> repeat across MoveToFreeSpace and
    /// PointerTool views), so the gap-sweep finds 13 untranslated findings
    /// but the allowlist deduplicates to 10 unique strings.
    ///
    /// This test prevents regressions — if a new Avalonia view is added with
    /// an untranslated English label, this test fails and forces the
    /// contributor to either translate the label or add it to the allowlist
    /// (with justification).
    ///
    /// Threshold logic:
    /// - <c>Untranslated</c> findings (no ja AND no zh) MUST be in the
    ///   allowlist (URL/hex examples). Anything else is a regression.
    /// - <c>PartiallyTranslated</c> findings (one of ja/zh missing) MUST be
    ///   zero — the #356 success metric.
    /// </summary>
    public class L10nCoverageTest
    {
        /// <summary>
        /// Allowlist of 10 distinct literals that are deliberately kept English
        /// because they ARE the example/URL (not translatable content). Find
        /// counts at scan time (13) exceed allowlist size (10) because some
        /// literals repeat across files; the assertion logic dedupes via
        /// <see cref="HashSet{T}"/> comparison.
        /// </summary>
        static readonly HashSet<string> UrlHexAllowlist = new(StringComparer.Ordinal)
        {
            "e.g. 0x08000000",
            "e.g. 0x08F00000",
            "e.g. 0x100",
            "e.g. 0x01",
            "e.g. 0x02000000",
            "e.g. 0xFF",
            "e.g. 8",
            "https://github.com/laqieer/FEBuilderGBA-patch2.git",
            "https://github.com/Klokinator/FE-Repo",
            "https://github.com/laqieer/FE-Repo-Music-No-Preview",
            // #1380: literal git command the "Copy git command" button copies to
            // the clipboard verbatim — must stay byte-identical across locales
            // (same rationale as the URLs above), so it is allowlisted not
            // translated.
            "Copy: git submodule update --init resources/FE-Repo",
        };

        /// <summary>
        /// Run the gap-sweep L10n scanner against the ja/zh target set and
        /// assert coverage thresholds.
        /// </summary>
        [Fact]
        public void AvaloniaViews_HaveJaAndZhTranslations()
        {
            string repoRoot = FindRepoRoot();

            var findings = L10nScanner.Scan(repoRoot, new[] { "ja", "zh" });

            int partial = findings.Count(f => f.Verdict == L10nVerdict.PartiallyTranslated);
            int untranslated = findings.Count(f => f.Verdict == L10nVerdict.Untranslated);

            // Partial = 0 means every translation entry has both ja AND zh.
            Assert.True(
                partial == 0,
                $"Expected 0 PartiallyTranslated literals; found {partial}. " +
                "Sample: " + string.Join("; ",
                    findings.Where(f => f.Verdict == L10nVerdict.PartiallyTranslated)
                        .Take(5)
                        .Select(f => $"'{Truncate(f.Literal, 60)}' @ {f.AxamlPath}:{f.LineNumber}")));

            // Untranslated entries must ALL be in the URL/hex allowlist.
            var untranslatedLiterals = findings
                .Where(f => f.Verdict == L10nVerdict.Untranslated)
                .ToList();

            foreach (var f in untranslatedLiterals)
            {
                Assert.True(
                    UrlHexAllowlist.Contains(f.Literal),
                    $"Untranslated literal '{f.Literal}' at {f.AxamlPath}:{f.LineNumber} " +
                    $"is not in the URL/hex allowlist. Either add a translation to " +
                    $"config/translate/{{ja,zh}}.txt, or add this literal to the " +
                    $"UrlHexAllowlist in L10nCoverageTest.cs with justification.");
            }

            // Sanity: the count of distinct untranslated literals (deduped)
            // should not exceed the allowlist size. Findings may repeat the
            // same literal across multiple files (e.g. `e.g. 0x08000000`
            // appears in both PointerToolView and MoveToFreeSpaceView).
            int distinctUntranslated = untranslatedLiterals
                .Select(f => f.Literal)
                .Distinct(StringComparer.Ordinal)
                .Count();
            Assert.True(
                distinctUntranslated <= UrlHexAllowlist.Count,
                $"More distinct Untranslated literals ({distinctUntranslated}) than allowlist size " +
                $"({UrlHexAllowlist.Count}) — something regressed.");
        }

        /// <summary>
        /// Walk up the directory tree from the test assembly looking for
        /// FEBuilderGBA.sln. Mirrors the repo-root resolution in
        /// <see cref="FEBuilderGBA.Avalonia.App.FindRepoRoot"/> (the gap-sweep
        /// launcher in `FEBuilderGBA.Avalonia/App.axaml.cs`).
        /// </summary>
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
