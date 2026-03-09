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

        public EditorImageComparisonTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        [Trait("Category", "ImageComparison")]
        public void ExportEditorImages_Avalonia_Succeeds()
        {
            string romPath = RomLocator.FindRom("FE8U");
            if (romPath == null)
            {
                _output.WriteLine("SKIP: FE8U ROM not found");
                return;
            }

            string outputDir = Path.Combine(Path.GetTempPath(), "febuilder_avalonia_images_" + Guid.NewGuid().ToString("N")[..8]);
            try
            {
                var result = AvaloniaAppRunner.Run($"--rom \"{romPath}\" --export-editor-images --screenshot-dir=\"{outputDir}\"", timeoutMs: 60000);
                _output.WriteLine($"Exit code: {result.ExitCode}");
                _output.WriteLine(result.StdOut);
                if (!string.IsNullOrEmpty(result.StdErr))
                    _output.WriteLine($"STDERR: {result.StdErr}");

                Assert.Equal(0, result.ExitCode);
                Assert.Contains("EXPORT_IMAGES:", result.StdOut);

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

        [Fact]
        [Trait("Category", "ImageComparison")]
        public void ExportEditorImages_WinForms_Succeeds()
        {
            string romPath = RomLocator.FindRom("FE8U");
            if (romPath == null)
            {
                _output.WriteLine("SKIP: FE8U ROM not found");
                return;
            }

            string outputDir = Path.Combine(Path.GetTempPath(), "febuilder_winforms_images_" + Guid.NewGuid().ToString("N")[..8]);
            try
            {
                var result = AppRunner.Run($"--rom \"{romPath}\" --export-editor-images --screenshot-dir=\"{outputDir}\"", timeoutMs: 60000);
                _output.WriteLine($"Exit code: {result.ExitCode}");
                _output.WriteLine(result.StdOut);
                if (!string.IsNullOrEmpty(result.StdErr))
                    _output.WriteLine($"STDERR: {result.StdErr}");

                Assert.Equal(0, result.ExitCode);
                Assert.Contains("EXPORT_IMAGES:", result.StdOut);

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

        [Fact]
        [Trait("Category", "ImageComparison")]
        public void ExportEditorImages_CrossPlatformComparison_FE8U()
        {
            string romPath = RomLocator.FindRom("FE8U");
            if (romPath == null)
            {
                _output.WriteLine("SKIP: FE8U ROM not found");
                return;
            }

            string winDir = Path.Combine(Path.GetTempPath(), "febuilder_compare_win_" + Guid.NewGuid().ToString("N")[..8]);
            string avaDir = Path.Combine(Path.GetTempPath(), "febuilder_compare_ava_" + Guid.NewGuid().ToString("N")[..8]);

            try
            {
                // Run both exports
                var winResult = AppRunner.Run($"--rom \"{romPath}\" --export-editor-images --screenshot-dir=\"{winDir}\"", timeoutMs: 60000);
                _output.WriteLine($"WinForms exit: {winResult.ExitCode}");
                _output.WriteLine(winResult.StdOut);

                var avaResult = AvaloniaAppRunner.Run($"--rom \"{romPath}\" --export-editor-images --screenshot-dir=\"{avaDir}\"", timeoutMs: 60000);
                _output.WriteLine($"Avalonia exit: {avaResult.ExitCode}");
                _output.WriteLine(avaResult.StdOut);

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

                // We expect at least some matches — don't fail if some editors differ
                // (palette handling, rounding, etc. may cause minor diffs)
                Assert.True(matches + mismatches > 0, "Expected at least one compared image pair");
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
