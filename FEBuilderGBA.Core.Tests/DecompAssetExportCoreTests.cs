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

        [Fact]
        public void ExportMap_TileAtOrAbove0x2000_Refuses_NoFileWritten()
        {
            // A raw tile index >= 0x2000 cannot survive the <<3 .mar encoding (its top 3 bits
            // would be truncated by the (ushort) cast), so the .mar would NOT round-trip.
            // ExportMap must REJECT it rather than emit a silently-lossy .mar (Copilot #1148).
            string dir = NewTempDir();
            try
            {
                int w = 2, h = 1;
                byte[] rawMapBlob = new byte[2 + w * h * 2];
                rawMapBlob[0] = (byte)w; rawMapBlob[1] = (byte)h;
                // entry 0 = ok; entry 1 = 0x2000 (out of range)
                rawMapBlob[2] = 0x01; rawMapBlob[3] = 0x00;        // 0x0001
                rawMapBlob[4] = 0x00; rawMapBlob[5] = 0x20;        // 0x2000
                byte[] compressed = LZ77.compress(rawMapBlob);
                byte[] romData = new byte[0x100 + compressed.Length + 16];
                Array.Copy(compressed, 0, romData, 0x100, compressed.Length);

                var rom = new ROM();
                rom.SwapNewROMDataDirect(romData);

                string marPath = Path.Combine(dir, "lossy.mar");
                var result = DecompAssetExportCore.ExportMap(rom, 0x100, marPath);

                Assert.False(result.Ok);
                Assert.Equal(DecompAssetStatus.NotData, result.Status);
                Assert.False(File.Exists(marPath), "no .mar must be written when a tile is out of range");
                Assert.False(File.Exists(marPath + ".json"), "no sidecar must be written either");
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void ExportMap_Then_ImportMap_FullRoundTrip_IsByteIdentical()
        {
            // End-to-end: ROM tilemap -> ExportMap (.mar+sidecar) -> ImportMap (raw blob)
            // reconstructs the ORIGINAL decompressed blob byte-for-byte (entries < 0x2000).
            string dir = NewTempDir();
            try
            {
                int w = 4, h = 3;
                byte[] rawMapBlob = new byte[2 + w * h * 2];
                rawMapBlob[0] = (byte)w; rawMapBlob[1] = (byte)h;
                for (int i = 0; i < w * h; i++)
                {
                    ushort tile = (ushort)(i * 7 % 0x1FFF); // varied, all < 0x2000
                    rawMapBlob[2 + i * 2] = (byte)(tile & 0xFF);
                    rawMapBlob[2 + i * 2 + 1] = (byte)(tile >> 8);
                }

                byte[] compressed = LZ77.compress(rawMapBlob);
                byte[] romData = new byte[0x100 + compressed.Length + 16];
                Array.Copy(compressed, 0, romData, 0x100, compressed.Length);
                var rom = new ROM();
                rom.SwapNewROMDataDirect(romData);

                string marPath = Path.Combine(dir, "rt.mar");
                Assert.True(DecompAssetExportCore.ExportMap(rom, 0x100, marPath).Ok);

                string outPath = Path.Combine(dir, "rt.tmap_raw.bin");
                var imp = DecompAssetExportCore.ImportMap(marPath, outPath);
                Assert.True(imp.Ok, $"ImportMap failed: {imp.Message}");

                byte[] reconstructed = File.ReadAllBytes(outPath);
                Assert.Equal(rawMapBlob, reconstructed);
            }
            finally { Directory.Delete(dir, true); }
        }

        // ---- ImportMap (#1148) ----

        // Helpers: write a .mar body of (rawTile<<3) LE entries + matching sidecar JSON.
        static byte[] MakeMarBody(int w, int h, Func<int, ushort> rawTileForIndex)
        {
            byte[] body = new byte[w * h * 2];
            for (int i = 0; i < w * h; i++)
            {
                ushort marTile = (ushort)(rawTileForIndex(i) << 3);
                body[i * 2 + 0] = (byte)(marTile & 0xFF);
                body[i * 2 + 1] = (byte)(marTile >> 8);
            }
            return body;
        }

        static void WriteMarPlusSidecar(string marPath, int w, int h, byte[] body)
        {
            File.WriteAllBytes(marPath, body);
            File.WriteAllText(marPath + ".json",
                $"{{\n  \"width\": {w},\n  \"height\": {h},\n  \"srcAddr\": \"0x100\",\n  \"format\": \"febuilder-mar-u16-shl3\"\n}}\n");
        }

        [Fact]
        public void ImportMap_HappyPath_ReconstructsRawUncompressedBlob()
        {
            string dir = NewTempDir();
            try
            {
                int w = 4, h = 3; // 12 entries
                // rawTile values 0x0000, 0x0001, ... up to known values all < 0x2000
                ushort[] rawTiles = new ushort[w * h];
                for (int i = 0; i < rawTiles.Length; i++)
                    rawTiles[i] = (ushort)(i == rawTiles.Length - 1 ? 0x1FFF : i);

                byte[] body = MakeMarBody(w, h, i => rawTiles[i]);
                string marPath = Path.Combine(dir, "chapter.mar");
                WriteMarPlusSidecar(marPath, w, h, body);

                string outPath = Path.Combine(dir, "chapter.tmap_raw.bin");
                var result = DecompAssetExportCore.ImportMap(marPath, outPath);

                Assert.True(result.Ok, $"ImportMap failed: {result.Message}");
                Assert.True(File.Exists(outPath));

                // Expected raw blob = [w][h] + raw u16 LE entries.
                byte[] expected = new byte[2 + w * h * 2];
                expected[0] = (byte)w;
                expected[1] = (byte)h;
                for (int i = 0; i < w * h; i++)
                {
                    expected[2 + i * 2 + 0] = (byte)(rawTiles[i] & 0xFF);
                    expected[2 + i * 2 + 1] = (byte)(rawTiles[i] >> 8);
                }

                byte[] actual = File.ReadAllBytes(outPath);
                Assert.Equal(expected, actual);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void ImportMap_NoRomLoaded_StillWorks()
        {
            // Explicitly assert the import never depends on the ambient ROM.
            CoreState.ROM = null;
            string dir = NewTempDir();
            try
            {
                int w = 2, h = 2;
                byte[] body = MakeMarBody(w, h, i => (ushort)(i + 1));
                string marPath = Path.Combine(dir, "norom.mar");
                WriteMarPlusSidecar(marPath, w, h, body);

                string outPath = Path.Combine(dir, "norom.bin");
                var result = DecompAssetExportCore.ImportMap(marPath, outPath);

                Assert.True(result.Ok, $"ImportMap failed: {result.Message}");
                Assert.Null(CoreState.ROM); // unchanged — never set the ROM
                Assert.True(File.Exists(outPath));
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void ImportMap_BodyRoundTrips()
        {
            int w = 4, h = 3;
            byte[] body = MakeMarBody(w, h, i => (ushort)(i == 11 ? 0x1FFF : i));
            Assert.True(DecompAssetExportCore.RoundTripMarBody(body));
        }

        [Fact]
        public void ImportMap_MissingSidecar_Refuses_NoFileWritten()
        {
            string dir = NewTempDir();
            try
            {
                int w = 2, h = 2;
                byte[] body = MakeMarBody(w, h, i => (ushort)(i + 1));
                string marPath = Path.Combine(dir, "nosidecar.mar");
                File.WriteAllBytes(marPath, body); // NO sidecar

                string outPath = Path.Combine(dir, "nosidecar.bin");
                var result = DecompAssetExportCore.ImportMap(marPath, outPath);

                Assert.False(result.Ok);
                Assert.Equal(DecompAssetStatus.NotData, result.Status);
                Assert.False(File.Exists(outPath));
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void ImportMap_WidthZero_Refuses_NoFileWritten()
        {
            string dir = NewTempDir();
            try
            {
                // body length is irrelevant; the sidecar declares width=0.
                int w = 0, h = 2;
                string marPath = Path.Combine(dir, "w0.mar");
                File.WriteAllBytes(marPath, new byte[0]);
                File.WriteAllText(marPath + ".json",
                    $"{{\n  \"width\": {w},\n  \"height\": {h}\n}}\n");

                string outPath = Path.Combine(dir, "w0.bin");
                var result = DecompAssetExportCore.ImportMap(marPath, outPath);

                Assert.False(result.Ok);
                Assert.False(File.Exists(outPath));
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void ImportMap_Width256_Refuses_NoFileWritten()
        {
            string dir = NewTempDir();
            try
            {
                // Body matches w*h*2 so the validator passes the length check, but
                // width=256 exceeds the u8 header bound — ImportMap must refuse.
                int w = 256, h = 1;
                byte[] body = new byte[w * h * 2]; // all zero → low-3-bits clear, length matches
                string marPath = Path.Combine(dir, "w256.mar");
                File.WriteAllBytes(marPath, body);
                File.WriteAllText(marPath + ".json",
                    $"{{\n  \"width\": {w},\n  \"height\": {h}\n}}\n");

                string outPath = Path.Combine(dir, "w256.bin");
                var result = DecompAssetExportCore.ImportMap(marPath, outPath);

                Assert.False(result.Ok);
                Assert.Equal(DecompAssetStatus.NotData, result.Status);
                Assert.False(File.Exists(outPath));
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void ImportMap_CorruptShift_Refuses_NoFileWritten()
        {
            string dir = NewTempDir();
            try
            {
                int w = 2, h = 2;
                byte[] body = MakeMarBody(w, h, i => (ushort)(i + 1));
                body[0] |= 1; // set a low-3-bit on the first entry → <<3 invariant broken
                string marPath = Path.Combine(dir, "corrupt.mar");
                WriteMarPlusSidecar(marPath, w, h, body);

                string outPath = Path.Combine(dir, "corrupt.bin");
                var result = DecompAssetExportCore.ImportMap(marPath, outPath);

                Assert.False(result.Ok);
                Assert.Equal(DecompAssetStatus.NotData, result.Status);
                Assert.False(File.Exists(outPath));
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void ImportMap_LengthMismatch_Refuses_NoFileWritten()
        {
            string dir = NewTempDir();
            try
            {
                int w = 2, h = 2; // sidecar declares 8-byte body
                byte[] body = MakeMarBody(w, h, i => (ushort)(i + 1));
                byte[] truncated = new byte[body.Length - 2]; // 6 bytes != 2*2*2
                Array.Copy(body, truncated, truncated.Length);
                string marPath = Path.Combine(dir, "trunc.mar");
                WriteMarPlusSidecar(marPath, w, h, truncated);

                string outPath = Path.Combine(dir, "trunc.bin");
                var result = DecompAssetExportCore.ImportMap(marPath, outPath);

                Assert.False(result.Ok);
                Assert.Equal(DecompAssetStatus.NotData, result.Status);
                Assert.False(File.Exists(outPath));
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void ImportMap_NullArgs_ReturnBadArgs()
        {
            var r1 = DecompAssetExportCore.ImportMap(null, "/tmp/x.bin");
            Assert.False(r1.Ok);
            Assert.Equal(DecompAssetStatus.BadArgs, r1.Status);

            var r2 = DecompAssetExportCore.ImportMap("/tmp/x.mar", null);
            Assert.False(r2.Ok);
            Assert.Equal(DecompAssetStatus.BadArgs, r2.Status);
        }

        // ---- RoundTripMarBody (pure) ----

        [Fact]
        public void RoundTripMarBody_True_ForCleanEvenBody()
        {
            byte[] body = MakeMarBody(4, 3, i => (ushort)(i == 11 ? 0x1FFF : i));
            Assert.True(DecompAssetExportCore.RoundTripMarBody(body));
        }

        [Fact]
        public void RoundTripMarBody_False_ForOddLengthBody()
        {
            Assert.False(DecompAssetExportCore.RoundTripMarBody(new byte[3]));
        }

        [Fact]
        public void RoundTripMarBody_False_ForNull()
        {
            Assert.False(DecompAssetExportCore.RoundTripMarBody(null));
        }

        [Fact]
        public void RoundTripMarBody_False_ForLowBitsSet()
        {
            byte[] body = MakeMarBody(2, 2, i => (ushort)(i + 1));
            body[0] |= 1; // entry no longer (rawTile<<3) — fails the shift invariant
            Assert.False(DecompAssetExportCore.RoundTripMarBody(body));
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

        // ---- ExportShops / FormatShops (#1149) ----

        static DecompAssetExportCore.ShopExportRecord MakeShop(
            string label, uint shopAddr, uint slotAddr, params ushort[] items)
        {
            var list = new System.Collections.Generic.List<DecompAssetExportCore.ShopItemEntry>();
            foreach (ushort v in items ?? Array.Empty<ushort>())
                list.Add(new DecompAssetExportCore.ShopItemEntry(v, "Item" + (v & 0xFF)));
            return new DecompAssetExportCore.ShopExportRecord(label, shopAddr, slotAddr, list);
        }

        static int CountOccurrences(string haystack, string needle)
        {
            int n = 0, i = 0;
            while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
            return n;
        }

        [Fact]
        public void FormatShops_NullList_DoesNotThrow_EmitsHeaderOnly()
        {
            string body = DecompAssetExportCore.FormatShops(null);
            Assert.NotNull(body);
            Assert.Contains("FEBuilderGBA shop-list migration export (#1149)", body);
            Assert.DoesNotContain("ORG 0x", body);
            Assert.DoesNotContain("SHORT 0x", body);
        }

        [Fact]
        public void FormatShops_EmptyList_EmitsHeaderOnly()
        {
            string body = DecompAssetExportCore.FormatShops(
                new System.Collections.Generic.List<DecompAssetExportCore.ShopExportRecord>());
            Assert.Contains("migration export (#1149)", body);
            Assert.DoesNotContain("ORG 0x", body);
        }

        [Fact]
        public void FormatShops_EmptyShop_EmitsOrgAndTerminatorOnly()
        {
            var shops = new System.Collections.Generic.List<DecompAssetExportCore.ShopExportRecord>
            {
                MakeShop("Empty Shop", 0x800100, 0x800200),
            };
            string body = DecompAssetExportCore.FormatShops(shops);
            Assert.Contains("ORG 0x800100", body);
            Assert.Contains("SHORT 0x0000", body);                 // terminator
            Assert.Equal(1, CountOccurrences(body, "SHORT 0x"));   // only the terminator
        }

        [Fact]
        public void FormatShops_MultiShopMultiItem_EmitsU16EntriesAndTerminators()
        {
            var shops = new System.Collections.Generic.List<DecompAssetExportCore.ShopExportRecord>
            {
                MakeShop("Preparation Shop", 0x800100, 0x800010, 0x0001, 0x0002, 0x8016),
                MakeShop("Ch1 Armory", 0x800200, 0x800020, 0x004B),
            };
            string body = DecompAssetExportCore.FormatShops(shops);

            Assert.Contains("// Shop: Preparation Shop  (list @ 0x800100, ptr-slot @ 0x800010)", body);
            Assert.Contains("ORG 0x800100", body);
            Assert.Contains("// Shop: Ch1 Armory  (list @ 0x800200, ptr-slot @ 0x800020)", body);
            Assert.Contains("ORG 0x800200", body);

            // u16 entries emitted as 4-digit SHORT, high byte preserved (0x8016 stays 0x8016).
            Assert.Contains("SHORT 0x0001", body);
            Assert.Contains("SHORT 0x0002", body);
            Assert.Contains("SHORT 0x8016", body);   // high-byte flag preserved verbatim
            Assert.Contains("SHORT 0x004B", body);

            // 4 item entries + 2 terminators = 6 SHORT lines.
            Assert.Equal(6, CountOccurrences(body, "SHORT 0x"));
            Assert.Equal(2, CountOccurrences(body, "SHORT 0x0000"));

            // Pre-resolved names appear in the trailing comment (formatter does NOT touch the ROM).
            Assert.Contains("SHORT 0x0001   // Item1", body);
            Assert.Contains("SHORT 0x004B   // Item75", body);   // 0x4B & 0xFF = 75
        }

        [Fact]
        public void FormatShops_IsRomIndependent_NullRom_StillEmitsStoredNames()
        {
            // FINDING B/D: FormatShops must be genuinely PURE — formatting must NOT read the
            // ROM. With CoreState.ROM cleared, formatting the pre-resolved record still works
            // and emits the stored names verbatim.
            var savedRom = CoreState.ROM;
            try
            {
                CoreState.ROM = null;
                var shops = new System.Collections.Generic.List<DecompAssetExportCore.ShopExportRecord>
                {
                    MakeShop("Shop", 0x800100, 0x800010, 0x0001),
                };
                string body = DecompAssetExportCore.FormatShops(shops);
                Assert.Contains("SHORT 0x0001   // Item1", body);   // stored name, no ROM read
            }
            finally { CoreState.ROM = savedRom; }
        }

        [Fact]
        public void SanitizeLabel_StripsControlChars_CollapsesToSpace()
        {
            // FINDING A: worldmap point names can carry raw FE control bytes (e.g. 0x1F) —
            // they must not corrupt the // Shop comment line. Build the dirty string with
            // explicit control chars so the test has no invisible bytes.
            string dirty = "Ide" + (char)0x1F + "WorldMap" + (char)0x1F + "Armory";
            string clean = DecompAssetExportCore.SanitizeLabel(dirty);
            foreach (char c in clean)
                Assert.True(c >= 0x20 && c != 0x7F, $"control char survived: 0x{(int)c:X2}");
            Assert.Equal("Ide WorldMap Armory", clean);
        }

        [Fact]
        public void SanitizeLabel_NullOrEmpty_ReturnsEmpty()
        {
            Assert.Equal("", DecompAssetExportCore.SanitizeLabel(null));
            Assert.Equal("", DecompAssetExportCore.SanitizeLabel(""));
        }

        [Fact]
        public void SanitizeLabel_LeadingTrailingControl_Trimmed()
        {
            string dirty = (char)0x01 + "Mid" + (char)0x1F;
            Assert.Equal("Mid", DecompAssetExportCore.SanitizeLabel(dirty));
        }

        [Fact]
        public void FormatShops_SanitizedLabelIsUsed_NoControlCharsInComment()
        {
            // ExportShops sanitizes the label; here we pass a control-char label THROUGH
            // SanitizeLabel and confirm the emitted comment is clean.
            string sanitized = DecompAssetExportCore.SanitizeLabel("A" + (char)0x01 + "B");
            var shops = new System.Collections.Generic.List<DecompAssetExportCore.ShopExportRecord>
            {
                MakeShop(sanitized, 0x800100, 0x800010, 0x0001),
            };
            string body = DecompAssetExportCore.FormatShops(shops);
            Assert.Contains("// Shop: A B  (list @ 0x800100", body);
            foreach (char c in body)
                Assert.True(c >= 0x20 || c == '\n' || c == '\r' || c == '\t',
                    $"control char in body: 0x{(int)c:X2}");
        }

        [Fact]
        public void ExportShops_NullRom_ReturnsBadArgs_NoThrow()
        {
            var ex = Record.Exception(() =>
            {
                var result = DecompAssetExportCore.ExportShops(null, NewTempDir());
                Assert.False(result.Ok);
                Assert.Equal(DecompAssetStatus.BadArgs, result.Status);
            });
            Assert.Null(ex);
        }

        [Fact]
        public void ExportShops_NullOrEmptyOut_ReturnsBadArgs()
        {
            var rom = new ROM();
            rom.SwapNewROMDataDirect(new byte[0x200]);
            var r1 = DecompAssetExportCore.ExportShops(rom, null);
            Assert.Equal(DecompAssetStatus.BadArgs, r1.Status);
            var r2 = DecompAssetExportCore.ExportShops(rom, "");
            Assert.Equal(DecompAssetStatus.BadArgs, r2.Status);
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
