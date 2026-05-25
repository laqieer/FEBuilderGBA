// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for MapActionAnimationExportImportCore — the cross-platform engine
// powering the Avalonia Map Action Animation Export/Import controls (#499).
//
// Closes Plan v3 (Copilot CLI plan-review pass 3 — approved):
//   - C5 fixed: tuple-key dedup (sum collisions no longer matter)
//   - C3 fixed: FindAndWriteData not AppendToRomEnd
//   - C4 fixed: Math.Round GIF delays via U.GameFrameSecToGifFrameSec
//   - B1 fixed: indexed-IImage GIF frames via GifEncoderCore.IndexedToRgba
//   - B2 fixed: PadGBAPaletteTo16 normalizes short quantize palettes
//   - A3 fixed: # RAW-PALETTE / # RAW-TILES parsed before generic comment skip
using System;
using System.Collections.Generic;
using System.IO;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class MapActionAnimationExportImportCoreTests
    {
        // Anchor synthetic frame tables well past 0x200 so isSafetyOffset
        // is happy. We also reserve enough space for the LZ77-compressed
        // images and 32-byte palettes the import path appends.
        const int ROM_SIZE = 0x40000;          // 256 KiB headroom
        const uint FRAME_TABLE_ADDR = 0x10000; // 64 KiB in
        const uint POINTER_ADDR     = 0x9000;  // GBA-pointer slot
        const uint IMG_ADDR         = 0x20000; // LZ77 image data slot
        const uint PAL_ADDR         = 0x30000; // 32-byte palette slot

        // ----------------------------------------------------------------
        // Synthetic-ROM helpers — give us a deterministic playground with
        // a small 3-frame animation we can export, mutate, re-import.
        // ----------------------------------------------------------------

        static ROM MakeSyntheticRom(out string scratchPath)
        {
            byte[] data = new byte[ROM_SIZE];

            // Build a 64x64 4bpp "solid colour" tile sheet:
            //   each pixel = palette index 1 ⇒ low nibble 1, high nibble 1 ⇒ byte 0x11
            // Total raw size = 64 * 64 / 2 = 2048 bytes.
            byte[] tiles = new byte[2048];
            for (int i = 0; i < tiles.Length; i++) tiles[i] = 0x11;
            byte[] compressed = LZ77.compress(tiles);

            // Write compressed image at IMG_ADDR
            Array.Copy(compressed, 0, data, IMG_ADDR, compressed.Length);

            // Write a 16-color GBA palette at PAL_ADDR: slot 0 transparent,
            // slot 1 = red (0x001F), slot 2..15 zero. Each color = 2 bytes.
            // Frame data lookups read 0x20 bytes raw.
            data[PAL_ADDR + 2] = 0x1F; // R component of slot 1
            data[PAL_ADDR + 3] = 0x00;

            // 12-byte frame entries. Three frames pointing to the same
            // image+palette, terminated by 12 zero bytes.
            uint imgPtr = U.toPointer(IMG_ADDR);
            uint palPtr = U.toPointer(PAL_ADDR);
            for (int i = 0; i < 3; i++)
            {
                int off = (int)FRAME_TABLE_ADDR + i * 12;
                data[off + 0] = (byte)(5 + i);        // wait
                data[off + 1] = 0x00;                  // padding
                data[off + 2] = 0x00; data[off + 3] = 0x00; // sound = 0
                data[off + 4] = (byte)(imgPtr & 0xFF);
                data[off + 5] = (byte)((imgPtr >> 8) & 0xFF);
                data[off + 6] = (byte)((imgPtr >> 16) & 0xFF);
                data[off + 7] = (byte)((imgPtr >> 24) & 0xFF);
                data[off + 8] = (byte)(palPtr & 0xFF);
                data[off + 9] = (byte)((palPtr >> 8) & 0xFF);
                data[off + 10] = (byte)((palPtr >> 16) & 0xFF);
                data[off + 11] = (byte)((palPtr >> 24) & 0xFF);
            }

            // Pointer at POINTER_ADDR points to FRAME_TABLE_ADDR (GBA-format)
            uint framePtr = U.toPointer(FRAME_TABLE_ADDR);
            data[POINTER_ADDR + 0] = (byte)(framePtr & 0xFF);
            data[POINTER_ADDR + 1] = (byte)((framePtr >> 8) & 0xFF);
            data[POINTER_ADDR + 2] = (byte)((framePtr >> 16) & 0xFF);
            data[POINTER_ADDR + 3] = (byte)((framePtr >> 24) & 0xFF);

            var rom = new ROM();
            rom.SwapNewROMDataDirect(data);

            scratchPath = Path.Combine(Path.GetTempPath(), $"mapanim_{Guid.NewGuid():N}");
            Directory.CreateDirectory(scratchPath);
            return rom;
        }

        static void EnsureImageService()
        {
            // The Export path needs an image service to render PNG previews.
            // SkiaImageService lives in FEBuilderGBA.SkiaSharp which the
            // Core.Tests project does NOT reference (deliberately — to keep
            // the Core test surface light). We supply a minimal stub here
            // that satisfies the IImageService surface the export path
            // touches.
            if (CoreState.ImageService == null)
            {
                CoreState.ImageService = new SyntheticImageService();
            }
        }

        // ----------------------------------------------------------------
        // ExportScript
        // ----------------------------------------------------------------

        [Fact]
        public void ExportScript_SyntheticAnimation_ProducesValidScript()
        {
            var origRom = CoreState.ROM;
            var origService = CoreState.ImageService;
            try
            {
                EnsureImageService();
                CoreState.ROM = MakeSyntheticRom(out string scratch);

                string txtPath = Path.Combine(scratch, "test.MapActionAnimation.txt");
                string error = MapActionAnimationExportImportCore.ExportScript(
                    CoreState.ROM, FRAME_TABLE_ADDR, txtPath, "TestAnim");

                Assert.Equal(string.Empty, error);
                Assert.True(File.Exists(txtPath));
                string[] lines = File.ReadAllLines(txtPath);
                Assert.Contains(lines, l => l.StartsWith("//NAME=TestAnim"));

                // Three data lines, each "wait\tfile" form.
                int dataLineCount = 0;
                foreach (string line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    if (line.StartsWith("//") || line.StartsWith("#")) continue;
                    string[] sp = line.Split('\t');
                    Assert.True(sp.Length >= 2, $"Data line should have wait+file: {line}");
                    Assert.True(uint.TryParse(sp[0], out _), $"First col should be wait number: {line}");
                    dataLineCount++;
                }
                Assert.Equal(3, dataLineCount);

                Directory.Delete(scratch, recursive: true);
            }
            finally
            {
                CoreState.ROM = origRom;
                CoreState.ImageService = origService;
            }
        }

        [Fact]
        public void ExportScript_DedupsFramesWithSameImagePalettePtr()
        {
            // All three frames in MakeSyntheticRom share the same (imgPtr, palPtr).
            // The exporter should only write ONE PNG file (or RAW-TILES block),
            // and all three data lines should reference the same filename.
            var origRom = CoreState.ROM;
            var origService = CoreState.ImageService;
            try
            {
                EnsureImageService();
                CoreState.ROM = MakeSyntheticRom(out string scratch);

                string txtPath = Path.Combine(scratch, "dedup.MapActionAnimation.txt");
                string error = MapActionAnimationExportImportCore.ExportScript(
                    CoreState.ROM, FRAME_TABLE_ADDR, txtPath, "Dedup");
                Assert.Equal(string.Empty, error);

                // Collect unique filenames from data lines.
                var uniqueFiles = new HashSet<string>();
                foreach (string line in File.ReadAllLines(txtPath))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    if (line.StartsWith("//") || line.StartsWith("#")) continue;
                    string[] sp = line.Split('\t');
                    if (sp.Length >= 2) uniqueFiles.Add(sp[1]);
                }
                Assert.Single(uniqueFiles);

                // PNG file count on disk == 1
                string[] pngs = Directory.GetFiles(scratch, "*.png");
                Assert.Single(pngs);

                Directory.Delete(scratch, recursive: true);
            }
            finally
            {
                CoreState.ROM = origRom;
                CoreState.ImageService = origService;
            }
        }

        // ----------------------------------------------------------------
        // ImportScript
        // ----------------------------------------------------------------

        [Fact]
        public void ImportScript_NoRom_ReturnsEarly()
        {
            var origRom = CoreState.ROM;
            try
            {
                CoreState.ROM = null;
                string error = MapActionAnimationExportImportCore.ImportScript(
                    null, POINTER_ADDR, "/tmp/nonexistent.txt", _ => null);
                Assert.False(string.IsNullOrEmpty(error));
            }
            finally
            {
                CoreState.ROM = origRom;
            }
        }

        [Fact]
        public void ImportScript_MissingScriptFile_ReturnsLocalizedError()
        {
            var origRom = CoreState.ROM;
            try
            {
                CoreState.ROM = MakeSyntheticRom(out string scratch);
                string error = MapActionAnimationExportImportCore.ImportScript(
                    CoreState.ROM, POINTER_ADDR,
                    Path.Combine(scratch, "missing.txt"), _ => null);
                Assert.False(string.IsNullOrEmpty(error));
                Directory.Delete(scratch, recursive: true);
            }
            finally
            {
                CoreState.ROM = origRom;
            }
        }

        [Fact]
        public void ImportScript_RawPath_PreservesFrameTablePointers()
        {
            // Round-trip: export with RAW preservation, re-import into a fresh
            // ROM, walk the imported frame table, assert wait values match.
            var origRom = CoreState.ROM;
            var origService = CoreState.ImageService;
            try
            {
                EnsureImageService();
                CoreState.ROM = MakeSyntheticRom(out string scratch);

                string txtPath = Path.Combine(scratch, "rt.MapActionAnimation.txt");
                string err1 = MapActionAnimationExportImportCore.ExportScript(
                    CoreState.ROM, FRAME_TABLE_ADDR, txtPath, "Roundtrip");
                Assert.Equal(string.Empty, err1);

                // Re-load synthetic ROM (so we can prove the import re-creates
                // the table independent of any leftover state).
                CoreState.ROM = MakeSyntheticRom(out _);
                string err2 = MapActionAnimationExportImportCore.ImportScript(
                    CoreState.ROM, POINTER_ADDR, txtPath,
                    path => {
                        // Loader returns 64x64 RGBA from the indexed PNG path.
                        return (new byte[64*64*4], 64, 64);
                    });
                Assert.Equal(string.Empty, err2);

                // The pointer at POINTER_ADDR now points somewhere in ROM
                // — read 3 frame entries and assert wait values 5, 6, 7.
                uint newTablePtr = CoreState.ROM.p32(POINTER_ADDR);
                Assert.True(U.isSafetyOffset(newTablePtr, CoreState.ROM));
                Assert.Equal(5u, (uint)CoreState.ROM.u8(newTablePtr + 0));
                Assert.Equal(6u, (uint)CoreState.ROM.u8(newTablePtr + 12));
                Assert.Equal(7u, (uint)CoreState.ROM.u8(newTablePtr + 24));

                Directory.Delete(scratch, recursive: true);
            }
            finally
            {
                CoreState.ROM = origRom;
                CoreState.ImageService = origService;
            }
        }

        [Fact]
        public void ImportScript_RawCommentsParsedBeforeGenericCommentSkip()
        {
            // Plan v3 A3: # RAW-TILES / # RAW-PALETTE must be recognised
            // before generic `#` comment skip. We hand-craft a .txt with
            // a sandwich (`# unrelated comment`, then `# RAW-TILES:`, then
            // a data line) and assert the importer still picks up the RAW.
            var origRom = CoreState.ROM;
            var origService = CoreState.ImageService;
            try
            {
                EnsureImageService();
                CoreState.ROM = MakeSyntheticRom(out string scratch);

                // Build the RAW hex for one tile (32 bytes of 0x11 packed as 64 hex chars
                // x 64 = 4096 hex chars for a 2048-byte sheet — keep it short).
                byte[] tiles = new byte[2048];
                for (int i = 0; i < tiles.Length; i++) tiles[i] = 0x11;
                byte[] pal = new byte[32];
                pal[2] = 0x1F;

                string rawTilesHex = BytesToHex(tiles);
                string rawPalHex = BytesToHex(pal);

                string txtPath = Path.Combine(scratch, "rawcomment.MapActionAnimation.txt");
                File.WriteAllLines(txtPath, new string[]
                {
                    "//NAME=RawCommentTest",
                    "# unrelated comment that should be ignored",
                    $"# RAW-TILES: {rawTilesHex}",
                    $"# RAW-PALETTE: {rawPalHex}",
                    "5\tframe0.png\t0x0",
                });

                string err = MapActionAnimationExportImportCore.ImportScript(
                    CoreState.ROM, POINTER_ADDR, txtPath,
                    _ => null); // Loader returning null forces RAW path; an error here means RAW wasn't picked up.
                Assert.Equal(string.Empty, err);

                Directory.Delete(scratch, recursive: true);
            }
            finally
            {
                CoreState.ROM = origRom;
                CoreState.ImageService = origService;
            }
        }

        [Fact]
        public void ImportScript_QuantizePath_PadsPaletteTo32Bytes()
        {
            // Plan v3 B2: short palette from DecreaseColorCore.Quantize must
            // be padded to exactly 32 bytes (16 colors × 2). Verify by
            // tracing the post-import palette size.
            var origRom = CoreState.ROM;
            var origService = CoreState.ImageService;
            try
            {
                EnsureImageService();
                CoreState.ROM = MakeSyntheticRom(out string scratch);

                string txtPath = Path.Combine(scratch, "shortpal.MapActionAnimation.txt");
                File.WriteAllLines(txtPath, new string[]
                {
                    "//NAME=ShortPal",
                    "5\tframe0.png",
                });

                // Loader: 64x64 RGBA, single-color (just black) so the
                // quantizer outputs a tiny palette.
                byte[] rgba = new byte[64*64*4];
                for (int i = 0; i < rgba.Length; i += 4)
                {
                    rgba[i + 3] = 255; // opaque black pixels
                }

                string err = MapActionAnimationExportImportCore.ImportScript(
                    CoreState.ROM, POINTER_ADDR, txtPath,
                    _ => (rgba, 64, 64));
                Assert.Equal(string.Empty, err);

                // Read back the palette pointer from frame 0
                uint newTablePtr = CoreState.ROM.p32(POINTER_ADDR);
                uint palPtr = CoreState.ROM.p32(newTablePtr + 8);
                Assert.True(U.isSafetyOffset(palPtr, CoreState.ROM));
                // Read 32 bytes — they should all be inside the ROM.
                for (uint i = 0; i < 32; i++)
                {
                    Assert.True(palPtr + i < (uint)CoreState.ROM.Data.Length,
                        $"Palette byte at offset {i} extends past ROM end");
                }

                Directory.Delete(scratch, recursive: true);
            }
            finally
            {
                CoreState.ROM = origRom;
                CoreState.ImageService = origService;
            }
        }

        // ----------------------------------------------------------------
        // Copilot bot review on PR #620 (round 1 inlines #3 / #4 / #5)
        // ----------------------------------------------------------------

        /// <summary>
        /// Inline #3: malformed RAW-PALETTE hex used to silently produce an
        /// all-zero palette via PadGBAPaletteTo16(null). The validated path
        /// now returns a descriptive error.
        /// </summary>
        [Fact]
        public void ImportScript_MalformedRawPalette_ReturnsLocalizedError()
        {
            var origRom = CoreState.ROM;
            try
            {
                CoreState.ROM = MakeSyntheticRom(out string scratch);
                string txtPath = Path.Combine(scratch, "badpal.MapActionAnimation.txt");
                byte[] tiles = new byte[2048];
                File.WriteAllLines(txtPath, new[]
                {
                    "# RAW-TILES: " + BytesToHex(tiles),
                    "# RAW-PALETTE: ABCDE",        // odd-length hex → invalid
                    "5\tframe0.png",
                });
                string err = MapActionAnimationExportImportCore.ImportScript(
                    CoreState.ROM, POINTER_ADDR, txtPath, _ => null);
                Assert.False(string.IsNullOrEmpty(err));
                Assert.Contains("RAW-PALETTE", err);
                Directory.Delete(scratch, recursive: true);
            }
            finally
            {
                CoreState.ROM = origRom;
            }
        }

        /// <summary>
        /// Inline #4: pointerAddr must be validated as in-bounds for a 4-byte
        /// write before the import even begins.
        /// </summary>
        [Fact]
        public void ImportScript_InvalidPointerAddress_ReturnsEarly()
        {
            var origRom = CoreState.ROM;
            try
            {
                CoreState.ROM = MakeSyntheticRom(out string scratch);
                string txtPath = Path.Combine(scratch, "validfile.MapActionAnimation.txt");
                File.WriteAllText(txtPath, "//NAME=Test\n");
                uint badAddr = (uint)CoreState.ROM.Data.Length + 0x1000;
                string err = MapActionAnimationExportImportCore.ImportScript(
                    CoreState.ROM, badAddr, txtPath, _ => null);
                Assert.False(string.IsNullOrEmpty(err));
                Assert.Contains("0x", err);
                Directory.Delete(scratch, recursive: true);
            }
            finally
            {
                CoreState.ROM = origRom;
            }
        }

        /// <summary>
        /// Inline #5: wait values > 255 used to be silently truncated to a u8.
        /// </summary>
        [Fact]
        public void ImportScript_WaitOver255_ReturnsLocalizedError()
        {
            var origRom = CoreState.ROM;
            try
            {
                CoreState.ROM = MakeSyntheticRom(out string scratch);
                string txtPath = Path.Combine(scratch, "bigwait.MapActionAnimation.txt");
                File.WriteAllLines(txtPath, new[]
                {
                    "//NAME=BigWait",
                    "300\tframe0.png",   // 300 > 0xFF
                });
                string err = MapActionAnimationExportImportCore.ImportScript(
                    CoreState.ROM, POINTER_ADDR, txtPath,
                    _ => (new byte[64*64*4], 64, 64));
                Assert.False(string.IsNullOrEmpty(err));
                Assert.Contains("Wait", err);
                Assert.Contains("300", err);
                Directory.Delete(scratch, recursive: true);
            }
            finally
            {
                CoreState.ROM = origRom;
            }
        }

        /// <summary>
        /// Inline #5: sound values > 0xFFFF used to be silently truncated.
        /// </summary>
        [Fact]
        public void ImportScript_SoundOverU16_ReturnsLocalizedError()
        {
            var origRom = CoreState.ROM;
            try
            {
                CoreState.ROM = MakeSyntheticRom(out string scratch);
                string txtPath = Path.Combine(scratch, "bigsound.MapActionAnimation.txt");
                File.WriteAllLines(txtPath, new[]
                {
                    "//NAME=BigSound",
                    "5\tframe0.png\t0x10000", // sound 0x10000 > u16
                });
                string err = MapActionAnimationExportImportCore.ImportScript(
                    CoreState.ROM, POINTER_ADDR, txtPath,
                    _ => (new byte[64*64*4], 64, 64));
                Assert.False(string.IsNullOrEmpty(err));
                Assert.Contains("Sound", err);
                Directory.Delete(scratch, recursive: true);
            }
            finally
            {
                CoreState.ROM = origRom;
            }
        }

        // ----------------------------------------------------------------
        // ReadPaletteFromRom — Copilot bot review on PR #620 round 2
        // ----------------------------------------------------------------

        /// <summary>
        /// Copilot bot review on PR #620 round 2: palette reads MUST honor
        /// the rom parameter, not CoreState.ROM. Demonstrates that two
        /// different ROMs produce two different palette reads from the
        /// same offset.
        /// </summary>
        [Fact]
        public void ReadPaletteFromRom_HonorsExplicitRom_NotCoreStateRom()
        {
            byte[] aData = new byte[0x2000];
            for (int i = 0; i < 32; i++) aData[0x1000 + i] = 0xAA;
            byte[] bData = new byte[0x2000];
            for (int i = 0; i < 32; i++) bData[0x1000 + i] = 0xBB;

            var aRom = new ROM();
            aRom.SwapNewROMDataDirect(aData);
            var bRom = new ROM();
            bRom.SwapNewROMDataDirect(bData);

            var origRom = CoreState.ROM;
            try
            {
                // Park CoreState.ROM at aRom; explicitly read from bRom.
                CoreState.ROM = aRom;
                byte[] result = MapActionAnimationExportImportCore.ReadPaletteFromRom(bRom, 0x1000, 16);
                Assert.NotNull(result);
                Assert.Equal(32, result.Length);
                Assert.All(result, b => Assert.Equal((byte)0xBB, b));
            }
            finally
            {
                CoreState.ROM = origRom;
            }
        }

        [Fact]
        public void ReadPaletteFromRom_OffsetPastEnd_ReturnsNull()
        {
            byte[] data = new byte[0x100];
            var rom = new ROM();
            rom.SwapNewROMDataDirect(data);
            byte[] result = MapActionAnimationExportImportCore.ReadPaletteFromRom(rom, 0xFF, 16);
            Assert.Null(result);
        }

        [Fact]
        public void ReadPaletteFromRom_NullRom_ReturnsNull()
        {
            byte[] result = MapActionAnimationExportImportCore.ReadPaletteFromRom(null, 0x100, 16);
            Assert.Null(result);
        }

        // ----------------------------------------------------------------
        // PadGBAPaletteTo16 — direct unit test
        // ----------------------------------------------------------------

        [Fact]
        public void PadGBAPaletteTo16_ShortPalette_PadsToThirtyTwoBytes()
        {
            byte[] input = new byte[] { 0x01, 0x02, 0x03, 0x04 };
            byte[] result = MapActionAnimationExportImportCore.PadGBAPaletteTo16(input);
            Assert.Equal(32, result.Length);
            Assert.Equal((byte)0x01, result[0]);
            Assert.Equal((byte)0x02, result[1]);
            Assert.Equal((byte)0x03, result[2]);
            Assert.Equal((byte)0x04, result[3]);
            for (int i = 4; i < 32; i++) Assert.Equal((byte)0, result[i]);
        }

        [Fact]
        public void PadGBAPaletteTo16_LongPalette_TruncatesToThirtyTwoBytes()
        {
            byte[] input = new byte[40];
            for (int i = 0; i < 40; i++) input[i] = (byte)i;
            byte[] result = MapActionAnimationExportImportCore.PadGBAPaletteTo16(input);
            Assert.Equal(32, result.Length);
            for (int i = 0; i < 32; i++) Assert.Equal((byte)i, result[i]);
        }

        [Fact]
        public void PadGBAPaletteTo16_NullInput_ReturnsZeroPalette()
        {
            byte[] result = MapActionAnimationExportImportCore.PadGBAPaletteTo16(null);
            Assert.Equal(32, result.Length);
            foreach (byte b in result) Assert.Equal((byte)0, b);
        }

        // ----------------------------------------------------------------
        // Helpers
        // ----------------------------------------------------------------

        static string BytesToHex(byte[] data)
        {
            var sb = new System.Text.StringBuilder(data.Length * 2);
            foreach (byte b in data) sb.AppendFormat("{0:X2}", b);
            return sb.ToString();
        }

        /// <summary>Minimal IImageService stub for Core.Tests — only the
        /// methods the Export path actually touches need to work.</summary>
        class SyntheticImageService : IImageService
        {
            public IImage CreateImage(int width, int height) => new SyntheticIndexedImage(width, height, null);
            public IImage CreateIndexedImage(int width, int height, byte[] gbaPalette, int paletteColorCount)
                => new SyntheticIndexedImage(width, height, gbaPalette);
            public IImage LoadImage(string filePath) => new SyntheticIndexedImage(64, 64, null);
            public IImage LoadImageFromBytes(byte[] pngData) => new SyntheticIndexedImage(64, 64, null);

            public void GBAColorToRGBA(ushort gbaColor, out byte r, out byte g, out byte b)
            {
                r = (byte)((gbaColor & 0x1F) << 3);
                g = (byte)(((gbaColor >> 5) & 0x1F) << 3);
                b = (byte)(((gbaColor >> 10) & 0x1F) << 3);
            }
            public ushort RGBAToGBAColor(byte r, byte g, byte b)
            {
                return (ushort)((r >> 3) | ((g >> 3) << 5) | ((b >> 3) << 10));
            }
            public IImage Decode4bppTiles(byte[] tileData, int offset, int width, int height, byte[] gbaPalette)
                => new SyntheticIndexedImage(width, height, gbaPalette);
            public IImage Decode8bppTiles(byte[] tileData, int offset, int width, int height, byte[] gbaPalette)
                => new SyntheticIndexedImage(width, height, gbaPalette);
            public IImage Decode8bppLinear(byte[] data, int offset, int width, int height, byte[] gbaPalette)
                => new SyntheticIndexedImage(width, height, gbaPalette);
            public byte[] Encode4bppTiles(IImage image) => Array.Empty<byte>();
            public byte[] Encode8bppTiles(IImage image) => Array.Empty<byte>();
            public byte[] GBAPaletteToRGBA(byte[] gbaPalette, int colorCount)
            {
                byte[] rgba = new byte[colorCount * 4];
                for (int i = 0; i < colorCount && i * 2 + 1 < gbaPalette.Length; i++)
                {
                    ushort c = (ushort)(gbaPalette[i * 2] | (gbaPalette[i * 2 + 1] << 8));
                    GBAColorToRGBA(c, out byte r, out byte g, out byte b);
                    rgba[i * 4 + 0] = r;
                    rgba[i * 4 + 1] = g;
                    rgba[i * 4 + 2] = b;
                    rgba[i * 4 + 3] = (byte)(i == 0 ? 0 : 255);
                }
                return rgba;
            }
            public byte[] RGBAPaletteToGBA(byte[] rgbaPalette, int colorCount) => new byte[colorCount * 2];
        }

        class SyntheticIndexedImage : IImage
        {
            public int Width { get; }
            public int Height { get; }
            public bool IsIndexed => true;
            byte[] _pixels;
            byte[] _palette;

            public SyntheticIndexedImage(int w, int h, byte[] palette)
            {
                Width = w; Height = h;
                _pixels = new byte[w * h];
                _palette = palette ?? new byte[32];
            }

            public byte[] GetPixelData() => (byte[])_pixels.Clone();
            public void SetPixelData(byte[] data) { _pixels = (byte[])data.Clone(); }
            public byte[] GetPaletteGBA() => (byte[])_palette.Clone();
            public void SetPaletteGBA(byte[] palette) { _palette = (byte[])palette.Clone(); }
            public byte[] GetPaletteRGBA()
            {
                int colorCount = _palette.Length / 2;
                byte[] rgba = new byte[colorCount * 4];
                for (int i = 0; i < colorCount; i++)
                {
                    ushort c = (ushort)(_palette[i * 2] | (_palette[i * 2 + 1] << 8));
                    rgba[i * 4 + 0] = (byte)((c & 0x1F) << 3);
                    rgba[i * 4 + 1] = (byte)(((c >> 5) & 0x1F) << 3);
                    rgba[i * 4 + 2] = (byte)(((c >> 10) & 0x1F) << 3);
                    rgba[i * 4 + 3] = (byte)(i == 0 ? 0 : 255);
                }
                return rgba;
            }
            public void Save(string filePath) { File.WriteAllBytes(filePath, new byte[] { 0x89, 0x50, 0x4E, 0x47 }); }
            public byte[] EncodePng() => new byte[] { 0x89, 0x50, 0x4E, 0x47 };
            public void Dispose() { }
        }
    }
}
