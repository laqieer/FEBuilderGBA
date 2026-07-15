// SPDX-License-Identifier: GPL-3.0-or-later
// #1890 — the WebAssembly (Browser) head has NO system fonts and shipped only Inter (Latin), so
// Japanese game text and the ja/zh UI rendered as tofu. The fix embeds a compact Noto Sans CJK SC
// subset and registers it as a per-codepoint FontFallback. These tests guard that fix:
//   * the shipped subset actually covers representative Japanese + Simplified-Chinese + kana glyphs
//     (a wrong/empty/over-trimmed font would silently reintroduce tofu);
//   * the font's internal family name matches the "#Noto Sans CJK SC" suffix used in Program.cs
//     (a mismatched suffix makes the avares fallback silently do nothing — the #1 failure mode);
//   * Program.cs registers the fallback and the csproj embeds the font as an AvaloniaResource.
// The net10.0 test project cannot reference the net10.0-browser head, so the font is loaded from disk
// and the wiring is asserted by scanning the head's source.
using System;
using System.IO;
using SkiaSharp;
using Xunit;

namespace FEBuilderGBA.Core.Tests;

public class BrowserCjkFontTests
{
    const string FontRelPath = "FEBuilderGBA.Browser/Assets/Fonts/NotoSansCJKsc-Subset.otf";
    const string ExpectedFamily = "Noto Sans CJK SC";
    const string FallbackUri =
        "avares://FEBuilderGBA.Browser/Assets/Fonts/NotoSansCJKsc-Subset.otf#Noto Sans CJK SC";

    [Fact]
    public void Subset_font_exists_and_is_compact()
    {
        string path = RepoPath(FontRelPath);
        Assert.True(File.Exists(path), $"CJK fallback font missing at {path}");
        long size = new FileInfo(path).Length;
        // ~2.8 MB. Lower bound guards an empty/broken file; upper bound guards a full ~16 MB font
        // slipping in un-subset (a real download+payload regression).
        Assert.InRange(size, 512 * 1024L, 8 * 1024 * 1024L);
    }

    [Fact]
    public void Subset_font_family_name_matches_the_program_fallback_suffix()
    {
        using var tf = SKTypeface.FromFile(RepoPath(FontRelPath));
        Assert.NotNull(tf);
        Assert.Equal(ExpectedFamily, tf!.FamilyName);
        // The '#<family>' suffix in the Program.cs avares URI must equal the real family name,
        // otherwise Avalonia's FontManager can't resolve the fallback and CJK stays tofu.
        Assert.EndsWith("#" + tf.FamilyName, FallbackUri);
    }

    [Theory]
    [InlineData(0x65E5, "CJK ideograph ri (ja/zh)")]
    [InlineData(0x8A9E, "JIS kanji go")]
    [InlineData(0x9F8D, "JIS kanji ryu")]
    [InlineData(0x4F60, "GB2312 hanzi ni")]
    [InlineData(0x8FD9, "simplified-only hanzi zhe")]
    [InlineData(0x3042, "hiragana a")]
    [InlineData(0x30A2, "katakana a")]
    [InlineData(0x3001, "CJK ideographic comma")]
    [InlineData(0xFF21, "fullwidth A")]
    [InlineData(0x0041, "Latin A")]
    public void Subset_font_covers_representative_codepoints(int codepoint, string description)
    {
        using var tf = SKTypeface.FromFile(RepoPath(FontRelPath));
        Assert.NotNull(tf);
        ushort glyph = tf!.GetGlyph(codepoint);
        Assert.True(glyph != 0,
            $"font has no glyph for U+{codepoint:X4} ({description}) — the CJK fallback would render tofu");
    }

    [Fact]
    public void Browser_program_registers_the_cjk_font_fallback()
    {
        string src = File.ReadAllText(RepoPath("FEBuilderGBA.Browser/Program.cs"));
        Assert.Contains(".WithInterFont()", src);
        Assert.Contains("FontFallbacks", src);
        Assert.Contains(FallbackUri, src);
        // Assert the fallback options are actually CHAINED into BuildAvaloniaApp — a
        // defined-but-uncalled factory would still pass a bare name-Contains check.
        Assert.Matches(@"\.WithInterFont\(\)\s*\.With\(\s*CreateBrowserFontManagerOptions\(\)\s*\)", src);
    }

    [Fact]
    public void Browser_csproj_embeds_the_font_and_ships_its_license()
    {
        string proj = File.ReadAllText(RepoPath("FEBuilderGBA.Browser/FEBuilderGBA.Browser.csproj"));
        Assert.Contains("<AvaloniaResource Include=\"Assets\\Fonts\\NotoSansCJKsc-Subset.otf\"", proj);
        Assert.Contains("OFL.txt", proj);
        Assert.True(File.Exists(RepoPath("FEBuilderGBA.Browser/Assets/Fonts/OFL.txt")),
            "the font's SIL OFL license text must ship alongside the binary");
    }

    static string RepoPath(string rel)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "FEBuilderGBA.sln")))
                return Path.Combine(dir.FullName, rel.Replace('/', Path.DirectorySeparatorChar));
            dir = dir.Parent;
        }
        throw new InvalidOperationException("repo root (FEBuilderGBA.sln) not found");
    }
}
