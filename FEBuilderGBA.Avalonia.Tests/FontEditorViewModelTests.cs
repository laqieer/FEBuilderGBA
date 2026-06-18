// SPDX-License-Identifier: GPL-3.0-or-later
// #1165 tests for the Avalonia main Font editor (FontEditorViewModel +
// FontEditorView). Two layers:
//   * VM functional tests on a real FE8U ROM (skipped when absent) — LoadList
//     non-empty, LoadEntry sets a glyph, FontType switch reloads, no-ROM guard.
//   * Source-text parity on the View — the import/export handlers wire the Core
//     seams + the 4-color remap + ambient undo + AutomationIds.
using System;
using System.IO;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using FEBuilderGBA.Core;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class FontEditorViewModelTests
    {
        // ---------------- VM functional (real FE8U) ----------------

        [Fact]
        public void LoadList_NonEmpty_FE8U()
        {
            if (TestRomLocator.FindRom("FE8U") == null) return; // skip when ROM absent
            RomTestHelper.WithRom("FE8U", () =>
            {
                var vm = new FontEditorViewModel { FontTypeIndex = 0 };
                var items = vm.LoadList();
                Assert.NotEmpty(items);
            });
        }

        [Fact]
        public void LoadEntry_SetsGlyph_FE8U()
        {
            if (TestRomLocator.FindRom("FE8U") == null) return;
            RomTestHelper.WithRom("FE8U", () =>
            {
                // The glyph render needs CoreState.ImageService (SkiaImageService).
                if (CoreState.ImageService == null)
                    CoreState.ImageService = new FEBuilderGBA.SkiaSharp.SkiaImageService();

                var vm = new FontEditorViewModel { FontTypeIndex = 1 }; // serif
                var items = vm.LoadList();
                Assert.NotEmpty(items);

                var first = items[0];
                vm.LoadEntry(first.addr, first.tag);
                Assert.True(vm.IsLoaded);
                Assert.Equal(first.addr, vm.CurrentAddr);

                // The selected glyph renders to a non-null preview.
                using IImage img = vm.TryRenderGlyph();
                Assert.NotNull(img);
            });
        }

        [Fact]
        public void FontTypeSwitch_Reloads_FE8U()
        {
            if (TestRomLocator.FindRom("FE8U") == null) return;
            RomTestHelper.WithRom("FE8U", () =>
            {
                var vm = new FontEditorViewModel { FontTypeIndex = 0 };
                var item = vm.LoadList();
                vm.FontTypeIndex = 1;
                var serif = vm.LoadList();
                // Both fonts populate (item vs serif are distinct tables, both vanilla).
                Assert.NotEmpty(item);
                Assert.NotEmpty(serif);
            });
        }

        [Fact]
        public void LoadList_NoRom_ReturnsEmpty_NoThrow()
        {
            var prev = CoreState.ROM;
            try
            {
                CoreState.ROM = null;
                var vm = new FontEditorViewModel();
                Assert.Empty(vm.LoadList());
            }
            finally { CoreState.ROM = prev; }
        }

        [Fact]
        public void FindAddrByMoji_RoundTripsAListedGlyph_FE8U()
        {
            if (TestRomLocator.FindRom("FE8U") == null) return;
            RomTestHelper.WithRom("FE8U", () =>
            {
                var vm = new FontEditorViewModel { FontTypeIndex = 0 };
                var items = vm.LoadList();
                Assert.NotEmpty(items);
                // A listed glyph's tag (moji) must resolve back to its address —
                // this is what re-selects the just-imported glyph after a reload.
                var sample = items[items.Count / 2];
                Assert.Equal(sample.addr, vm.FindAddrByMoji(sample.tag));
            });
        }

        [Fact]
        public void FindAddrByMoji_NoRom_ReturnsZero()
        {
            var prev = CoreState.ROM;
            try
            {
                CoreState.ROM = null;
                var vm = new FontEditorViewModel();
                Assert.Equal(0u, vm.FindAddrByMoji(0x41));
            }
            finally { CoreState.ROM = prev; }
        }

        // ---------------- Source-text parity (View) ----------------

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

        static string ReadView(string ext)
        {
            string? root = FindRepoRoot();
            Assert.NotNull(root);
            string path = Path.Combine(root!, "FEBuilderGBA.Avalonia", "Views", "FontEditorView" + ext);
            Assert.True(File.Exists(path), $"Missing {path}");
            return File.ReadAllText(path);
        }

        [Fact]
        public void Axaml_HasExpectedAutomationIds()
        {
            string axaml = ReadView(".axaml");
            Assert.Contains("FontEditor_Entry_List", axaml);
            Assert.Contains("FontEditor_FontType_Combo", axaml);
            Assert.Contains("FontEditor_Glyph_Image", axaml);
            Assert.Contains("FontEditor_ImportPng_Button", axaml);
            Assert.Contains("FontEditor_ExportPng_Button", axaml);
            Assert.Contains("FontEditor_ExportAll_Button", axaml);
            Assert.Contains("FontEditor_ImportAll_Button", axaml);
        }

        [Fact]
        public void CodeBehind_WiresCoreImport_4ColorRemap_AndUndo()
        {
            string cs = ReadView(".axaml.cs");
            // Per-glyph import goes through the Core seam.
            Assert.Contains("FontGlyphRenderCore.ImportGlyph", cs);
            // Bulk flows go through the Core seams.
            Assert.Contains("FontBulkExportCore.ExportAll", cs);
            Assert.Contains("FontBulkImportCore.ImportAll", cs);
            // 4-color remap (colorCount 4), NOT a 16-color quantize.
            Assert.Contains("LoadAndRemapToExistingPalette", cs);
            Assert.Contains("GetFontPaletteGBA", cs);
            // Ambient undo scope around mutation.
            Assert.Contains("_undoService.Begin", cs);
            Assert.Contains("_undoService.Commit", cs);
            Assert.Contains("_undoService.Rollback", cs);
        }

        // ---------------- #1232 auto-generate-from-font ----------------

        [Fact]
        public void Axaml_HasAutoGenAutomationIds()
        {
            string axaml = ReadView(".axaml");
            Assert.Contains("FontEditor_LoadFont_Button", axaml);
            Assert.Contains("FontEditor_ClearFont_Button", axaml);
            Assert.Contains("FontEditor_FontFile_Label", axaml);
            Assert.Contains("FontEditor_FontFamily_Input", axaml);
            Assert.Contains("FontEditor_FontSize_Input", axaml);
            Assert.Contains("FontEditor_VOffset_Input", axaml);
            Assert.Contains("FontEditor_AutoGen_Button", axaml);
        }

        [Fact]
        public void CodeBehind_WiresAutoGen_RasterizerCoreAndUndo()
        {
            string cs = ReadView(".axaml.cs");
            // Auto-gen goes through the Core seam with a SkiaSharp rasterizer.
            Assert.Contains("FontAutoGenCore.AutoGenerateGlyph", cs);
            Assert.Contains("SkiaFontRasterizer", cs);
            // FontFilePath is preferred over an installed-family name.
            Assert.Contains("FontFilePath", cs);
            // Undo scope around the mutation (begin/commit/rollback already
            // asserted globally above; confirm the auto-gen label is present).
            Assert.Contains("Auto-Generate Font Glyph", cs);
        }

        // The "@hex" undecodable fallback must be rejected, but a REAL '@'
        // character (moji 0x40, whose decoded label is the lone "@") must
        // rasterize normally — Copilot PR #1240 review #1.
        [Theory]
        [InlineData("@", false)]        // real at-sign glyph (moji 0x40)
        [InlineData("@4140", true)]     // SJIS @hex fallback (4 hex digits)
        [InlineData("@A0", true)]       // UTF-8 @hex fallback (2 hex digits)
        [InlineData("@FF", true)]
        [InlineData("A", false)]        // ordinary letter
        [InlineData("@G", false)]       // '@' + non-hex => not the fallback marker
        [InlineData("@@", false)]       // '@' + non-hex
        [InlineData("", false)]
        public void IsAtHexFallback_DistinguishesRealAtSignFromHexMarker(string input, bool expected)
        {
            Assert.Equal(expected, FontEditorView.IsAtHexFallback(input));
        }
    }
}
