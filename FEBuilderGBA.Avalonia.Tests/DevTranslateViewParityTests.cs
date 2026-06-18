using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// Static build-gate assertions for the Developer Translation Tool view
    /// (#1164 — Avalonia port of WF DevTranslateForm / Core MyTranslateBuild).
    ///
    /// Guards:
    ///   * The code-only runtime R._ status/error literals (which the AXAML
    ///     L10nCoverageTest cannot see) have ja AND zh translations.
    ///   * The tool is a read-no-ROM-bytes orphan: its VM must NOT take part in
    ///     the data-verification contract (no IDataVerifiable).
    ///   * The view is wired into the MainWindow editor registry.
    /// </summary>
    public class DevTranslateViewParityTests
    {
        static string RepoRoot()
        {
            string dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
                dir = Path.GetDirectoryName(dir)!;
            return dir ?? throw new InvalidOperationException("Cannot find solution root");
        }

        [Theory]
        // Status / progress messages built at runtime via R._() (code-only).
        [InlineData("Translating... This may take a while.")]
        [InlineData("Translation complete. Please restart the tool.")]
        [InlineData("Translation failed. See the log for details.")]
        [InlineData("Converting designer strings...")]
        [InlineData("Designer string conversion complete.")]
        [InlineData("Reversing designer strings...")]
        [InlineData("Designer string reverse complete.")]
        [InlineData("Scanning: ")]
        [InlineData("Could not open the folder picker.")]
        [InlineData("Please select a target language (ja and auto cannot be selected).")]
        [InlineData("Please select a valid source code folder.")]
        public void CodeOnlyLiteral_HasJaAndZhTranslation(string literal)
        {
            string repo = RepoRoot();
            var ja = LoadForwardKeys(Path.Combine(repo, "config", "translate", "ja.txt"));
            var zh = LoadForwardKeys(Path.Combine(repo, "config", "translate", "zh.txt"));

            Assert.True(ja.Contains(literal), $"ja.txt is missing a translation for: {literal}");
            Assert.True(zh.Contains(literal), $"zh.txt is missing a translation for: {literal}");
        }

        [Fact]
        public void ViewModel_DoesNotImplementDataVerifiable()
        {
            // This is a read-no-ROM-bytes tool; the VM is an orphan by the
            // FEBuilderGBA.Tests data-verification contract. Even MENTIONING the
            // interface name in the VM source would trip NoOrphanVMs, so assert
            // the source never references it.
            string repo = RepoRoot();
            string vm = Path.Combine(repo, "FEBuilderGBA.Avalonia", "ViewModels", "DevTranslateViewModel.cs");
            string src = File.ReadAllText(vm);
            Assert.DoesNotContain("IDataVerifiable", src);
        }

        [Fact]
        public void View_IsRegisteredInMainWindow()
        {
            string repo = RepoRoot();
            string mainWindow = File.ReadAllText(
                Path.Combine(repo, "FEBuilderGBA.Avalonia", "Views", "MainWindow.axaml.cs"));
            // The editor factory entry + the menu button handler must both exist.
            Assert.Contains("Open<DevTranslateView>()", mainWindow);
            Assert.Contains("\"DevTranslateView\"", mainWindow);
        }

        // --- SanitizeProgress: the status-label sanitizer (#1252 review fix) -----

        [Theory]
        [InlineData("a\r\nb", "a b")]                 // CRLF -> single space
        [InlineData("a\nb\nc", "a b c")]              // LF -> spaces
        [InlineData("a\tb", "a b")]                   // tab -> space
        [InlineData("a\r\n\r\n\r\nb", "a b")]         // runs of newlines collapse to one space
        [InlineData("  a   b  ", "a b")]              // leading/trailing + inner runs collapse
        [InlineData("plain", "plain")]               // unchanged
        [InlineData("", "")]                          // empty
        public void SanitizeProgress_CollapsesWhitespaceToSingleLine(string input, string expected)
        {
            Assert.Equal(expected, FEBuilderGBA.Avalonia.Views.DevTranslateView.SanitizeProgress(input));
        }

        [Fact]
        public void SanitizeProgress_TruncatesOverlongValues()
        {
            string input = new string('x', 500);
            string result = FEBuilderGBA.Avalonia.Views.DevTranslateView.SanitizeProgress(input);
            // 120 chars + the single-char ellipsis.
            Assert.Equal(121, result.Length);
            Assert.EndsWith("…", result);
            Assert.StartsWith("xxxx", result);
        }

        [Fact]
        public void SanitizeProgress_MultilineLongValue_IsSingleLineAndTruncated()
        {
            // A realistic worst case: a multiline untranslated target longer than
            // the cap. Result must be one line (no newlines) and truncated.
            string input = "line one of a very long target\r\n" + new string('y', 300) + "\r\nlast line";
            string result = FEBuilderGBA.Avalonia.Views.DevTranslateView.SanitizeProgress(input);
            Assert.DoesNotContain('\n', result);
            Assert.DoesNotContain('\r', result);
            Assert.True(result.Length <= 121, $"expected <=121 chars, got {result.Length}");
            Assert.EndsWith("…", result);
        }

        /// <summary>
        /// Parse a translate file and return the set of source keys (`:Key` lines)
        /// that have a non-empty following translation line.
        /// </summary>
        static HashSet<string> LoadForwardKeys(string path)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            if (!File.Exists(path)) return keys;

            string? src = null;
            foreach (string raw in File.ReadLines(path))
            {
                if (raw.Length == 0) { src = null; continue; }
                if (src == null)
                {
                    if (raw[0] != ':') continue;
                    src = raw.Substring(1).Replace("\\r\\n", "\r\n");
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(raw))
                        keys.Add(src);
                    src = null;
                }
            }
            return keys;
        }
    }
}
