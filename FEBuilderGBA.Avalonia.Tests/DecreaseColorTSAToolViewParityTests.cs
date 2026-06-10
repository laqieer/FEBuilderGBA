using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// Static build-gate assertions for the Color Reduction Tool view (#998 PR 2).
    /// These guard the two parity points Copilot flagged in plan review:
    ///   * Method/Size/Reserve combo labels must be code-populated via R._()
    ///     (ViewTranslationHelper does NOT translate ComboBoxItem content), so
    ///     the AXAML must contain NO hardcoded &lt;ComboBoxItem&gt; literals.
    ///   * The new code-only combo/status literals must have ja AND zh entries
    ///     in config/translate/{ja,zh}.txt (the AXAML L10nCoverageTest can't see
    ///     code-populated strings).
    /// </summary>
    public class DecreaseColorTSAToolViewParityTests
    {
        static string RepoRoot()
        {
            string dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
                dir = Path.GetDirectoryName(dir)!;
            return dir ?? throw new InvalidOperationException("Cannot find solution root");
        }

        static string ViewAxamlPath() => Path.Combine(
            RepoRoot(), "FEBuilderGBA.Avalonia", "Views", "DecreaseColorTSAToolView.axaml");

        static string CodeBehindPath() => Path.Combine(
            RepoRoot(), "FEBuilderGBA.Avalonia", "Views", "DecreaseColorTSAToolView.axaml.cs");

        [Fact]
        public void Axaml_HasNoHardcodedComboBoxItemLabels()
        {
            string axaml = File.ReadAllText(ViewAxamlPath());
            // ViewTranslationHelper does not translate ComboBoxItem.Content, so
            // these would never localize. Combo items must be populated in code.
            Assert.DoesNotContain("<ComboBoxItem", axaml, StringComparison.Ordinal);
        }

        [Fact]
        public void CodeBehind_PopulatesComboItemsViaR()
        {
            string code = File.ReadAllText(CodeBehindPath());
            Assert.Contains("MethodCombo.Items.Add(R._(", code, StringComparison.Ordinal);
            Assert.Contains("SizeMethodCombo.Items.Add(R._(", code, StringComparison.Ordinal);
            Assert.Contains("ReserveCombo.Items.Add(R._(", code, StringComparison.Ordinal);
        }

        [Theory]
        // Method combo labels
        [InlineData("0: Manual (no preset)")]
        [InlineData("1: BG & CG")]
        [InlineData("2: Battle BG")]
        [InlineData("3: World Map (large)")]
        [InlineData("4: World Map (event)")]
        [InlineData("5: 256-color no-TSA")]
        [InlineData("6: Status screen BG (FE8)")]
        [InlineData("7: Single-image map chips")]
        [InlineData("8: Single-image map chips (10 colors)")]
        [InlineData("9: BG 256-color no-TSA (cutscene)")]
        [InlineData("A: BG 224-color no-TSA (talk)")]
        // Size + reserve combo labels
        [InlineData("Resize (crop/pad)")]
        [InlineData("Scale")]
        // Status messages
        [InlineData("Color reduction complete:")]
        [InlineData("Please select a valid input and output file.")]
        [InlineData("Color reduction failed. See the log for details.")]
        [InlineData("Reducing colors...")]
        public void CodeOnlyLiteral_HasJaAndZhTranslation(string literal)
        {
            string repo = RepoRoot();
            var ja = LoadForwardKeys(Path.Combine(repo, "config", "translate", "ja.txt"));
            var zh = LoadForwardKeys(Path.Combine(repo, "config", "translate", "zh.txt"));

            Assert.True(ja.Contains(literal), $"ja.txt is missing a translation for: {literal}");
            Assert.True(zh.Contains(literal), $"zh.txt is missing a translation for: {literal}");
        }

        /// <summary>
        /// Parse a translate file and return the set of source keys (`:Key` lines)
        /// that have a non-empty following translation line.
        /// </summary>
        static System.Collections.Generic.HashSet<string> LoadForwardKeys(string path)
        {
            var keys = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
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
