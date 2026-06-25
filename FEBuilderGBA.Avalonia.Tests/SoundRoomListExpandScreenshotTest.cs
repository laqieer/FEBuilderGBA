using System;
using System.IO;
using System.Linq;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.Media.Imaging;
using global::Avalonia.VisualTree;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// Issue #1450: render the Avalonia <see cref="SoundRoomViewerView"/> AFTER a
    /// List-Expansion to a PNG that proves the list grew and the new "List
    /// Expansion" button is present. Headless RenderTargetBitmap — works on locked
    /// machines and in CI. Mirrors <see cref="WorldMapImageListExpandScreenshotTest"/>.
    ///
    /// <para>By default the PNG is written to a per-test temp dir (so normal/CI runs
    /// do NOT dirty the repo's tracked <c>pr-screenshots/</c>); set
    /// <c>FEBUILDERGBA_SCREENSHOT_DIR</c> to override — pointing it at the repo's
    /// <c>pr-screenshots/</c> is how the canonical PR screenshot is regenerated.</para>
    /// </summary>
    [Collection("SharedState")]
    public class SoundRoomListExpandScreenshotTest : IClassFixture<RomFixture>
    {
        readonly RomFixture _fixture;
        readonly ITestOutputHelper _output;

        public SoundRoomListExpandScreenshotTest(RomFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        [AvaloniaFact]
        public void SoundRoomView_ListExpand_RaisesCount_SavesScreenshot()
        {
            if (!_fixture.IsAvailable)
            {
                _output.WriteLine("SKIP: no ROM available");
                return;
            }

            CoreState.Services ??= new HeadlessAppServices();
            CoreState.Undo ??= new Undo();

            var view = new SoundRoomViewerView();

            // The Opened handler runs LoadList on a real desktop; invoke directly.
            Invoke(view, "LoadList");

            const int W = 1100;
            const int H = 640;
            view.Measure(new Size(W, H));
            view.Arrange(new Rect(0, 0, W, H));

            var entryList = view.FindControl<AddressListControl>("EntryList");
            Assert.NotNull(entryList);

            // The new List-Expansion button must be present.
            var expandButton = view.FindControl<Button>("ListExpandButton");
            Assert.NotNull(expandButton);

            int rowsBefore = entryList!.ItemCount;
            _output.WriteLine($"Sound room rows before expand: {rowsBefore}");
            if (rowsBefore == 0)
            {
                _output.WriteLine("SKIP: sound room table empty in this ROM");
                return;
            }

            // Drive the expand the same way the button does once confirmed: open an
            // undo scope, grow by +3, then reload honoring the new count. Do NOT
            // Push into the shared CoreState.Undo buffer (avoids leaking history
            // into later SharedState tests — mirrors the WorldMap screenshot test).
            int undoBufferCountBefore = CoreState.Undo.UndoBuffer.Count;
            int undoPositionBefore = CoreState.Undo.Postion;

            var vm = GetVm(view);
            uint newCount = (uint)rowsBefore + 3;
            var ud = CoreState.Undo.NewUndoData("SoundRoom ExpandList screenshot");
            string err;
            using (ROM.BeginUndoScope(ud))
            {
                err = vm.ExpandList(newCount, ud);
            }
            Assert.True(string.IsNullOrEmpty(err), $"expand error: {err}");

            Assert.Equal(undoBufferCountBefore, CoreState.Undo.UndoBuffer.Count);
            Assert.Equal(undoPositionBefore, CoreState.Undo.Postion);

            entryList.SetItems(vm.LoadSoundRoomList());

            int rowsAfter = entryList.ItemCount;
            _output.WriteLine($"Sound room rows after expand: {rowsAfter}");
            Assert.Equal((int)newCount, rowsAfter);
            Assert.True(rowsAfter > rowsBefore, "row count must rise after expand");

            try
            {
                view.Measure(new Size(W, H));
                view.Arrange(new Rect(0, 0, W, H));
                view.UpdateLayout();
                using var bitmap = new RenderTargetBitmap(new PixelSize(W, H), new Vector(96, 96));
                bitmap.Render(view);

                string outDir = ResolveScreenshotOutputDir();
                Directory.CreateDirectory(outDir);
                string outPath = Path.Combine(outDir, "pr1450-soundroom-list-expand.png");
                // Encode to a MemoryStream then write bytes — the direct
                // bitmap.Save(path) can be a silent no-op on this headless Skia
                // backend for some visual trees; the stream encode forces a flush.
                using (var ms = new MemoryStream())
                {
                    bitmap.Save(ms);
                    File.WriteAllBytes(outPath, ms.ToArray());
                }
                _output.WriteLine($"Saved screenshot to: {outPath} ({new FileInfo(outPath).Length} bytes)");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Headless render failed (environment, not the #1450 fix): {ex.Message}");
            }
        }

        // ----------------------------------------------------------------
        // Helpers
        // ----------------------------------------------------------------

        static SoundRoomViewerViewModel GetVm(SoundRoomViewerView view)
        {
            var f = typeof(SoundRoomViewerView).GetField("_vm",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(f);
            return (SoundRoomViewerViewModel)f!.GetValue(view)!;
        }

        static void Invoke(SoundRoomViewerView view, string method)
        {
            var m = typeof(SoundRoomViewerView).GetMethod(method,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                null, Type.EmptyTypes, null);
            Assert.NotNull(m);
            m!.Invoke(view, Array.Empty<object?>());
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
