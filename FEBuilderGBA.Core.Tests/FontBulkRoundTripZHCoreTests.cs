// SPDX-License-Identifier: GPL-3.0-or-later
// #1268 Core tests for FontBulkExportZHCore / FontBulkImportZHCore — the Chinese
// (ZH) `.fontall.txt` bulk export + atomic bulk import (ports of WF FontZHForm
// ExportALL / bulk re-import). Uses an in-memory render/load pair of delegates so
// the round-trip runs without the filesystem (and without the Avalonia layer).
//
// The synthetic ROM is the same multibyte FE8J ROM as FontGlyphZHCoreTests: a
// 44-byte glyph struct planted at topaddress + CalcCodeB(moji). The bulk export
// shifts the 16x13 glyph into a 16x16 PNG (the WF ConvertVanillaFontSizeBitmap
// geometry); the bulk import un-shifts it back, so render -> shift -> unshift ->
// pack is a clean round-trip when the glyph is full-width (renderWidth == 16).
using System;
using System.Collections.Generic;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class FontBulkRoundTripZHCoreTests
    {
        const uint ROM_LEN = 0x01000000; // 16 MiB (FE8 ZH serif head 0x578020 in-bounds)
        const uint MOJI_TEN = 0x8181;    // '、' (TBL 8181 -> LE 0x8181), codeB = 0x54

        sealed class ImageServiceScope : IDisposable
        {
            readonly IImageService _prev;
            public ImageServiceScope()
            {
                _prev = CoreState.ImageService;
                CoreState.ImageService = new StubImageService();
            }
            public void Dispose() { CoreState.ImageService = _prev; }
        }

        sealed class RomScope : IDisposable
        {
            readonly ROM _prev;
            public RomScope() { _prev = CoreState.ROM; }
            public void Dispose() { CoreState.ROM = _prev; }
        }

        // The bulk export enumerates glyphs via the ZH TBL (config/translate/zh_tbl/
        // FE8.tbl), so the export/round-trip tests set CoreState.BaseDirectory to the
        // repo root and skip cleanly when it (or the .sln / .tbl) is absent.
        static string? FindRepoRoot()
        {
            string thisAssembly = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string? dir = System.IO.Path.GetDirectoryName(thisAssembly);
            for (int i = 0; i < 10 && dir != null; i++)
            {
                if (System.IO.File.Exists(System.IO.Path.Combine(dir, "FEBuilderGBA.sln")))
                    return dir;
                dir = System.IO.Path.GetDirectoryName(dir);
            }
            return null;
        }

        static bool ZhTblPresent(string repoRoot) =>
            System.IO.File.Exists(System.IO.Path.Combine(repoRoot, "config", "translate", "zh_tbl", "FE8.tbl"));

        sealed class BaseDirScope : IDisposable
        {
            readonly string _prev;
            public BaseDirScope(string dir) { _prev = CoreState.BaseDirectory; CoreState.BaseDirectory = dir; }
            public void Dispose() { CoreState.BaseDirectory = _prev; }
        }

        // Synthetic multibyte FE8J ROM with one full-width serif glyph for '、'.
        static ROM MakeRom(uint width = 16)
        {
            var rom = new ROM();
            byte[] data = new byte[ROM_LEN];
            Array.Fill(data, (byte)0xFF);
            for (uint i = 0; i < 0x600000; i++) data[i] = 0x00;
            rom.LoadLow("synth.gba", data, "BE8J01"); // multibyte FE8J (version 8)

            PlantGlyph(rom, isItem: false, MOJI_TEN, width);
            return rom;
        }

        // Plant a 44-byte ZH glyph struct (header + a deterministic 40-byte bitmap
        // built so render(pack) round-trips at the full 16px width).
        static uint PlantGlyph(ROM rom, bool isItem, uint moji, uint width)
        {
            uint topaddress = FontGlyphZHCore.GetFontPointerZH(rom.RomInfo.version, isItem);
            uint codeB = FontGlyphZHCore.CalcCodeB(moji);
            uint addr = topaddress + codeB;

            U.write_u8(rom.Data, addr + 0, 0xD);
            U.write_u8(rom.Data, addr + 1, width);
            U.write_u8(rom.Data, addr + 2, 0xD);
            U.write_u8(rom.Data, addr + 3, 0);
            // Deterministic 2bpp pattern across all 40 bytes (all 4 indices appear).
            for (uint i = 0; i < 40; i++) rom.Data[addr + 4 + i] = (byte)(0xE4 ^ (i & 0xFF));
            return addr;
        }

        // RGBA -> 0..3 ZH palette index. The export renders index 0 as TRANSPARENT
        // (alpha 0); the 3 foreground colors are white/gray/black (alpha 255). Map
        // by nearest of those 3 (an exact palette in this synthetic round-trip).
        static byte RgbaToZhIndex(byte r, byte g, byte b, byte a)
        {
            if (a == 0) return 0;            // background
            if (r >= 0xE0) return 1;         // white (0xF8)
            if (r >= 0x80) return 2;         // gray  (0xA8)
            return 3;                        // black (0x28)
        }

        // Convert a 16x16 RGBA buffer to a 16x16 indexed (0..3) buffer.
        static byte[] RgbaToIndexed16x16(byte[] rgba)
        {
            int w = FontGlyphZHCore.GLYPH_W;
            byte[] idx = new byte[w * w];
            for (int i = 0; i < w * w; i++)
            {
                int po = i * 4;
                idx[i] = RgbaToZhIndex(rgba[po + 0], rgba[po + 1], rgba[po + 2], rgba[po + 3]);
            }
            return idx;
        }

        [Fact]
        public void ExportAll_BuildsManifestForPlantedSerifGlyph()
        {
            string? repoRoot = FindRepoRoot();
            if (repoRoot == null || !ZhTblPresent(repoRoot)) return; // skip without the ZH TBL

            using var rs = new RomScope();
            using var svc = new ImageServiceScope();
            using var bd = new BaseDirScope(repoRoot);

            ROM rom = MakeRom(width: 9);
            CoreState.ROM = rom;

            var savedNames = new List<string>();
            string manifest = FontBulkExportZHCore.ExportAll(rom, userFontOnly: false, (img, pngName) =>
            {
                Assert.NotNull(img);
                Assert.Equal(16, img.Width);   // vanilla-size 16x16 canvas
                Assert.Equal(16, img.Height);
                savedNames.Add(pngName);
                return true;
            });

            // The serif glyph for '、' (moji 0x8181) with stored width 9.
            Assert.Contains("\ttext\t9\t", manifest);
            Assert.Contains("text_" + U.ToHexString(MOJI_TEN) + ".png", manifest);
            Assert.Contains("text_" + U.ToHexString(MOJI_TEN) + ".png", savedNames);
        }

        [Fact]
        public void ExportThenImportAll_RoundTripsBytesIdentical()
        {
            string? repoRoot = FindRepoRoot();
            if (repoRoot == null || !ZhTblPresent(repoRoot)) return; // skip without the ZH TBL

            using var rs = new RomScope();
            using var svc = new ImageServiceScope();
            using var bd = new BaseDirScope(repoRoot);

            ROM rom = MakeRom(width: 16); // full width so render(pack) round-trips
            CoreState.ROM = rom;

            uint addr = FontGlyphZHCore.GetFontPointerZH(8, isItemFont: false) + 0x54;
            byte[] originalBitmap = rom.getBinaryData(addr + 4, 40);

            // Export: capture each glyph's rendered 16x16 RGBA keyed by filename.
            var pngs = new Dictionary<string, byte[]>();
            string manifest = FontBulkExportZHCore.ExportAll(rom, userFontOnly: false, (img, pngName) =>
            {
                pngs[pngName] = (byte[])img.GetPixelData().Clone();
                return true;
            });
            Assert.NotEmpty(pngs);

            // Mutate the glyph so the import has to actually rewrite it.
            for (uint i = 0; i < 40; i++) rom.write_u8(addr + 4 + i, 0x00);
            Assert.NotEqual(originalBitmap, rom.getBinaryData(addr + 4, 40));

            // Import: feed back the captured RGBA (RGBA -> indexed 16x16).
            string err = FontBulkImportZHCore.ImportAll(rom, manifest, (pngName, type) =>
            {
                if (!pngs.TryGetValue(pngName, out byte[] rgba)) return null;
                return new FontGlyphZHPixels
                {
                    Indexed = RgbaToIndexed16x16(rgba),
                    Width = 16,
                    Height = 16,
                };
            });
            Assert.Equal("", err);

            // The re-imported bitmap must equal the original planted bytes.
            byte[] reimported = rom.getBinaryData(addr + 4, 40);
            Assert.Equal(originalBitmap, reimported);
            // The stored width (16) is preserved from the manifest.
            Assert.Equal(16u, rom.u8(addr + 1));
        }

        [Fact]
        public void ImportAll_AtomicOnLoaderFailure()
        {
            using var rs = new RomScope();
            using var svc = new ImageServiceScope();

            ROM rom = MakeRom(width: 16);
            CoreState.ROM = rom;
            byte[] snap = (byte[])rom.Data.Clone();

            // A two-row manifest whose loader fails on the 2nd row must restore the
            // ROM byte-identical (the 1st row's write is rolled back too).
            string manifest =
                "、\ttext\t16\ttext_8181.png\n" +
                "。\ttext\t16\ttext_8182.png\n";
            int call = 0;
            byte[] idx16 = new byte[16 * 16];
            for (int i = 0; i < idx16.Length; i++) idx16[i] = 1;

            string err = FontBulkImportZHCore.ImportAll(rom, manifest, (pngName, type) =>
            {
                call++;
                if (call == 2) return null; // simulate a load failure on row 2
                return new FontGlyphZHPixels { Indexed = idx16, Width = 16, Height = 16 };
            });
            Assert.NotEqual("", err);
            Assert.Equal(snap, rom.Data); // byte-identical restore
        }

        [Theory]
        [InlineData("A\tbogus\t9\ttext_8181.png\n")]   // unknown type
        [InlineData("A\ttext\tNaN\ttext_8181.png\n")]  // non-numeric width
        [InlineData("A\ttext\t99\ttext_8181.png\n")]   // out-of-range width
        [InlineData("A\ttext\t9\n")]                    // too few columns
        [InlineData("A\ttext\t9\tnoUnderscore.png\n")] // unkeyable filename
        public void ImportAll_MalformedRow_AbortsAndRestores(string manifest)
        {
            using var rs = new RomScope();
            using var svc = new ImageServiceScope();

            ROM rom = MakeRom(width: 16);
            CoreState.ROM = rom;
            byte[] snap = (byte[])rom.Data.Clone();
            byte[] idx16 = new byte[16 * 16];

            string err = FontBulkImportZHCore.ImportAll(rom, manifest, (n, t) =>
                new FontGlyphZHPixels { Indexed = idx16, Width = 16, Height = 16 });
            Assert.NotEqual("", err);
            Assert.Equal(snap, rom.Data);
        }

        [Fact]
        public void ImportAll_NonZHRom_ReturnsError_NoMutation()
        {
            using var rs = new RomScope();
            using var svc = new ImageServiceScope();

            var rom = new ROM();
            byte[] data = new byte[ROM_LEN];
            Array.Fill(data, (byte)0x00);
            rom.LoadLow("synth.gba", data, "BE8E01"); // FE8U not multibyte
            CoreState.ROM = rom;
            byte[] snap = (byte[])rom.Data.Clone();

            byte[] idx16 = new byte[16 * 16];
            string err = FontBulkImportZHCore.ImportAll(rom, "A\ttext\t9\ttext_8181.png\n",
                (n, t) => new FontGlyphZHPixels { Indexed = idx16, Width = 16, Height = 16 });
            Assert.NotEqual("", err);
            Assert.Equal(snap, rom.Data);
        }

        [Fact]
        public void ExportAll_NonZHRom_ReturnsEmpty()
        {
            using var rs = new RomScope();
            using var svc = new ImageServiceScope();

            var rom = new ROM();
            byte[] data = new byte[ROM_LEN];
            Array.Fill(data, (byte)0x00);
            rom.LoadLow("synth.gba", data, "BE8E01"); // FE8U not multibyte
            CoreState.ROM = rom;

            string manifest = FontBulkExportZHCore.ExportAll(rom, userFontOnly: false, (i, n) => true);
            Assert.Equal("", manifest);
        }

        [Theory]
        [InlineData(false, 1)] // serif shift = 1
        [InlineData(true, 2)]  // item shift = 2
        public void Unshift_ReversesShift(bool isItemFont, int shift)
        {
            using var svc = new ImageServiceScope();

            // Build a 16x13 indexed glyph, shift it into 16x16, then un-shift; the
            // recovered 16x13 must equal the source (shift/unshift are inverses).
            int w = FontGlyphZHCore.GLYPH_W;
            int h = FontGlyphZHCore.GLYPH_H;
            byte[] src13 = new byte[w * h];
            int seed = 7;
            for (int i = 0; i < src13.Length; i++)
            {
                seed = (seed * 1103515245 + 12345) & 0x7fffffff;
                src13[i] = (byte)(seed & 0x03);
            }

            // Shift 16x13 indices into a 16x16 indexed buffer at y = shift.
            byte[] shifted16 = new byte[w * w];
            for (int y = 0; y < h; y++)
                Array.Copy(src13, y * w, shifted16, (y + shift) * w, w);

            byte[] recovered = FontBulkImportZHCore.UnshiftTo16x13(shifted16, w, w, isItemFont);
            Assert.NotNull(recovered);
            Assert.Equal(src13, recovered);
        }

        [Fact]
        public void Unshift_WrongSize_ReturnsNull()
        {
            byte[] bad = new byte[16 * 13]; // 16x13, not 16x16
            Assert.Null(FontBulkImportZHCore.UnshiftTo16x13(bad, 16, 13, isItemFont: false));
        }
    }
}
