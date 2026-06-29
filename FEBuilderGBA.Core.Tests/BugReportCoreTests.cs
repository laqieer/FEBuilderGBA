using System;
using System.Collections.Generic;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    public class BugReportCoreTests
    {
        private static readonly string[] KnownPlatformLabels =
        {
            "Windows x64", "Windows x86 (WinForms)", "Linux x64",
            "macOS Apple Silicon (arm64)", "macOS Intel (x64)", "Android", "Other"
        };

        [Fact]
        public void BuildIssueUrl_ContainsTemplate()
        {
            var url = BugReportCore.BuildIssueUrl("laqieer", "FEBuilderGBA", "gui_bug.yml",
                new Dictionary<string, string>());
            Assert.Contains("?template=gui_bug.yml", url);
        }

        [Fact]
        public void BuildIssueUrl_EncodesSpace()
        {
            var url = BugReportCore.BuildIssueUrl("laqieer", "FEBuilderGBA", "gui_bug.yml",
                new Dictionary<string, string> { ["editor"] = "Class Editor" });
            Assert.Contains("Class%20Editor", url);
        }

        [Fact]
        public void BuildIssueUrl_EncodesHash()
        {
            var url = BugReportCore.BuildIssueUrl("laqieer", "FEBuilderGBA", "gui_bug.yml",
                new Dictionary<string, string> { ["editor"] = "Test#1" });
            Assert.Contains("%231", url);
        }

        [Fact]
        public void BuildIssueUrl_EncodesNonAscii()
        {
            var url = BugReportCore.BuildIssueUrl("laqieer", "FEBuilderGBA", "gui_bug.yml",
                new Dictionary<string, string> { ["editor"] = "テスト" });
            Assert.DoesNotContain("テスト", url);
        }

        [Fact]
        public void BuildIssueUrl_OmitsEmptyFields()
        {
            var url = BugReportCore.BuildIssueUrl("laqieer", "FEBuilderGBA", "gui_bug.yml",
                new Dictionary<string, string> { ["editor"] = "", ["version"] = "ver_20260629.10" });
            Assert.DoesNotContain("editor=", url);
            Assert.Contains("version=ver_20260629.10", url);
        }

        [Fact]
        public void BuildIssueUrl_RoundTrip_ContainsAllKeys()
        {
            var fields = new Dictionary<string, string>
            {
                ["version"]  = "ver_20260629.10",
                ["rom"]      = "FE8 (US)",
                ["editor"]   = "Class Editor",
                ["platform"] = "Windows x64",
                ["app"]      = "Avalonia GUI (cross-platform)"
            };
            var url = BugReportCore.BuildIssueUrl("laqieer", "FEBuilderGBA", "gui_bug.yml", fields);
            Assert.Contains("template=gui_bug.yml", url);
            Assert.Contains("version=ver_20260629.10", url);
            Assert.Contains("rom=FE8%20%28US%29", url);
            Assert.Contains("editor=Class%20Editor", url);
        }

        [Fact]
        public void DetectPlatformLabel_ReturnsKnownValue()
        {
            var label = BugReportCore.DetectPlatformLabel();
            Assert.NotEmpty(label);
            Assert.Contains(label, KnownPlatformLabels);
        }

        [Theory]
        [InlineData("FE6",  "FE6 (JP)")]
        [InlineData("FE7J", "FE7 (JP)")]
        [InlineData("FE7U", "FE7 (US)")]
        [InlineData("FE8J", "FE8 (JP)")]
        [InlineData("FE8U", "FE8 (US)")]
        [InlineData(null,   "Other / not sure")]
        [InlineData("",     "Other / not sure")]
        [InlineData("FEX",  "Other / not sure")]
        public void RomTagToLabel_AllCases(string? input, string expected)
        {
            Assert.Equal(expected, BugReportCore.RomTagToLabel(input));
        }

        [Fact]
        public void BuildPrefill_ProducesExpectedUrl()
        {
            var fields = BugReportCore.BuildPrefill(
                "ver_20260629.10", "FE8U", "Class Editor", "Avalonia GUI (cross-platform)");
            var url = BugReportCore.BuildIssueUrl(
                BugReportCore.Owner, BugReportCore.Repo, BugReportCore.GuiBugTemplate, fields);
            Assert.Contains("template=gui_bug.yml", url);
            Assert.Contains("version=ver_20260629.10", url);
            Assert.Contains("rom=FE8%20%28US%29", url);
            Assert.Contains("editor=Class%20Editor", url);
            // Print for verification
            Console.WriteLine($"[BugReportCoreTests] Example URL: {url}");
        }

        [Theory]
        [InlineData("ver_20260629.10", "ver_20260629.10")]
        [InlineData("20260629.10", "ver_20260629.10")]
        [InlineData(null, "")]
        [InlineData("", "")]
        public void NormalizeVersion_Tests(string? input, string expected)
        {
            Assert.Equal(expected, BugReportCore.NormalizeVersion(input));
        }

        [Fact]
        public void BuildPrefill_VersionStartsWithVer()
        {
            var fields = BugReportCore.BuildPrefill("20260629.10", "FE8U", null, null);
            Assert.True(fields.ContainsKey("version"));
            Assert.StartsWith("ver_", fields["version"]);
        }

        [Fact]
        public void BuildPrefill_DoesNotPrefillScreenshotOrReport7z()
        {
            var fields = BugReportCore.BuildPrefill("ver_20260629.10", "FE8U", "Class Editor", "Avalonia GUI (cross-platform)");
            Assert.DoesNotContain("screenshot", fields.Keys);
            Assert.DoesNotContain("report7z", fields.Keys);
        }
    }
}
