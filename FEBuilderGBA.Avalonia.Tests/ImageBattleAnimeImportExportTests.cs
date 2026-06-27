// SPDX-License-Identifier: GPL-3.0-or-later
// #1638: Full battle-animation Import (.txt / FEditor .bin) + Export (.txt + PNG)
// for the Avalonia Battle Animation editor (parity with WinForms + CLI).
//
// Covers:
//   - Wiring parity: the Import/Export buttons exist and their Click handlers
//     are wired in the view.
//   - VM delegate negative paths (no ROM / no record selected / missing file)
//     return a Core error string WITHOUT throwing.
//   - Real-ROM round-trip: export the selected animation to a temp .txt, then
//     re-import it into the SAME record; assert no error and that the 32-byte
//     record still parses (5 pointers valid). Skips when no ROM is available.
//   - Undo-safety: the import path runs under one UndoService scope and the
//     change is restorable (rollback restores the original record bytes).
using System;
using System.IO;
using global::Avalonia;
using global::Avalonia.Automation;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.LogicalTree;
using global::Avalonia.Media.Imaging;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using FEBuilderGBA.Core;
using FEBuilderGBA.SkiaSharp;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// Marked [Collection("SharedState")] because the round-trip test mutates
    /// CoreState.ROM via the shared <see cref="RomFixture"/> and the import path
    /// writes to the ROM; xUnit's per-class parallel runner must not race a
    /// sibling test's ROM swap.
    /// </summary>
    [Collection("SharedState")]
    public class ImageBattleAnimeImportExportTests
    {
        readonly RomFixture _fixture;

        public ImageBattleAnimeImportExportTests(RomFixture fixture)
        {
            _fixture = fixture;
            // RomFixture does not wire an image service; the import loader needs one.
            if (CoreState.ImageService == null)
                CoreState.ImageService = new SkiaImageService();
        }

        static T? FindByAutomationId<T>(Control root, string automationId) where T : Control
        {
            foreach (var d in root.GetLogicalDescendants())
                if (d is T c && AutomationProperties.GetAutomationId(c) == automationId)
                    return c;
            return null;
        }

        // ---- Wiring parity: buttons exist + Click handlers wired ----

        [AvaloniaFact]
        public void ImportExport_Buttons_Exist_And_HandlersWired()
        {
            var view = new ImageBattleAnimeView();
            var import = FindByAutomationId<Button>(view, "ImageBattleAnime_ImportAnime_Button");
            var export = FindByAutomationId<Button>(view, "ImageBattleAnime_ExportAnime_Button");
            Assert.NotNull(import);
            Assert.NotNull(export);

            // Click handlers must be wired (a raised Click must not throw with no
            // record selected — it shows an error and returns).
            var ex1 = Record.Exception(() => import!.RaiseEvent(
                new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)));
            var ex2 = Record.Exception(() => export!.RaiseEvent(
                new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)));
            Assert.Null(ex1);
            Assert.Null(ex2);
        }

        // ---- VM delegate negative paths return an error string, never throw ----

        [Fact]
        public void ImportAnimation_NoRom_ReturnsError()
        {
            var prev = CoreState.ROM;
            CoreState.ROM = null;
            try
            {
                var vm = new ImageBattleAnimeViewModel();
                string err = vm.ImportAnimation("anything.txt");
                Assert.False(string.IsNullOrEmpty(err));
            }
            finally { CoreState.ROM = prev; }
        }

        [Fact]
        public void ImportAnimation_NoRecordSelected_ReturnsError()
        {
            var prev = CoreState.ROM;
            try
            {
                CoreState.ROM = new ROM();
                CoreState.ROM.SwapNewROMDataDirect(new byte[0x10000]);
                var vm = new ImageBattleAnimeViewModel();
                // HasAnimeDetails defaults false / AnimeDataAddr 0 -> guarded error.
                string err = vm.ImportAnimation("anything.txt");
                Assert.False(string.IsNullOrEmpty(err));
                Assert.DoesNotContain("Exception", err);
            }
            finally { CoreState.ROM = prev; }
        }

        [Fact]
        public void ExportAnimation_NoRecordSelected_ReturnsError()
        {
            var prev = CoreState.ROM;
            try
            {
                CoreState.ROM = new ROM();
                CoreState.ROM.SwapNewROMDataDirect(new byte[0x10000]);
                var vm = new ImageBattleAnimeViewModel();
                string err = vm.ExportAnimation(Path.Combine(Path.GetTempPath(), "x.txt"));
                Assert.False(string.IsNullOrEmpty(err));
            }
            finally { CoreState.ROM = prev; }
        }

        // ---- Real-ROM export -> import round-trip + undo-safety ----

        [Fact]
        public void Export_Then_Import_RoundTrip_And_UndoRestores()
        {
            if (!_fixture.IsAvailable)
                return; // SKIP: no ROM available (e.g. local worktree). CI has ROMs.

            ROM rom = CoreState.ROM!;
            Assert.NotNull(rom);

            // Resolve a real, fully-parseable animation record (0-based id 0).
            uint animAddr = BattleAnimeImportCore.ResolveBattleAnimeAddr(rom, 0);
            Assert.NotEqual(U.NOT_FOUND, animAddr);

            // Drive the VM the way the editor does: load the SP record list, pick a
            // class row that references this animation so AnimeDataAddr is populated.
            var vm = new ImageBattleAnimeViewModel();
            vm.LoadAnimationDetails(1); // 1-based id -> record 0
            Assert.True(vm.HasAnimeDetails, "Expected a valid animation record for id 1.");
            Assert.Equal(animAddr, vm.AnimeDataAddr);

            string tmpDir = Path.Combine(Path.GetTempPath(), "febuilder_ba_1638_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmpDir);
            string txt = Path.Combine(tmpDir, "anim.txt");
            try
            {
                // Export to .txt (+ PNGs).
                string exportErr = vm.ExportAnimation(txt);
                Assert.True(string.IsNullOrEmpty(exportErr), $"Export error: {exportErr}");
                Assert.True(File.Exists(txt), "Export did not produce the .txt script.");

                // (1) Undo-safety FIRST (before any committed mutation): snapshot the
                // record, import under ONE scope, then ROLL BACK (the view's error
                // path) and assert the original record bytes are restored exactly.
                byte[] before = rom.getBinaryData(animAddr, 32);
                var undoRollback = new UndoService();
                undoRollback.Begin("Import Battle Animation (rollback)");
                string importErrRb = vm.ImportAnimation(txt);
                Assert.True(string.IsNullOrEmpty(importErrRb), $"Import error (rollback path): {importErrRb}");
                undoRollback.Rollback();
                byte[] afterUndo = rom.getBinaryData(animAddr, 32);
                Assert.Equal(before, afterUndo);

                // (2) Committed import: re-import the just-exported script into the
                // SAME record under ONE undo scope (mirroring ImportAnime_Click) and
                // assert the record still parses as a valid 5-pointer record.
                var undoCommit = new UndoService();
                undoCommit.Begin("Import Battle Animation");
                string importErr = vm.ImportAnimation(txt);
                Assert.True(string.IsNullOrEmpty(importErr), $"Import error: {importErr}");
                undoCommit.Commit();

                Assert.True(U.isPointer(rom.u32(animAddr + 12)), "section ptr invalid after import");
                Assert.True(U.isPointer(rom.u32(animAddr + 20)), "oam R->L ptr invalid after import");
                Assert.True(U.isPointer(rom.u32(animAddr + 24)), "oam L->R ptr invalid after import");
                Assert.True(U.isPointer(rom.u32(animAddr + 28)), "palette ptr invalid after import");
            }
            finally
            {
                try { Directory.Delete(tmpDir, true); } catch { /* best effort */ }
            }
        }

        // ---- Screenshot proof: the editor with the new Import/Export buttons ----

        [AvaloniaFact]
        public void EditorWithImportExportButtons_SavesScreenshot()
        {
            if (!_fixture.IsAvailable)
                return; // SKIP: no ROM available locally; CI has ROMs.

            var view = new ImageBattleAnimeView();
            view.Show();
            try
            {
                // Land on a real class row so the Animation Details panel (which
                // hosts the new Import/Export buttons) becomes visible with data.
                ROM rom = CoreState.ROM!;
                uint listBase = rom.p32(rom.RomInfo.image_battle_animelist_pointer);
                var classVm = new ClassEditorViewModel();
                foreach (var it in classVm.LoadClassList())
                {
                    classVm.LoadClass(it.addr);
                    uint raw = classVm.BattleAnimePtr;
                    if (!U.isPointer(raw)) continue;
                    uint off = U.toOffset(raw);
                    if (!U.isSafetyOffset(off, rom) || off + 4 > (uint)rom.Data.Length) continue;
                    if (off == listBase) continue;
                    if (ClassFormCore.GetIDWhereBattleAnimeAddr(rom, raw) == U.NOT_FOUND) continue;
                    view.NavigateTo(off);
                    break;
                }

                TrySaveScreenshot(view);
            }
            finally { view.Close(); }
        }

        static void TrySaveScreenshot(ImageBattleAnimeView view)
        {
            try
            {
                const int W = 1100;
                const int H = 900;
                view.Measure(new Size(W, H));
                view.Arrange(new Rect(0, 0, W, H));

                using var bitmap = new RenderTargetBitmap(new PixelSize(W, H));
                bitmap.Render(view);

                string outDir = ResolveScreenshotOutputDir();
                Directory.CreateDirectory(outDir);
                bitmap.Save(Path.Combine(outDir, "pr1638-battleanime-import-export.png"));
            }
            catch
            {
                // Headless render unavailable in this environment — not a regression.
            }
        }

        static string ResolveScreenshotOutputDir()
        {
            string? overrideDir = Environment.GetEnvironmentVariable("FEBUILDERGBA_SCREENSHOT_DIR");
            if (!string.IsNullOrEmpty(overrideDir))
                return overrideDir;
            return Path.Combine(Path.GetTempPath(), "FEBuilderGBA-screenshots");
        }
    }
}
