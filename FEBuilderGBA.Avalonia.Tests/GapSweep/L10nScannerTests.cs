// SPDX-License-Identifier: GPL-3.0-or-later
// Phase 6 tests — L10nScanner literal detection + translation join + formatting. (#374, #356)
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FEBuilderGBA.Avalonia.GapSweep;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests.GapSweep;

/// <summary>
/// Tests for <see cref="L10nScanner"/>. Almost everything runs via in-memory
/// XML strings plus hand-built translation maps so the suite is hermetic; a
/// single end-to-end test exercises the on-disk parser to catch encoding /
/// EOL regressions.
/// </summary>
public class L10nScannerTests
{
    // =====================================================================
    // IsCandidateLiteral — heuristic gate for "should we record this as a
    // literal?".
    // =====================================================================

    [Fact]
    public void IsCandidateLiteral_DetectsMultiwordEnglish()
    {
        // Multi-word labels are the canonical case.
        Assert.True(L10nScanner.IsCandidateLiteral("Base Stats", out bool ne));
        Assert.False(ne);
    }

    [Fact]
    public void IsCandidateLiteral_DetectsSingleWordEnglish()
    {
        // Single word with ≥ 4 chars and high ASCII-letter density.
        Assert.True(L10nScanner.IsCandidateLiteral("Save", out bool ne));
        Assert.False(ne);
    }

    [Fact]
    public void IsCandidateLiteral_SkipsMarkupExtensions()
    {
        // Markup extensions are the most common false positive; binding
        // syntax must be ignored entirely.
        Assert.False(L10nScanner.IsCandidateLiteral("{Binding Text}", out _));
        Assert.False(L10nScanner.IsCandidateLiteral("{StaticResource X}", out _));
        Assert.False(L10nScanner.IsCandidateLiteral("{x:Static c:Strings.Foo}", out _));
        Assert.False(L10nScanner.IsCandidateLiteral("{DynamicResource Y}", out _));
        Assert.False(L10nScanner.IsCandidateLiteral("{TemplateBinding Z}", out _));
    }

    [Fact]
    public void IsCandidateLiteral_SkipsEmptyAndPunctuation()
    {
        Assert.False(L10nScanner.IsCandidateLiteral("", out _));
        Assert.False(L10nScanner.IsCandidateLiteral(" ", out _));
        Assert.False(L10nScanner.IsCandidateLiteral("?", out _));
        Assert.False(L10nScanner.IsCandidateLiteral("...", out _));
        Assert.False(L10nScanner.IsCandidateLiteral(":", out _));
    }

    [Fact]
    public void IsCandidateLiteral_SkipsSingleChar()
    {
        Assert.False(L10nScanner.IsCandidateLiteral("X", out _));
        Assert.False(L10nScanner.IsCandidateLiteral("1", out _));
    }

    [Fact]
    public void IsCandidateLiteral_SkipsPureDigits()
    {
        Assert.False(L10nScanner.IsCandidateLiteral("100", out _));
        Assert.False(L10nScanner.IsCandidateLiteral("0x10", out _));
        Assert.False(L10nScanner.IsCandidateLiteral("3.14", out _));
    }

    [Fact]
    public void IsCandidateLiteral_FlagsCjkAsNonEnglish()
    {
        // Hiragana / Katakana / CJK Unified are all treated as already-localised
        // source and tagged NonEnglish so the report can call them out separately.
        Assert.True(L10nScanner.IsCandidateLiteral("保存", out bool ne));
        Assert.True(ne);

        Assert.True(L10nScanner.IsCandidateLiteral("セーブ", out bool ne2));
        Assert.True(ne2);

        Assert.True(L10nScanner.IsCandidateLiteral("저장", out bool ne3));
        Assert.True(ne3);
    }

    [Fact]
    public void IsCandidateLiteral_SkipsShortPseudoWords()
    {
        // 3-character single-word tokens without space — too noisy.
        Assert.False(L10nScanner.IsCandidateLiteral("RGB", out _));
    }

    [Fact]
    public void IsCandidateLiteral_RejectsLeadingPunctuation()
    {
        // ":Status" is a metadata-y leftover, not a label.
        Assert.False(L10nScanner.IsCandidateLiteral(":Status", out _));
    }

    // =====================================================================
    // Translation join — given an English literal and the maps, can we look
    // up a translation in each language?
    // =====================================================================

    [Fact]
    public void TranslationJoin_AllLanguagesHaveIt_Translated()
    {
        var (reverseEn, langMaps) = BuildMaps(
            entries: new[]
            {
                ("書き込み", "Save", "保存", "保存", "저장"),
            });

        var xml = """
            <UserControl xmlns="https://github.com/avaloniaui">
                <Button Content="Save" />
            </UserControl>
            """;

        var findings = L10nScanner.ScanXmlString(xml, "fake.axaml", reverseEn, langMaps);
        var finding = Assert.Single(findings);
        Assert.Equal(L10nVerdict.Translated, finding.Verdict);
        Assert.True(finding.TranslationStatus["ja"]);
        Assert.True(finding.TranslationStatus["zh"]);
        Assert.True(finding.TranslationStatus["ko"]);
    }

    [Fact]
    public void TranslationJoin_OnlyJaHasIt_PartiallyTranslated()
    {
        // Build maps where ja has a translation but zh/ko don't have an entry
        // for "Save"'s pivot Japanese key.
        var reverseEn = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Save"] = "書き込み",
        };
        var jaMap = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["書き込み"] = "保存",
        };
        var zhMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var koMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var langMaps = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
        {
            ["ja"] = jaMap,
            ["zh"] = zhMap,
            ["ko"] = koMap,
        };

        var xml = """
            <UserControl xmlns="https://github.com/avaloniaui">
                <Button Content="Save" />
            </UserControl>
            """;

        var findings = L10nScanner.ScanXmlString(xml, "fake.axaml", reverseEn, langMaps);
        var finding = Assert.Single(findings);
        Assert.Equal(L10nVerdict.PartiallyTranslated, finding.Verdict);
        Assert.True(finding.TranslationStatus["ja"]);
        Assert.False(finding.TranslationStatus["zh"]);
        Assert.False(finding.TranslationStatus["ko"]);
    }

    [Fact]
    public void TranslationJoin_NoLanguageHasIt_Untranslated()
    {
        // Empty maps — every language reports "no translation".
        var reverseEn = new Dictionary<string, string>(StringComparer.Ordinal);
        var langMaps = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
        {
            ["ja"] = new Dictionary<string, string>(StringComparer.Ordinal),
            ["zh"] = new Dictionary<string, string>(StringComparer.Ordinal),
            ["ko"] = new Dictionary<string, string>(StringComparer.Ordinal),
        };

        var xml = """
            <UserControl xmlns="https://github.com/avaloniaui">
                <TextBlock Text="Identity" />
            </UserControl>
            """;

        var findings = L10nScanner.ScanXmlString(xml, "fake.axaml", reverseEn, langMaps);
        var finding = Assert.Single(findings);
        Assert.Equal(L10nVerdict.Untranslated, finding.Verdict);
        Assert.False(finding.TranslationStatus["ja"]);
        Assert.False(finding.TranslationStatus["zh"]);
        Assert.False(finding.TranslationStatus["ko"]);
    }

    [Fact]
    public void TranslationJoin_NormalisesTrailingColon()
    {
        // AXAML uses "Save", translation tables historically use "Save:".
        // Normalisation must collide them to the same key. Build the maps via
        // LoadReverseEnglishMap / LoadForwardMap so the trim-trailing-colon
        // alias really exists (the in-disk parsers add both raw + normalised
        // forms — direct-dictionary tests bypass that).
        string tempDir = Path.Combine(Path.GetTempPath(), "L10nNormaliseTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string enFile = Path.Combine(tempDir, "en.txt");
            string jaFile = Path.Combine(tempDir, "ja.txt");
            File.WriteAllText(enFile, ":書き込み:\nSave:\n\n");      // Both sides carry the trailing colon.
            File.WriteAllText(jaFile, ":書き込み:\n保存\n\n");        // Translation under the colon-form key.

            var reverseEn = L10nScanner.LoadReverseEnglishMap(enFile);
            var jaMap = L10nScanner.LoadForwardMap(jaFile);
            var langMaps = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
            {
                ["ja"] = jaMap,
            };

            var xml = """
                <UserControl xmlns="https://github.com/avaloniaui">
                    <Button Content="Save" />
                </UserControl>
                """;

            var findings = L10nScanner.ScanXmlString(xml, "fake.axaml", reverseEn, langMaps);
            var finding = Assert.Single(findings);
            Assert.True(finding.TranslationStatus["ja"], "Trailing-colon normalisation should match \"Save\" → \"Save:\" entry");
            Assert.Equal(L10nVerdict.Translated, finding.Verdict);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); }
            catch { /* best effort */ }
        }
    }

    // =====================================================================
    // AXAML attribute parsing — the gate that decides which attributes
    // contribute candidates.
    // =====================================================================

    [Fact]
    public void Scan_PicksUpButtonContent()
    {
        // Plain Content attribute.
        var xml = """
            <UserControl xmlns="https://github.com/avaloniaui">
                <Button Content="Save" />
            </UserControl>
            """;
        var findings = L10nScanner.ScanXmlString(xml, "fake.axaml", EmptyReverse(), EmptyLangs("ja"));
        Assert.Single(findings, f => f.Literal == "Save" && f.AttributeName == "Content");
    }

    [Fact]
    public void Scan_PicksUpTextBoxWatermark()
    {
        var xml = """
            <UserControl xmlns="https://github.com/avaloniaui">
                <TextBox Watermark="Search items..." />
            </UserControl>
            """;
        var findings = L10nScanner.ScanXmlString(xml, "fake.axaml", EmptyReverse(), EmptyLangs("ja"));
        Assert.Single(findings, f => f.Literal == "Search items..." && f.AttributeName == "Watermark");
    }

    [Fact]
    public void Scan_PicksUpToolTipTipAttachedProperty()
    {
        // CRITICAL: ToolTip.Tip is the Avalonia attached-property form. PR
        // #377 Phase 2 had a bug where this was missed until Copilot caught
        // it. Do not regress.
        var xml = """
            <UserControl xmlns="https://github.com/avaloniaui">
                <TextBlock Text="Foo" ToolTip.Tip="Help text" />
            </UserControl>
            """;
        var findings = L10nScanner.ScanXmlString(xml, "fake.axaml", EmptyReverse(), EmptyLangs("ja"));
        Assert.Contains(findings, f => f.AttributeName == "ToolTip.Tip" && f.Literal == "Help text");
    }

    [Fact]
    public void Scan_PicksUpHeaderAttribute()
    {
        var xml = """
            <UserControl xmlns="https://github.com/avaloniaui">
                <Expander Header="Advanced Settings" />
            </UserControl>
            """;
        var findings = L10nScanner.ScanXmlString(xml, "fake.axaml", EmptyReverse(), EmptyLangs("ja"));
        Assert.Single(findings, f => f.Literal == "Advanced Settings" && f.AttributeName == "Header");
    }

    [Fact]
    public void Scan_SkipsMarkupExtensionAttributes()
    {
        // Bindings, resources, static refs — must all be skipped at the
        // attribute level (not just classified, but never appear in the
        // findings list at all).
        var xml = """
            <UserControl xmlns="https://github.com/avaloniaui">
                <Button Content="{Binding Text}" />
                <TextBlock Text="{StaticResource X}" />
                <TextBox Watermark="{DynamicResource Y}" />
            </UserControl>
            """;
        var findings = L10nScanner.ScanXmlString(xml, "fake.axaml", EmptyReverse(), EmptyLangs("ja"));
        Assert.Empty(findings);
    }

    [Fact]
    public void Scan_SkipsInsideDataTemplate()
    {
        // Per-row DataTemplate Text isn't a label — it's a template. Skip.
        var xml = """
            <UserControl xmlns="https://github.com/avaloniaui">
                <ListBox.ItemTemplate>
                    <DataTemplate>
                        <TextBlock Text="Per-row Label" />
                    </DataTemplate>
                </ListBox.ItemTemplate>
                <Button Content="Outside Template" />
            </UserControl>
            """;
        var findings = L10nScanner.ScanXmlString(xml, "fake.axaml", EmptyReverse(), EmptyLangs("ja"));
        // The button outside the template counts; the TextBlock inside doesn't.
        Assert.Single(findings, f => f.Literal == "Outside Template");
        Assert.DoesNotContain(findings, f => f.Literal == "Per-row Label");
    }

    [Fact]
    public void Scan_SkipsInsideStyle()
    {
        var xml = """
            <UserControl xmlns="https://github.com/avaloniaui">
                <UserControl.Styles>
                    <Style>
                        <Setter Property="TextBlock.Text" Value="Style Text" />
                    </Style>
                </UserControl.Styles>
                <Button Content="Outside Style" />
            </UserControl>
            """;
        var findings = L10nScanner.ScanXmlString(xml, "fake.axaml", EmptyReverse(), EmptyLangs("ja"));
        // The button outside counts; setters inside don't (Setter has Value
        // attribute which isn't in our CandidateAttributes list anyway, but
        // belt-and-braces).
        Assert.Single(findings, f => f.Literal == "Outside Style");
    }

    [Fact]
    public void Scan_CapturesLineNumbers()
    {
        // Line numbers come from IXmlLineInfo via LoadOptions.SetLineInfo.
        var xml = "<UserControl xmlns=\"https://github.com/avaloniaui\">\n"
                + "    <Button Content=\"Save\" />\n"
                + "    <Button Content=\"Cancel\" />\n"
                + "</UserControl>";
        var findings = L10nScanner.ScanXmlString(xml, "fake.axaml", EmptyReverse(), EmptyLangs("ja"));
        Assert.Equal(2, findings.Count);
        Assert.Equal(2, findings.First(f => f.Literal == "Save").LineNumber);
        Assert.Equal(3, findings.First(f => f.Literal == "Cancel").LineNumber);
    }

    [Fact]
    public void Scan_RecordsElementAndAttributeNames()
    {
        var xml = """
            <UserControl xmlns="https://github.com/avaloniaui">
                <TextBlock Text="Hello World" />
            </UserControl>
            """;
        var findings = L10nScanner.ScanXmlString(xml, "fake.axaml", EmptyReverse(), EmptyLangs("ja"));
        var f = Assert.Single(findings);
        Assert.Equal("TextBlock", f.ElementName);
        Assert.Equal("Text", f.AttributeName);
        Assert.Equal("Hello World", f.Literal);
    }

    [Fact]
    public void Scan_PreservesOriginalCasingAndPunctuation()
    {
        // The Literal field preserves the AXAML attribute value verbatim,
        // not the normalised key.
        var xml = """
            <UserControl xmlns="https://github.com/avaloniaui">
                <Button Content="  Save:  " />
            </UserControl>
            """;
        var findings = L10nScanner.ScanXmlString(xml, "fake.axaml", EmptyReverse(), EmptyLangs("ja"));
        var f = Assert.Single(findings);
        Assert.Equal("  Save:  ", f.Literal);
    }

    // =====================================================================
    // FormatReport — markdown layout, LF newlines, per-language coverage %.
    // =====================================================================

    [Fact]
    public void FormatReport_UsesLfNewlinesOnly()
    {
        var findings = new[]
        {
            MakeFinding("a.axaml", 1, "Untranslated", new() { ["ja"] = false, ["zh"] = false, ["ko"] = false }, L10nVerdict.Untranslated),
        };
        string report = L10nScanner.FormatReport(findings, new[] { "ja", "zh", "ko" });
        Assert.DoesNotContain("\r", report);
    }

    [Fact]
    public void FormatReport_IncludesPerLanguageCoverage()
    {
        var findings = new[]
        {
            MakeFinding("a.axaml", 1, "Save",      new() { ["ja"] = true,  ["zh"] = true,  ["ko"] = true  }, L10nVerdict.Translated),
            MakeFinding("a.axaml", 2, "Cancel",    new() { ["ja"] = true,  ["zh"] = false, ["ko"] = false }, L10nVerdict.PartiallyTranslated),
            MakeFinding("a.axaml", 3, "Identity",  new() { ["ja"] = false, ["zh"] = false, ["ko"] = false }, L10nVerdict.Untranslated),
        };
        string report = L10nScanner.FormatReport(findings, new[] { "ja", "zh", "ko" });
        // 2 / 3 ja-coverage = 66.7 %; 1 / 3 zh-coverage = 33.3 %; 1 / 3 ko-coverage = 33.3 %.
        Assert.Contains("`ja`", report);
        Assert.Contains("66.7", report);
        Assert.Contains("33.3", report);
        Assert.Contains("## Per-language Coverage", report);
    }

    [Fact]
    public void FormatReport_HandlesEmptyFindings()
    {
        // Empty findings should produce a sensible report with "no untranslated" message.
        var findings = Array.Empty<L10nFinding>();
        string report = L10nScanner.FormatReport(findings, new[] { "ja" });
        Assert.Contains("No untranslated literals", report);
        // Body should still terminate with a single newline (trailing-blank trim).
        Assert.EndsWith("\n", report);
        Assert.DoesNotMatch(@"\n\n$", report);
    }

    [Fact]
    public void FormatReport_GroupsUntranslatedByFile()
    {
        var findings = new[]
        {
            MakeFinding("Views/A.axaml", 1, "Foo", new() { ["ja"] = false }, L10nVerdict.Untranslated),
            MakeFinding("Views/A.axaml", 5, "Bar", new() { ["ja"] = false }, L10nVerdict.Untranslated),
            MakeFinding("Views/B.axaml", 1, "Qux", new() { ["ja"] = false }, L10nVerdict.Untranslated),
        };
        string report = L10nScanner.FormatReport(findings, new[] { "ja" });
        Assert.Contains("### `Views/A.axaml`", report);
        Assert.Contains("### `Views/B.axaml`", report);
        // A has more untranslated than B → A appears first.
        int idxA = report.IndexOf("### `Views/A.axaml`", StringComparison.Ordinal);
        int idxB = report.IndexOf("### `Views/B.axaml`", StringComparison.Ordinal);
        Assert.True(idxA < idxB, "Files with more Untranslated should sort first");
    }

    // =====================================================================
    // End-to-end: synthetic AXAML + synthetic translation files on disk.
    // =====================================================================

    [Fact]
    public void EndToEnd_OnDiskScanProducesExpectedFindings()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "L10nScannerTests_" + Guid.NewGuid().ToString("N"));
        try
        {
            // Build the directory layout the scanner expects.
            string viewsDir = Path.Combine(tempRoot, "FEBuilderGBA.Avalonia", "Views");
            string translateDir = Path.Combine(tempRoot, "config", "translate");
            Directory.CreateDirectory(viewsDir);
            Directory.CreateDirectory(translateDir);

            File.WriteAllText(Path.Combine(viewsDir, "FakeView.axaml"), """
                <UserControl xmlns="https://github.com/avaloniaui">
                    <Button Content="Save" />
                    <Button Content="Identity" />
                    <Button Content="{Binding X}" />
                    <TextBlock Text="保存" />
                </UserControl>
                """);

            // en.txt — Japanese → English
            File.WriteAllText(Path.Combine(translateDir, "en.txt"), ":書き込み\nSave\n\n");
            // ja.txt — Japanese forward map; here it would normally be a no-op
            // (key IS the source), but include it so the language has entries.
            File.WriteAllText(Path.Combine(translateDir, "ja.txt"), ":書き込み\n保存\n\n");
            // zh.txt — Japanese → Chinese
            File.WriteAllText(Path.Combine(translateDir, "zh.txt"), ":書き込み\n保存\n\n");
            // ko.txt — no entries; ko coverage stays at 0.

            var findings = L10nScanner.Scan(tempRoot, new[] { "ja", "zh", "ko" });

            // 3 candidates: "Save" (translated in ja+zh, not ko → Partial),
            // "Identity" (no entries anywhere → Untranslated),
            // "保存" (NonEnglish).
            // The Binding attr is skipped entirely.
            Assert.Equal(3, findings.Count);

            var save = findings.Single(f => f.Literal == "Save");
            Assert.Equal(L10nVerdict.PartiallyTranslated, save.Verdict);
            Assert.True(save.TranslationStatus["ja"]);
            Assert.True(save.TranslationStatus["zh"]);
            Assert.False(save.TranslationStatus["ko"]);

            var identity = findings.Single(f => f.Literal == "Identity");
            Assert.Equal(L10nVerdict.Untranslated, identity.Verdict);

            var jp = findings.Single(f => f.Literal == "保存");
            Assert.Equal(L10nVerdict.NonEnglish, jp.Verdict);

            // Repo-relative path uses forward slashes.
            Assert.All(findings, f =>
            {
                Assert.DoesNotContain("\\", f.AxamlPath);
                Assert.EndsWith("FakeView.axaml", f.AxamlPath);
            });
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); }
            catch { /* best effort */ }
        }
    }

    [Fact]
    public void LoadForwardMap_HandlesEscapedCrLf()
    {
        // The on-disk format encodes CRLF as `\r\n` (literal backslash-r-backslash-n).
        // The parser must translate that back to real CRLF before the lookup.
        string tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, ":line1\\r\\nline2\nvalue\n");
            var map = L10nScanner.LoadForwardMap(tempFile);
            Assert.True(map.ContainsKey("line1\r\nline2"));
            Assert.Equal("value", map["line1\r\nline2"]);
        }
        finally
        {
            try { File.Delete(tempFile); }
            catch { /* best effort */ }
        }
    }

    // =====================================================================
    // Helpers
    // =====================================================================

    static L10nFinding MakeFinding(
        string path, int line, string literal,
        Dictionary<string, bool> status, L10nVerdict verdict)
    {
        return new L10nFinding(
            AxamlPath: path,
            LineNumber: line,
            ElementName: "TextBlock",
            AttributeName: "Text",
            Literal: literal,
            TranslationStatus: status,
            Verdict: verdict);
    }

    // =====================================================================
    // ScanCodeLiterals / ScanCsString — R._("literal") code-literal sweep (#1635)
    // =====================================================================

    [Fact]
    public void ScanCsString_FindsRUnderscoreLiteral_AndJoinsTranslation()
    {
        var (rev, langs) = BuildMaps(new[]
        {
            ("ジャンプ", "Jump", "ジャンプ", "跳转", "점프"),
        });
        const string src = "void M(){ var s = R._(\"Jump\"); }";
        var findings = L10nScanner.ScanCsString(src, "X.cs", rev, langs);
        Assert.Single(findings);
        Assert.Equal("Jump", findings[0].Literal);
        Assert.Equal(L10nVerdict.Translated, findings[0].Verdict);
    }

    [Fact]
    public void ScanCsString_DirectForwardMapKey_Resolves()
    {
        // ja.txt keys many entries by the English literal directly (e.g.
        // ":Characters" -> "キャラクター"). The direct forward-map lookup must hit.
        var langs = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
        {
            ["ja"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["Save failed: {0}"] = "保存に失敗しました: {0}" },
            ["zh"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["Save failed: {0}"] = "保存失败: {0}" },
        };
        const string src = "ShowError(R._(\"Save failed: {0}\", ex));";
        var f = L10nScanner.ScanCsString(src, "X.cs", EmptyReverse(), langs);
        Assert.Single(f);
        Assert.Equal(L10nVerdict.Translated, f[0].Verdict);
    }

    [Fact]
    public void ScanCsString_Untranslated_WhenNoEntry()
    {
        const string src = "ShowError(R._(\"2-ROM Diff: no ROM loaded.\"));";
        var f = L10nScanner.ScanCsString(src, "X.cs", EmptyReverse(), EmptyLangs("ja", "zh"));
        Assert.Single(f);
        Assert.Equal(L10nVerdict.Untranslated, f[0].Verdict);
    }

    [Fact]
    public void ScanCsString_PartiallyTranslated_WhenOnlyOneLang()
    {
        var langs = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
        {
            ["ja"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["Update failed: {0}"] = "更新に失敗しました: {0}" },
            ["zh"] = new Dictionary<string, string>(StringComparer.Ordinal), // missing zh
        };
        const string src = "R._(\"Update failed: {0}\", e);";
        var f = L10nScanner.ScanCsString(src, "X.cs", EmptyReverse(), langs);
        Assert.Single(f);
        Assert.Equal(L10nVerdict.PartiallyTranslated, f[0].Verdict);
    }

    [Fact]
    public void ScanCsString_CjkSourceLiteral_IsNonEnglish()
    {
        // A WinForms Japanese-source literal reused verbatim is already localized
        // in JP mode — classify as NonEnglish (out of scope), not a gap.
        const string src = "R._(\"コピー(&C)\");";
        var f = L10nScanner.ScanCsString(src, "X.cs", EmptyReverse(), EmptyLangs("ja", "zh"));
        Assert.Single(f);
        Assert.Equal(L10nVerdict.NonEnglish, f[0].Verdict);
    }

    [Fact]
    public void ScanCsString_IgnoresLiteralsInLineComments()
    {
        const string src = "// example: R._(\"Commented out string\")\nint x = 1;";
        var f = L10nScanner.ScanCsString(src, "X.cs", EmptyReverse(), EmptyLangs("ja", "zh"));
        Assert.Empty(f);
    }

    [Fact]
    public void ScanCsString_IgnoresLiteralsInBlockComments()
    {
        const string src = "/* R._(\"Block comment string\") */\nint x = 1;";
        var f = L10nScanner.ScanCsString(src, "X.cs", EmptyReverse(), EmptyLangs("ja", "zh"));
        Assert.Empty(f);
    }

    [Fact]
    public void ScanCsString_DocCommentExampleIsIgnored_RealCallIsFound()
    {
        // Mirrors THIS scanner file's own situation: a `///` doc comment that
        // mentions R._("literal") must NOT count, but a real call on the next
        // line MUST.
        var langs = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
        {
            ["ja"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["Home"] = "ホーム" },
            ["zh"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["Home"] = "主页" },
        };
        const string src = "/// Matches R._(\"literal\") calls.\nvar t = R._(\"Home\");";
        var f = L10nScanner.ScanCsString(src, "X.cs", EmptyReverse(), langs);
        Assert.Single(f);
        Assert.Equal("Home", f[0].Literal);
        Assert.Equal(2, f[0].LineNumber); // comment on line 1, real call on line 2
    }

    [Fact]
    public void ReadCsStringLiteral_DecodesEscapes()
    {
        const string src = "R._(\"a\\r\\nb\\\"c\")";
        int q = src.IndexOf('"');
        Assert.True(L10nScanner.ReadCsStringLiteral(src, q, verbatim: false, out string v));
        Assert.Equal("a\r\nb\"c", v);
    }

    [Fact]
    public void ReadCsStringLiteral_HandlesVerbatim()
    {
        const string src = "R._(@\"path\\to\\\"\"file\"\"\")";
        int q = src.IndexOf('"');
        Assert.True(L10nScanner.ReadCsStringLiteral(src, q, verbatim: true, out string v));
        Assert.Equal("path\\to\\\"file\"", v);
    }

    [Fact]
    public void StripComments_PreservesStringsAndLayout()
    {
        const string src = "var url = \"http://x\"; // comment\nint y;";
        string stripped = L10nScanner.StripCommentsPreservingLayout(src);
        // The URL string survives (the // inside it is NOT a comment).
        Assert.Contains("\"http://x\"", stripped);
        // The trailing // comment body is blanked.
        Assert.DoesNotContain("comment", stripped);
        // Length and newline count are unchanged.
        Assert.Equal(src.Length, stripped.Length);
        Assert.Equal(src.Count(c => c == '\n'), stripped.Count(c => c == '\n'));
    }

    [Fact]
    public void ScanCodeLiterals_OverRepo_FindsTranslatedToolDiffLiteral()
    {
        // End-to-end against the real repo + shipped translate files: the #1635
        // example literal must now resolve as Translated (no Partial/Untranslated
        // for it).
        string repoRoot = FindRepoRootForTest();
        var findings = L10nScanner.ScanCodeLiterals(repoRoot, new[] { "ja", "zh" });
        Assert.NotEmpty(findings);
        var diff = findings.FirstOrDefault(f => f.Literal == "2-ROM Diff: no ROM loaded.");
        Assert.NotNull(diff);
        Assert.Equal(L10nVerdict.Translated, diff!.Verdict);
    }

    static string FindRepoRootForTest()
    {
        string start = AppContext.BaseDirectory;
        for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "FEBuilderGBA.sln")))
                return dir.FullName;
        }
        throw new InvalidOperationException("Could not locate FEBuilderGBA.sln");
    }

    static IReadOnlyDictionary<string, string> EmptyReverse()
        => new Dictionary<string, string>(StringComparer.Ordinal);

    static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> EmptyLangs(params string[] langs)
    {
        var dict = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        foreach (string lang in langs)
            dict[lang] = new Dictionary<string, string>(StringComparer.Ordinal);
        return dict;
    }

    static (IReadOnlyDictionary<string, string> reverseEn,
            IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> langMaps)
        BuildMaps(
            (string japaneseKey, string english, string japanese, string chinese, string korean)[] entries)
    {
        var reverseEn = new Dictionary<string, string>(StringComparer.Ordinal);
        var jaMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var zhMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var koMap = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var e in entries)
        {
            reverseEn[e.english] = e.japaneseKey;
            jaMap[e.japaneseKey] = e.japanese;
            zhMap[e.japaneseKey] = e.chinese;
            koMap[e.japaneseKey] = e.korean;
        }
        var langs = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
        {
            ["ja"] = jaMap,
            ["zh"] = zhMap,
            ["ko"] = koMap,
        };
        return (reverseEn, langs);
    }
}
