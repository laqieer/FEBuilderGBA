using System;
using System.IO;
using FEBuilderGBA.Avalonia.GapSweep;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// Regression tests for issue #1691: six Avalonia views shipped the Expand List
    /// button label as the raw Japanese literal <c>Content="リストの拡張"</c> rather
    /// than the English source literal <c>Content="Data Expansion"</c>.  Because
    /// <see cref="ViewTranslationHelper.IsTranslatable"/> only translates text that
    /// contains at least one ASCII letter, the raw Japanese literal was skipped and
    /// rendered unchanged in English mode.
    ///
    /// The fix changes the six attribute values to <c>"Data Expansion"</c>, which
    /// already has ja/zh entries in config/translate/ (リストの拡張 / 扩展列表).
    /// These tests pin that fix: if any of the six files regresses back to the raw
    /// Japanese literal, or if the translation entries are removed, these tests fail.
    /// </summary>
    public class JapaneseExpandButtonL10nRegressionTests
    {
        /// <summary>
        /// Relative paths (forward-slash) of the six views that were fixed.
        /// Used by both tests in this class.
        /// </summary>
        static readonly string[] FixedViewRelPaths = new[]
        {
            "FEBuilderGBA.Avalonia/Views/MapSettingView.axaml",
            "FEBuilderGBA.Avalonia/Views/MapSettingFE6View.axaml",
            "FEBuilderGBA.Avalonia/Views/MapSettingFE7View.axaml",
            "FEBuilderGBA.Avalonia/Views/MapSettingFE7UView.axaml",
            "FEBuilderGBA.Avalonia/Views/SummonUnitViewerView.axaml",
            "FEBuilderGBA.Avalonia/Views/StatusOptionView.axaml",
        };

        /// <summary>
        /// Assert that none of the six fixed view files still contains the raw
        /// Japanese literal as a Content attribute value.  The check is scoped to
        /// <c>Content="リストの拡張"</c> so unrelated Japanese labels in (e.g.)
        /// MapSettingFE6View that are out of scope for this fix do NOT trip it.
        /// </summary>
        [Fact]
        public void ExpandListButton_NoRawJapaneseLiteral()
        {
            string repoRoot = FindRepoRoot();
            foreach (string relPath in FixedViewRelPaths)
            {
                string fullPath = Path.Combine(repoRoot, relPath.Replace('/', Path.DirectorySeparatorChar));
                string text = File.ReadAllText(fullPath);
                Assert.False(
                    text.Contains("Content=\"リストの拡張\"", StringComparison.Ordinal),
                    $"File '{relPath}' still contains raw Japanese Content attribute " +
                    $"'Content=\"リストの拡張\"'. Change it to Content=\"Data Expansion\" " +
                    $"so the Avalonia localizer can translate it via the existing " +
                    $"config/translate/ entries (fix for issue #1691).");
            }
        }

        /// <summary>
        /// Assert that the English literal "Data Expansion" resolves to non-empty
        /// translations in both Japanese and Chinese via the translate-file pipeline:
        ///   en.txt reverse map: "Data Expansion" → "リストの拡張"
        ///   ja.txt forward map: "リストの拡張" → non-empty
        ///   zh.txt forward map: "リストの拡張" → "扩展列表" (non-empty)
        /// This guards against accidental removal of the entries from the translate
        /// files, which would silently break the runtime translation even after the
        /// AXAML fix.
        /// </summary>
        [Fact]
        public void DataExpansionLiteral_HasJaAndZhTranslation()
        {
            string repoRoot = FindRepoRoot();
            string translateDir = Path.Combine(repoRoot, "config", "translate");

            var reverseEnMap = L10nScanner.LoadReverseEnglishMap(Path.Combine(translateDir, "en.txt"));
            var jaMap = L10nScanner.LoadForwardMap(Path.Combine(translateDir, "ja.txt"));
            var zhMap = L10nScanner.LoadForwardMap(Path.Combine(translateDir, "zh.txt"));

            const string literal = "Data Expansion";

            // Step 1: reverse-map English literal → Japanese key.
            Assert.True(
                reverseEnMap.TryGetValue(literal, out string? jaKey) && !string.IsNullOrEmpty(jaKey),
                $"config/translate/en.txt does not map '{literal}' to any Japanese key. " +
                $"Expected entry ':リストの拡張' / 'Data Expansion' in en.txt.");

            // Step 2: forward-map Japanese key in ja.txt → non-empty.
            Assert.True(
                jaMap.TryGetValue(jaKey!, out string? jaTranslation) && !string.IsNullOrEmpty(jaTranslation),
                $"config/translate/ja.txt does not have a non-empty entry for key '{jaKey}'. " +
                $"The Japanese translation of 'Data Expansion' is missing.");

            // Step 3: forward-map Japanese key in zh.txt → "扩展列表" (non-empty).
            Assert.True(
                zhMap.TryGetValue(jaKey!, out string? zhTranslation) && !string.IsNullOrEmpty(zhTranslation),
                $"config/translate/zh.txt does not have a non-empty entry for key '{jaKey}'. " +
                $"Expected Chinese translation '扩展列表' (fix for issue #1691).");

            // Sanity: confirm the Chinese value matches the expected string.
            Assert.Equal("扩展列表", zhTranslation);
        }

        /// <summary>
        /// Walk up the directory tree from the test assembly until FEBuilderGBA.sln
        /// is found.  Mirrors the pattern in <see cref="L10nCoverageTest.FindRepoRoot"/>.
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
    }
}
