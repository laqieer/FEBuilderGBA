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

        // ============================================================================
        // Map-change OVERLAY (raw uncompressed u16 LE, #1355)
        // ============================================================================

        // Build a raw u16 LE overlay body (any value valid — no <<3 shift).
        static byte[] MakeOverlayBody(int w, int h, Func<int, ushort> valueForIndex)
        {
            byte[] body = new byte[w * h * 2];
            for (int i = 0; i < w * h; i++)
            {
                ushort v = valueForIndex(i);
                body[i * 2 + 0] = (byte)(v & 0xFF);
                body[i * 2 + 1] = (byte)(v >> 8);
            }
            return body;
        }

        static void WriteChangePlusSidecar(string changePath, int w, int h, byte[] body, string format = "febuilder-mapchange-u16")
        {
            File.WriteAllBytes(changePath, body);
            File.WriteAllText(changePath + ".json",
                $"{{\n  \"width\": {w},\n  \"height\": {h},\n  \"srcAddr\": \"0x200\",\n  \"format\": \"{format}\"\n}}\n");
        }

        // ---- RoundTripMapChangeBody (pure) ----

        [Fact]
        public void RoundTripMapChangeBody_True_ForEvenWxH2Body()
        {
            int w = 4, h = 3;
            byte[] body = MakeOverlayBody(w, h, i => (ushort)(i * 137)); // arbitrary u16, may be >= 0x2000
            Assert.True(DecompAssetExportCore.RoundTripMapChangeBody(body, w, h));
        }

        [Fact]
        public void RoundTripMapChangeBody_False_ForNull()
        {
            Assert.False(DecompAssetExportCore.RoundTripMapChangeBody(null, 2, 2));
        }

        [Fact]
        public void RoundTripMapChangeBody_False_ForOddLength()
        {
            Assert.False(DecompAssetExportCore.RoundTripMapChangeBody(new byte[3], 1, 1));
        }

        [Fact]
        public void RoundTripMapChangeBody_False_ForWrongCount()
        {
            byte[] body = MakeOverlayBody(2, 2, i => (ushort)i); // 8 bytes
            Assert.False(DecompAssetExportCore.RoundTripMapChangeBody(body, 3, 3)); // 18 expected
        }

        [Fact]
        public void RoundTripMapChangeBody_False_ForDimsBelow1()
        {
            byte[] body = MakeOverlayBody(2, 2, i => (ushort)i);
            Assert.False(DecompAssetExportCore.RoundTripMapChangeBody(body, 0, 2));
            Assert.False(DecompAssetExportCore.RoundTripMapChangeBody(body, 2, 0));
        }

        // ---- ImportMapChange ----

        [Fact]
        public void ImportMapChange_HappyPath_IdentityCopy()
        {
            string dir = NewTempDir();
            try
            {
                int w = 3, h = 2; // 6 entries
                byte[] body = MakeOverlayBody(w, h, i => (ushort)(i == 5 ? 0xFFFF : i + 0x1000));
                string changePath = Path.Combine(dir, "ch.change");
                WriteChangePlusSidecar(changePath, w, h, body);

                // Capture CoreState.ROM before/after to prove it is untouched.
                ROM before = CoreState.ROM;

                string outPath = Path.Combine(dir, "ch.change_raw.bin");
                var result = DecompAssetExportCore.ImportMapChange(changePath, outPath);

                Assert.True(result.Ok, $"ImportMapChange failed: {result.Message}");
                Assert.True(File.Exists(outPath));
                Assert.Same(before, CoreState.ROM); // ROM reference unchanged

                // Output blob == input body byte-for-byte (identity copy, no header, no shift).
                byte[] actual = File.ReadAllBytes(outPath);
                Assert.Equal(body, actual);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void ImportMapChange_RootConfined_OutputLandsWhereTold()
        {
            // The Core method writes verbatim to absOutBlobPath; containment is a CLI concern.
            // Assert the output is exactly the path we passed (under the temp dir).
            string dir = NewTempDir();
            try
            {
                int w = 2, h = 2;
                byte[] body = MakeOverlayBody(w, h, i => (ushort)i);
                string changePath = Path.Combine(dir, "r.change");
                WriteChangePlusSidecar(changePath, w, h, body);

                string outPath = Path.Combine(dir, "sub", "r.bin");
                var result = DecompAssetExportCore.ImportMapChange(changePath, outPath);

                Assert.True(result.Ok, result.Message);
                Assert.Contains(outPath, result.WrittenPaths);
                Assert.True(File.Exists(outPath));
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void ImportMapChange_MissingSidecar_ReturnsNotData_NoThrow_NoFile()
        {
            string dir = NewTempDir();
            try
            {
                int w = 2, h = 2;
                byte[] body = MakeOverlayBody(w, h, i => (ushort)i);
                string changePath = Path.Combine(dir, "nosidecar.change");
                File.WriteAllBytes(changePath, body); // NO sidecar

                string outPath = Path.Combine(dir, "nosidecar.bin");
                var result = DecompAssetExportCore.ImportMapChange(changePath, outPath);

                Assert.False(result.Ok);
                Assert.Equal(DecompAssetStatus.NotData, result.Status);
                Assert.False(File.Exists(outPath));
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void ImportMapChange_NullArgs_ReturnBadArgs()
        {
            var r1 = DecompAssetExportCore.ImportMapChange(null, "/tmp/x.bin");
            Assert.Equal(DecompAssetStatus.BadArgs, r1.Status);
            var r2 = DecompAssetExportCore.ImportMapChange("/tmp/x.change", null);
            Assert.Equal(DecompAssetStatus.BadArgs, r2.Status);
        }

        // ---- ExportMapChange ----

        [Fact]
        public void ExportMapChange_WritesRawBody_AndSidecar()
        {
            string dir = NewTempDir();
            try
            {
                int w = 3, h = 2; // 6 entries → 12 bytes
                uint addr = 0x200;
                byte[] romData = new byte[0x400];
                // Plant overlay u16 LE entries at offset 0x200.
                ushort[] vals = new ushort[w * h];
                for (int i = 0; i < vals.Length; i++)
                {
                    vals[i] = (ushort)(i * 0x123 + 0x2345); // varied, includes >= 0x2000 (valid for overlay)
                    romData[addr + i * 2 + 0] = (byte)(vals[i] & 0xFF);
                    romData[addr + i * 2 + 1] = (byte)(vals[i] >> 8);
                }
                var rom = new ROM();
                rom.SwapNewROMDataDirect(romData);

                string changePath = Path.Combine(dir, "c.change");
                var result = DecompAssetExportCore.ExportMapChange(rom, addr, w, h, changePath);

                Assert.True(result.Ok, $"ExportMapChange failed: {result.Message}");
                Assert.True(File.Exists(changePath));

                // .change bytes == planted u16 LE.
                byte[] changeBytes = File.ReadAllBytes(changePath);
                Assert.Equal(w * h * 2, changeBytes.Length);
                for (int i = 0; i < vals.Length; i++)
                {
                    ushort actual = (ushort)(changeBytes[i * 2] | (changeBytes[i * 2 + 1] << 8));
                    Assert.Equal(vals[i], actual);
                }

                // Sidecar has width/height/srcAddr/format.
                string jsonPath = changePath + ".json";
                Assert.True(File.Exists(jsonPath));
                string json = File.ReadAllText(jsonPath);
                Assert.Contains($"\"width\": {w}", json);
                Assert.Contains($"\"height\": {h}", json);
                Assert.Contains($"\"srcAddr\": \"0x{addr:X}\"", json);
                Assert.Contains("\"format\": \"febuilder-mapchange-u16\"", json);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void ExportMapChange_OutOfBounds_ReturnsNotData_NoFileWritten()
        {
            string dir = NewTempDir();
            try
            {
                var rom = new ROM();
                rom.SwapNewROMDataDirect(new byte[0x210]); // tiny ROM
                // addr 0x200 + 10*10*2 = 0x200 + 200 = past the 0x210 end.
                string changePath = Path.Combine(dir, "oob.change");
                var result = DecompAssetExportCore.ExportMapChange(rom, 0x200, 10, 10, changePath);

                Assert.False(result.Ok);
                Assert.Equal(DecompAssetStatus.NotData, result.Status);
                Assert.False(File.Exists(changePath), "no .change must be written on a bounds fault");
                Assert.False(File.Exists(changePath + ".json"), "no sidecar either");
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void ExportMapChange_BadDims_ReturnsNotData_NoFileWritten()
        {
            string dir = NewTempDir();
            try
            {
                var rom = new ROM();
                rom.SwapNewROMDataDirect(new byte[0x400]);
                string changePath = Path.Combine(dir, "baddims.change");
                var result = DecompAssetExportCore.ExportMapChange(rom, 0x200, 256, 1, changePath); // width > 255

                Assert.False(result.Ok);
                Assert.Equal(DecompAssetStatus.NotData, result.Status);
                Assert.False(File.Exists(changePath));
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void ExportMapChange_NullRom_ReturnsBadArgs()
        {
            var result = DecompAssetExportCore.ExportMapChange(null, 0x200, 2, 2, "/tmp/x.change");
            Assert.False(result.Ok);
            Assert.Equal(DecompAssetStatus.BadArgs, result.Status);
        }

        [Fact]
        public void ExportMapChange_DoesNotMutateRomData()
        {
            string dir = NewTempDir();
            try
            {
                int w = 4, h = 4;
                uint addr = 0x200;
                byte[] romData = new byte[0x400];
                for (int i = 0; i < w * h; i++)
                {
                    romData[addr + i * 2 + 0] = (byte)(i & 0xFF);
                    romData[addr + i * 2 + 1] = (byte)(i >> 8);
                }
                var rom = new ROM();
                rom.SwapNewROMDataDirect(romData);
                byte[] before = (byte[])rom.Data.Clone();

                DecompAssetExportCore.ExportMapChange(rom, addr, w, h, Path.Combine(dir, "x.change"));

                Assert.Equal(before, rom.Data);
            }
            finally { Directory.Delete(dir, true); }
        }

        // ---- VerifyMapChangeAgainstRom ----

        [Fact]
        public void VerifyMapChangeAgainstRom_Match_ReturnsOk()
        {
            string dir = NewTempDir();
            try
            {
                int w = 3, h = 2;
                uint addr = 0x200;
                byte[] body = MakeOverlayBody(w, h, i => (ushort)(i * 0x1111));
                byte[] romData = new byte[0x400];
                Array.Copy(body, 0, romData, addr, body.Length);
                var rom = new ROM();
                rom.SwapNewROMDataDirect(romData);

                string changePath = Path.Combine(dir, "v.change");
                WriteChangePlusSidecar(changePath, w, h, body);

                var result = DecompAssetExportCore.VerifyMapChangeAgainstRom(rom, addr, w, h, changePath);
                Assert.True(result.Ok, $"verify failed: {result.Message}");
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void VerifyMapChangeAgainstRom_SingleByteEdit_ReturnsNotData_WithFirstDiffOffset()
        {
            string dir = NewTempDir();
            try
            {
                int w = 3, h = 2;
                uint addr = 0x200;
                byte[] body = MakeOverlayBody(w, h, i => (ushort)(i + 1));
                byte[] romData = new byte[0x400];
                Array.Copy(body, 0, romData, addr, body.Length);
                var rom = new ROM();
                rom.SwapNewROMDataDirect(romData);

                // Edit byte offset 4 in the file body (not in the ROM).
                byte[] edited = (byte[])body.Clone();
                edited[4] ^= 0xFF;
                string changePath = Path.Combine(dir, "v.change");
                WriteChangePlusSidecar(changePath, w, h, edited);

                var result = DecompAssetExportCore.VerifyMapChangeAgainstRom(rom, addr, w, h, changePath);
                Assert.False(result.Ok);
                Assert.Equal(DecompAssetStatus.NotData, result.Status);
                Assert.Contains("byte offset 4", result.Message);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void VerifyMapChangeAgainstRom_OutOfBoundsAddr_ReturnsNotData_NoThrow()
        {
            string dir = NewTempDir();
            try
            {
                int w = 10, h = 10;
                byte[] body = MakeOverlayBody(w, h, i => (ushort)i);
                string changePath = Path.Combine(dir, "oob.change");
                WriteChangePlusSidecar(changePath, w, h, body);

                var rom = new ROM();
                rom.SwapNewROMDataDirect(new byte[0x210]); // too small for 200-byte overlay at 0x200
                byte[] before = (byte[])rom.Data.Clone();

                var result = DecompAssetExportCore.VerifyMapChangeAgainstRom(rom, 0x200, w, h, changePath);
                Assert.False(result.Ok);
                Assert.Equal(DecompAssetStatus.NotData, result.Status);
                Assert.Equal(before, rom.Data); // ROM unchanged
            }
            finally { Directory.Delete(dir, true); }
        }

        // ---- ValidateMapChange (via DecompAssetValidatorCore) ----

        [Fact]
        public void ValidateMapChange_CleanOverlay_Ok()
        {
            string dir = NewTempDir();
            try
            {
                int w = 4, h = 3;
                byte[] body = MakeOverlayBody(w, h, i => (ushort)(i * 999)); // values may exceed 0x2000
                string changePath = Path.Combine(dir, "ok.change");
                WriteChangePlusSidecar(changePath, w, h, body);

                var v = DecompAssetValidatorCore.ValidateAsset(AssetKind.MapChangeOverlay, changePath);
                Assert.True(v.Ok, "expected clean overlay to validate: " + DescribeErrors(v));
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void ValidateMapChange_OddLength_BadLength()
        {
            string dir = NewTempDir();
            try
            {
                string changePath = Path.Combine(dir, "odd.change");
                File.WriteAllBytes(changePath, new byte[5]); // odd
                File.WriteAllText(changePath + ".json",
                    "{\n  \"width\": 1,\n  \"height\": 1,\n  \"format\": \"febuilder-mapchange-u16\"\n}\n");

                var v = DecompAssetValidatorCore.ValidateAsset(AssetKind.MapChangeOverlay, changePath);
                Assert.False(v.Ok);
                Assert.Contains(v.Errors, e => e.Code == "BAD_MAPCHANGE_LENGTH");
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void ValidateMapChange_WrongCountVsSidecar_BadLength()
        {
            string dir = NewTempDir();
            try
            {
                // sidecar says 3x3 (18 bytes) but body is 2x2 (8 bytes).
                byte[] body = new byte[8];
                string changePath = Path.Combine(dir, "wrongcount.change");
                File.WriteAllBytes(changePath, body);
                File.WriteAllText(changePath + ".json",
                    "{\n  \"width\": 3,\n  \"height\": 3,\n  \"format\": \"febuilder-mapchange-u16\"\n}\n");

                var v = DecompAssetValidatorCore.ValidateAsset(AssetKind.MapChangeOverlay, changePath);
                Assert.False(v.Ok);
                Assert.Contains(v.Errors, e => e.Code == "BAD_MAPCHANGE_LENGTH");
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void ValidateMapChange_DimsOver255_BadDims()
        {
            string dir = NewTempDir();
            try
            {
                int w = 256, h = 1;
                byte[] body = new byte[w * h * 2];
                string changePath = Path.Combine(dir, "bigdims.change");
                File.WriteAllBytes(changePath, body);
                File.WriteAllText(changePath + ".json",
                    $"{{\n  \"width\": {w},\n  \"height\": {h},\n  \"format\": \"febuilder-mapchange-u16\"\n}}\n");

                var v = DecompAssetValidatorCore.ValidateAsset(AssetKind.MapChangeOverlay, changePath);
                Assert.False(v.Ok);
                Assert.Contains(v.Errors, e => e.Code == "BAD_MAPCHANGE_DIMS");
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void ValidateMapChange_MissingSidecar_NoSidecarError()
        {
            string dir = NewTempDir();
            try
            {
                string changePath = Path.Combine(dir, "nos.change");
                File.WriteAllBytes(changePath, new byte[8]); // no sidecar

                var v = DecompAssetValidatorCore.ValidateAsset(AssetKind.MapChangeOverlay, changePath);
                Assert.False(v.Ok);
                Assert.Contains(v.Errors, e => e.Code == "MAPCHANGE_NO_SIDECAR");
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void ValidateMapChange_WrongFormat_BadFormat()
        {
            string dir = NewTempDir();
            try
            {
                int w = 2, h = 2;
                byte[] body = new byte[w * h * 2];
                string changePath = Path.Combine(dir, "wrongfmt.change");
                File.WriteAllBytes(changePath, body);
                // Wrong format (the .mar format, not the overlay format).
                File.WriteAllText(changePath + ".json",
                    $"{{\n  \"width\": {w},\n  \"height\": {h},\n  \"format\": \"febuilder-mar-u16-shl3\"\n}}\n");

                var v = DecompAssetValidatorCore.ValidateAsset(AssetKind.MapChangeOverlay, changePath);
                Assert.False(v.Ok);
                Assert.Contains(v.Errors, e => e.Code == "BAD_MAPCHANGE_FORMAT");
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void ValidateMapChange_MissingFormat_BadFormat()
        {
            string dir = NewTempDir();
            try
            {
                int w = 2, h = 2;
                byte[] body = new byte[w * h * 2];
                string changePath = Path.Combine(dir, "nofmt.change");
                File.WriteAllBytes(changePath, body);
                File.WriteAllText(changePath + ".json",
                    $"{{\n  \"width\": {w},\n  \"height\": {h}\n}}\n"); // no format field

                var v = DecompAssetValidatorCore.ValidateAsset(AssetKind.MapChangeOverlay, changePath);
                Assert.False(v.Ok);
                Assert.Contains(v.Errors, e => e.Code == "BAD_MAPCHANGE_FORMAT");
            }
            finally { Directory.Delete(dir, true); }
        }

        // ============================================================================
        // Map tile-animation-2 PALETTE block (raw uncompressed u16 LE, #1360)
        // ============================================================================

        // Build a raw u16 LE palette body (any value valid — no <<3 shift, no compression).
        static byte[] MakeAnime2PalBody(int count, Func<int, ushort> valueForIndex)
        {
            byte[] body = new byte[count * 2];
            for (int i = 0; i < count; i++)
            {
                ushort v = valueForIndex(i);
                body[i * 2 + 0] = (byte)(v & 0xFF);
                body[i * 2 + 1] = (byte)(v >> 8);
            }
            return body;
        }

        static void WriteAnime2PalPlusSidecar(string palPath, int count, byte[] body, string format = "febuilder-mapanime2-pal-u16")
        {
            File.WriteAllBytes(palPath, body);
            File.WriteAllText(palPath + ".json",
                $"{{\n  \"count\": {count},\n  \"srcAddr\": \"0x200\",\n  \"format\": \"{format}\"\n}}\n");
        }

        // ---- RoundTripMapAnime2PalBody (pure) ----

        [Fact]
        public void RoundTripMapAnime2PalBody_True_ForEvenCount2Body()
        {
            int count = 12;
            byte[] body = MakeAnime2PalBody(count, i => (ushort)(i * 137)); // arbitrary u16, may be >= 0x2000
            Assert.True(DecompAssetExportCore.RoundTripMapAnime2PalBody(body, count));
        }

        [Fact]
        public void RoundTripMapAnime2PalBody_False_ForNull()
        {
            Assert.False(DecompAssetExportCore.RoundTripMapAnime2PalBody(null, 4));
        }

        [Fact]
        public void RoundTripMapAnime2PalBody_False_ForOddLength()
        {
            Assert.False(DecompAssetExportCore.RoundTripMapAnime2PalBody(new byte[3], 1));
        }

        [Fact]
        public void RoundTripMapAnime2PalBody_False_ForWrongCount()
        {
            byte[] body = MakeAnime2PalBody(4, i => (ushort)i); // 8 bytes
            Assert.False(DecompAssetExportCore.RoundTripMapAnime2PalBody(body, 9)); // 18 expected
        }

        [Fact]
        public void RoundTripMapAnime2PalBody_False_ForCountBelow1()
        {
            byte[] body = MakeAnime2PalBody(4, i => (ushort)i);
            Assert.False(DecompAssetExportCore.RoundTripMapAnime2PalBody(body, 0));
        }

        // ---- ImportMapAnime2Pal ----

        [Fact]
        public void ImportMapAnime2Pal_HappyPath_IdentityCopy()
        {
            string dir = NewTempDir();
            try
            {
                int count = 6;
                byte[] body = MakeAnime2PalBody(count, i => (ushort)(i == 5 ? 0xFFFF : i + 0x1000));
                string palPath = Path.Combine(dir, "p.mapanime2pal");
                WriteAnime2PalPlusSidecar(palPath, count, body);

                // Capture CoreState.ROM before/after to prove it is untouched.
                ROM before = CoreState.ROM;

                string outPath = Path.Combine(dir, "p.mapanime2pal_raw.bin");
                var result = DecompAssetExportCore.ImportMapAnime2Pal(palPath, outPath);

                Assert.True(result.Ok, $"ImportMapAnime2Pal failed: {result.Message}");
                Assert.True(File.Exists(outPath));
                Assert.Same(before, CoreState.ROM); // ROM reference unchanged

                // Output blob == input body byte-for-byte (identity copy, no header, no shift).
                byte[] actual = File.ReadAllBytes(outPath);
                Assert.Equal(body, actual);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void ImportMapAnime2Pal_RootConfined_OutputLandsWhereTold()
        {
            string dir = NewTempDir();
            try
            {
                int count = 4;
                byte[] body = MakeAnime2PalBody(count, i => (ushort)i);
                string palPath = Path.Combine(dir, "r.mapanime2pal");
                WriteAnime2PalPlusSidecar(palPath, count, body);

                string outPath = Path.Combine(dir, "sub", "r.bin");
                var result = DecompAssetExportCore.ImportMapAnime2Pal(palPath, outPath);

                Assert.True(result.Ok, result.Message);
                Assert.Contains(outPath, result.WrittenPaths);
                Assert.True(File.Exists(outPath));
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void ImportMapAnime2Pal_MissingSidecar_ReturnsNotData_NoThrow_NoFile()
        {
            string dir = NewTempDir();
            try
            {
                int count = 4;
                byte[] body = MakeAnime2PalBody(count, i => (ushort)i);
                string palPath = Path.Combine(dir, "nosidecar.mapanime2pal");
                File.WriteAllBytes(palPath, body); // NO sidecar

                string outPath = Path.Combine(dir, "nosidecar.bin");
                var result = DecompAssetExportCore.ImportMapAnime2Pal(palPath, outPath);

                Assert.False(result.Ok);
                Assert.Equal(DecompAssetStatus.NotData, result.Status);
                Assert.False(File.Exists(outPath));
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void ImportMapAnime2Pal_NullArgs_ReturnBadArgs()
        {
            var r1 = DecompAssetExportCore.ImportMapAnime2Pal(null, "/tmp/x.bin");
            Assert.Equal(DecompAssetStatus.BadArgs, r1.Status);
            var r2 = DecompAssetExportCore.ImportMapAnime2Pal("/tmp/x.mapanime2pal", null);
            Assert.Equal(DecompAssetStatus.BadArgs, r2.Status);
        }

        // ---- ExportMapAnime2Pal ----

        [Fact]
        public void ExportMapAnime2Pal_WritesRawBody_AndSidecar()
        {
            string dir = NewTempDir();
            try
            {
                int count = 6; // 6 colors → 12 bytes
                uint addr = 0x200;
                byte[] romData = new byte[0x400];
                // Plant palette u16 LE colors at offset 0x200.
                ushort[] vals = new ushort[count];
                for (int i = 0; i < vals.Length; i++)
                {
                    vals[i] = (ushort)(i * 0x123 + 0x2345); // varied, includes >= 0x2000
                    romData[addr + i * 2 + 0] = (byte)(vals[i] & 0xFF);
                    romData[addr + i * 2 + 1] = (byte)(vals[i] >> 8);
                }
                var rom = new ROM();
                rom.SwapNewROMDataDirect(romData);

                string palPath = Path.Combine(dir, "p.mapanime2pal");
                var result = DecompAssetExportCore.ExportMapAnime2Pal(rom, addr, count, palPath);

                Assert.True(result.Ok, $"ExportMapAnime2Pal failed: {result.Message}");
                Assert.True(File.Exists(palPath));

                // .mapanime2pal bytes == planted u16 LE.
                byte[] palBytes = File.ReadAllBytes(palPath);
                Assert.Equal(count * 2, palBytes.Length);
                for (int i = 0; i < vals.Length; i++)
                {
                    ushort actual = (ushort)(palBytes[i * 2] | (palBytes[i * 2 + 1] << 8));
                    Assert.Equal(vals[i], actual);
                }

                // Sidecar has count/srcAddr/format.
                string jsonPath = palPath + ".json";
                Assert.True(File.Exists(jsonPath));
                string json = File.ReadAllText(jsonPath);
                Assert.Contains($"\"count\": {count}", json);
                Assert.Contains($"\"srcAddr\": \"0x{addr:X}\"", json);
                Assert.Contains("\"format\": \"febuilder-mapanime2-pal-u16\"", json);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void ExportMapAnime2Pal_OutOfBounds_ReturnsNotData_NoFileWritten()
        {
            string dir = NewTempDir();
            try
            {
                var rom = new ROM();
                rom.SwapNewROMDataDirect(new byte[0x210]); // tiny ROM
                // addr 0x200 + 100*2 = 0x200 + 200 = past the 0x210 end.
                string palPath = Path.Combine(dir, "oob.mapanime2pal");
                var result = DecompAssetExportCore.ExportMapAnime2Pal(rom, 0x200, 100, palPath);

                Assert.False(result.Ok);
                Assert.Equal(DecompAssetStatus.NotData, result.Status);
                Assert.False(File.Exists(palPath), "no .mapanime2pal must be written on a bounds fault");
                Assert.False(File.Exists(palPath + ".json"), "no sidecar either");
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void ExportMapAnime2Pal_CountBelow1_ReturnsNotData_NoFileWritten()
        {
            string dir = NewTempDir();
            try
            {
                var rom = new ROM();
                rom.SwapNewROMDataDirect(new byte[0x400]);
                string palPath = Path.Combine(dir, "zero.mapanime2pal");
                var result = DecompAssetExportCore.ExportMapAnime2Pal(rom, 0x200, 0, palPath); // count < 1

                Assert.False(result.Ok);
                Assert.Equal(DecompAssetStatus.NotData, result.Status);
                Assert.False(File.Exists(palPath));
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void ExportMapAnime2Pal_CountOver255_ReturnsNotData_NoFileWritten()
        {
            string dir = NewTempDir();
            try
            {
                var rom = new ROM();
                rom.SwapNewROMDataDirect(new byte[0x1000]);
                string palPath = Path.Combine(dir, "big.mapanime2pal");
                var result = DecompAssetExportCore.ExportMapAnime2Pal(rom, 0x200, 256, palPath); // count > 255

                Assert.False(result.Ok);
                Assert.Equal(DecompAssetStatus.NotData, result.Status);
                Assert.False(File.Exists(palPath));
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void ExportMapAnime2Pal_NullRom_ReturnsBadArgs()
        {
            var result = DecompAssetExportCore.ExportMapAnime2Pal(null, 0x200, 4, "/tmp/x.mapanime2pal");
            Assert.False(result.Ok);
            Assert.Equal(DecompAssetStatus.BadArgs, result.Status);
        }

        [Fact]
        public void ExportMapAnime2Pal_DoesNotMutateRomData()
        {
            string dir = NewTempDir();
            try
            {
                int count = 16;
                uint addr = 0x200;
                byte[] romData = new byte[0x400];
                for (int i = 0; i < count; i++)
                {
                    romData[addr + i * 2 + 0] = (byte)(i & 0xFF);
                    romData[addr + i * 2 + 1] = (byte)(i >> 8);
                }
                var rom = new ROM();
                rom.SwapNewROMDataDirect(romData);
                byte[] before = (byte[])rom.Data.Clone();

                DecompAssetExportCore.ExportMapAnime2Pal(rom, addr, count, Path.Combine(dir, "x.mapanime2pal"));

                Assert.Equal(before, rom.Data);
            }
            finally { Directory.Delete(dir, true); }
        }

        // ---- Export → Import full round-trip ----

        [Fact]
        public void ExportThenImportMapAnime2Pal_IsByteIdentical()
        {
            string dir = NewTempDir();
            try
            {
                int count = 10;
                uint addr = 0x200;
                byte[] romData = new byte[0x400];
                byte[] planted = MakeAnime2PalBody(count, i => (ushort)(i * 0x0707 + 0x1234));
                Array.Copy(planted, 0, romData, addr, planted.Length);
                var rom = new ROM();
                rom.SwapNewROMDataDirect(romData);

                string palPath = Path.Combine(dir, "rt.mapanime2pal");
                var exp = DecompAssetExportCore.ExportMapAnime2Pal(rom, addr, count, palPath);
                Assert.True(exp.Ok, exp.Message);

                string outPath = Path.Combine(dir, "rt_raw.bin");
                var imp = DecompAssetExportCore.ImportMapAnime2Pal(palPath, outPath);
                Assert.True(imp.Ok, imp.Message);

                byte[] roundtripped = File.ReadAllBytes(outPath);
                Assert.Equal(planted, roundtripped);
            }
            finally { Directory.Delete(dir, true); }
        }

        // ---- VerifyMapAnime2PalAgainstRom ----

        [Fact]
        public void VerifyMapAnime2PalAgainstRom_Match_ReturnsOk()
        {
            string dir = NewTempDir();
            try
            {
                int count = 6;
                uint addr = 0x200;
                byte[] body = MakeAnime2PalBody(count, i => (ushort)(i * 0x1111));
                byte[] romData = new byte[0x400];
                Array.Copy(body, 0, romData, addr, body.Length);
                var rom = new ROM();
                rom.SwapNewROMDataDirect(romData);

                string palPath = Path.Combine(dir, "v.mapanime2pal");
                WriteAnime2PalPlusSidecar(palPath, count, body);

                var result = DecompAssetExportCore.VerifyMapAnime2PalAgainstRom(rom, addr, count, palPath);
                Assert.True(result.Ok, $"verify failed: {result.Message}");
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void VerifyMapAnime2PalAgainstRom_SingleByteEdit_ReturnsNotData_WithFirstDiffOffset()
        {
            string dir = NewTempDir();
            try
            {
                int count = 6;
                uint addr = 0x200;
                byte[] body = MakeAnime2PalBody(count, i => (ushort)(i + 1));
                byte[] romData = new byte[0x400];
                Array.Copy(body, 0, romData, addr, body.Length);
                var rom = new ROM();
                rom.SwapNewROMDataDirect(romData);

                // Edit byte offset 4 in the file body (not in the ROM).
                byte[] edited = (byte[])body.Clone();
                edited[4] ^= 0xFF;
                string palPath = Path.Combine(dir, "v.mapanime2pal");
                WriteAnime2PalPlusSidecar(palPath, count, edited);

                var result = DecompAssetExportCore.VerifyMapAnime2PalAgainstRom(rom, addr, count, palPath);
                Assert.False(result.Ok);
                Assert.Equal(DecompAssetStatus.NotData, result.Status);
                Assert.Contains("byte offset 4", result.Message);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void VerifyMapAnime2PalAgainstRom_OutOfBoundsAddr_ReturnsNotData_NoThrow()
        {
            string dir = NewTempDir();
            try
            {
                int count = 100;
                byte[] body = MakeAnime2PalBody(count, i => (ushort)i);
                string palPath = Path.Combine(dir, "oob.mapanime2pal");
                WriteAnime2PalPlusSidecar(palPath, count, body);

                var rom = new ROM();
                rom.SwapNewROMDataDirect(new byte[0x210]); // too small for 200-byte palette at 0x200
                byte[] before = (byte[])rom.Data.Clone();

                var result = DecompAssetExportCore.VerifyMapAnime2PalAgainstRom(rom, 0x200, count, palPath);
                Assert.False(result.Ok);
                Assert.Equal(DecompAssetStatus.NotData, result.Status);
                Assert.Equal(before, rom.Data); // ROM unchanged
            }
            finally { Directory.Delete(dir, true); }
        }

        // ---- ValidateMapAnime2Pal (via DecompAssetValidatorCore) ----

        [Fact]
        public void ValidateMapAnime2Pal_CleanBlock_Ok()
        {
            string dir = NewTempDir();
            try
            {
                int count = 12;
                byte[] body = MakeAnime2PalBody(count, i => (ushort)(i * 999)); // values may exceed 0x2000
                string palPath = Path.Combine(dir, "ok.mapanime2pal");
                WriteAnime2PalPlusSidecar(palPath, count, body);

                var v = DecompAssetValidatorCore.ValidateAsset(AssetKind.MapTileAnimation2Palette, palPath);
                Assert.True(v.Ok, "expected clean palette block to validate: " + DescribeErrors(v));
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void ValidateMapAnime2Pal_OddLength_BadLength()
        {
            string dir = NewTempDir();
            try
            {
                string palPath = Path.Combine(dir, "odd.mapanime2pal");
                File.WriteAllBytes(palPath, new byte[5]); // odd
                File.WriteAllText(palPath + ".json",
                    "{\n  \"count\": 1,\n  \"format\": \"febuilder-mapanime2-pal-u16\"\n}\n");

                var v = DecompAssetValidatorCore.ValidateAsset(AssetKind.MapTileAnimation2Palette, palPath);
                Assert.False(v.Ok);
                Assert.Contains(v.Errors, e => e.Code == "BAD_MAPANIME2PAL_LENGTH");
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void ValidateMapAnime2Pal_WrongCountVsSidecar_BadLength()
        {
            string dir = NewTempDir();
            try
            {
                // sidecar says 9 colors (18 bytes) but body is 4 colors (8 bytes).
                byte[] body = new byte[8];
                string palPath = Path.Combine(dir, "wrongcount.mapanime2pal");
                File.WriteAllBytes(palPath, body);
                File.WriteAllText(palPath + ".json",
                    "{\n  \"count\": 9,\n  \"format\": \"febuilder-mapanime2-pal-u16\"\n}\n");

                var v = DecompAssetValidatorCore.ValidateAsset(AssetKind.MapTileAnimation2Palette, palPath);
                Assert.False(v.Ok);
                Assert.Contains(v.Errors, e => e.Code == "BAD_MAPANIME2PAL_LENGTH");
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void ValidateMapAnime2Pal_CountOver255_BadCount()
        {
            string dir = NewTempDir();
            try
            {
                int count = 256;
                byte[] body = new byte[count * 2];
                string palPath = Path.Combine(dir, "bigcount.mapanime2pal");
                File.WriteAllBytes(palPath, body);
                File.WriteAllText(palPath + ".json",
                    $"{{\n  \"count\": {count},\n  \"format\": \"febuilder-mapanime2-pal-u16\"\n}}\n");

                var v = DecompAssetValidatorCore.ValidateAsset(AssetKind.MapTileAnimation2Palette, palPath);
                Assert.False(v.Ok);
                Assert.Contains(v.Errors, e => e.Code == "BAD_MAPANIME2PAL_COUNT");
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void ValidateMapAnime2Pal_CountZero_BadCount()
        {
            string dir = NewTempDir();
            try
            {
                // count==0 is an INTENTIONAL refusal (a meaningful source asset must have >=1 color).
                byte[] body = new byte[0];
                string palPath = Path.Combine(dir, "zerocount.mapanime2pal");
                File.WriteAllBytes(palPath, body);
                File.WriteAllText(palPath + ".json",
                    "{\n  \"count\": 0,\n  \"format\": \"febuilder-mapanime2-pal-u16\"\n}\n");

                var v = DecompAssetValidatorCore.ValidateAsset(AssetKind.MapTileAnimation2Palette, palPath);
                Assert.False(v.Ok);
                Assert.Contains(v.Errors, e => e.Code == "BAD_MAPANIME2PAL_COUNT");
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void ValidateMapAnime2Pal_MissingSidecar_NoSidecarError()
        {
            string dir = NewTempDir();
            try
            {
                string palPath = Path.Combine(dir, "nos.mapanime2pal");
                File.WriteAllBytes(palPath, new byte[8]); // no sidecar

                var v = DecompAssetValidatorCore.ValidateAsset(AssetKind.MapTileAnimation2Palette, palPath);
                Assert.False(v.Ok);
                Assert.Contains(v.Errors, e => e.Code == "MAPANIME2PAL_NO_SIDECAR");
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void ValidateMapAnime2Pal_WrongFormat_BadFormat()
        {
            string dir = NewTempDir();
            try
            {
                int count = 4;
                byte[] body = new byte[count * 2];
                string palPath = Path.Combine(dir, "wrongfmt.mapanime2pal");
                File.WriteAllBytes(palPath, body);
                // Wrong format (the map-change overlay format, not the palette format).
                File.WriteAllText(palPath + ".json",
                    $"{{\n  \"count\": {count},\n  \"format\": \"febuilder-mapchange-u16\"\n}}\n");

                var v = DecompAssetValidatorCore.ValidateAsset(AssetKind.MapTileAnimation2Palette, palPath);
                Assert.False(v.Ok);
                Assert.Contains(v.Errors, e => e.Code == "BAD_MAPANIME2PAL_FORMAT");
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void ValidateMapAnime2Pal_MissingFormat_BadFormat()
        {
            string dir = NewTempDir();
            try
            {
                int count = 4;
                byte[] body = new byte[count * 2];
                string palPath = Path.Combine(dir, "nofmt.mapanime2pal");
                File.WriteAllBytes(palPath, body);
                File.WriteAllText(palPath + ".json",
                    $"{{\n  \"count\": {count}\n}}\n"); // no format field

                var v = DecompAssetValidatorCore.ValidateAsset(AssetKind.MapTileAnimation2Palette, palPath);
                Assert.False(v.Ok);
                Assert.Contains(v.Errors, e => e.Code == "BAD_MAPANIME2PAL_FORMAT");
            }
            finally { Directory.Delete(dir, true); }
        }

        static string DescribeErrors(AssetValidationResult v)
        {
            var sb = new StringBuilder();
            foreach (AssetIssue e in v.Errors) sb.Append($"[{e.Code}] {e.Message}; ");
            return sb.ToString();
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


        // ============================================================
        // OBJ tileset section (#1371)
        // ============================================================

        static byte[] MakeAndPlantLz77(uint addr, int rawSize, out byte[] raw)
        {
            raw = new byte[rawSize];
            for (int i = 0; i < rawSize; i++) raw[i] = (byte)(i ^ 0xAB); // known pattern
            byte[] comp = LZ77.compress(raw);
            int baseOffset = checked((int)addr);
            byte[] romData = new byte[baseOffset + comp.Length + 16];
            Array.Copy(comp, 0, romData, baseOffset, comp.Length);
            return romData;
        }

        static ROM MakeRomWithLz77(uint addr, int rawSize, out byte[] raw)
        {
            byte[] romData = MakeAndPlantLz77(addr, rawSize, out raw);
            var rom = new ROM();
            rom.SwapNewROMDataDirect(romData);
            return rom;
        }

        // ---- ExportObjTiles ----

        [Fact]
        public void ExportObjTiles_WritesDecompressedBody_AndSidecar()
        {
            string dir = NewTempDir();
            try
            {
                uint addr = 0x200;
                var rom = MakeRomWithLz77(addr, 128, out byte[] raw);
                string outPath = Path.Combine(dir, "obj.objtiles");

                var result = DecompAssetExportCore.ExportObjTiles(rom, addr, outPath);
                Assert.True(result.Ok, $"ExportObjTiles failed: {result.Message}");
                Assert.True(File.Exists(outPath));

                byte[] body = File.ReadAllBytes(outPath);
                Assert.Equal(raw, body);

                string jsonPath = outPath + ".json";
                Assert.True(File.Exists(jsonPath));
                string json = File.ReadAllText(jsonPath);
                Assert.Contains($"\"length\": {raw.Length}", json);
                Assert.Contains($"\"srcAddr\": \"0x{addr:X}\"", json);
                Assert.Contains("\"format\": \"febuilder-objtiles-lz77\"", json);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void ExportObjTiles_NonLz77Addr_ReturnsNotData_NoFile()
        {
            string dir = NewTempDir();
            try
            {
                var rom = new ROM();
                rom.SwapNewROMDataDirect(new byte[0x400]); // zeros — not a valid LZ77 stream
                string outPath = Path.Combine(dir, "obj.objtiles");

                var result = DecompAssetExportCore.ExportObjTiles(rom, 0x200, outPath);
                Assert.False(result.Ok);
                Assert.Equal(DecompAssetStatus.NotData, result.Status);
                Assert.False(File.Exists(outPath));
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void ExportObjTiles_OverBounds_ReturnsNotData()
        {
            string dir = NewTempDir();
            try
            {
                // Put a valid LZ77 header at 0x200 but with decompressed size pointing beyond ROM
                var rom = new ROM();
                byte[] romData = new byte[0x210]; // tiny
                // Set LZ77 magic byte 0x10, then 3 bytes of uncompressed size (large)
                romData[0x200] = 0x10;
                romData[0x201] = 0x00;
                romData[0x202] = 0x04;
                romData[0x203] = 0x00; // 0x400 uncompressed
                // Compressed data is missing/truncated → getCompressedSize returns 0
                rom.SwapNewROMDataDirect(romData);
                string outPath = Path.Combine(dir, "obj.objtiles");

                var result = DecompAssetExportCore.ExportObjTiles(rom, 0x200, outPath);
                Assert.False(result.Ok);
                Assert.False(File.Exists(outPath));
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void ExportObjTiles_NullRom_ReturnsBadArgs()
        {
            var result = DecompAssetExportCore.ExportObjTiles(null, 0x200, "/tmp/obj.objtiles");
            Assert.Equal(DecompAssetStatus.BadArgs, result.Status);
        }

        [Fact]
        public void ExportObjTiles_AddrInLastBytes_ReturnsNotData_NoThrow()
        {
            // Regression (#1371 Copilot review): an addr within the last 1-3 ROM bytes must
            // surface as a clean NotData (the 4-byte LZ77 header is out of bounds), NOT a
            // Faulted IndexOutOfRangeException from getCompressedSize reading input[offset+3].
            string dir = NewTempDir();
            try
            {
                var rom = new ROM();
                rom.SwapNewROMDataDirect(new byte[0x400]);
                string outPath = Path.Combine(dir, "obj.objtiles");

                // 0x3FE leaves only 2 bytes; header read of +3 would overrun.
                var result = DecompAssetExportCore.ExportObjTiles(rom, 0x3FE, outPath);
                Assert.False(result.Ok);
                Assert.Equal(DecompAssetStatus.NotData, result.Status);
                Assert.False(File.Exists(outPath));
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void VerifyObjTilesAgainstRom_AddrInLastBytes_ReturnsNotData_NoThrow()
        {
            // Same boundary hazard as ExportObjTiles, on the verify path (#1371 Copilot review).
            string dir = NewTempDir();
            try
            {
                var rom = new ROM();
                rom.SwapNewROMDataDirect(new byte[0x400]);
                byte[] body = new byte[32];
                string inPath = Path.Combine(dir, "obj.objtiles");
                WriteObjTilesPlusSidecar(inPath, body);

                var result = DecompAssetExportCore.VerifyObjTilesAgainstRom(rom, 0x3FE, inPath);
                Assert.False(result.Ok);
                Assert.Equal(DecompAssetStatus.NotData, result.Status);
            }
            finally { Directory.Delete(dir, true); }
        }

        // ---- ImportObjTiles ----

        static void WriteObjTilesPlusSidecar(string path, byte[] body, string format = "febuilder-objtiles-lz77")
        {
            File.WriteAllBytes(path, body);
            File.WriteAllText(path + ".json",
                $"{{\n  \"length\": {body.Length},\n  \"srcAddr\": \"0x200\",\n  \"format\": \"{format}\"\n}}\n");
        }

        [Fact]
        public void ImportObjTiles_IdentityCopy()
        {
            string dir = NewTempDir();
            try
            {
                byte[] body = new byte[64];
                for (int i = 0; i < body.Length; i++) body[i] = (byte)(i * 3);
                string inPath = Path.Combine(dir, "obj.objtiles");
                WriteObjTilesPlusSidecar(inPath, body);

                string outPath = Path.Combine(dir, "obj.bin");
                var result = DecompAssetExportCore.ImportObjTiles(inPath, outPath);
                Assert.True(result.Ok, $"ImportObjTiles failed: {result.Message}");
                Assert.True(File.Exists(outPath));
                Assert.Equal(body, File.ReadAllBytes(outPath));
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void ImportObjTiles_DoesNotMutateCoreStateRom()
        {
            string dir = NewTempDir();
            try
            {
                byte[] body = new byte[32];
                string inPath = Path.Combine(dir, "obj.objtiles");
                WriteObjTilesPlusSidecar(inPath, body);

                ROM before = CoreState.ROM;
                DecompAssetExportCore.ImportObjTiles(inPath, Path.Combine(dir, "obj.bin"));
                Assert.Same(before, CoreState.ROM);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void ImportObjTiles_MissingSidecar_Fails()
        {
            string dir = NewTempDir();
            try
            {
                byte[] body = new byte[32];
                string inPath = Path.Combine(dir, "obj.objtiles");
                File.WriteAllBytes(inPath, body); // no sidecar

                var result = DecompAssetExportCore.ImportObjTiles(inPath, Path.Combine(dir, "obj.bin"));
                Assert.False(result.Ok);
                Assert.Equal(DecompAssetStatus.NotData, result.Status);
            }
            finally { Directory.Delete(dir, true); }
        }

        // ---- RoundTripObjTilesBody ----

        [Fact]
        public void RoundTripObjTilesBody_Matches_True()
        {
            byte[] body = new byte[128];
            Assert.True(DecompAssetExportCore.RoundTripObjTilesBody(body, 128));
        }

        [Fact]
        public void RoundTripObjTilesBody_WrongLen_False()
        {
            byte[] body = new byte[128];
            Assert.False(DecompAssetExportCore.RoundTripObjTilesBody(body, 64));
        }

        [Fact]
        public void RoundTripObjTilesBody_Null_False()
        {
            Assert.False(DecompAssetExportCore.RoundTripObjTilesBody(null, 64));
        }

        // ---- VerifyObjTilesAgainstRom ----

        [Fact]
        public void VerifyObjTilesAgainstRom_Match_Ok()
        {
            string dir = NewTempDir();
            try
            {
                uint addr = 0x200;
                var rom = MakeRomWithLz77(addr, 64, out byte[] raw);
                string inPath = Path.Combine(dir, "obj.objtiles");
                WriteObjTilesPlusSidecar(inPath, raw);

                var result = DecompAssetExportCore.VerifyObjTilesAgainstRom(rom, addr, inPath);
                Assert.True(result.Ok, $"verify failed: {result.Message}");
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void VerifyObjTilesAgainstRom_Mismatch_ReturnsNotData()
        {
            string dir = NewTempDir();
            try
            {
                uint addr = 0x200;
                var rom = MakeRomWithLz77(addr, 64, out byte[] raw);
                byte[] tampered = (byte[])raw.Clone();
                tampered[5] ^= 0xFF; // flip byte at offset 5
                string inPath = Path.Combine(dir, "obj.objtiles");
                WriteObjTilesPlusSidecar(inPath, tampered);

                var result = DecompAssetExportCore.VerifyObjTilesAgainstRom(rom, addr, inPath);
                Assert.False(result.Ok);
                Assert.Equal(DecompAssetStatus.NotData, result.Status);
                Assert.Contains("offset 5", result.Message);
            }
            finally { Directory.Delete(dir, true); }
        }

        // ============================================================
        // Map chipset TSA/config section (#1375) — structural TWIN of OBJ tileset
        // ============================================================

        static void WriteMapChipConfigPlusSidecar(string path, byte[] body, string format = "febuilder-mapchipconfig-lz77")
        {
            File.WriteAllBytes(path, body);
            File.WriteAllText(path + ".json",
                $"{{\n  \"length\": {body.Length},\n  \"srcAddr\": \"0x200\",\n  \"format\": \"{format}\"\n}}\n");
        }

        // ---- ExportMapChipConfig ----

        [Fact]
        public void ExportMapChipConfig_WritesDecompressedBody_AndSidecar()
        {
            string dir = NewTempDir();
            try
            {
                uint addr = 0x200;
                var rom = MakeRomWithLz77(addr, 128, out byte[] raw);
                string outPath = Path.Combine(dir, "chip.mapchipconfig");

                var result = DecompAssetExportCore.ExportMapChipConfig(rom, addr, outPath);
                Assert.True(result.Ok, $"ExportMapChipConfig failed: {result.Message}");
                Assert.True(File.Exists(outPath));

                byte[] body = File.ReadAllBytes(outPath);
                Assert.Equal(raw, body);

                string jsonPath = outPath + ".json";
                Assert.True(File.Exists(jsonPath));
                string json = File.ReadAllText(jsonPath);
                Assert.Contains($"\"length\": {raw.Length}", json);
                Assert.Contains($"\"srcAddr\": \"0x{addr:X}\"", json);
                Assert.Contains("\"format\": \"febuilder-mapchipconfig-lz77\"", json);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void ExportMapChipConfig_NonLz77Addr_ReturnsNotData_NoFile()
        {
            string dir = NewTempDir();
            try
            {
                var rom = new ROM();
                rom.SwapNewROMDataDirect(new byte[0x400]); // zeros — not a valid LZ77 stream
                string outPath = Path.Combine(dir, "chip.mapchipconfig");

                var result = DecompAssetExportCore.ExportMapChipConfig(rom, 0x200, outPath);
                Assert.False(result.Ok);
                Assert.Equal(DecompAssetStatus.NotData, result.Status);
                Assert.False(File.Exists(outPath));
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void ExportMapChipConfig_OverBounds_ReturnsNotData()
        {
            string dir = NewTempDir();
            try
            {
                // Valid LZ77 header but truncated stream → getCompressedSize returns 0.
                var rom = new ROM();
                byte[] romData = new byte[0x210];
                romData[0x200] = 0x10;
                romData[0x201] = 0x00;
                romData[0x202] = 0x04;
                romData[0x203] = 0x00; // 0x400 uncompressed
                rom.SwapNewROMDataDirect(romData);
                string outPath = Path.Combine(dir, "chip.mapchipconfig");

                var result = DecompAssetExportCore.ExportMapChipConfig(rom, 0x200, outPath);
                Assert.False(result.Ok);
                Assert.False(File.Exists(outPath));
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void ExportMapChipConfig_NullRom_ReturnsBadArgs()
        {
            var result = DecompAssetExportCore.ExportMapChipConfig(null, 0x200, "/tmp/chip.mapchipconfig");
            Assert.Equal(DecompAssetStatus.BadArgs, result.Status);
        }

        [Fact]
        public void ExportMapChipConfig_AddrInLastBytes_ReturnsNotData_NoThrow()
        {
            // Boundary hazard (#1375, same as objtiles #1371): an addr within the last 1-3 ROM
            // bytes must surface as a clean NotData (the 4-byte LZ77 header is out of bounds),
            // NOT a Faulted IndexOutOfRangeException from getCompressedSize reading input[offset+3].
            string dir = NewTempDir();
            try
            {
                var rom = new ROM();
                rom.SwapNewROMDataDirect(new byte[0x400]);
                string outPath = Path.Combine(dir, "chip.mapchipconfig");

                var result = DecompAssetExportCore.ExportMapChipConfig(rom, 0x3FE, outPath);
                Assert.False(result.Ok);
                Assert.Equal(DecompAssetStatus.NotData, result.Status);
                Assert.False(File.Exists(outPath));
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void ExportMapChipConfig_ReadOnly_RomLengthAndSha256Invariant()
        {
            // READ-ONLY invariant: export must NOT mutate the ROM (length AND SHA-256 unchanged).
            string dir = NewTempDir();
            try
            {
                uint addr = 0x200;
                var rom = MakeRomWithLz77(addr, 96, out _);
                byte[] before = (byte[])rom.Data.Clone();
                string beforeSha = Sha256Hex(before);

                var result = DecompAssetExportCore.ExportMapChipConfig(rom, addr, Path.Combine(dir, "chip.mapchipconfig"));
                Assert.True(result.Ok, $"export failed: {result.Message}");

                Assert.Equal(before.Length, rom.Data.Length);
                Assert.Equal(beforeSha, Sha256Hex(rom.Data));
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void VerifyMapChipConfigAgainstRom_AddrInLastBytes_ReturnsNotData_NoThrow()
        {
            string dir = NewTempDir();
            try
            {
                var rom = new ROM();
                rom.SwapNewROMDataDirect(new byte[0x400]);
                byte[] body = new byte[32];
                string inPath = Path.Combine(dir, "chip.mapchipconfig");
                WriteMapChipConfigPlusSidecar(inPath, body);

                var result = DecompAssetExportCore.VerifyMapChipConfigAgainstRom(rom, 0x3FE, inPath);
                Assert.False(result.Ok);
                Assert.Equal(DecompAssetStatus.NotData, result.Status);
            }
            finally { Directory.Delete(dir, true); }
        }

        // ---- ImportMapChipConfig ----

        [Fact]
        public void ImportMapChipConfig_IdentityCopy()
        {
            string dir = NewTempDir();
            try
            {
                byte[] body = new byte[64];
                for (int i = 0; i < body.Length; i++) body[i] = (byte)(i * 3);
                string inPath = Path.Combine(dir, "chip.mapchipconfig");
                WriteMapChipConfigPlusSidecar(inPath, body);

                string outPath = Path.Combine(dir, "chip.bin");
                var result = DecompAssetExportCore.ImportMapChipConfig(inPath, outPath);
                Assert.True(result.Ok, $"ImportMapChipConfig failed: {result.Message}");
                Assert.True(File.Exists(outPath));
                Assert.Equal(body, File.ReadAllBytes(outPath));
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void ImportMapChipConfig_DoesNotMutateCoreStateRom()
        {
            string dir = NewTempDir();
            try
            {
                byte[] body = new byte[32];
                string inPath = Path.Combine(dir, "chip.mapchipconfig");
                WriteMapChipConfigPlusSidecar(inPath, body);

                ROM before = CoreState.ROM;
                DecompAssetExportCore.ImportMapChipConfig(inPath, Path.Combine(dir, "chip.bin"));
                Assert.Same(before, CoreState.ROM);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void ImportMapChipConfig_MissingSidecar_Fails()
        {
            string dir = NewTempDir();
            try
            {
                byte[] body = new byte[32];
                string inPath = Path.Combine(dir, "chip.mapchipconfig");
                File.WriteAllBytes(inPath, body); // no sidecar

                var result = DecompAssetExportCore.ImportMapChipConfig(inPath, Path.Combine(dir, "chip.bin"));
                Assert.False(result.Ok);
                Assert.Equal(DecompAssetStatus.NotData, result.Status);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void ImportMapChipConfig_WrongFormatSidecar_Fails()
        {
            string dir = NewTempDir();
            try
            {
                byte[] body = new byte[32];
                string inPath = Path.Combine(dir, "chip.mapchipconfig");
                // A wrong format declaration (e.g. the objtiles format) must be refused.
                WriteMapChipConfigPlusSidecar(inPath, body, format: "febuilder-objtiles-lz77");

                var result = DecompAssetExportCore.ImportMapChipConfig(inPath, Path.Combine(dir, "chip.bin"));
                Assert.False(result.Ok);
                Assert.Equal(DecompAssetStatus.NotData, result.Status);
            }
            finally { Directory.Delete(dir, true); }
        }

        // ---- RoundTripMapChipConfigBody ----

        [Fact]
        public void RoundTripMapChipConfigBody_Matches_True()
        {
            byte[] body = new byte[128];
            Assert.True(DecompAssetExportCore.RoundTripMapChipConfigBody(body, 128));
        }

        [Fact]
        public void RoundTripMapChipConfigBody_WrongLen_False()
        {
            byte[] body = new byte[128];
            Assert.False(DecompAssetExportCore.RoundTripMapChipConfigBody(body, 64));
        }

        [Fact]
        public void RoundTripMapChipConfigBody_Null_False()
        {
            Assert.False(DecompAssetExportCore.RoundTripMapChipConfigBody(null, 64));
        }

        // ---- VerifyMapChipConfigAgainstRom ----

        [Fact]
        public void VerifyMapChipConfigAgainstRom_Match_Ok()
        {
            string dir = NewTempDir();
            try
            {
                uint addr = 0x200;
                var rom = MakeRomWithLz77(addr, 64, out byte[] raw);
                string inPath = Path.Combine(dir, "chip.mapchipconfig");
                WriteMapChipConfigPlusSidecar(inPath, raw);

                var result = DecompAssetExportCore.VerifyMapChipConfigAgainstRom(rom, addr, inPath);
                Assert.True(result.Ok, $"verify failed: {result.Message}");
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void VerifyMapChipConfigAgainstRom_Mismatch_ReturnsNotData()
        {
            string dir = NewTempDir();
            try
            {
                uint addr = 0x200;
                var rom = MakeRomWithLz77(addr, 64, out byte[] raw);
                byte[] tampered = (byte[])raw.Clone();
                tampered[5] ^= 0xFF; // flip byte at offset 5
                string inPath = Path.Combine(dir, "chip.mapchipconfig");
                WriteMapChipConfigPlusSidecar(inPath, tampered);

                var result = DecompAssetExportCore.VerifyMapChipConfigAgainstRom(rom, addr, inPath);
                Assert.False(result.Ok);
                Assert.Equal(DecompAssetStatus.NotData, result.Status);
                Assert.Contains("offset 5", result.Message);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void VerifyMapChipConfigAgainstRom_ReadOnly_RomLengthAndSha256Invariant()
        {
            // The verify path re-decompresses the ROM block READ-ONLY — it must not mutate the ROM.
            string dir = NewTempDir();
            try
            {
                uint addr = 0x200;
                var rom = MakeRomWithLz77(addr, 64, out byte[] raw);
                string inPath = Path.Combine(dir, "chip.mapchipconfig");
                WriteMapChipConfigPlusSidecar(inPath, raw);

                byte[] before = (byte[])rom.Data.Clone();
                string beforeSha = Sha256Hex(before);

                var result = DecompAssetExportCore.VerifyMapChipConfigAgainstRom(rom, addr, inPath);
                Assert.True(result.Ok, $"verify failed: {result.Message}");

                Assert.Equal(before.Length, rom.Data.Length);
                Assert.Equal(beforeSha, Sha256Hex(rom.Data));
            }
            finally { Directory.Delete(dir, true); }
        }

        static string Sha256Hex(byte[] data)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            byte[] hash = sha.ComputeHash(data);
            var sb = new System.Text.StringBuilder(hash.Length * 2);
            foreach (byte b in hash) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        // ============================================================
        // Map tile-animation-1 per-entry RAW 4bpp GRAPHICS section (#1389)
        // Structural TWIN of mapchange/mapanime2pal (RAW, length-sized) —
        // NOT the LZ77 objtiles/mapchipconfig pattern.
        // ============================================================

        static void WriteMapAnime1GfxPlusSidecar(string path, byte[] body, string format = "febuilder-mapanime1gfx-raw4bpp")
        {
            File.WriteAllBytes(path, body);
            File.WriteAllText(path + ".json",
                $"{{\n  \"length\": {body.Length},\n  \"srcAddr\": \"0x200\",\n  \"format\": \"{format}\"\n}}\n");
        }

        // Plant a raw byte block at addr; return the ROM and the planted bytes.
        static ROM MakeRomWithRawBlock(uint addr, int length, out byte[] raw)
        {
            byte[] romData = new byte[0x1000];
            raw = new byte[length];
            for (int i = 0; i < length; i++) raw[i] = (byte)((i * 7 + 3) & 0xFF);
            Array.Copy(raw, 0, romData, addr, length);
            var rom = new ROM();
            rom.SwapNewROMDataDirect(romData);
            return rom;
        }

        // ---- ExportMapAnime1Gfx ----

        [Fact]
        public void ExportMapAnime1Gfx_WritesRawBody_AndSidecar()
        {
            string dir = NewTempDir();
            try
            {
                uint addr = 0x200;
                int length = 128;
                var rom = MakeRomWithRawBlock(addr, length, out byte[] raw);
                string outPath = Path.Combine(dir, "gfx.mapanime1gfx");

                var result = DecompAssetExportCore.ExportMapAnime1Gfx(rom, addr, length, outPath);
                Assert.True(result.Ok, $"ExportMapAnime1Gfx failed: {result.Message}");
                Assert.True(File.Exists(outPath));

                byte[] body = File.ReadAllBytes(outPath);
                Assert.Equal(raw, body);

                string jsonPath = outPath + ".json";
                Assert.True(File.Exists(jsonPath));
                string json = File.ReadAllText(jsonPath);
                Assert.Contains($"\"length\": {length}", json);
                Assert.Contains($"\"srcAddr\": \"0x{addr:X}\"", json);
                Assert.Contains("\"format\": \"febuilder-mapanime1gfx-raw4bpp\"", json);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void ExportMapAnime1Gfx_OutOfBounds_ReturnsNotData_NoFileWritten()
        {
            string dir = NewTempDir();
            try
            {
                var rom = new ROM();
                rom.SwapNewROMDataDirect(new byte[0x210]); // tiny
                string outPath = Path.Combine(dir, "gfx.mapanime1gfx");

                var result = DecompAssetExportCore.ExportMapAnime1Gfx(rom, 0x200, 0x100, outPath); // runs past end
                Assert.False(result.Ok);
                Assert.Equal(DecompAssetStatus.NotData, result.Status);
                Assert.False(File.Exists(outPath));
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void ExportMapAnime1Gfx_LengthBelow1_ReturnsNotData_NoFileWritten()
        {
            string dir = NewTempDir();
            try
            {
                var rom = MakeRomWithRawBlock(0x200, 64, out _);
                string outPath = Path.Combine(dir, "gfx.mapanime1gfx");

                var result = DecompAssetExportCore.ExportMapAnime1Gfx(rom, 0x200, 0, outPath); // length < 1
                Assert.False(result.Ok);
                Assert.Equal(DecompAssetStatus.NotData, result.Status);
                Assert.False(File.Exists(outPath));
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void ExportMapAnime1Gfx_LengthOver65535_ReturnsNotData_NoFileWritten()
        {
            string dir = NewTempDir();
            try
            {
                var rom = new ROM();
                rom.SwapNewROMDataDirect(new byte[0x20000]);
                string outPath = Path.Combine(dir, "gfx.mapanime1gfx");

                var result = DecompAssetExportCore.ExportMapAnime1Gfx(rom, 0x200, 0x10000, outPath); // length > 65535
                Assert.False(result.Ok);
                Assert.Equal(DecompAssetStatus.NotData, result.Status);
                Assert.False(File.Exists(outPath));
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void ExportMapAnime1Gfx_NullRom_ReturnsBadArgs()
        {
            var result = DecompAssetExportCore.ExportMapAnime1Gfx(null, 0x200, 64, "/tmp/x.mapanime1gfx");
            Assert.Equal(DecompAssetStatus.BadArgs, result.Status);
        }

        [Fact]
        public void ExportMapAnime1Gfx_DoesNotMutateRom_ShaInvariant()
        {
            string dir = NewTempDir();
            try
            {
                uint addr = 0x200;
                var rom = MakeRomWithRawBlock(addr, 64, out _);
                byte[] before = (byte[])rom.Data.Clone();
                string beforeSha = Sha256Hex(before);

                var result = DecompAssetExportCore.ExportMapAnime1Gfx(rom, addr, 64, Path.Combine(dir, "gfx.mapanime1gfx"));
                Assert.True(result.Ok);

                Assert.Equal(before.Length, rom.Data.Length);
                Assert.Equal(beforeSha, Sha256Hex(rom.Data));
            }
            finally { Directory.Delete(dir, true); }
        }

        // ---- ImportMapAnime1Gfx ----

        [Fact]
        public void ImportMapAnime1Gfx_IdentityCopy()
        {
            string dir = NewTempDir();
            try
            {
                byte[] body = new byte[64];
                for (int i = 0; i < body.Length; i++) body[i] = (byte)(i * 3);
                string inPath = Path.Combine(dir, "gfx.mapanime1gfx");
                WriteMapAnime1GfxPlusSidecar(inPath, body);

                string outPath = Path.Combine(dir, "gfx.bin");
                var result = DecompAssetExportCore.ImportMapAnime1Gfx(inPath, outPath);
                Assert.True(result.Ok, $"ImportMapAnime1Gfx failed: {result.Message}");
                Assert.True(File.Exists(outPath));
                Assert.Equal(body, File.ReadAllBytes(outPath));
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void ImportMapAnime1Gfx_DoesNotMutateCoreStateRom()
        {
            string dir = NewTempDir();
            try
            {
                byte[] body = new byte[32];
                string inPath = Path.Combine(dir, "gfx.mapanime1gfx");
                WriteMapAnime1GfxPlusSidecar(inPath, body);

                ROM before = CoreState.ROM;
                DecompAssetExportCore.ImportMapAnime1Gfx(inPath, Path.Combine(dir, "gfx.bin"));
                Assert.Same(before, CoreState.ROM);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void ImportMapAnime1Gfx_MissingSidecar_Fails()
        {
            string dir = NewTempDir();
            try
            {
                byte[] body = new byte[32];
                string inPath = Path.Combine(dir, "gfx.mapanime1gfx");
                File.WriteAllBytes(inPath, body); // no sidecar

                var result = DecompAssetExportCore.ImportMapAnime1Gfx(inPath, Path.Combine(dir, "gfx.bin"));
                Assert.False(result.Ok);
                Assert.Equal(DecompAssetStatus.NotData, result.Status);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void ImportMapAnime1Gfx_BadFormat_Fails()
        {
            string dir = NewTempDir();
            try
            {
                byte[] body = new byte[32];
                string inPath = Path.Combine(dir, "gfx.mapanime1gfx");
                WriteMapAnime1GfxPlusSidecar(inPath, body, format: "febuilder-objtiles-lz77"); // wrong format

                var result = DecompAssetExportCore.ImportMapAnime1Gfx(inPath, Path.Combine(dir, "gfx.bin"));
                Assert.False(result.Ok);
                Assert.Equal(DecompAssetStatus.NotData, result.Status);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void ImportMapAnime1Gfx_LengthMismatch_Fails()
        {
            string dir = NewTempDir();
            try
            {
                byte[] body = new byte[32];
                string inPath = Path.Combine(dir, "gfx.mapanime1gfx");
                File.WriteAllBytes(inPath, body);
                // sidecar declares 64 but body is 32 → validator rejects
                File.WriteAllText(inPath + ".json",
                    "{\n  \"length\": 64,\n  \"srcAddr\": \"0x200\",\n  \"format\": \"febuilder-mapanime1gfx-raw4bpp\"\n}\n");

                var result = DecompAssetExportCore.ImportMapAnime1Gfx(inPath, Path.Combine(dir, "gfx.bin"));
                Assert.False(result.Ok);
                Assert.Equal(DecompAssetStatus.NotData, result.Status);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void ImportMapAnime1Gfx_NullArgs_ReturnBadArgs()
        {
            var r1 = DecompAssetExportCore.ImportMapAnime1Gfx(null, "/tmp/x.bin");
            Assert.Equal(DecompAssetStatus.BadArgs, r1.Status);
            var r2 = DecompAssetExportCore.ImportMapAnime1Gfx("/tmp/x.mapanime1gfx", null);
            Assert.Equal(DecompAssetStatus.BadArgs, r2.Status);
        }

        // ---- RoundTripMapAnime1GfxBody ----

        [Fact]
        public void RoundTripMapAnime1GfxBody_Matches_True()
        {
            byte[] body = new byte[128];
            Assert.True(DecompAssetExportCore.RoundTripMapAnime1GfxBody(body, 128));
        }

        [Fact]
        public void RoundTripMapAnime1GfxBody_WrongLen_False()
        {
            byte[] body = new byte[128];
            Assert.False(DecompAssetExportCore.RoundTripMapAnime1GfxBody(body, 64));
        }

        [Fact]
        public void RoundTripMapAnime1GfxBody_Null_False()
        {
            Assert.False(DecompAssetExportCore.RoundTripMapAnime1GfxBody(null, 64));
        }

        // ---- VerifyMapAnime1GfxAgainstRom ----

        [Fact]
        public void VerifyMapAnime1GfxAgainstRom_Match_Ok()
        {
            string dir = NewTempDir();
            try
            {
                uint addr = 0x200;
                int length = 64;
                var rom = MakeRomWithRawBlock(addr, length, out byte[] raw);
                string inPath = Path.Combine(dir, "gfx.mapanime1gfx");
                WriteMapAnime1GfxPlusSidecar(inPath, raw);

                var result = DecompAssetExportCore.VerifyMapAnime1GfxAgainstRom(rom, addr, length, inPath);
                Assert.True(result.Ok, $"verify failed: {result.Message}");
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void VerifyMapAnime1GfxAgainstRom_Mismatch_ReturnsNotData()
        {
            string dir = NewTempDir();
            try
            {
                uint addr = 0x200;
                int length = 64;
                var rom = MakeRomWithRawBlock(addr, length, out byte[] raw);
                byte[] tampered = (byte[])raw.Clone();
                tampered[5] ^= 0xFF; // flip byte at offset 5
                string inPath = Path.Combine(dir, "gfx.mapanime1gfx");
                WriteMapAnime1GfxPlusSidecar(inPath, tampered);

                var result = DecompAssetExportCore.VerifyMapAnime1GfxAgainstRom(rom, addr, length, inPath);
                Assert.False(result.Ok);
                Assert.Equal(DecompAssetStatus.NotData, result.Status);
                Assert.Contains("offset 5", result.Message);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void VerifyMapAnime1GfxAgainstRom_ReadOnly_ShaInvariant()
        {
            string dir = NewTempDir();
            try
            {
                uint addr = 0x200;
                int length = 64;
                var rom = MakeRomWithRawBlock(addr, length, out byte[] raw);
                string inPath = Path.Combine(dir, "gfx.mapanime1gfx");
                WriteMapAnime1GfxPlusSidecar(inPath, raw);

                byte[] before = (byte[])rom.Data.Clone();
                string beforeSha = Sha256Hex(before);

                var result = DecompAssetExportCore.VerifyMapAnime1GfxAgainstRom(rom, addr, length, inPath);
                Assert.True(result.Ok, $"verify failed: {result.Message}");

                Assert.Equal(before.Length, rom.Data.Length);
                Assert.Equal(beforeSha, Sha256Hex(rom.Data));
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void VerifyMapAnime1GfxAgainstRom_OutOfBounds_ReturnsNotData()
        {
            string dir = NewTempDir();
            try
            {
                var rom = new ROM();
                rom.SwapNewROMDataDirect(new byte[0x210]);
                byte[] body = new byte[0x100];
                string inPath = Path.Combine(dir, "gfx.mapanime1gfx");
                WriteMapAnime1GfxPlusSidecar(inPath, body);

                var result = DecompAssetExportCore.VerifyMapAnime1GfxAgainstRom(rom, 0x200, 0x100, inPath); // runs past end
                Assert.False(result.Ok);
                Assert.Equal(DecompAssetStatus.NotData, result.Status);
            }
            finally { Directory.Delete(dir, true); }
        }

        // ---- MapTileAnimation1Core +4 pointer / +2 length proof (#1389) ----

        [Fact]
        public void MapTileAnimation1Core_Entry_GraphicsPointerAtPlus4_LengthAtPlus2()
        {
            // Prove the dereferenced --addr contract: an anime-1 entry's graphics pointer is at +4
            // (the inverse of anime-2's +0) and its raw byte length is the +2 u16 field. This is the
            // exact (addr,length) pair the mapanime1gfx exporter consumes.
            byte[] romData = new byte[0x1000];
            uint baseAddr = 0x200;
            uint gfxAddr = 0x800;
            ushort wait = 0x000A;
            ushort length = 0x0080;
            // entry row: wait@+0, length@+2, imagePointer@+4 (GBA pointer 0x08000000 | gfxAddr)
            romData[baseAddr + 0] = (byte)(wait & 0xFF);
            romData[baseAddr + 1] = (byte)(wait >> 8);
            romData[baseAddr + 2] = (byte)(length & 0xFF);
            romData[baseAddr + 3] = (byte)(length >> 8);
            uint gbaPtr = 0x08000000 | gfxAddr;
            romData[baseAddr + 4] = (byte)(gbaPtr & 0xFF);
            romData[baseAddr + 5] = (byte)((gbaPtr >> 8) & 0xFF);
            romData[baseAddr + 6] = (byte)((gbaPtr >> 16) & 0xFF);
            romData[baseAddr + 7] = (byte)((gbaPtr >> 24) & 0xFF);
            // Terminator: next row's +4 is not a pointer (zero).
            var rom = new ROM();
            rom.SwapNewROMDataDirect(romData);

            var rows = MapTileAnimation1Core.ScanEntries(rom, baseAddr);
            Assert.Single(rows);
            Assert.Equal(baseAddr, rows[0].Addr);
            Assert.Equal((uint)wait, rows[0].Wait);
            Assert.Equal((uint)length, rows[0].Length);
            // The +4 pointer dereferences to gfxAddr (the exporter's --addr).
            Assert.Equal(gfxAddr, rom.p32(rows[0].Addr + 4));
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
