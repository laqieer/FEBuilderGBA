using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using FEBuilderGBA.E2ETests.Helpers;
using Xunit;

namespace FEBuilderGBA.E2ETests.Tests
{
    /// <summary>
    /// Runs image/map CLI commands (decreasecolor variants, convertmap1picture) and
    /// persists stdout/stderr/exitCode to .log files for regression tracking.
    /// </summary>
    public class CliOutputLogImageTests : IDisposable
    {
        private static readonly string CliExe = AppRunner.FindCliExePath();
        private readonly List<string> _tempFiles = new();

        private string TempFile(string ext = ".tmp")
        {
            var path = Path.Combine(Path.GetTempPath(), $"febuilder_log_{Guid.NewGuid():N}{ext}");
            _tempFiles.Add(path);
            return path;
        }

        public void Dispose()
        {
            foreach (var f in _tempFiles)
                try { if (File.Exists(f)) File.Delete(f); } catch { }
        }

        private static void SaveLog(string fileName, int exitCode, string stdout, string stderr)
        {
            string dir = Environment.GetEnvironmentVariable("FEBUILDERGBA_CLI_LOG_DIR")
                ?? Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "cli-logs");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, fileName);
            File.WriteAllText(path,
                $"=== {fileName} ===\r\nTimestamp: {DateTime.UtcNow:O}\r\nExitCode: {exitCode}\r\n\r\n--- STDOUT ---\r\n{stdout}\r\n--- STDERR ---\r\n{stderr}\r\n");
        }

        /// <summary>Creates a small 16x16 test PNG with 4 distinct colors.</summary>
        private string CreateTestPng()
        {
            var pngPath = TempFile(".png");
            using var bmp = new Bitmap(16, 16, PixelFormat.Format32bppArgb);
            // Paint four 8x8 quadrants in different colors
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                {
                    Color c = (x < 8, y < 8) switch
                    {
                        (true, true) => Color.Red,
                        (false, true) => Color.Green,
                        (true, false) => Color.Blue,
                        _ => Color.Yellow,
                    };
                    bmp.SetPixel(x, y, c);
                }
            bmp.Save(pngPath, ImageFormat.Png);
            return pngPath;
        }

        [Fact]
        public void Log_DecreaseColor_Basic()
        {
            var input = CreateTestPng();
            var output = TempFile(".png");

            var (code, stdout, stderr) = AppRunner.Run(
                CliExe, $"--decreasecolor --in=\"{input}\" --out=\"{output}\" --paletteno=16",
                timeoutMs: 30_000);
            SaveLog("CLI_decreasecolor_basic.log", code, stdout, stderr);
            Assert.True(stdout.Length + stderr.Length > 0, "Process should produce output");
        }

        [Fact]
        public void Log_DecreaseColor_NoScale()
        {
            var input = CreateTestPng();
            var output = TempFile(".png");

            var (code, stdout, stderr) = AppRunner.Run(
                CliExe, $"--decreasecolor --in=\"{input}\" --out=\"{output}\" --paletteno=16 --noScale",
                timeoutMs: 30_000);
            SaveLog("CLI_decreasecolor_noScale.log", code, stdout, stderr);
            Assert.True(stdout.Length + stderr.Length > 0, "Process should produce output");
        }

        [Fact]
        public void Log_DecreaseColor_NoReserve1stColor()
        {
            var input = CreateTestPng();
            var output = TempFile(".png");

            var (code, stdout, stderr) = AppRunner.Run(
                CliExe, $"--decreasecolor --in=\"{input}\" --out=\"{output}\" --paletteno=16 --noReserve1stColor",
                timeoutMs: 30_000);
            SaveLog("CLI_decreasecolor_noReserve1stColor.log", code, stdout, stderr);
            Assert.True(stdout.Length + stderr.Length > 0, "Process should produce output");
        }

        [Fact]
        public void Log_DecreaseColor_IgnoreTSA()
        {
            var input = CreateTestPng();
            var output = TempFile(".png");

            var (code, stdout, stderr) = AppRunner.Run(
                CliExe, $"--decreasecolor --in=\"{input}\" --out=\"{output}\" --paletteno=16 --ignoreTSA",
                timeoutMs: 30_000);
            SaveLog("CLI_decreasecolor_ignoreTSA.log", code, stdout, stderr);
            Assert.True(stdout.Length + stderr.Length > 0, "Process should produce output");
        }

        [Fact]
        public void Log_DecreaseColor_AllFlags()
        {
            var input = CreateTestPng();
            var output = TempFile(".png");

            var (code, stdout, stderr) = AppRunner.Run(
                CliExe, $"--decreasecolor --in=\"{input}\" --out=\"{output}\" --paletteno=16 --noScale --noReserve1stColor --ignoreTSA",
                timeoutMs: 30_000);
            SaveLog("CLI_decreasecolor_allFlags.log", code, stdout, stderr);
            Assert.True(stdout.Length + stderr.Length > 0, "Process should produce output");
        }

        [Fact]
        public void Log_ConvertMap1Picture()
        {
            // Create an 8x8 test image (minimum valid map size: must be multiple of 8)
            var pngPath = TempFile(".png");
            using (var bmp = new Bitmap(8, 8, PixelFormat.Format32bppArgb))
            {
                for (int y = 0; y < 8; y++)
                    for (int x = 0; x < 8; x++)
                        bmp.SetPixel(x, y, Color.FromArgb(255, x * 32, y * 32, 128));
                bmp.Save(pngPath, ImageFormat.Png);
            }

            var outImg = TempFile(".bin");
            var outTSA = TempFile(".bin");

            var (code, stdout, stderr) = AppRunner.Run(
                CliExe, $"--convertmap1picture --in=\"{pngPath}\" --outImg=\"{outImg}\" --outTSA=\"{outTSA}\"",
                timeoutMs: 30_000);
            SaveLog("CLI_convertmap1picture.log", code, stdout, stderr);
            Assert.True(stdout.Length + stderr.Length > 0, "Process should produce output");
        }
    }
}
