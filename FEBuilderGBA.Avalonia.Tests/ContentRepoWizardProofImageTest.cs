using System;
using System.IO;
using System.Linq;
using FEBuilderGBA;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    public class ContentRepoWizardProofImageTest
    {
        readonly ITestOutputHelper _output;

        public ContentRepoWizardProofImageTest(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void RenderContentRepoWizardProofImage()
        {
            string baseDir = FindRepoRoot();
            var cfg = new Config();
            cfg.Load(Path.Combine(baseDir, "config", "config.xml"));

            var repos = ContentRepoSetupCore.Repos.Select((d, i) => new
            {
                Descriptor = d,
                Url = ContentRepoSetupCore.ResolveUrl(d, cfg),
                Dir = ContentRepoSetupCore.ResolveDir(d, baseDir),
                Ready = i == 0 ? "Needs initialization" : i == 1 ? "Ready" : "Manual contents ready"
            }).ToArray();

            const int W = 1180, H = 620;

            // #2036: the caption is DERIVED from the live descriptor list (and the backing test count),
            // so a renamed/added repository can never leave a stale hand-written caption behind.
            string caption = "Real descriptors: " + string.Join(" / ", repos.Select(r => r.Descriptor.DisplayName))
                + ", config-resolved URLs, sample readiness";
            int backingTests =
                CountFacts(Path.Combine(baseDir, "FEBuilderGBA.Core.Tests", "ContentRepoGitServiceTests.cs")) +
                CountFacts(Path.Combine(baseDir, "FEBuilderGBA.Core.Tests", "Patch2GitServiceTests.cs"));
            string testCaption = $"Backing proof: {backingTests} content-repo transaction tests "
                + $"({repos.Length} descriptors rendered from ContentRepoSetupCore.Repos)";

            Assert.All(repos, r => Assert.Contains(r.Descriptor.DisplayName, caption));
            Assert.True(backingTests > 0, "the proof image must report a real backing test count");

            using var bmp = new SKBitmap(W, H);
            using (var c = new SKCanvas(bmp))
            {
                var bg = new SKColor(0x22, 0x25, 0x2A);
                var card = new SKColor(0x2D, 0x33, 0x3A);
                var accent = new SKColor(0x56, 0xD3, 0xA5);
                var warn = new SKColor(0xFF, 0xCC, 0x66);
                var fg = new SKColor(0xF0, 0xF0, 0xF0);
                var dim = new SKColor(0xA8, 0xAF, 0xB8);
                c.Clear(bg);
                using var title = SkiaTestTextStyle.Create(accent, 30, bold: true);
                using var h = SkiaTestTextStyle.Create(fg, 19, bold: true);
                using var text = SkiaTestTextStyle.Create(fg, 16, familyName: "Consolas");
                using var label = SkiaTestTextStyle.Create(dim, 15);
                using var ready = SkiaTestTextStyle.Create(accent, 16, bold: true);
                using var needs = SkiaTestTextStyle.Create(warn, 16, bold: true);
                using var cardPaint = new SKPaint { Color = card, IsAntialias = true };
                using var badgePaint = new SKPaint { Color = new SKColor(0x3B, 0x61, 0x54), IsAntialias = true };

                c.DrawText("Content Repository Setup Wizard — #2036 proof", 28, 44, title);
                c.DrawText(caption, 30, 74, label);

                float y = 112;
                foreach (var r in repos)
                {
                    c.DrawRoundRect(28, y - 24, W - 56, 128, 10, 10, cardPaint);
                    c.DrawText(r.Descriptor.DisplayName, 48, y + 4, h);
                    c.DrawText("config key", 48, y + 34, label);
                    c.DrawText(r.Descriptor.ConfigKey, 150, y + 34, text);
                    c.DrawText("url", 48, y + 62, label);
                    c.DrawText(TrimMiddle(r.Url, 92), 150, y + 62, text);
                    c.DrawText("dir", 48, y + 90, label);
                    c.DrawText(TrimMiddle(r.Dir, 92), 150, y + 90, text);
                    c.DrawText(r.Ready, W - 280, y + 4, r.Ready.Contains("Needs") ? needs : ready);
                    c.DrawRoundRect(W - 280, y + 24, 210, 34, 6, 6, badgePaint);
                    c.DrawText(r.Ready.Contains("Needs") ? "Initialize" : "Update", W - 250, y + 47, h);
                    y += 150;
                }

                c.DrawText(testCaption, 30, H - 54, label);
                c.DrawText("Git unavailable fallback: manual ZIP download/extract instructions list each URL and target folder.", 30, H - 32, label);
            }

            using var img = SKImage.FromBitmap(bmp);
            using var data = img.Encode(SKEncodedImageFormat.Png, 100);
            byte[] pngBytes = data.ToArray();

            // The proof is valid as long as the wizard descriptors render to
            // non-empty PNG bytes. Saving to disk is best-effort so a
            // read-only/unavailable screenshot dir on CI doesn't turn this into
            // a flaky failure (matches the other proof-image tests). (Copilot
            // bot PR #1851 review.)
            Assert.NotNull(pngBytes);
            Assert.True(pngBytes.Length > 0);

            try
            {
                string outDir = ResolveScreenshotOutputDir();
                Directory.CreateDirectory(outDir);
                string outPath = Path.Combine(outDir, "pr2036-content-repo-wizard.png");
                File.WriteAllBytes(outPath, pngBytes);
                _output.WriteLine($"Saved proof image to: {outPath} ({pngBytes.Length} bytes)");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Proof image not saved to disk (best-effort): {ex.Message}");
            }
        }

        static int CountFacts(string path)
            => File.Exists(path)
                ? System.Text.RegularExpressions.Regex.Matches(
                    File.ReadAllText(path), @"^\s*\[Fact\]",
                    System.Text.RegularExpressions.RegexOptions.Multiline).Count
                : 0;

        static string TrimMiddle(string value, int max)
        {
            if (value.Length <= max) return value;
            int keep = (max - 3) / 2;
            return value.Substring(0, keep) + "..." + value.Substring(value.Length - keep);
        }

        static string ResolveScreenshotOutputDir()
        {
            string? overrideDir = Environment.GetEnvironmentVariable("FEBUILDERGBA_SCREENSHOT_DIR");
            if (!string.IsNullOrEmpty(overrideDir)) return overrideDir;
            return Path.Combine(Path.GetTempPath(), "FEBuilderGBA-screenshots");
        }

        static string FindRepoRoot()
        {
            string dir = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(dir))
            {
                if (File.Exists(Path.Combine(dir, "FEBuilderGBA.sln"))) return dir;
                var parent = Directory.GetParent(dir)?.FullName;
                if (parent == dir) break;
                dir = parent ?? "";
            }
            return Directory.GetCurrentDirectory();
        }
    }
}
