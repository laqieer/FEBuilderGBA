using System;
using System.Collections.Generic;
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
        public void ConvertMap1Picture_Json_Success_PartialOutput_TsaNull()
        {
            var png = GenerateTestPng();
            var img = TempFile(".bin");
            // Only --outImg requested → the deterministic contract reports outTSA/outTSABytes as null.
            var (code, stdout, _) = AppRunner.Run(CliExe,
                $"--convertmap1picture --in=\"{png}\" --outImg=\"{img}\" --json", timeoutMs: 30_000);
            Assert.Equal(0, code);
            using var doc = JsonDocument.Parse(stdout);
            var root = doc.RootElement;
            Assert.Equal("convertmap1picture", root.GetProperty("command").GetString());
            Assert.True(root.GetProperty("ok").GetBoolean());
            Assert.True(root.GetProperty("outImgBytes").GetInt64() > 0);
            Assert.Equal(JsonValueKind.Null, root.GetProperty("outTSA").ValueKind);
            Assert.Equal(JsonValueKind.Null, root.GetProperty("outTSABytes").ValueKind);
            Assert.True(root.GetProperty("tiles").GetInt32() >= 1);
            Assert.True(File.Exists(img));
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
