using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// #1784 app-wide regression guard (audit from #1772). A fixed-width action
    /// <c>&lt;Button&gt;</c> with <c>HorizontalAlignment="Right"</c> but no
    /// <c>HorizontalContentAlignment</c> renders its label left-shifted, because
    /// Avalonia's <c>Button</c> content alignment defaults to <c>Left</c>/<c>Top</c>
    /// (the Fluent template does not override it). This test fails if any such button
    /// is (re)introduced in any Avalonia view.
    /// </summary>
    public class DialogButtonCenteringTests
    {
        [Fact]
        public void NoFixedWidthRightAlignedButton_LacksContentCentering()
        {
            var viewsDir = FindViewsDir();
            if (viewsDir == null)
            {
                // Packaged CI without the repo checkout — nothing to scan.
                return;
            }

            var buttonRx = new Regex(@"<Button\b[^>]*?/?>", RegexOptions.Singleline);
            var widthRx = new Regex("Width=\"\\d");

            var offenders = new List<string>();
            int scanned = 0;
            foreach (var file in Directory.GetFiles(viewsDir, "*.axaml"))
            {
                scanned++;
                var text = File.ReadAllText(file);
                foreach (Match m in buttonRx.Matches(text))
                {
                    var tag = m.Value;
                    if (widthRx.IsMatch(tag)
                        && tag.Contains("HorizontalAlignment=\"Right\"")
                        && !tag.Contains("HorizontalContentAlignment"))
                    {
                        var content = Regex.Match(tag, "Content=\"([^\"]*)\"");
                        offenders.Add(Path.GetFileName(file) + " -> \"" +
                            (content.Success ? content.Groups[1].Value : "?") + "\"");
                    }
                }
            }

            Assert.True(scanned > 50,
                $"Expected to scan the Avalonia views, only saw {scanned} .axaml files.");

            Assert.True(offenders.Count == 0,
                "Fixed-width right-aligned buttons must also set " +
                "HorizontalContentAlignment=\"Center\" (else the label renders left-shifted; " +
                "see #1772/#1784). Offenders:\n  " + string.Join("\n  ", offenders));
        }

        private static string FindViewsDir()
        {
            var dir = AppContext.BaseDirectory;
            for (int i = 0; i < 12; i++)
            {
                var candidate = Path.Combine(dir, "FEBuilderGBA.Avalonia", "Views");
                if (Directory.Exists(candidate))
                    return candidate;
                dir = Path.GetDirectoryName(dir);
                if (dir == null) break;
            }
            return null;
        }
    }
}
