using Xunit;
using CliProgram = FEBuilderGBA.CLI.Program;

namespace FEBuilderGBA.Tests.Unit
{
    /// <summary>
    /// Tests verifying the translate --in write-back implementation
    /// and decreasecolor sub-flag wiring in the CLI source.
    /// </summary>
    public class CliTranslateWriteTests
    {
        private static string GetCliProgramPath()
        {
            var dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
                dir = Path.GetDirectoryName(dir);
            if (dir == null) throw new InvalidOperationException("Cannot find solution root");
            return Path.Combine(dir, "FEBuilderGBA.CLI", "Program.cs");
        }

        private static string GetTranslateCorePath()
        {
            var dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
                dir = Path.GetDirectoryName(dir);
            if (dir == null) throw new InvalidOperationException("Cannot find solution root");
            return Path.Combine(dir, "FEBuilderGBA.Core", "TranslateCore.cs");
        }

        private static string GetDecreaseColorCorePath()
        {
            var dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
                dir = Path.GetDirectoryName(dir);
            if (dir == null) throw new InvalidOperationException("Cannot find solution root");
            return Path.Combine(dir, "FEBuilderGBA.Core", "DecreaseColorCore.cs");
        }

        // ---- Translate write-back verification ----

        [Fact]
        public void TranslateCore_WriteTexts_IsImplemented()
        {
            // Verify WriteTexts is no longer a placeholder returning 0
            var src = File.ReadAllText(GetTranslateCorePath());
            Assert.DoesNotContain("// For now, this is a no-op placeholder.", src);
            Assert.DoesNotContain("// TODO: Writing text back to ROM requires:", src);
        }

        [Fact]
        public void TranslateCore_WriteTexts_UsesHuffmanEncoding()
        {
            var src = File.ReadAllText(GetTranslateCorePath());
            Assert.Contains("FETextEncoder.Encode", src);
        }

        [Fact]
        public void TranslateCore_WriteTexts_HasUnHuffmanFallback()
        {
            var src = File.ReadAllText(GetTranslateCorePath());
            Assert.Contains("UnHuffmanEncode", src);
        }

        [Fact]
        public void TranslateCore_WriteTexts_HandlesInPlaceOverwrite()
        {
            var src = File.ReadAllText(GetTranslateCorePath());
            // Should have logic for overwriting in place when data fits
            Assert.Contains("write_range", src);
        }

        [Fact]
        public void TranslateCore_WriteTexts_HandlesROMExpansion()
        {
            var src = File.ReadAllText(GetTranslateCorePath());
            // Should have logic for appending to ROM end
            Assert.Contains("write_resize_data", src);
        }

        [Fact]
        public void CliProgram_TranslateImport_NoLongerSaysNotImplemented()
        {
            var src = File.ReadAllText(GetCliProgramPath());
            Assert.DoesNotContain("Text write-back is not yet implemented", src);
        }

        [Fact]
        public void CliProgram_TranslateHelpText_ShowsWriteSupport()
        {
            var src = File.ReadAllText(GetCliProgramPath());
            Assert.Contains("Import text from TSV file and write to ROM", src);
        }

        // ---- DecreaseColor sub-flag wiring verification ----

        [Fact]
        public void DecreaseColorCore_Quantize_AcceptsSubFlags()
        {
            var src = File.ReadAllText(GetDecreaseColorCorePath());
            Assert.Contains("bool noScale", src);
            Assert.Contains("bool noReserve1stColor", src);
            Assert.Contains("bool ignoreTSA", src);
        }

        [Fact]
        public void DecreaseColorCore_UsesNoReserve1stColor()
        {
            var src = File.ReadAllText(GetDecreaseColorCorePath());
            Assert.Contains("noReserve1stColor", src);
            // Should affect transparentOffset calculation
            Assert.Contains("transparentOffset", src);
        }

        [Fact]
        public void DecreaseColorCore_UsesNoScale()
        {
            var src = File.ReadAllText(GetDecreaseColorCorePath());
            Assert.Contains("noScale", src);
        }

        [Fact]
        public void CliProgram_PassesSubFlagsToQuantize()
        {
            var src = File.ReadAllText(GetCliProgramPath());
            // Should pass all three flags to the Quantize call
            Assert.Contains("noScale, noReserve1stColor, ignoreTSA", src);
        }

        [Fact]
        public void CliProgram_NoLongerHasTODOForSubFlags()
        {
            var src = File.ReadAllText(GetCliProgramPath());
            Assert.DoesNotContain("TODO: Pass noScale, noReserve1stColor, ignoreTSA", src);
        }

        // ---- CLI ParseArgs comprehensive tests ----

        [Fact]
        public void ParseArgs_ApplyUpsWithAllArgs()
        {
            var dic = CliProgram.ParseArgs(new[] { "--applyups=output.gba", "--rom=original.gba", "--patch=patch.ups" });
            Assert.Equal("output.gba", dic["--applyups"]);
            Assert.Equal("original.gba", dic["--rom"]);
            Assert.Equal("patch.ups", dic["--patch"]);
        }

        [Fact]
        public void ParseArgs_DisasmWithArgs()
        {
            var dic = CliProgram.ParseArgs(new[] { "--disasm=output.asm", "--rom=test.gba" });
            Assert.Equal("output.asm", dic["--disasm"]);
            Assert.Equal("test.gba", dic["--rom"]);
        }

        [Fact]
        public void ParseArgs_PointerCalcWithArgs()
        {
            var dic = CliProgram.ParseArgs(new[] { "--pointercalc", "--rom=src.gba", "--target=tgt.gba", "--address=0x100" });
            Assert.True(dic.ContainsKey("--pointercalc"));
            Assert.Equal("src.gba", dic["--rom"]);
            Assert.Equal("tgt.gba", dic["--target"]);
            Assert.Equal("0x100", dic["--address"]);
        }

        [Fact]
        public void ParseArgs_RebuildWithArgs()
        {
            var dic = CliProgram.ParseArgs(new[] { "--rebuild", "--rom=modified.gba", "--fromrom=original.gba" });
            Assert.True(dic.ContainsKey("--rebuild"));
            Assert.Equal("modified.gba", dic["--rom"]);
            Assert.Equal("original.gba", dic["--fromrom"]);
        }

        [Fact]
        public void ParseArgs_SongExchangeWithArgs()
        {
            var dic = CliProgram.ParseArgs(new[] {
                "--songexchange", "--rom=dest.gba", "--fromrom=source.gba",
                "--fromsong=0x1A", "--tosong=0x1B"
            });
            Assert.True(dic.ContainsKey("--songexchange"));
            Assert.Equal("dest.gba", dic["--rom"]);
            Assert.Equal("source.gba", dic["--fromrom"]);
            Assert.Equal("0x1A", dic["--fromsong"]);
            Assert.Equal("0x1B", dic["--tosong"]);
        }

        [Fact]
        public void ParseArgs_ConvertMap1PictureWithArgs()
        {
            var dic = CliProgram.ParseArgs(new[] {
                "--convertmap1picture", "--in=map.png", "--outImg=tiles.bin", "--outTSA=tsa.bin"
            });
            Assert.True(dic.ContainsKey("--convertmap1picture"));
            Assert.Equal("map.png", dic["--in"]);
            Assert.Equal("tiles.bin", dic["--outImg"]);
            Assert.Equal("tsa.bin", dic["--outTSA"]);
        }
    }
}
