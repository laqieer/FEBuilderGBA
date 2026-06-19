// SPDX-License-Identifier: GPL-3.0-or-later
// #1166 follow-up — the Chinese (ZH) Font editor's PNG-import path. The merged
// editor wired the per-glyph import through ImageImportService
// .LoadAndRemapToExistingPalette, whose remap helper (LoadAndRemapFromFile)
// hard-rejected any image whose width/height weren't multiples of 8. ZH glyphs are
// 16x13 — height 13 is NOT a multiple of 8 — so a correctly-sized 16x13 PNG ALWAYS
// failed with "Image dimensions must be multiples of 8". The fix threads a
// requireTileMultiple flag (default true, like the #871 LoadAndQuantizeFromFile
// path) and the View opts out with requireTileMultiple: false.
//
// Two layers, mirroring FontEditorViewModelTests:
//   * Source-text parity — the service guards the %8 check + the View opts out.
//   * Functional — a real 16x13 PNG loads + remaps to the 4-color ZH palette
//     (no %8 rejection) and the indexed pixels import into a synthetic ZH ROM.
using System;
using System.IO;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Core;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class FontZHViewImportParityTests
    {
        static string? FindRepoRoot()
        {
            string start = AppDomain.CurrentDomain.BaseDirectory;
            for (DirectoryInfo? dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "FEBuilderGBA.sln")))
                    return dir.FullName;
            }
            return null;
        }

        static string ReadRepoFile(params string[] parts)
        {
            string? root = FindRepoRoot();
            Assert.NotNull(root);
            string path = Path.Combine(root!, Path.Combine(parts));
            Assert.True(File.Exists(path), $"Missing {path}");
            return File.ReadAllText(path);
        }

        // ---------------- Source-text parity ----------------

        [Fact]
        public void Service_GuardsTileMultipleCheck_OnRemapPath()
        {
            string svc = ReadRepoFile("FEBuilderGBA.Avalonia", "Services", "ImageImportService.cs");
            // Both remap overloads expose the opt-out flag (default keeps old behavior).
            Assert.Contains("bool requireTileMultiple = true", svc);
            // The remap %8 check is now GUARDED by the flag (was unconditional).
            Assert.Contains("requireTileMultiple && (image.Width % 8 != 0 || image.Height % 8 != 0)", svc);
        }

        [Fact]
        public void ZHView_ImportPng_OptsOutOfTileMultiple()
        {
            string cs = ReadRepoFile("FEBuilderGBA.Avalonia", "Views", "FontZHView.axaml.cs");
            // The ZH import remaps onto the 4-color ZH palette AND opts out of the
            // tile-size guard so a 16x13 (height 13) PNG is accepted.
            Assert.Contains("LoadAndRemapToExistingPalette", cs);
            Assert.Contains("GetFontPaletteGBA", cs);
            Assert.Contains("requireTileMultiple: false", cs);
        }

        // ---------------- Functional: 16x13 round-trips the load+remap path ----------------

        static IImageService EnsureImageService()
        {
            if (CoreState.ImageService == null)
                CoreState.ImageService = new FEBuilderGBA.SkiaSharp.SkiaImageService();
            return CoreState.ImageService;
        }

        // Build a 16x13 RGBA image painted with the 4 ZH serif-font colors (so the
        // closest-color remap maps each pixel to a distinct 0..3 index) and save it.
        static string Write16x13Png(IImageService svc, bool isItemFont)
        {
            // Mirror FontGlyphZHCore's fixed ZH palette ordering: bg / white / gray / black.
            (byte R, byte G, byte B)[] rgb = isItemFont
                ? new[] { ((byte)0x68, (byte)0x88, (byte)0xA8), ((byte)0xF8, (byte)0xF8, (byte)0xF8), ((byte)0xA8, (byte)0xA8, (byte)0xA7), ((byte)0x28, (byte)0x28, (byte)0x28) }
                : new[] { ((byte)0xE0, (byte)0xE0, (byte)0xE0), ((byte)0xF8, (byte)0xF8, (byte)0xF8), ((byte)0xA8, (byte)0xA8, (byte)0xA7), ((byte)0x28, (byte)0x28, (byte)0x28) };

            const int W = 16, H = 13;
            using IImage img = svc.CreateImage(W, H);
            byte[] rgba = new byte[W * H * 4];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int idx = (x + y) & 0x03; // all 4 colors appear
                    int po = (y * W + x) * 4;
                    rgba[po + 0] = rgb[idx].R;
                    rgba[po + 1] = rgb[idx].G;
                    rgba[po + 2] = rgb[idx].B;
                    rgba[po + 3] = 255;
                }
            img.SetPixelData(rgba);

            string path = Path.Combine(Path.GetTempPath(), "fontzh_16x13_" + Guid.NewGuid().ToString("N") + ".png");
            img.Save(path);
            return path;
        }

        [Fact]
        public void LoadAndRemap_16x13_Accepted_NoTileMultipleError()
        {
            var svc = EnsureImageService();
            string png = Write16x13Png(svc, isItemFont: false);
            try
            {
                byte[] pal = FontGlyphZHCore.GetFontPaletteGBA(isItemFont: false);
                // requireTileMultiple:false — the 16x13 (height 13) image MUST load.
                var lr = ImageImportService.LoadAndRemapFromFile(png,
                    FontGlyphZHCore.GLYPH_W, FontGlyphZHCore.GLYPH_H, pal, 4,
                    strictSize: true, requireTileMultiple: false);

                Assert.True(lr.Success, lr.Error);
                Assert.Equal(FontGlyphZHCore.GLYPH_W, lr.Width);
                Assert.Equal(FontGlyphZHCore.GLYPH_H, lr.Height);
                Assert.Equal(FontGlyphZHCore.GLYPH_W * FontGlyphZHCore.GLYPH_H, lr.IndexedPixels.Length);
                foreach (byte b in lr.IndexedPixels)
                    Assert.InRange(b, (byte)0, (byte)3); // stays in the 4-color range
            }
            finally { try { File.Delete(png); } catch { } }
        }

        [Fact]
        public void LoadAndRemap_16x13_DefaultStillRejects()
        {
            // Regression guard: the DEFAULT (requireTileMultiple omitted) must still
            // reject 16x13 so existing tile-based callers are unaffected.
            var svc = EnsureImageService();
            string png = Write16x13Png(svc, isItemFont: false);
            try
            {
                byte[] pal = FontGlyphZHCore.GetFontPaletteGBA(isItemFont: false);
                var lr = ImageImportService.LoadAndRemapFromFile(png,
                    FontGlyphZHCore.GLYPH_W, FontGlyphZHCore.GLYPH_H, pal, 4, strictSize: true);
                Assert.False(lr.Success);
                Assert.Contains("multiples of 8", lr.Error);
            }
            finally { try { File.Delete(png); } catch { } }
        }

        // Full end-to-end: load a 16x13 PNG (requireTileMultiple:false) then feed the
        // remapped indices into the Core import on a synthetic ZH ROM — proving the
        // editor's import path now works for a correctly-sized ZH glyph PNG.
        [Fact]
        public void LoadAndRemap_Then_ImportGlyphZH_RoundTrips()
        {
            const uint ROM_LEN = 0x01000000;
            const uint MOJI_TEN = 0x8181; // '、', codeB 0x54
            var svc = EnsureImageService();

            var prevRom = CoreState.ROM;
            string png = Write16x13Png(svc, isItemFont: false);
            try
            {
                var rom = new ROM();
                byte[] data = new byte[ROM_LEN];
                Array.Fill(data, (byte)0xFF);
                for (uint i = 0; i < 0x600000; i++) data[i] = 0x00;
                rom.LoadLow("synth.gba", data, "BE8J01"); // multibyte FE8J (ZH)
                // Plant the serif glyph slot so the in-place import has a target.
                uint top = FontGlyphZHCore.GetFontPointerZH(8, isItemFont: false);
                uint addr = top + FontGlyphZHCore.CalcCodeB(MOJI_TEN);
                U.write_u8(rom.Data, addr + 0, 0xD);
                U.write_u8(rom.Data, addr + 1, 16);
                U.write_u8(rom.Data, addr + 2, 0xD);
                CoreState.ROM = rom;

                byte[] pal = FontGlyphZHCore.GetFontPaletteGBA(isItemFont: false);
                var lr = ImageImportService.LoadAndRemapFromFile(png,
                    FontGlyphZHCore.GLYPH_W, FontGlyphZHCore.GLYPH_H, pal, 4,
                    strictSize: true, requireTileMultiple: false);
                Assert.True(lr.Success, lr.Error);

                string err = FontGlyphZHCore.ImportGlyphZH(rom, isItemFont: false, MOJI_TEN,
                    lr.IndexedPixels, lr.Width, lr.Height);
                Assert.Equal("", err); // import succeeds — no "%8" / size rejection
                // Header stays a valid ZH glyph struct.
                Assert.Equal(0xDu, rom.u8(addr + 0));
                Assert.Equal(0xDu, rom.u8(addr + 2));
            }
            finally
            {
                CoreState.ROM = prevRom;
                try { File.Delete(png); } catch { }
            }
        }
    }
}
