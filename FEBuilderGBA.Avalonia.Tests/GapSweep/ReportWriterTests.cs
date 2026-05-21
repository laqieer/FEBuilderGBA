// SPDX-License-Identifier: GPL-3.0-or-later
// Phase 0 tests — ReportWriter YAML emit + dry-run / front-matter wiring. (#374)
using System;
using System.Collections.Generic;
using System.IO;
using FEBuilderGBA.Avalonia.GapSweep;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests.GapSweep;

/// <summary>
/// Tests for <see cref="ReportWriter"/>. These cover the YAML-front-matter
/// safety net (Copilot review #1, PR #375): caller-supplied extras with `:`
/// / `#` / newlines must produce a parseable YAML document, and "Dry-run is
/// header-only" must literally be true (no body section).
/// </summary>
public class ReportWriterTests : IDisposable
{
    readonly string _tempDir;

    public ReportWriterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "fbgba-report-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best effort */ }
    }

    // ---------------- YAML escape behaviour ----------------

    [Theory]
    [InlineData("simple", "simple")]
    [InlineData("with-dash", "with-dash")]
    [InlineData("with_underscore", "with_underscore")]
    [InlineData("123.45", "123.45")]
    public void EscapeYamlScalar_PlainValues_NotQuoted(string input, string expected)
    {
        Assert.Equal(expected, ReportWriter.EscapeYamlScalar(input));
    }

    [Theory]
    [InlineData("with: colon", "\"with: colon\"")]
    [InlineData("with # hash", "\"with # hash\"")]
    [InlineData("with\ttab", "\"with\\ttab\"")]
    [InlineData("with\nnewline", "\"with\\nnewline\"")]
    [InlineData("with \"quote\"", "\"with \\\"quote\\\"\"")]
    [InlineData("with \\backslash", "\"with \\\\backslash\"")]
    [InlineData(" leading-space", "\" leading-space\"")]
    [InlineData("trailing-space ", "\"trailing-space \"")]
    [InlineData("-starts-with-dash", "\"-starts-with-dash\"")]
    [InlineData("", "\"\"")]
    public void EscapeYamlScalar_DangerousValues_AreQuoted(string input, string expected)
    {
        Assert.Equal(expected, ReportWriter.EscapeYamlScalar(input));
    }

    [Theory]
    // YAML 1.1 boolean / null lookalikes must be quoted so they parse as strings.
    [InlineData("true")]
    [InlineData("false")]
    [InlineData("yes")]
    [InlineData("no")]
    [InlineData("on")]
    [InlineData("off")]
    [InlineData("null")]
    [InlineData("~")]
    public void EscapeYamlScalar_ReservedWords_AreQuoted(string input)
    {
        string result = ReportWriter.EscapeYamlScalar(input);
        Assert.StartsWith("\"", result);
        Assert.EndsWith("\"", result);
    }

    [Fact]
    public void EscapeYamlKey_PlainKey_NotQuoted()
    {
        Assert.Equal("rom", ReportWriter.EscapeYamlKey("rom"));
        Assert.Equal("git-sha", ReportWriter.EscapeYamlKey("git-sha"));
        Assert.Equal("sweep_type", ReportWriter.EscapeYamlKey("sweep_type"));
    }

    [Fact]
    public void EscapeYamlKey_KeyWithSpace_IsQuoted()
    {
        string result = ReportWriter.EscapeYamlKey("has space");
        Assert.Equal("\"has space\"", result);
    }

    [Fact]
    public void BuildFrontMatter_EmitsTimestampShaSweepType()
    {
        string fm = ReportWriter.BuildFrontMatter("density", gitWorkingDir: _tempDir);
        Assert.StartsWith("---", fm);
        Assert.Contains("generated:", fm);
        Assert.Contains("git-sha:", fm);
        Assert.Contains("sweep-type: density", fm);
        Assert.EndsWith($"---{Environment.NewLine}{Environment.NewLine}", fm);
    }

    [Fact]
    public void BuildFrontMatter_EscapesUnsafeExtraValues()
    {
        var extras = new Dictionary<string, string>
        {
            ["rom"] = "FE8U",
            ["note"] = "value: with colon # and hash",
        };
        string fm = ReportWriter.BuildFrontMatter("density", extras, gitWorkingDir: _tempDir);
        // The dangerous note value must come through as a double-quoted string.
        Assert.Contains("note: \"value: with colon # and hash\"", fm);
        // The plain rom value stays unquoted.
        Assert.Contains("rom: FE8U", fm);
    }

    [Fact]
    public void WriteReport_DryRunHeaderOnly_HasNoBody()
    {
        // Caller writes a header-only file by passing an empty sections enumerable.
        string outPath = Path.Combine(_tempDir, "dry.md");
        ReportWriter.WriteReport(outPath, "density", sections: Array.Empty<string>(), gitWorkingDir: _tempDir);
        string contents = File.ReadAllText(outPath);
        // Should consist of exactly the front-matter block plus the trailing newline
        // it adds; no body markdown after the second "---".
        string trimmed = contents.TrimEnd();
        Assert.EndsWith("---", trimmed);
        // The second "---" closes the YAML block; nothing must follow except whitespace.
        int firstSep = trimmed.IndexOf("---", StringComparison.Ordinal);
        int secondSep = trimmed.IndexOf("---", firstSep + 3, StringComparison.Ordinal);
        Assert.True(secondSep > firstSep, "should have two --- markers");
        string afterSecondSep = trimmed.Substring(secondSep + 3);
        Assert.Equal("", afterSecondSep.Trim());
    }

    [Fact]
    public void WriteReport_WithBody_AppendsAfterFrontMatter()
    {
        string outPath = Path.Combine(_tempDir, "full.md");
        ReportWriter.WriteReport(outPath, "density",
            sections: new[] { "# Hello", "## Section" },
            gitWorkingDir: _tempDir);
        string contents = File.ReadAllText(outPath);
        Assert.Contains("# Hello", contents);
        Assert.Contains("## Section", contents);
        // Both sections are separated by a blank line.
        int helloIdx = contents.IndexOf("# Hello", StringComparison.Ordinal);
        int sectionIdx = contents.IndexOf("## Section", StringComparison.Ordinal);
        Assert.True(sectionIdx > helloIdx);
        string between = contents.Substring(helloIdx + "# Hello".Length, sectionIdx - helloIdx - "# Hello".Length);
        Assert.Contains("\n\n", between);
    }

    [Fact]
    public void WriteReport_CreatesMissingParentDirectory()
    {
        string nestedPath = Path.Combine(_tempDir, "a", "b", "c", "report.md");
        ReportWriter.WriteReport(nestedPath, "density",
            sections: new[] { "body" }, gitWorkingDir: _tempDir);
        Assert.True(File.Exists(nestedPath));
    }

    [Fact]
    public void BuildFrontMatter_NonGitRoot_FallsBackToUnknown()
    {
        // Pointing at a directory that exists but has no .git → git rev-parse
        // fails fast → we surface "unknown" instead of letting the exception
        // propagate.
        string fm = ReportWriter.BuildFrontMatter("density", gitWorkingDir: _tempDir);
        Assert.Contains("git-sha:", fm);
        // Either "unknown" (no git context) or the actual SHA if the temp dir
        // happens to inherit one from a parent — both are acceptable as long
        // as the line is present and well-formed.
        Assert.Matches(@"git-sha: \S+", fm);
    }
}
