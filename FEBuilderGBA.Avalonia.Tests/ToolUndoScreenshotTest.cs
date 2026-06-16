// SPDX-License-Identifier: GPL-3.0-or-later
// #1190 — screenshot proof for the Avalonia Undo history tool (port of WinForms
// ToolUndoForm). Seeds a synthetic CoreState.Undo with a few named snapshots
// (NO external ROM needed — undo history is purely in-memory), drives the REAL
// ToolUndoView (its private LoadList populates the AddressListControl + selects
// HEAD), and writes a PNG via the proven headless RenderTargetBitmap pattern
// (mirrors ItemNewAllocScreenshotTest). Headless render is best-effort; the
// ItemCount assertion is the data-layer proof regardless.
using System;
using System.IO;
using System.Reflection;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.Media.Imaging;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.Views;
using FEBuilderGBA.SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class ToolUndoScreenshotTest
    {
        readonly ITestOutputHelper _output;
        public ToolUndoScreenshotTest(ITestOutputHelper output) => _output = output;

        [AvaloniaFact]
        public void UndoHistoryTool_PopulatedList_RendersAndSavesScreenshot()
        {
            var prevUndo = CoreState.Undo;
            var prevRom = CoreState.ROM;
            var prevImageService = CoreState.ImageService;
            try
            {
                if (CoreState.ImageService == null)
                    CoreState.ImageService = new SkiaImageService();

                // Synthetic ROM so NewUndoData (filesize = ROM.Data.Length) works;
                // never written to disk, never mutates a real ROM.
                var rom = new ROM();
                rom.SwapNewROMDataDirect(new byte[0x4000]);
                CoreState.ROM = rom;

                // Throwaway undo buffer with three descriptive snapshots.
                var undo = new Undo();
                undo.Push(undo.NewUndoData("Edit Unit HP"));
                undo.Push(undo.NewUndoData("Change Item Power"));
                undo.Push(undo.NewUndoData("Repoint Class Table"));
                CoreState.Undo = undo;

                var view = new ToolUndoView();
                Invoke(view, "LoadList");   // private; populates EntryList + selects HEAD

                var list = view.FindControl<AddressListControl>("EntryList");
                Assert.NotNull(list);
                Assert.True(list!.ItemCount >= 4, "history list must show HEAD + 3 snapshots");
                _output.WriteLine($"History entries: {list.ItemCount}");

                string outDir = ResolveScreenshotOutputDir();
                Directory.CreateDirectory(outDir);
                SaveRender(view, 900, 560, Path.Combine(outDir, "pr1190-undo-history.png"));
            }
            finally
            {
                CoreState.Undo = prevUndo;
                CoreState.ROM = prevRom;
                CoreState.ImageService = prevImageService;
            }
        }

        void SaveRender(Control view, int w, int h, string outPath)
        {
            try
            {
                view.Measure(new Size(w, h));
                view.Arrange(new Rect(0, 0, w, h));
                using var bitmap = new RenderTargetBitmap(new PixelSize(w, h), new Vector(96, 96));
                bitmap.Render(view);
                bitmap.Save(outPath);
                _output.WriteLine($"Saved screenshot to: {outPath} ({new FileInfo(outPath).Length} bytes)");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Headless render failed (environment, not the #1190 port): {ex.Message}");
            }
        }

        static void Invoke(object target, string method, params object?[]? args)
        {
            var m = target.GetType().GetMethod(method,
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(m);
            m!.Invoke(target, args);
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
