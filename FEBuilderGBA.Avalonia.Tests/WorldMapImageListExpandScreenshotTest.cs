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
    /// Issue #825: render the Avalonia <see cref="WorldMapImageView"/> Border tab
    /// AFTER a list-expand to a PNG that proves the Read Count rose and the
    /// AddressList grew. Headless RenderTargetBitmap — does NOT require a visible
    /// desktop, so it works on locked machines and in CI. Mirrors
    /// <see cref="ItemShopFirstIconScreenshotTest"/>.
    ///
    /// <para>By default the PNG is written to a per-test temp directory (so
    /// normal/CI runs do NOT dirty the repo's tracked <c>pr-screenshots/</c>);
    /// set <c>FEBUILDERGBA_SCREENSHOT_DIR</c> to override — pointing it at the
    /// repo's <c>pr-screenshots/</c> is how the canonical PR screenshot is
    /// regenerated locally.</para>
    ///
    /// <para>The expand is driven through the VM's <c>ExpandBorderList</c> +
    /// the View's <c>RefreshBorderListFromReadConfig</c> (the same code the
    /// "List Expand" button runs once the user confirms the count prompt) — the
    /// modal <c>NumberInputDialog</c> is bypassed because Avalonia headless can
    /// not pump a nested modal loop. The rendered visual tree is the REAL view
    /// with REAL ROM data.</para>
    /// </summary>
    [Collection("SharedState")]
    public class WorldMapImageListExpandScreenshotTest : IClassFixture<RomFixture>
    {
        const int BorderTabIndex = 4; // Main=0,Event=1,Mini=2,PointIcon=3,Border=4,IconData=5

        readonly RomFixture _fixture;
        readonly ITestOutputHelper _output;

        public WorldMapImageListExpandScreenshotTest(RomFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        [AvaloniaFact]
        public void WorldMapImageView_BorderListExpand_RaisesCount_SavesScreenshot()
        {
            if (!_fixture.IsAvailable)
            {
                _output.WriteLine("SKIP: no ROM available");
                return;
            }
            // Only FE8U/FE8J carry the world-map tables.
            string ver = _fixture.Version ?? "";
            if (!ver.StartsWith("FE8"))
            {
                _output.WriteLine($"SKIP: world map editor requires FE8 (got {ver})");
                return;
            }

            CoreState.Services ??= new HeadlessAppServices();
            CoreState.Undo ??= new Undo();

            var view = new WorldMapImageView();

            // The Opened handler runs LoadAll on a real desktop; headless does
            // not raise Opened on the same timeline, so invoke it directly.
            Invoke(view, "LoadAll");

            // Realize the visual tree with an initial layout pass so the tab
            // content (and the Border AddressList inside it) is materialized.
            const int W = 1100;
            const int H = 720;
            view.Measure(new Size(W, H));
            view.Arrange(new Rect(0, 0, W, H));

            // Switch to the Border tab so the render shows it, then re-layout so
            // the Border tab's content is realized.
            var tab = view.GetVisualDescendants().OfType<TabControl>().FirstOrDefault();
            Assert.NotNull(tab);
            tab!.SelectedIndex = BorderTabIndex;
            view.Measure(new Size(W, H));
            view.Arrange(new Rect(0, 0, W, H));

            // Border_EntryList is a named element in the AXAML, so the logical
            // FindControl resolves it regardless of visual realization.
            var entryList = view.FindControl<AddressListControl>("Border_EntryList");
            Assert.NotNull(entryList);

            int rowsBefore = entryList!.ItemCount;
            _output.WriteLine($"Border rows before expand: {rowsBefore}");
            // A real FE8U border table has a handful of rows; if the ROM has an
            // empty/invalid table we can't demonstrate a rise — skip rather than
            // assert a fake number.
            if (rowsBefore == 0)
            {
                _output.WriteLine("SKIP: border table empty in this ROM");
                return;
            }

            // Drive the expand the same way the button does once confirmed:
            // open an undo scope, grow by +2, then refresh the list honoring the
            // new count (NOTE B — no re-scan).
            //
            // This is a one-shot screenshot test that never rolls back, so we do
            // NOT Push the undo record into the shared CoreState.Undo buffer —
            // doing so would leak undo history into later [Collection("SharedState")]
            // tests (RomFixture does not restore CoreState.Undo), causing
            // order-dependent failures. The local `ud` captures the writes for
            // the scope's lifetime and is then discarded; the ROM bytes are
            // written regardless. (Copilot PR #827 review thread 3.)
            // Snapshot the shared undo buffer so we can prove this test does NOT
            // leak undo history into later [Collection("SharedState")] tests.
            int undoBufferCountBefore = CoreState.Undo.UndoBuffer.Count;
            int undoPositionBefore = CoreState.Undo.Postion;

            uint newCount = (uint)rowsBefore + 2;
            var ud = CoreState.Undo.NewUndoData("WorldMap Border ExpandList screenshot");
            string err;
            using (ROM.BeginUndoScope(ud))
            {
                err = InvokeExpand(view, newCount, ud);
            }
            Assert.True(string.IsNullOrEmpty(err), $"expand error: {err}");

            // The shared undo buffer is untouched (no Push) — no shared-state leak.
            Assert.Equal(undoBufferCountBefore, CoreState.Undo.UndoBuffer.Count);
            Assert.Equal(undoPositionBefore, CoreState.Undo.Postion);

            Invoke(view, "RefreshBorderListFromReadConfig");

            int rowsAfter = entryList.ItemCount;
            _output.WriteLine($"Border rows after expand: {rowsAfter}");
            Assert.Equal((int)newCount, rowsAfter);
            Assert.True(rowsAfter > rowsBefore, "row count must rise after expand");

            // Render the real view (Border tab, risen count) to a PNG. Re-layout
            // first so the freshly-grown AddressList is measured.
            try
            {
                view.Measure(new Size(W, H));
                view.Arrange(new Rect(0, 0, W, H));
                using var bitmap = new RenderTargetBitmap(new PixelSize(W, H));
                bitmap.Render(view);

                string outDir = ResolveScreenshotOutputDir();
                Directory.CreateDirectory(outDir);
                string outPath = Path.Combine(outDir, "pr825-list-expand.png");
                bitmap.Save(outPath);
                _output.WriteLine($"Saved screenshot to: {outPath} ({new FileInfo(outPath).Length} bytes)");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Headless render failed (environment, not the #825 fix): {ex.Message}");
            }
        }

        // ----------------------------------------------------------------
        // Helpers
        // ----------------------------------------------------------------

        static string InvokeExpand(WorldMapImageView view, uint newCount, Undo.UndoData ud)
        {
            // ExpandBorderList lives on the VM; reach it via the view's private
            // _vm field so we exercise the exact production method, threading the
            // SAME undo transaction the outer scope opened (so ExpandTableTo's
            // ambient writes + RepointAllReferences' slot writes land together).
            var vmField = typeof(WorldMapImageView).GetField("_vm",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(vmField);
            var vm = (WorldMapImageViewModel)vmField!.GetValue(view)!;
            return vm.ExpandBorderList(newCount, ud);
        }

        static void Invoke(WorldMapImageView view, string method)
        {
            var m = typeof(WorldMapImageView).GetMethod(method,
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
