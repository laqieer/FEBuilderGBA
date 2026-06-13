using System;
using System.IO;
using System.Text;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Unit tests for <see cref="DecompAssetExportCore"/>.
    ///
    /// Tests verify:
    /// - Path resolution (containment guards, null handling)
    /// - ExportPalette writes JASC .pal with correct color order
    /// - ExportMap writes .mar with (raw&lt;&lt;3) entries and sidecar JSON
    /// - FormatTexts produces correct texts.txt / textdefs.txt layouts
    /// - ROM data is never mutated by any export
    /// - Null/out-of-bounds → typed failure, no throw, no file written
    /// </summary>
    [Collection("SharedState")]
    public class DecompAssetExportCoreTests : IDisposable
    {
        readonly IImageService _savedImageService;
        readonly ROM _savedRom;

        public DecompAssetExportCoreTests()
        {
            _savedImageService = CoreState.ImageService;
            _savedRom = CoreState.ROM;
            // Wire a StubImageService so ExportGraphics can decode
            CoreState.ImageService = new StubImageServiceForDecomp();
        }

        public void Dispose()
        {
            CoreState.ImageService = _savedImageService;
            CoreState.ROM = _savedRom;
        }

        // ---- Temp dir helper ----

        static string NewTempDir()
        {
            string d = Path.Combine(Path.GetTempPath(), "decomp_asset_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(d);
            return d;
        }

        static byte[] MakeSyntheticPalette16()
        {
            byte[] pal = new byte[32];
            for (int i = 0; i < 16; i++)
            {
                ushort gba = (ushort)(i & 0x1F);
                pal[i * 2] = (byte)(gba & 0xFF);
                pal[i * 2 + 1] = (byte)(gba >> 8);
            }
            return pal;
        }

        // ---- ResolveSourcePath ----

        [Fact]
        public void ResolveSourcePath_ProjectRelative_Ok_ResolvesUnderRoot()
        {
            string dir = NewTempDir();
            try
            {
                var proj = new DecompProject { ProjectRoot = dir };
                string result = DecompAssetExportCore.ResolveSourcePath(proj, "gfx/palette.pal");
                Assert.NotNull(result);
                Assert.StartsWith(dir, result, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("palette.pal", result);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void ResolveSourcePath_ProjectRelative_DotDotEscape_ReturnsNull()
        {
            string dir = NewTempDir();
            try
            {
                var proj = new DecompProject { ProjectRoot = dir };
                string result = DecompAssetExportCore.ResolveSourcePath(proj, "../outside.pal");
                Assert.Null(result);
                // No file created outside the project root
                string outside = Path.Combine(Path.GetDirectoryName(dir)!, "outside.pal");
                Assert.False(File.Exists(outside));
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void ResolveSourcePath_ProjectRelative_AbsolutePath_ReturnsNull()
        {
            string dir = NewTempDir();
            try
            {
                var proj = new DecompProject { ProjectRoot = dir };
                // Absolute path should be rejected when project is set
                string absPath = Path.Combine(Path.GetTempPath(), "absolute.pal");
                string result = DecompAssetExportCore.ResolveSourcePath(proj, absPath);
                Assert.Null(result);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void ResolveSourcePath_NoProject_AbsolutePath_Accepted()
        {
            string absPath = Path.Combine(Path.GetTempPath(), "noproject_" + Guid.NewGuid().ToString("N") + ".pal");
            string result = DecompAssetExportCore.ResolveSourcePath(null, absPath);
            Assert.NotNull(result);
            Assert.Equal(Path.GetFullPath(absPath), result, StringComparer.OrdinalIgnoreCase);
        }

        [Fact]
        public void ResolveSourcePath_NullRelPath_ReturnsNull()
        {
            string result = DecompAssetExportCore.ResolveSourcePath(null, null);
            Assert.Null(result);
        }

        // ---- ExportPalette ----

        [Fact]
        public void ExportPalette_WritesJascPal_WithColorsInIndexOrder()
        {
            string dir = NewTempDir();
            try
            {
                // Build a ROM with synthetic palette at offset 0x100
                byte[] romData = new byte[0x200];
                byte[] pal = MakeSyntheticPalette16();
                Array.Copy(pal, 0, romData, 0x100, 32);

                var rom = new ROM();
                rom.SwapNewROMDataDirect(romData);

                string outPath = Path.Combine(dir, "palette.pal");
                var result = DecompAssetExportCore.ExportPalette(rom, 0x100, 16, outPath);

                Assert.True(result.Ok, $"Export failed: {result.Message}");
                Assert.True(File.Exists(outPath));
                Assert.Contains(outPath, result.WrittenPaths);

                string content = File.ReadAllText(outPath);
                Assert.StartsWith("JASC-PAL", content);

                // First color: GBA=0 → R=0, G=0, B=0
                Assert.Contains("0 0 0", content);
                // Color 1: GBA=1 → R=(1<<3)=8, G=0, B=0
                Assert.Contains("8 0 0", content);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void ExportPalette_NullRom_ReturnsBadArgs()
        {
            var result = DecompAssetExportCore.ExportPalette(null, 0, 16, "/tmp/x.pal");
            Assert.False(result.Ok);
            Assert.Equal(DecompAssetStatus.BadArgs, result.Status);
        }

        [Fact]
        public void ExportPalette_OutOfBoundsAddr_ReturnsNotData()
        {
            var rom = new ROM();
            rom.SwapNewROMDataDirect(new byte[64]);
            // 16 colors = 32 bytes; offset 50 + 32 > 64
            var result = DecompAssetExportCore.ExportPalette(rom, 50, 16, "/tmp/x.pal");
            Assert.False(result.Ok);
            Assert.Equal(DecompAssetStatus.NotData, result.Status);
        }

        [Fact]
        public void ExportPalette_DoesNotMutateRomData()
        {
            string dir = NewTempDir();
            try
            {
                byte[] romData = new byte[0x200];
                byte[] pal = MakeSyntheticPalette16();
                Array.Copy(pal, 0, romData, 0x100, 32);

                var rom = new ROM();
                rom.SwapNewROMDataDirect(romData);
                byte[] before = (byte[])rom.Data.Clone();

                DecompAssetExportCore.ExportPalette(rom, 0x100, 16, Path.Combine(dir, "x.pal"));

                Assert.Equal(before, rom.Data);
            }
            finally { Directory.Delete(dir, true); }
        }

        // ---- ExportMap ----

        [Fact]
        public void ExportMap_WritesMar_WithTilesShiftedLeft3()
        {
            string dir = NewTempDir();
            try
            {
                // Build synthetic tilemap: 4x4
                int w = 4, h = 4;
                byte[] rawMapBlob = new byte[2 + w * h * 2];
                rawMapBlob[0] = (byte)w;
                rawMapBlob[1] = (byte)h;
                for (int i = 0; i < w * h; i++)
                {
                    ushort tile = (ushort)(i + 1); // non-zero tile ids
                    rawMapBlob[2 + i * 2] = (byte)(tile & 0xFF);
                    rawMapBlob[2 + i * 2 + 1] = (byte)(tile >> 8);
                }

                // LZ77-compress the blob
                byte[] compressed = LZ77.compress(rawMapBlob);
                Assert.NotNull(compressed);

                // Build ROM with compressed map at offset 0x100
                byte[] romData = new byte[0x100 + compressed.Length + 16];
                Array.Copy(compressed, 0, romData, 0x100, compressed.Length);

                var rom = new ROM();
                rom.SwapNewROMDataDirect(romData);

                string marPath = Path.Combine(dir, "chapter.mar");
                var result = DecompAssetExportCore.ExportMap(rom, 0x100, marPath);

                Assert.True(result.Ok, $"ExportMap failed: {result.Message}");
                Assert.True(File.Exists(marPath));

                // Verify .mar contents: each u16 = (rawTile << 3) little-endian
                byte[] marBytes = File.ReadAllBytes(marPath);
                Assert.Equal(w * h * 2, marBytes.Length);
                for (int i = 0; i < w * h; i++)
                {
                    ushort rawTile = (ushort)(i + 1);
                    ushort expected = (ushort)(rawTile << 3);
                    ushort actual = (ushort)(marBytes[i * 2] | (marBytes[i * 2 + 1] << 8));
                    Assert.Equal(expected, actual);
                }

                // Verify sidecar JSON
                string jsonPath = marPath + ".json";
                Assert.True(File.Exists(jsonPath));
                string json = File.ReadAllText(jsonPath);
                Assert.Contains($"\"width\": {w}", json);
                Assert.Contains($"\"height\": {h}", json);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void ExportMap_NullRom_ReturnsBadArgs()
        {
            var result = DecompAssetExportCore.ExportMap(null, 0, "/tmp/x.mar");
            Assert.False(result.Ok);
            Assert.Equal(DecompAssetStatus.BadArgs, result.Status);
        }

        [Fact]
        public void ExportMap_BadData_ReturnsNotData()
        {
            var rom = new ROM();
            rom.SwapNewROMDataDirect(new byte[0x200]);
            // Empty ROM, no LZ77 data
            var result = DecompAssetExportCore.ExportMap(rom, 0x100, "/tmp/x.mar");
            Assert.False(result.Ok);
            Assert.NotEqual(DecompAssetStatus.Ok, result.Status);
        }

        [Fact]
        public void ExportMap_DoesNotMutateRomData()
        {
            string dir = NewTempDir();
            try
            {
                int w = 2, h = 2;
                byte[] rawMapBlob = new byte[2 + w * h * 2];
                rawMapBlob[0] = (byte)w; rawMapBlob[1] = (byte)h;
                byte[] compressed = LZ77.compress(rawMapBlob);
                byte[] romData = new byte[0x100 + compressed.Length + 16];
                Array.Copy(compressed, 0, romData, 0x100, compressed.Length);

                var rom = new ROM();
                rom.SwapNewROMDataDirect(romData);
                byte[] before = (byte[])rom.Data.Clone();

                DecompAssetExportCore.ExportMap(rom, 0x100, Path.Combine(dir, "x.mar"));

                Assert.Equal(before, rom.Data);
            }
            finally { Directory.Delete(dir, true); }
        }

        // ---- FormatTexts (internal, testable without ROM) ----

        [Fact]
        public void FormatTexts_EmptyList_ReturnsEmptyStrings()
        {
            var (texts, defs) = DecompAssetExportCore.FormatTexts(new System.Collections.Generic.List<(uint, string)>());
            Assert.NotNull(texts);
            Assert.NotNull(defs);
        }

        [Fact]
        public void FormatTexts_SingleEntry_FormatsCorrectly()
        {
            var entries = new System.Collections.Generic.List<(uint, string)> { (0x10, "Hello world") };
            var (texts, defs) = DecompAssetExportCore.FormatTexts(entries);

            Assert.Contains("# msg 0x0010", texts);
            Assert.Contains("Hello world", texts);
            Assert.Contains("#define MSG_0x0010 16", defs);
        }

        [Fact]
        public void FormatTexts_MultipleEntries_AllPresent()
        {
            var entries = new System.Collections.Generic.List<(uint, string)>
            {
                (0, "Empty text"),
                (1, "Text one"),
                (255, "Last text"),
            };
            var (texts, defs) = DecompAssetExportCore.FormatTexts(entries);

            Assert.Contains("# msg 0x0000", texts);
            Assert.Contains("# msg 0x0001", texts);
            Assert.Contains("# msg 0x00FF", texts);
            Assert.Contains("#define MSG_0x0000 0", defs);
            Assert.Contains("#define MSG_0x0001 1", defs);
            Assert.Contains("#define MSG_0x00FF 255", defs);
        }

        // ---- ExportText ----

        [Fact]
        public void ExportText_NullRom_ReturnsBadArgs()
        {
            var result = DecompAssetExportCore.ExportText(null, "/tmp/textdir");
            Assert.False(result.Ok);
            Assert.Equal(DecompAssetStatus.BadArgs, result.Status);
        }

        // ---- ExportGraphics (null IImageService) ----

        [Fact]
        public void ExportGraphics_NullImageService_ReturnsFaulted()
        {
            var savedSvc = CoreState.ImageService;
            try
            {
                CoreState.ImageService = null;
                var rom = new ROM();
                rom.SwapNewROMDataDirect(new byte[0x200]);
                var result = DecompAssetExportCore.ExportGraphics(rom, 0, 8, 8, 4, false, 0, 16, "/tmp/x.png");
                Assert.False(result.Ok);
                Assert.Equal(DecompAssetStatus.Faulted, result.Status);
            }
            finally
            {
                CoreState.ImageService = savedSvc;
            }
        }

        [Fact]
        public void ExportGraphics_NullRom_ReturnsBadArgs()
        {
            var result = DecompAssetExportCore.ExportGraphics(null, 0, 8, 8, 4, false, 0, 16, "/tmp/x.png");
            Assert.False(result.Ok);
            Assert.Equal(DecompAssetStatus.BadArgs, result.Status);
        }

        // ---- FINDING 1: non-multiple-of-8 dimensions rejected ----

        [Fact]
        public void ExportGraphics_NonMultipleOf8Width_ReturnsBadArgs_NoFile()
        {
            string dir = NewTempDir();
            try
            {
                var rom = new ROM();
                rom.SwapNewROMDataDirect(new byte[0x400]);
                string outPng = Path.Combine(dir, "bad_w.png");

                // width=20 is not a multiple of 8 → must be rejected with BadArgs
                var result = DecompAssetExportCore.ExportGraphics(rom, 0, 20, 8, 4, false, 0x100, 16, outPng);

                Assert.False(result.Ok);
                Assert.Equal(DecompAssetStatus.BadArgs, result.Status);
                Assert.False(File.Exists(outPng), "No file should be written when dims are rejected");
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void ExportGraphics_NonMultipleOf8Height_ReturnsBadArgs_NoFile()
        {
            string dir = NewTempDir();
            try
            {
                var rom = new ROM();
                rom.SwapNewROMDataDirect(new byte[0x400]);
                string outPng = Path.Combine(dir, "bad_h.png");

                // height=12 is not a multiple of 8 → must be rejected with BadArgs
                var result = DecompAssetExportCore.ExportGraphics(rom, 0, 8, 12, 4, false, 0x100, 16, outPng);

                Assert.False(result.Ok);
                Assert.Equal(DecompAssetStatus.BadArgs, result.Status);
                Assert.False(File.Exists(outPng), "No file should be written when dims are rejected");
            }
            finally { Directory.Delete(dir, true); }
        }

        // ---- FINDING 2: compressed tile data shorter than required rejected ----

        [Fact]
        public void ExportGraphics_CompressedTooShort_ReturnsNotData_NoFile()
        {
            string dir = NewTempDir();
            try
            {
                // Build a tiny LZ77 blob that decompresses to FEWER bytes than
                // a 16x16 4bpp image requires (16*16*4/8 = 128 bytes).
                // Decompressing a 16-byte payload yields 16 bytes << 128.
                byte[] tooShort = new byte[16];
                for (int i = 0; i < tooShort.Length; i++) tooShort[i] = (byte)i;
                byte[] compressed = LZ77.compress(tooShort);
                Assert.NotNull(compressed);

                byte[] romData = new byte[0x100 + compressed.Length + 64];
                Array.Copy(compressed, 0, romData, 0x100, compressed.Length);
                // Put a palette at 0x80 (16 colors = 32 bytes)
                byte[] pal = MakeSyntheticPalette16();
                Array.Copy(pal, 0, romData, 0x80, 32);

                var rom = new ROM();
                rom.SwapNewROMDataDirect(romData);

                string outPng = Path.Combine(dir, "short.png");
                // Request 16x16 4bpp (needs 128 tile bytes) but only 16 decompress
                var result = DecompAssetExportCore.ExportGraphics(rom, 0x100, 16, 16, 4, true, 0x80, 16, outPng);

                Assert.False(result.Ok);
                Assert.Equal(DecompAssetStatus.NotData, result.Status);
                Assert.False(File.Exists(outPng), "No file should be written when tile data is too short");
            }
            finally { Directory.Delete(dir, true); }
        }

        // ---- Never-throws guard ----

        [Fact]
        public void AllMethods_NeverThrow_OnNullInputs()
        {
            var ex1 = Record.Exception(() => DecompAssetExportCore.ExportPalette(null, 0, 0, null));
            Assert.Null(ex1);
            var ex2 = Record.Exception(() => DecompAssetExportCore.ExportGraphics(null, 0, 0, 0, 0, false, 0, 0, null));
            Assert.Null(ex2);
            var ex3 = Record.Exception(() => DecompAssetExportCore.ExportMap(null, 0, null));
            Assert.Null(ex3);
            var ex4 = Record.Exception(() => DecompAssetExportCore.ExportText(null, null));
            Assert.Null(ex4);
            var ex5 = Record.Exception(() => DecompAssetExportCore.ResolveSourcePath(null, null));
            Assert.Null(ex5);
        }
    }

    /// <summary>
    /// Stub IImageService for DecompAssetExportCore tests that need Decode4bppTiles.
    /// Returns an indexed StubImage with the input bytes as pixel data.
    /// </summary>
    internal class StubImageServiceForDecomp : IImageService
    {
        public IImage CreateImage(int w, int h) => new StubIndexedImage(w, h, Array.Empty<byte>());
        public IImage CreateIndexedImage(int w, int h, byte[] p, int c) => new StubIndexedImage(w, h, p);
        public IImage LoadImage(string f) => null;
        public IImage LoadImageFromBytes(byte[] d) => null;
        public void GBAColorToRGBA(ushort gba, out byte r, out byte g, out byte b)
        {
            r = (byte)((gba & 0x1F) << 3);
            g = (byte)(((gba >> 5) & 0x1F) << 3);
            b = (byte)(((gba >> 10) & 0x1F) << 3);
        }
        public ushort RGBAToGBAColor(byte r, byte g, byte b) => 0;
        public IImage Decode4bppTiles(byte[] t, int o, int w, int h, byte[] p)
        {
            // Return an indexed image with pixel count = w*h, indices cycling 0..15
            var img = new StubIndexedImage(w, h, p ?? Array.Empty<byte>());
            byte[] idx = new byte[w * h];
            for (int i = 0; i < idx.Length; i++) idx[i] = (byte)(i % 16);
            img.SetPixelData(idx);
            return img;
        }
        public IImage Decode8bppTiles(byte[] t, int o, int w, int h, byte[] p)
        {
            var img = new StubIndexedImage(w, h, p ?? Array.Empty<byte>());
            byte[] idx = new byte[w * h];
            for (int i = 0; i < idx.Length; i++) idx[i] = (byte)(i % 256);
            img.SetPixelData(idx);
            return img;
        }
        public IImage Decode8bppLinear(byte[] d, int o, int w, int h, byte[] p) => new StubIndexedImage(w, h, p ?? Array.Empty<byte>());
        public byte[] Encode4bppTiles(IImage i) => null;
        public byte[] Encode8bppTiles(IImage i) => null;
        public byte[] GBAPaletteToRGBA(byte[] p, int c) => null;
        public byte[] RGBAPaletteToGBA(byte[] p, int c) => null;
    }

    /// <summary>Indexed IImage stub that stores pixel data and returns a GBA palette.</summary>
    internal class StubIndexedImage : IImage
    {
        readonly byte[] _palette;
        byte[] _pixels;
        public int Width { get; }
        public int Height { get; }
        public bool IsIndexed => true;

        public StubIndexedImage(int w, int h, byte[] gbaPalette)
        {
            Width = w; Height = h;
            _palette = gbaPalette;
            _pixels = new byte[w * h];
        }

        public byte[] GetPixelData() => _pixels;
        public void SetPixelData(byte[] data) { _pixels = data; }
        public byte[] GetPaletteGBA() => _palette;
        public void SetPaletteGBA(byte[] p) { }
        public byte[] GetPaletteRGBA() => Array.Empty<byte>();
        public void Save(string f) { }
        public byte[] EncodePng() => Array.Empty<byte>();
        public void Dispose() { }
    }
}
