// SPDX-License-Identifier: GPL-3.0-or-later
// #1268 — the Chinese (ZH) Font editor's bulk export/import + .ttf auto-generate
// (Slice 2 of #1166). Mirrors the #1165 main-font bulk/auto-gen wiring in
// FontEditorView for the ZH 44-byte (16x13) format.
//
// Three layers:
//   * Source-text parity — the View wires Export-All / Import-All / Auto-Generate
//     through the new ZH Core seams under UndoService scopes + new AXAML buttons.
//   * Translation parity — the one Core-only R._ literal new to this slice has
//     ja AND zh entries (the L10n gate only scans AXAML, not code-behind R._()).
//   * Functional — a 16x16 vanilla-size PNG bulk-imports through the real
//     ImageImportService + FontBulkImportZHCore into a synthetic ZH ROM.
using System;
using System.IO;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Core;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class FontZHViewBulkParityTests
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
        public void ZHView_WiresBulkAndAutoGen_ThroughZHCoreSeams()
        {
            string cs = ReadRepoFile("FEBuilderGBA.Avalonia", "Views", "FontZHView.axaml.cs");
            // Bulk export/import go through the ZH Core seams (NOT the main-font ones).
            Assert.Contains("FontBulkExportZHCore.ExportAll", cs);
            Assert.Contains("FontBulkImportZHCore.ImportAll", cs);
            // Auto-generate goes through the ZH Core seam + the cross-platform rasterizer.
            Assert.Contains("FontAutoGenZHCore.AutoGenerateGlyphZH", cs);
            Assert.Contains("SkiaFontRasterizer", cs);
            // All three ROM-mutating paths run under an UndoService scope.
            Assert.Contains("_undoService.Begin(\"Import All Chinese Fonts\")", cs);
            Assert.Contains("_undoService.Begin(\"Auto-Generate Chinese Font Glyph\")", cs);
        }

        [Fact]
        public void ZHView_Axaml_HasBulkAndAutoGenButtons()
        {
            string ax = ReadRepoFile("FEBuilderGBA.Avalonia", "Views", "FontZHView.axaml");
            Assert.Contains("FontZH_ExportAll_Button", ax);
            Assert.Contains("FontZH_ImportAll_Button", ax);
            Assert.Contains("FontZH_LoadFont_Button", ax);
            Assert.Contains("FontZH_AutoGen_Button", ax);
            // The embeddable descriptor must keep manual sizing (the Avalonia gate).
            string cs = ReadRepoFile("FEBuilderGBA.Avalonia", "Views", "FontZHView.axaml.cs");
            Assert.Contains("public EditorDescriptor Descriptor => new(\"Font Editor (Chinese)\", 640, 640)", cs);
            Assert.DoesNotContain("SizeToContent:", cs);
        }

        [Fact]
        public void ZHView_ExportAll_GuardsEmptyManifest()
        {
            // Copilot #2: ExportAll must NOT write an empty file / claim success when
            // nothing was exported. The View checks the data-row count first.
            string cs = ReadRepoFile("FEBuilderGBA.Avalonia", "Views", "FontZHView.axaml.cs");
            Assert.Contains("FontBulkExportZHCore.CountManifestDataRows(manifest) == 0", cs);
            Assert.Contains("No font glyphs were exported.", cs);
        }

        [Fact]
        public void ZHView_AutoGen_UsesVmCharNotTrimmedLabel()
        {
            // Copilot #3: the auto-gen char must come from the VM (carried verbatim),
            // NOT a trimmed re-parse of the label (which kills a whitespace glyph).
            string cs = ReadRepoFile("FEBuilderGBA.Avalonia", "Views", "FontZHView.axaml.cs");
            Assert.Contains("_vm.CurrentChar", cs);
            // The fragile trimmed-substring helper is gone.
            Assert.DoesNotContain("GlyphCharacterOfSelected", cs);
        }

        // ---------------- Translation parity (Core-only R._ literals) ----------------

        [Theory]
        [InlineData("ja")]
        [InlineData("zh")]
        public void NewCoreLiterals_HaveTranslation(string lang)
        {
            // The L10n gate only scans AXAML literals; these code-behind / Core R._()
            // strings need an explicit ja/zh parity check.
            string tx = ReadRepoFile("config", "translate", lang + ".txt");
            Assert.Contains(":Invalid font glyph image size: {0}", tx);
            Assert.Contains(":No font glyphs were exported.", tx);
        }

        // ---------------- CharFromLabel: whitespace glyph char survives (Copilot #3) ----------------

        [Theory]
        [InlineData("8181 、", "、")]        // normal CJK char after the first space
        [InlineData("8140  ", " ")]          // a SPACE glyph: char part is a single space, preserved
        [InlineData("8181 ", "")]            // nothing after the space -> ""
        [InlineData("8181", "")]             // no space at all -> ""
        [InlineData("", "")]                 // empty label
        [InlineData("40 @", "@")]            // the literal '@' char (not a fallback) survives
        public void CharFromLabel_PreservesWhitespaceChar(string label, string expected)
        {
            Assert.Equal(expected, FontZHViewModel.CharFromLabel(label));
        }

        // ---------------- Functional: 16x16 vanilla-size PNG bulk-imports ----------------

        static IImageService EnsureImageService()
        {
            if (CoreState.ImageService == null)
                CoreState.ImageService = new FEBuilderGBA.SkiaSharp.SkiaImageService();
            return CoreState.ImageService;
        }

        // A 16x16 RGBA PNG painted with the 4 ZH serif-font colors (the vanilla-size
        // bulk export geometry). The bulk import un-shifts it back to 16x13.
        static string Write16x16Png(IImageService svc)
        {
            (byte R, byte G, byte B)[] rgb =
            {
                ((byte)0xE0, (byte)0xE0, (byte)0xE0), // bg (serif)
                ((byte)0xF8, (byte)0xF8, (byte)0xF8), // white
                ((byte)0xA8, (byte)0xA8, (byte)0xA7), // gray
                ((byte)0x28, (byte)0x28, (byte)0x28), // black
            };
            const int W = 16, H = 16;
            using IImage img = svc.CreateImage(W, H);
            byte[] rgba = new byte[W * H * 4];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int idx = (x + y) & 0x03;
                    int po = (y * W + x) * 4;
                    rgba[po + 0] = rgb[idx].R;
                    rgba[po + 1] = rgb[idx].G;
                    rgba[po + 2] = rgb[idx].B;
                    rgba[po + 3] = 255;
                }
            img.SetPixelData(rgba);

            string path = Path.Combine(Path.GetTempPath(), "fontzh_bulk_16x16_" + Guid.NewGuid().ToString("N") + ".png");
            img.Save(path);
            return path;
        }

        [Fact]
        public void BulkImport_16x16VanillaPng_RoundTripsIntoZHRom()
        {
            const uint ROM_LEN = 0x01000000;
            const uint MOJI_TEN = 0x8181; // '、', codeB 0x54
            var svc = EnsureImageService();

            var prevRom = CoreState.ROM;
            string png = Write16x16Png(svc);
            string manifestPath = Path.Combine(Path.GetTempPath(),
                "fontzh_bulk_" + Guid.NewGuid().ToString("N") + ".fontall.txt");
            try
            {
                var rom = new ROM();
                byte[] data = new byte[ROM_LEN];
                Array.Fill(data, (byte)0xFF);
                for (uint i = 0; i < 0x600000; i++) data[i] = 0x00;
                rom.LoadLow("synth.gba", data, "BE8J01"); // multibyte FE8J (ZH)
                uint top = FontGlyphZHCore.GetFontPointerZH(8, isItemFont: false);
                uint addr = top + FontGlyphZHCore.CalcCodeB(MOJI_TEN);
                U.write_u8(rom.Data, addr + 0, 0xD);
                U.write_u8(rom.Data, addr + 1, 16);
                U.write_u8(rom.Data, addr + 2, 0xD);
                CoreState.ROM = rom;

                // The PNG filename in the manifest must use the <type>_<mojiHex>.png
                // key so the import recovers the moji; copy the painted PNG to that name.
                string dir = Path.GetDirectoryName(manifestPath)!;
                string pngName = "text_" + U.ToHexString(MOJI_TEN) + ".png";
                File.Copy(png, Path.Combine(dir, pngName), overwrite: true);
                File.WriteAllText(manifestPath, "//char\ttype\tWidth\tFilename\n、\ttext\t16\t" + pngName + "\n");

                string manifest = File.ReadAllText(manifestPath);
                string err = FontBulkImportZHCore.ImportAll(rom, manifest, (name, type) =>
                {
                    byte[] pal = FontGlyphZHCore.GetFontPaletteGBA(isItemFont: (type == "item"));
                    var r = ImageImportService.LoadAndRemapFromFile(Path.Combine(dir, name),
                        FontGlyphZHCore.GLYPH_W, FontGlyphZHCore.GLYPH_W, pal, 4, strictSize: true);
                    if (r == null || !r.Success) return null;
                    return new FontGlyphZHPixels { Indexed = r.IndexedPixels, Width = r.Width, Height = r.Height };
                });
                Assert.Equal("", err);
                // The glyph struct stayed valid and the stored width (16) was preserved.
                Assert.Equal(0xDu, rom.u8(addr + 0));
                Assert.Equal(0xDu, rom.u8(addr + 2));
                Assert.Equal(16u, rom.u8(addr + 1));
            }
            finally
            {
                CoreState.ROM = prevRom;
                try { File.Delete(png); } catch { }
                try { File.Delete(manifestPath); } catch { }
                try { File.Delete(Path.Combine(Path.GetDirectoryName(manifestPath)!, "text_" + U.ToHexString(MOJI_TEN) + ".png")); } catch { }
            }
        }
    }
}
