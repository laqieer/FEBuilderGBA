using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using FEBuilderGBA.E2ETests.Helpers;
using Xunit;

namespace FEBuilderGBA.E2ETests.Tests
{
    public sealed class CliGenerateFontE2ETests : IDisposable
    {
        private static readonly string CliExe = AppRunner.FindCliExePath();
        private readonly List<string> _paths = new();
        private readonly string _workDir;

        public CliGenerateFontE2ETests()
        {
            _workDir = Path.Combine(AppContext.BaseDirectory, "cli-generate-font-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_workDir);
            _paths.Add(_workDir);
        }

        public void Dispose()
        {
            for (int i = _paths.Count - 1; i >= 0; i--)
            {
                try
                {
                    string path = _paths[i];
                    if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
                    else if (File.Exists(path)) File.Delete(path);
                }
                catch { }
            }
        }

        [Fact]
        public void GenerateFont_WithFontFile_ProducesCharacterTilesAndPreservesMessage()
        {
            string output = Path.Combine(_workDir, "font.png");
            string font = Path.Combine(FindSolutionRoot(), "FEBuilderGBA.Avalonia.Tests", "Fonts", "Tuffy-Regular.ttf");

            var (code, stdout, stderr) = AppRunner.Run(CliExe,
                $"--generate-font --out=\"{output}\" --text=ABZ --font-file=\"{font}\" --font-size=12 --vertical-offset=0",
                timeoutMs: 30_000);

            Assert.Equal(0, code);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal($"Font generated: {output} (3 chars, 48x16, 0 bytes){Environment.NewLine}", stdout);
            Assert.True(File.Exists(output));

            using var bitmap = new Bitmap(output);
            Assert.Equal(48, bitmap.Width);
            Assert.Equal(16, bitmap.Height);

            int nonWhitePixels = 0;
            int opaqueBlackPixels = 0;
            int white = Color.White.ToArgb();
            for (int y = 0; y < bitmap.Height; y++)
            {
                for (int x = 0; x < bitmap.Width; x++)
                {
                    Color pixel = bitmap.GetPixel(x, y);
                    if (pixel.ToArgb() != white)
                    {
                        nonWhitePixels++;
                    }

                    if (pixel.A == 255 && pixel.R == 0 && pixel.G == 0 && pixel.B == 0)
                    {
                        opaqueBlackPixels++;
                    }
                }
            }

            Assert.Equal(109, nonWhitePixels);
            Assert.Equal(24, opaqueBlackPixels);
        }

        [Fact]
        public void GenerateFont_MissingFontFile_PreservesErrorBehavior()
        {
            string output = Path.Combine(_workDir, "missing-font.png");
            string missingFont = Path.Combine(_workDir, "missing.ttf");

            var (code, stdout, stderr) = AppRunner.Run(CliExe,
                $"--generate-font --out=\"{output}\" --text=A --font-file=\"{missingFont}\"",
                timeoutMs: 30_000);

            Assert.Equal(1, code);
            Assert.Equal(string.Empty, stdout);
            Assert.Equal($"Error: Font file not found: {missingFont}{Environment.NewLine}", stderr);
            Assert.False(File.Exists(output));
        }

        private static string FindSolutionRoot()
        {
            string? dir = AppContext.BaseDirectory;
            for (int i = 0; i < 10 && dir != null; i++)
            {
                if (File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
                    return dir;
                dir = Directory.GetParent(dir)?.FullName;
            }

            throw new InvalidOperationException("Could not locate FEBuilderGBA.sln from test output.");
        }
    }
}
