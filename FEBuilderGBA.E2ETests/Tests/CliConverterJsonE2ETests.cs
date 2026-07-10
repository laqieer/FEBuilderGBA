using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text.Json;
using FEBuilderGBA.E2ETests.Helpers;
using Xunit;

namespace FEBuilderGBA.E2ETests.Tests
{
    /// <summary>
    /// E2E tests for the machine-readable <c>--json</c> output added to the two ROM-free
    /// converter verbs (<c>--convertmap1picture</c>, <c>--decreasecolor</c>) for #1941.
    /// Input PNGs are produced by the CLI's own <c>--generate-font</c> — no ROM and no extra
    /// image dependency required.
    /// </summary>
    public class CliConverterJsonE2ETests : IDisposable
    {
        private static readonly string CliExe = AppRunner.FindCliExePath();
        private readonly List<string> _tempFiles = new();

        public void Dispose()
        {
            foreach (var f in _tempFiles)
            {
                try { if (File.Exists(f)) File.Delete(f); } catch { }
            }
        }

        private string TempFile(string ext = ".tmp")
        {
            var path = Path.Combine(Path.GetTempPath(), $"febuilder_json_{Guid.NewGuid():N}{ext}");
            _tempFiles.Add(path);
            return path;
        }

        /// <summary>Generate a small valid PNG (64x16, a multiple of 8) via the CLI's --generate-font.</summary>
        private string GenerateTestPng()
        {
            var png = TempFile(".png");
            var (code, _, stderr) = AppRunner.Run(CliExe,
                $"--generate-font --out=\"{png}\" --text=ABCD", timeoutMs: 30_000);
            Assert.True(code == 0 && File.Exists(png), $"--generate-font setup failed (exit {code}): {stderr}");
            return png;
        }

        private string GenerateHighColorPng()
        {
            var png = TempFile(".png");
            using var bitmap = new Bitmap(8, 8, PixelFormat.Format32bppArgb);
            for (int i = 0; i < 64; i++)
            {
                bitmap.SetPixel(i % 8, i / 8,
                    Color.FromArgb(255, (i & 7) * 32, (i >> 3) * 32, 128));
            }
            bitmap.Save(png, ImageFormat.Png);
            return png;
        }

        private string GenerateTooManyTilesPng()
        {
            const int tilesX = 33;
            const int tilesY = 32;
            var png = TempFile(".png");
            using var bitmap = new Bitmap(tilesX * 8, tilesY * 8, PixelFormat.Format32bppArgb);
            for (int tile = 0; tile < tilesX * tilesY; tile++)
            {
                int tileX = tile % tilesX;
                int tileY = tile / tilesX;
                for (int pixel = 0; pixel < 64; pixel++)
                {
                    int value = pixel < 11 && (tile & (1 << pixel)) != 0 ? 255 : 0;
                    bitmap.SetPixel(tileX * 8 + (pixel % 8), tileY * 8 + (pixel / 8),
                        Color.FromArgb(255, value, value, value));
                }
            }
            bitmap.Save(png, ImageFormat.Png);
            return png;
        }

        [Fact]
        public void DecreaseColor_Json_Success()
        {
            var png = GenerateTestPng();
            var outp = TempFile(".png");
            var (code, stdout, _) = AppRunner.Run(CliExe,
                $"--decreasecolor --in=\"{png}\" --out=\"{outp}\" --paletteno=16 --json", timeoutMs: 30_000);
            Assert.Equal(0, code);
            using var doc = JsonDocument.Parse(stdout);
            var root = doc.RootElement;
            Assert.Equal("decreasecolor", root.GetProperty("command").GetString());
            Assert.True(root.GetProperty("ok").GetBoolean());
            Assert.Equal(16, root.GetProperty("paletteNo").GetInt32());
            Assert.True(root.GetProperty("outBytes").GetInt64() > 0);
            Assert.True(root.GetProperty("colors").GetInt32() > 0);
            Assert.True(File.Exists(outp));
        }

        [Fact]
        public void ConvertMap1Picture_Json_Success_PartialOutput_IsRaw4Bpp()
        {
            var png = GenerateTestPng();
            // The CLI contract is raw 4bpp tile data even when the caller supplies a .png extension.
            var img = TempFile(".png");
            var (code, stdout, _) = AppRunner.Run(CliExe,
                $"--convertmap1picture --in=\"{png}\" --outImg=\"{img}\" --json", timeoutMs: 30_000);
            Assert.Equal(0, code);
            using var doc = JsonDocument.Parse(stdout);
            var root = doc.RootElement;
            Assert.Equal("convertmap1picture", root.GetProperty("command").GetString());
            Assert.True(root.GetProperty("ok").GetBoolean());
            int tiles = root.GetProperty("tiles").GetInt32();
            long outImgBytes = root.GetProperty("outImgBytes").GetInt64();
            Assert.Equal(tiles * 32L, outImgBytes);
            Assert.Equal(JsonValueKind.Null, root.GetProperty("outTSA").ValueKind);
            Assert.Equal(JsonValueKind.Null, root.GetProperty("outTSABytes").ValueKind);
            byte[] tileData = File.ReadAllBytes(img);
            Assert.Equal(outImgBytes, tileData.LongLength);
            Assert.False(tileData.Length >= 8 &&
                tileData[0] == 0x89 && tileData[1] == 0x50 && tileData[2] == 0x4E && tileData[3] == 0x47 &&
                tileData[4] == 0x0D && tileData[5] == 0x0A && tileData[6] == 0x1A && tileData[7] == 0x0A);
        }

        [Fact]
        public void DecreaseColor_Json_Error_GoesToStdout_WithNonZeroExit()
        {
            var png = GenerateTestPng();
            // Missing --out → error. With --json the error object is emitted to STDOUT (one stream).
            var (code, stdout, _) = AppRunner.Run(CliExe,
                $"--decreasecolor --in=\"{png}\" --json", timeoutMs: 15_000);
            Assert.NotEqual(0, code);
            using var doc = JsonDocument.Parse(stdout);
            var root = doc.RootElement;
            Assert.Equal("decreasecolor", root.GetProperty("command").GetString());
            Assert.False(root.GetProperty("ok").GetBoolean());
            Assert.Contains("--out", root.GetProperty("error").GetString());
        }

        [Fact]
        public void ConvertMap1Picture_Json_Success_BothOutputs()
        {
            var png = GenerateTestPng();
            var img = TempFile(".bin");
            var tsa = TempFile(".bin");
            var (code, stdout, _) = AppRunner.Run(CliExe,
                $"--convertmap1picture --in=\"{png}\" --outImg=\"{img}\" --outTSA=\"{tsa}\" --json", timeoutMs: 30_000);
            Assert.Equal(0, code);
            using var doc = JsonDocument.Parse(stdout);
            var root = doc.RootElement;
            Assert.True(root.GetProperty("ok").GetBoolean());
            Assert.Equal(img, root.GetProperty("outImg").GetString());
            Assert.Equal(tsa, root.GetProperty("outTSA").GetString());
            Assert.True(root.GetProperty("outImgBytes").GetInt64() > 0);
            Assert.True(root.GetProperty("outTSABytes").GetInt64() > 0);
            Assert.True(File.Exists(img) && File.Exists(tsa));
        }

        [Fact]
        public void ConvertMap1Picture_Json_AliasedOutputPaths_Error()
        {
            var png = GenerateTestPng();
            var output = TempFile(".bin");
            string directory = Path.GetDirectoryName(output)
                ?? throw new InvalidOperationException("Temporary output path has no directory.");
            string alias = Path.Combine(directory, ".", Path.GetFileName(output));
            var (code, stdout, _) = AppRunner.Run(CliExe,
                $"--convertmap1picture --in=\"{png}\" --outImg=\"{output}\" --outTSA=\"{alias}\" --json",
                timeoutMs: 30_000);
            Assert.NotEqual(0, code);
            using var doc = JsonDocument.Parse(stdout);
            Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
            Assert.Contains("different files", doc.RootElement.GetProperty("error").GetString());
            Assert.False(File.Exists(output));
        }

        [Fact]
        public void DecreaseColor_Json_OutputWriteError_ToStdout()
        {
            var png = GenerateTestPng();
            // Output path in a non-existent directory → the write throws; with --json this
            // must still surface as {ok:false} JSON on stdout with a non-zero exit code.
            var badOut = Path.Combine(Path.GetTempPath(), $"febno_{Guid.NewGuid():N}", "out.png");
            var (code, stdout, _) = AppRunner.Run(CliExe,
                $"--decreasecolor --in=\"{png}\" --out=\"{badOut}\" --paletteno=16 --json", timeoutMs: 30_000);
            Assert.NotEqual(0, code);
            using var doc = JsonDocument.Parse(stdout);
            Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
            Assert.False(string.IsNullOrEmpty(doc.RootElement.GetProperty("error").GetString()));
        }

        [Fact]
        public void DecreaseColor_Json_InvalidPaletteno_Error()
        {
            var png = GenerateTestPng();
            var outp = TempFile(".png");
            var (code, stdout, _) = AppRunner.Run(CliExe,
                $"--decreasecolor --in=\"{png}\" --out=\"{outp}\" --paletteno=abc --json", timeoutMs: 15_000);
            Assert.NotEqual(0, code);
            using var doc = JsonDocument.Parse(stdout);
            Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
            Assert.Contains("paletteno", doc.RootElement.GetProperty("error").GetString(),
                StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void DecreaseColor_Json_EmptyPaletteno_Error()
        {
            var png = GenerateTestPng();
            var outp = TempFile(".png");
            var (code, stdout, _) = AppRunner.Run(CliExe,
                $"--decreasecolor --in=\"{png}\" --out=\"{outp}\" --paletteno= --json", timeoutMs: 15_000);
            Assert.NotEqual(0, code);
            using var doc = JsonDocument.Parse(stdout);
            Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
            Assert.Contains("paletteno", doc.RootElement.GetProperty("error").GetString(),
                StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void DecreaseColor_Json_BarePaletteno_Error()
        {
            var png = GenerateTestPng();
            var outp = TempFile(".png");
            var (code, stdout, _) = AppRunner.Run(CliExe,
                $"--decreasecolor --in=\"{png}\" --out=\"{outp}\" --paletteno --json", timeoutMs: 15_000);
            Assert.NotEqual(0, code);
            using var doc = JsonDocument.Parse(stdout);
            Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
            Assert.Contains("paletteno", doc.RootElement.GetProperty("error").GetString(),
                StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void DecreaseColor_Json_PalettenoOutOfRange_Error()
        {
            var png = GenerateTestPng();
            var outp = TempFile(".png");
            // >256 would overflow the byte[] palette-index model → must be rejected.
            var (code, stdout, _) = AppRunner.Run(CliExe,
                $"--decreasecolor --in=\"{png}\" --out=\"{outp}\" --paletteno=999 --json", timeoutMs: 15_000);
            Assert.NotEqual(0, code);
            using var doc = JsonDocument.Parse(stdout);
            Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        }

        [Fact]
        public void DecreaseColor_Json_PalettenoOne_WithReservedColor_Error()
        {
            var png = GenerateTestPng();
            var outp = TempFile(".png");
            var (code, stdout, _) = AppRunner.Run(CliExe,
                $"--decreasecolor --in=\"{png}\" --out=\"{outp}\" --paletteno=1 --json", timeoutMs: 15_000);
            Assert.NotEqual(0, code);
            using var doc = JsonDocument.Parse(stdout);
            Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
            Assert.Contains("--noReserve1stColor", doc.RootElement.GetProperty("error").GetString());
        }

        [Fact]
        public void DecreaseColor_Json_PalettenoOne_WithoutReservedColor_Succeeds()
        {
            var png = GenerateTestPng();
            var outp = TempFile(".png");
            var (code, stdout, _) = AppRunner.Run(CliExe,
                $"--decreasecolor --in=\"{png}\" --out=\"{outp}\" --paletteno=1 --noReserve1stColor --json",
                timeoutMs: 30_000);
            Assert.Equal(0, code);
            using var doc = JsonDocument.Parse(stdout);
            Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal(1, doc.RootElement.GetProperty("paletteNo").GetInt32());
            Assert.Equal(1, doc.RootElement.GetProperty("colors").GetInt32());
        }

        [Fact]
        public void DecreaseColor_Json_CorruptImage_Error()
        {
            var input = TempFile(".png");
            File.WriteAllText(input, "not an image");
            var outp = TempFile(".png");
            var (code, stdout, _) = AppRunner.Run(CliExe,
                $"--decreasecolor --in=\"{input}\" --out=\"{outp}\" --json", timeoutMs: 15_000);
            Assert.NotEqual(0, code);
            using var doc = JsonDocument.Parse(stdout);
            Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
            Assert.Contains("Failed to load image", doc.RootElement.GetProperty("error").GetString());
        }

        [Fact]
        public void ConvertMap1Picture_Json_CorruptImage_Error()
        {
            var input = TempFile(".png");
            File.WriteAllText(input, "not an image");
            var outp = TempFile(".bin");
            var (code, stdout, _) = AppRunner.Run(CliExe,
                $"--convertmap1picture --in=\"{input}\" --outImg=\"{outp}\" --json", timeoutMs: 15_000);
            Assert.NotEqual(0, code);
            using var doc = JsonDocument.Parse(stdout);
            Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
            Assert.Contains("Failed to load image", doc.RootElement.GetProperty("error").GetString());
        }

        [Fact]
        public void ConvertMap1Picture_Json_MoreThan16Colors_Error()
        {
            var input = GenerateHighColorPng();
            var outp = TempFile(".bin");
            var (code, stdout, _) = AppRunner.Run(CliExe,
                $"--convertmap1picture --in=\"{input}\" --outImg=\"{outp}\" --json", timeoutMs: 30_000);
            Assert.NotEqual(0, code);
            using var doc = JsonDocument.Parse(stdout);
            Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
            Assert.Contains("at most 16", doc.RootElement.GetProperty("error").GetString());
            Assert.False(File.Exists(outp));
        }

        [Fact]
        public void ConvertMap1Picture_Json_MoreThan1024UniqueTiles_Error()
        {
            var input = GenerateTooManyTilesPng();
            var outp = TempFile(".bin");
            var (code, stdout, _) = AppRunner.Run(CliExe,
                $"--convertmap1picture --in=\"{input}\" --outImg=\"{outp}\" --json", timeoutMs: 30_000);
            Assert.NotEqual(0, code);
            using var doc = JsonDocument.Parse(stdout);
            Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
            Assert.Contains("1024 unique tiles", doc.RootElement.GetProperty("error").GetString());
            Assert.False(File.Exists(outp));
        }

        [Fact]
        public void DecreaseColor_Json_OverwriteTruncatesOutput()
        {
            var png = GenerateTestPng();
            var outp = TempFile(".png");
            File.WriteAllBytes(outp, new byte[1_000_000]);

            var (code, stdout, _) = AppRunner.Run(CliExe,
                $"--decreasecolor --in=\"{png}\" --out=\"{outp}\" --paletteno=16 --json", timeoutMs: 30_000);
            Assert.Equal(0, code);
            using var doc = JsonDocument.Parse(stdout);
            long reportedBytes = doc.RootElement.GetProperty("outBytes").GetInt64();
            Assert.Equal(new FileInfo(outp).Length, reportedBytes);
            Assert.InRange(reportedBytes, 1, 999_999);
        }

        [Fact]
        public void DecreaseColor_NoJson_KeepsHumanOutput()
        {
            var png = GenerateTestPng();
            var outp = TempFile(".png");
            // No --json → unchanged human-readable output (backward compatibility).
            var (code, stdout, _) = AppRunner.Run(CliExe,
                $"--decreasecolor --in=\"{png}\" --out=\"{outp}\" --paletteno=16", timeoutMs: 30_000);
            Assert.Equal(0, code);
            Assert.Contains("Color reduction complete", stdout);
            Assert.DoesNotContain("\"command\"", stdout);
        }
    }
}
