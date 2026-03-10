using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using FEBuilderGBA.E2ETests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.E2ETests.Tests
{
    /// <summary>
    /// Compares decoded graphics editor images between WinForms and Avalonia.
    /// Both apps export images via --export-editor-images, then we compare pixel data.
    /// </summary>
    [Collection("E2E")]
    public class EditorImageComparisonTests
    {
        private readonly ITestOutputHelper _output;
        private static readonly string? WinFormsExe = AppRunner.FindExePath();
        private static readonly string? AvaloniaExe = AvaloniaAppRunner.FindExePath();

        public EditorImageComparisonTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [SkippableFact]
        [Trait("Category", "ImageComparison")]
        public void ExportEditorImages_Avalonia_Succeeds()
        {
            Skip.If(AvaloniaExe == null, "Avalonia exe not found");
            string romPath = RomLocator.FE8U;
            Skip.If(romPath == null, "FE8U ROM not found");

            string outputDir = Path.Combine(Path.GetTempPath(), "febuilder_avalonia_images_" + Guid.NewGuid().ToString("N")[..8]);
            try
            {
                var (exitCode, stdout, stderr) = AvaloniaAppRunner.Run(
                    AvaloniaExe!, $"--rom \"{romPath}\" --export-editor-images --screenshot-dir=\"{outputDir}\"", timeoutMs: 60000);
                _output.WriteLine($"Exit code: {exitCode}");
                _output.WriteLine(stdout);
                if (!string.IsNullOrEmpty(stderr))
                    _output.WriteLine($"STDERR: {stderr}");

                Assert.Equal(0, exitCode);
                Assert.Contains("EXPORT_IMAGES:", stdout);

                // Check that at least some PNG files were created
                if (Directory.Exists(outputDir))
                {
                    var pngs = Directory.GetFiles(outputDir, "*.png");
                    _output.WriteLine($"Exported {pngs.Length} PNG files");
                    Assert.True(pngs.Length > 0, "Expected at least one exported image");
                }
            }
            finally
            {
                if (Directory.Exists(outputDir))
                    Directory.Delete(outputDir, true);
            }
        }

        [SkippableFact]
        [Trait("Category", "ImageComparison")]
        public void ExportEditorImages_WinForms_Succeeds()
        {
            Skip.If(WinFormsExe == null, "WinForms exe not found");
            string romPath = RomLocator.FE8U;
            Skip.If(romPath == null, "FE8U ROM not found");

            string outputDir = Path.Combine(Path.GetTempPath(), "febuilder_winforms_images_" + Guid.NewGuid().ToString("N")[..8]);
            try
            {
                var (exitCode, stdout, stderr) = AppRunner.Run(
                    WinFormsExe!, $"--rom \"{romPath}\" --export-editor-images --screenshot-dir=\"{outputDir}\"", timeoutMs: 60000);
                _output.WriteLine($"Exit code: {exitCode}");
                _output.WriteLine(stdout);
                if (!string.IsNullOrEmpty(stderr))
                    _output.WriteLine($"STDERR: {stderr}");

                Assert.Equal(0, exitCode);
                Assert.Contains("EXPORT_IMAGES:", stdout);

                if (Directory.Exists(outputDir))
                {
                    var pngs = Directory.GetFiles(outputDir, "*.png");
                    _output.WriteLine($"Exported {pngs.Length} PNG files");
                    Assert.True(pngs.Length > 0, "Expected at least one exported image");
                }
            }
            finally
            {
                if (Directory.Exists(outputDir))
                    Directory.Delete(outputDir, true);
            }
        }

        [SkippableFact]
        [Trait("Category", "ImageComparison")]
        public void ExportEditorImages_CrossPlatformComparison_FE8U()
        {
            Skip.If(WinFormsExe == null, "WinForms exe not found");
            Skip.If(AvaloniaExe == null, "Avalonia exe not found");
            string romPath = RomLocator.FE8U;
            Skip.If(romPath == null, "FE8U ROM not found");

            string winDir = Path.Combine(Path.GetTempPath(), "febuilder_compare_win_" + Guid.NewGuid().ToString("N")[..8]);
            string avaDir = Path.Combine(Path.GetTempPath(), "febuilder_compare_ava_" + Guid.NewGuid().ToString("N")[..8]);

            try
            {
                // Run both exports
                var (winExit, winOut, winErr) = AppRunner.Run(
                    WinFormsExe!, $"--rom \"{romPath}\" --export-editor-images --screenshot-dir=\"{winDir}\"", timeoutMs: 60000);
                _output.WriteLine($"WinForms exit: {winExit}");
                _output.WriteLine(winOut);

                var (avaExit, avaOut, avaErr) = AvaloniaAppRunner.Run(
                    AvaloniaExe!, $"--rom \"{romPath}\" --export-editor-images --screenshot-dir=\"{avaDir}\"", timeoutMs: 60000);
                _output.WriteLine($"Avalonia exit: {avaExit}");
                _output.WriteLine(avaOut);

                if (!Directory.Exists(winDir) || !Directory.Exists(avaDir))
                {
                    _output.WriteLine("SKIP: Output directories not created");
                    return;
                }

                // Compare matching files
                var winFiles = Directory.GetFiles(winDir, "*.png")
                    .Select(f => Path.GetFileName(f))
                    .ToHashSet();
                var avaFiles = Directory.GetFiles(avaDir, "*.png")
                    .Select(f => Path.GetFileName(f))
                    .ToHashSet();

                // Find matching editor names by stripping platform prefix
                var winEditors = winFiles.Select(f => f.Replace("WinForms_", "")).ToHashSet();
                var avaEditors = avaFiles.Select(f => f.Replace("Avalonia_", "")).ToHashSet();
                var commonEditors = winEditors.Intersect(avaEditors).ToList();

                _output.WriteLine($"WinForms files: {winFiles.Count}, Avalonia files: {avaFiles.Count}, Common: {commonEditors.Count}");

                int matches = 0;
                int mismatches = 0;
                var mismatchDetails = new List<string>();

                foreach (var editorFile in commonEditors)
                {
                    string winPath = Path.Combine(winDir, "WinForms_" + editorFile);
                    string avaPath = Path.Combine(avaDir, "Avalonia_" + editorFile);

                    if (!File.Exists(winPath) || !File.Exists(avaPath)) continue;

                    var comparison = CompareImages(winPath, avaPath);
                    if (comparison.Match)
                    {
                        matches++;
                        _output.WriteLine($"COMPARE: {editorFile} ... MATCH ({comparison.Width}x{comparison.Height})");
                    }
                    else
                    {
                        mismatches++;
                        string detail = $"{editorFile}: {comparison.Reason}";
                        mismatchDetails.Add(detail);
                        _output.WriteLine($"COMPARE: {editorFile} ... MISMATCH: {comparison.Reason}");
                    }
                }

                _output.WriteLine($"COMPARE: Results: {matches} match, {mismatches} mismatch out of {commonEditors.Count}");
                foreach (var detail in mismatchDetails)
                    _output.WriteLine($"COMPARE: Detail: {detail}");

                // Report per-editor pass/fail summary
                var expectedEditors = new[]
                {
                    "PortraitViewer", "BattleBGViewer", "BattleTerrainViewer", "BigCGViewer",
                    "ChapterTitleViewer", "ChapterTitleFE7Viewer", "ItemIconViewer",
                    "SystemIconViewer", "OPClassFontViewer", "OPPrologueViewer",
                    "ImagePortraitFE6", "ImageBG", "ImageCG", "ImageCGFE7U",
                    "ImageTSAAnime", "ImageBattleBG"
                };
                var coveredEditors = commonEditors.Select(f => f.Split('_')[0]).Distinct().ToHashSet();
                _output.WriteLine($"COMPARE: Editor coverage: {coveredEditors.Count}/{expectedEditors.Length}");
                foreach (var editor in expectedEditors)
                {
                    bool covered = coveredEditors.Contains(editor);
                    _output.WriteLine($"COMPARE: Editor {editor}: {(covered ? "COVERED" : "NOT COVERED")}");
                }

                // Strict: all compared images must match pixel-perfect
                Assert.True(matches + mismatches > 0, "Expected at least one compared image pair");
                Assert.True(mismatches == 0,
                    $"Pixel-perfect comparison failed: {mismatches} mismatched image(s):\n" +
                    string.Join("\n", mismatchDetails));
            }
            finally
            {
                if (Directory.Exists(winDir)) Directory.Delete(winDir, true);
                if (Directory.Exists(avaDir)) Directory.Delete(avaDir, true);
            }
        }

        struct ImageComparisonResult
        {
            public bool Match;
            public string Reason;
            public int Width;
            public int Height;
        }

        /// <summary>
        /// Compare two PNG images by loading raw bytes and checking dimensions + pixel data.
        /// </summary>
        static ImageComparisonResult CompareImages(string path1, string path2)
        {
            try
            {
                using var bmp1 = new System.Drawing.Bitmap(path1);
                using var bmp2 = new System.Drawing.Bitmap(path2);

                if (bmp1.Width != bmp2.Width || bmp1.Height != bmp2.Height)
                {
                    return new ImageComparisonResult
                    {
                        Match = false,
                        Reason = $"Size mismatch: {bmp1.Width}x{bmp1.Height} vs {bmp2.Width}x{bmp2.Height}",
                        Width = bmp1.Width,
                        Height = bmp1.Height
                    };
                }

                int diffPixels = 0;
                int totalPixels = bmp1.Width * bmp1.Height;

                for (int y = 0; y < bmp1.Height; y++)
                {
                    for (int x = 0; x < bmp1.Width; x++)
                    {
                        var c1 = bmp1.GetPixel(x, y);
                        var c2 = bmp2.GetPixel(x, y);
                        if (c1.ToArgb() != c2.ToArgb())
                            diffPixels++;
                    }
                }

                if (diffPixels == 0)
                {
                    return new ImageComparisonResult
                    {
                        Match = true,
                        Width = bmp1.Width,
                        Height = bmp1.Height
                    };
                }

                double diffPercent = (double)diffPixels / totalPixels * 100;
                return new ImageComparisonResult
                {
                    Match = false,
                    Reason = $"{diffPixels}/{totalPixels} pixels differ ({diffPercent:F1}%)",
                    Width = bmp1.Width,
                    Height = bmp1.Height
                };
            }
            catch (Exception ex)
            {
                return new ImageComparisonResult
                {
                    Match = false,
                    Reason = $"Error: {ex.Message}"
                };
            }
        }
    }
}
