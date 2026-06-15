using System;
using System.IO;
using System.Text;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Tests for the decomp asset import validator + indexed-PNG reader (#1150).
    ///
    /// The validator NEVER reads CoreState.ROM (it takes a file path only) — proven by a
    /// source assertion plus the fact that every test runs without a loaded ROM.
    /// Pure file I/O over temp files; no CoreState mutation, so no SharedState needed.
    /// </summary>
    public class DecompAssetValidatorCoreTests
    {
        // ---------------------------------------------------------------- helpers

        static byte[] BuildPalette(int colors)
        {
            // BGR555 LE, 2 bytes/color. Just zeros (black) — color content is irrelevant.
            return new byte[colors * 2];
        }

        static string WriteTemp(byte[] bytes, string ext)
        {
            string p = Path.Combine(Path.GetTempPath(), "asset_" + Guid.NewGuid().ToString("N") + ext);
            File.WriteAllBytes(p, bytes);
            return p;
        }

        static string WriteTempText(string text, string ext)
        {
            string p = Path.Combine(Path.GetTempPath(), "asset_" + Guid.NewGuid().ToString("N") + ext);
            File.WriteAllText(p, text);
            return p;
        }

        // ---------------------------------------------------------------- IndexedPngReader

        [Fact]
        public void IndexedPngReader_RoundTrips_WriterOutput()
        {
            int w = 16, h = 16;
            var indices = new byte[w * h];
            for (int i = 0; i < indices.Length; i++) indices[i] = (byte)(i % 16);
            byte[] png = IndexedPngWriter.Write(indices, w, h, BuildPalette(16), 16, transparentIndex: 0);
            Assert.NotNull(png);

            IndexedPngInfo info = IndexedPngReader.Read(png);
            Assert.True(info.Ok, info.Error);
            Assert.Equal(w, info.Width);
            Assert.Equal(h, info.Height);
            Assert.Equal(3, info.ColorType);
            Assert.Equal(16, info.PaletteColorCount);
            Assert.True(info.IndicesAvailable);
            Assert.Equal(indices, info.Indices);
            Assert.True(info.HasTrns);
        }

        [Fact]
        public void IndexedPngReader_Garbage_OkFalse_NoThrow()
        {
            IndexedPngInfo info = IndexedPngReader.Read(Encoding.ASCII.GetBytes("not a png"));
            Assert.False(info.Ok);
            Assert.False(string.IsNullOrEmpty(info.Error));
        }

        [Fact]
        public void IndexedPngReader_Null_OkFalse_NoThrow()
        {
            IndexedPngInfo info = IndexedPngReader.Read(null);
            Assert.False(info.Ok);
        }

        // ---------------------------------------------------------------- Graphics PNG

        [Fact]
        public void Validate_GoodIndexedGraphics_Ok()
        {
            var indices = new byte[16 * 16];
            for (int i = 0; i < indices.Length; i++) indices[i] = (byte)(i % 16);
            byte[] png = IndexedPngWriter.Write(indices, 16, 16, BuildPalette(16), 16, transparentIndex: 0);
            string path = WriteTemp(png, ".png");
            try
            {
                AssetValidationResult r = DecompAssetValidatorCore.ValidateAsset(AssetKind.Graphics, path);
                Assert.True(r.Ok, string.Join("; ", r.Errors.ConvertAll(e => e.Code)));
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void Validate_NonIndexedBlob_NotIndexedOrStructuralError()
        {
            string path = WriteTemp(Encoding.ASCII.GetBytes("not a png at all"), ".png");
            try
            {
                AssetValidationResult r = DecompAssetValidatorCore.ValidateAsset(AssetKind.Graphics, path);
                Assert.False(r.Ok);
                // It's a structural BAD_PNG (bad signature) — not NON_INDEXED (which needs a parseable header).
                Assert.Contains(r.Errors, e => e.Code == "BAD_PNG" || e.Code == "NON_INDEXED");
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void Validate_OddDimensionPng_NotTileAligned()
        {
            // 15x16 indexed PNG: width 15 is not a multiple of 8.
            int w = 15, h = 16;
            var indices = new byte[w * h];
            byte[] png = IndexedPngWriter.Write(indices, w, h, BuildPalette(4), 4, transparentIndex: 0);
            Assert.NotNull(png);
            string path = WriteTemp(png, ".png");
            try
            {
                AssetValidationResult r = DecompAssetValidatorCore.ValidateAsset(AssetKind.Graphics, path);
                Assert.False(r.Ok);
                Assert.Contains(r.Errors, e => e.Code == "NOT_TILE_ALIGNED");
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void Validate_Portrait_NonStandardDims_WarnsButOk()
        {
            // A tile-aligned 16x16 indexed PNG is structurally fine but not the 96x80 mug.
            var indices = new byte[16 * 16];
            byte[] png = IndexedPngWriter.Write(indices, 16, 16, BuildPalette(16), 16, transparentIndex: 0);
            string path = WriteTemp(png, ".png");
            try
            {
                AssetValidationResult r = DecompAssetValidatorCore.ValidateAsset(AssetKind.Portrait, path);
                Assert.True(r.Ok); // a dims mismatch is a warning, not an error
                Assert.Contains(r.Warnings, w2 => w2.Code == "PORTRAIT_DIMS");
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void Validate_Icon_StandardDims_NoIconDimsWarning()
        {
            var indices = new byte[16 * 16];
            byte[] png = IndexedPngWriter.Write(indices, 16, 16, BuildPalette(16), 16, transparentIndex: 0);
            string path = WriteTemp(png, ".png");
            try
            {
                AssetValidationResult r = DecompAssetValidatorCore.ValidateAsset(AssetKind.Icon, path);
                Assert.True(r.Ok);
                Assert.DoesNotContain(r.Warnings, w2 => w2.Code == "ICON_DIMS");
            }
            finally { File.Delete(path); }
        }

        // ---------------------------------------------------------------- Palette (.pal)

        [Fact]
        public void Validate_GoodJascPalette_Ok()
        {
            string pal = "JASC-PAL\r\n0100\r\n2\r\n0 0 0\r\n255 255 255\r\n";
            string path = WriteTempText(pal, ".pal");
            try
            {
                AssetValidationResult r = DecompAssetValidatorCore.ValidateAsset(AssetKind.Palette, path);
                Assert.True(r.Ok, string.Join("; ", r.Errors.ConvertAll(e => e.Code)));
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void Validate_BadPaletteHeader_Errors()
        {
            string path = WriteTempText("NOTJASC\r\n0100\r\n1\r\n0 0 0\r\n", ".pal");
            try
            {
                AssetValidationResult r = DecompAssetValidatorCore.ValidateAsset(AssetKind.Palette, path);
                Assert.False(r.Ok);
                Assert.Contains(r.Errors, e => e.Code == "BAD_PALETTE_HEADER");
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void Validate_BadPaletteCount_Errors()
        {
            string path = WriteTempText("JASC-PAL\r\n0100\r\n999\r\n0 0 0\r\n", ".pal");
            try
            {
                AssetValidationResult r = DecompAssetValidatorCore.ValidateAsset(AssetKind.Palette, path);
                Assert.False(r.Ok);
                Assert.Contains(r.Errors, e => e.Code == "BAD_PALETTE_COUNT");
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void Validate_BadPaletteColor_Errors()
        {
            string path = WriteTempText("JASC-PAL\r\n0100\r\n2\r\n0 0 0\r\n300 0 0\r\n", ".pal");
            try
            {
                AssetValidationResult r = DecompAssetValidatorCore.ValidateAsset(AssetKind.Palette, path);
                Assert.False(r.Ok);
                Assert.Contains(r.Errors, e => e.Code == "BAD_PALETTE_COLOR");
            }
            finally { File.Delete(path); }
        }

        // ---------------------------------------------------------------- MapLayout (.mar)

        static byte[] BuildMarBody(int w, int h, bool corruptShift)
        {
            var body = new byte[w * h * 2];
            for (int i = 0; i < w * h; i++)
            {
                // raw tile << 3 keeps low 3 bits zero (the .mar invariant).
                ushort marTile = (ushort)((i & 0x1FFF) << 3);
                if (corruptShift && i == 0) marTile |= 0x1; // break low bit on the first entry
                body[i * 2 + 0] = (byte)(marTile & 0xFF);
                body[i * 2 + 1] = (byte)(marTile >> 8);
            }
            return body;
        }

        static string WriteMarWithSidecar(byte[] body, int w, int h)
        {
            string p = Path.Combine(Path.GetTempPath(), "asset_" + Guid.NewGuid().ToString("N") + ".mar");
            File.WriteAllBytes(p, body);
            File.WriteAllText(p + ".json",
                $"{{\"width\":{w},\"height\":{h},\"srcAddr\":\"0x0\",\"format\":\"febuilder-mar-u16-shl3\"}}");
            return p;
        }

        [Fact]
        public void Validate_GoodMarWithSidecar_Ok()
        {
            int w = 4, h = 3;
            string path = WriteMarWithSidecar(BuildMarBody(w, h, false), w, h);
            try
            {
                AssetValidationResult r = DecompAssetValidatorCore.ValidateAsset(AssetKind.MapLayout, path);
                Assert.True(r.Ok, string.Join("; ", r.Errors.ConvertAll(e => e.Code)));
            }
            finally { File.Delete(path); File.Delete(path + ".json"); }
        }

        [Fact]
        public void Validate_MarCorruptShift_BadMarShift()
        {
            int w = 4, h = 3;
            string path = WriteMarWithSidecar(BuildMarBody(w, h, true), w, h);
            try
            {
                AssetValidationResult r = DecompAssetValidatorCore.ValidateAsset(AssetKind.MapLayout, path);
                Assert.False(r.Ok);
                Assert.Contains(r.Errors, e => e.Code == "BAD_MAR_SHIFT");
            }
            finally { File.Delete(path); File.Delete(path + ".json"); }
        }

        [Fact]
        public void Validate_MarWrongLength_BadMarLength()
        {
            int w = 4, h = 3;
            // sidecar claims 4x3 but the body is 2x2 worth of data.
            byte[] body = BuildMarBody(2, 2, false);
            string p = Path.Combine(Path.GetTempPath(), "asset_" + Guid.NewGuid().ToString("N") + ".mar");
            File.WriteAllBytes(p, body);
            File.WriteAllText(p + ".json", $"{{\"width\":{w},\"height\":{h}}}");
            try
            {
                AssetValidationResult r = DecompAssetValidatorCore.ValidateAsset(AssetKind.MapLayout, p);
                Assert.False(r.Ok);
                Assert.Contains(r.Errors, e => e.Code == "BAD_MAR_LENGTH");
            }
            finally { File.Delete(p); File.Delete(p + ".json"); }
        }

        [Fact]
        public void Validate_MarNoSidecar_WarnsNotThrows()
        {
            byte[] body = BuildMarBody(4, 3, false);
            string p = Path.Combine(Path.GetTempPath(), "asset_" + Guid.NewGuid().ToString("N") + ".mar");
            File.WriteAllBytes(p, body);
            try
            {
                AssetValidationResult r = DecompAssetValidatorCore.ValidateAsset(AssetKind.MapLayout, p);
                Assert.Contains(r.Warnings, w => w.Code == "MAR_NO_SIDECAR");
                Assert.True(r.Ok); // good shift + even length => no errors
            }
            finally { File.Delete(p); }
        }

        // ---------------------------------------------------------------- generic

        [Fact]
        public void Validate_MissingFile_FileNotFound()
        {
            AssetValidationResult r = DecompAssetValidatorCore.ValidateAsset(
                AssetKind.Graphics, Path.Combine(Path.GetTempPath(), "does_not_exist_" + Guid.NewGuid().ToString("N") + ".png"));
            Assert.False(r.Ok);
            Assert.Contains(r.Errors, e => e.Code == "FILE_NOT_FOUND");
        }

        [Theory]
        [InlineData("graphics", AssetKind.Graphics)]
        [InlineData("palette", AssetKind.Palette)]
        [InlineData("portrait", AssetKind.Portrait)]
        [InlineData("icon", AssetKind.Icon)]
        [InlineData("map", AssetKind.MapLayout)]
        [InlineData("MapLayout", AssetKind.MapLayout)]
        public void ParseKind_Maps(string s, AssetKind expected)
        {
            Assert.Equal(expected, DecompAssetValidatorCore.ParseKind(s));
        }

        [Fact]
        public void ParseKind_Unknown_Null()
        {
            Assert.Null(DecompAssetValidatorCore.ParseKind("widget"));
            Assert.Null(DecompAssetValidatorCore.ParseKind(null));
        }

        [SkippableFact]
        public void Validator_DoesNotReference_CoreStateRom_SourceAssertion()
        {
            // Proof the validator path never touches the ROM: its source text contains no
            // reference to CoreState.ROM. (The validator takes a path, not a ROM.)
            string root = FindRepoRoot();
            Skip.If(root == null, "repo root not found");
            string src = Path.Combine(root, "FEBuilderGBA.Core", "DecompAssetValidatorCore.cs");
            Skip.If(!File.Exists(src), "validator source not found");

            // Strip comment lines so the XML doc (which deliberately MENTIONS CoreState.ROM
            // to explain that it is never read) does not trip the assertion — only real CODE
            // references to the ROM should fail this guard.
            foreach (string line in File.ReadAllLines(src))
            {
                string trimmed = line.TrimStart();
                if (trimmed.StartsWith("//") || trimmed.StartsWith("///") || trimmed.StartsWith("*"))
                    continue;
                Assert.DoesNotContain("CoreState.ROM", line);
            }
        }

        static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "FEBuilderGBA.sln")))
                    return dir.FullName;
                dir = dir.Parent;
            }
            return null;
        }
    }
}
