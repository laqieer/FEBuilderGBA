// #1867: "Web (WebAssembly)" must be a selectable option in the bug-report Platform dropdown of
// BOTH issue forms, both forms must offer the same platform set, and every value
// BugReportCore.DetectPlatformLabel() can prefill must be one of those options (no drift between the
// GUI "report a bug" prefill and what the GitHub issue form actually accepts).
using System;
using System.Collections.Generic;
using System.IO;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    public class IssueTemplatePlatformTests
    {
        // The canonical Platform option list (order-significant), shared by gui_bug.yml and
        // bug_report.yml. Keep in sync with BugReportCore.DetectPlatformLabel's return values.
        static readonly string[] ExpectedPlatformOptions =
        {
            "Windows x64", "Windows x86 (WinForms)", "Linux x64",
            "macOS Apple Silicon (arm64)", "macOS Intel (x64)", "Android",
            "iOS / iPadOS", "Web (WebAssembly)", "Other"
        };

        static string FindRepoRoot()
        {
            var dir = AppContext.BaseDirectory;
            for (int i = 0; i < 12; i++)
            {
                if (File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
                    return dir;
                dir = Path.GetDirectoryName(dir);
                if (dir == null) break;
            }
            return null;
        }

        // Extract the option strings under the `id: platform` dropdown of a GitHub Issue-Form yml,
        // without a YAML dependency (the runner may not reference one).
        static List<string> ReadPlatformOptions(string ymlPath)
        {
            var options = new List<string>();
            bool inPlatform = false, inOptions = false;
            foreach (var raw in File.ReadAllLines(ymlPath))
            {
                var trimmed = raw.Trim();
                if (!inPlatform)
                {
                    if (trimmed == "id: platform") inPlatform = true;
                    continue;
                }
                if (!inOptions)
                {
                    if (trimmed == "options:") inOptions = true;
                    continue;
                }
                if (trimmed.StartsWith("- "))
                {
                    string v = trimmed.Substring(2).Trim();
                    // Accept "double", 'single', or unquoted values, and drop any inline `# comment`.
                    if (v.StartsWith("\""))
                    {
                        int e = v.IndexOf('"', 1);
                        v = e > 0 ? v.Substring(1, e - 1) : v.Trim('"');
                    }
                    else if (v.StartsWith("'"))
                    {
                        int e = v.IndexOf('\'', 1);
                        v = e > 0 ? v.Substring(1, e - 1) : v.Trim('\'');
                    }
                    else
                    {
                        int h = v.IndexOf(" #", StringComparison.Ordinal);
                        if (h >= 0) v = v.Substring(0, h);
                        v = v.Trim();
                    }
                    options.Add(v);
                }
                else if (trimmed.Length > 0)
                    break; // left the options list (e.g. "validations:")
            }
            return options;
        }

        [Theory]
        [InlineData("gui_bug.yml")]
        [InlineData("bug_report.yml")]
        public void PlatformDropdown_HasWeb_AndMatchesExpected(string template)
        {
            string root = FindRepoRoot();
            if (root == null) return; // packaged CI without the repo checkout

            string path = Path.Combine(root, ".github", "ISSUE_TEMPLATE", template);
            Assert.True(File.Exists(path), $"Missing issue template {path}");

            var options = ReadPlatformOptions(path);
            Assert.Contains("Web (WebAssembly)", options); // the #1867 gap
            Assert.Equal(ExpectedPlatformOptions, options.ToArray());
        }

        [Fact]
        public void DetectPlatformLabel_ReturnsASelectablePlatformOption()
        {
            // The GUI "report a bug" prefill must produce a value the Platform dropdown accepts —
            // otherwise the prefilled value is silently dropped by GitHub's issue-form validation.
            Assert.Contains(BugReportCore.DetectPlatformLabel(), ExpectedPlatformOptions);
        }
    }
}
