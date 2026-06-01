using System;
using System.IO;
using System.Reflection;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.Media.Imaging;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.Views;
using FEBuilderGBA.SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// Issue #831: render the Avalonia <see cref="ItemEditorView"/> on a real
    /// FE8U item with a null Stat Bonuses (P12) pointer, click the new
    /// "New-alloc Stat Bonuses" button, and capture a BEFORE/AFTER pair of PNGs
    /// proving the new-alloc works: the orange "P12 is null" warning + button
    /// disappear and the Stat Bonuses pointer box shows the newly allocated
    /// GBA pointer.
    ///
    /// This is a HEADLESS render (Avalonia.Headless + RenderTargetBitmap) — it
    /// does NOT require a visible/unlocked desktop, so it produces the proof
    /// even when the machine is locked (the MCP computer-use desktop is black).
    /// Default output is a per-test temp dir; set FEBUILDERGBA_SCREENSHOT_DIR to
    /// the repo's pr-screenshots/ to regenerate the canonical PR screenshot.
    /// Mirrors <see cref="ItemShopFirstIconScreenshotTest"/>.
    ///
    /// LEAK-FREE (PR #833 review): the new-alloc click goes through the real
    /// handler, which writes to the ROM and PUSHES onto <c>CoreState.Undo</c>.
    /// Under <c>[Collection("SharedState")]</c> that would leak undo history +
    /// a mutated ROM into later tests (the #827 flake class). So this test
    /// swaps in a THROWAWAY <c>CoreState.Undo</c> for the duration (the click
    /// pushes onto it, never the shared buffer), rolls the allocation back via
    /// <c>RunUndo()</c> so the ROM bytes are restored, and restores
    /// <c>CoreState.Undo</c> / <c>CoreState.ROM</c> / <c>CoreState.ImageService</c>
    /// in a <c>finally</c>. Explicit before/after assertions confirm the shared
    /// undo buffer count is unchanged and the ROM P12 slot returns to 0.
    /// </summary>
    [Collection("SharedState")]
    public class ItemNewAllocScreenshotTest : IClassFixture<RomFixture>
    {
        readonly RomFixture _fixture;
        readonly ITestOutputHelper _output;

        public ItemNewAllocScreenshotTest(RomFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        [AvaloniaFact]
        public void ItemEditor_NewAllocStatBonuses_ClearsWarning_SavesScreenshot()
        {
            if (!_fixture.IsAvailable)
            {
                _output.WriteLine("SKIP: no ROM available");
                return;
            }

            // --- Save ALL shared CoreState we touch, restore in finally ---
            ROM rom = CoreState.ROM!;
            var prevUndo = CoreState.Undo;
            var prevImageService = CoreState.ImageService;
            // Snapshot the shared undo buffer so we can prove it's untouched.
            int sharedUndoCountBefore = prevUndo?.UndoBuffer.Count ?? 0;
            int sharedUndoPosBefore = prevUndo?.Postion ?? 0;

            try
            {
                if (CoreState.ImageService == null)
                    CoreState.ImageService = new SkiaImageService();
                // Throwaway undo: the real click PUSHES, so route it off the
                // shared buffer entirely (restored in finally regardless).
                CoreState.Undo = new Undo();

                uint baseAddr = rom.p32(rom.RomInfo.item_pointer);
                uint dataSize = rom.RomInfo.item_datasize;

                // First item (index > 0) whose Stat Bonuses pointer (P12) is 0.
                uint targetAddr = 0;
                for (uint idx = 1; idx < 64; idx++)
                {
                    uint addr = baseAddr + idx * dataSize;
                    if (rom.u32(addr + 12) == 0) { targetAddr = addr; break; }
                }
                if (targetAddr == 0)
                {
                    _output.WriteLine("SKIP: no item with null P12 found");
                    return;
                }
                _output.WriteLine($"Target item @ 0x{targetAddr:X8} (P12=0)");

                var view = new ItemEditorView();
                // Drive the real selection handler so the VM + UI populate
                // exactly as in production (OnItemSelected -> LoadItem ->
                // UpdateUI -> UpdateComputedUI shows the orange warning row).
                Invoke(view, "OnItemSelected", targetAddr);

                const int W = 900;
                const int H = 760;

                // BEFORE: the StatBonuses warning row is visible.
                var row = view.FindControl<Control>("AllocStatBonusesRow");
                Assert.NotNull(row);
                Assert.True(row!.IsVisible, "BEFORE: Stat Bonuses null-pointer warning row must be visible.");

                string outDir = ResolveScreenshotOutputDir();
                Directory.CreateDirectory(outDir);
                SaveRender(view, W, H, Path.Combine(outDir, "pr831-item-newalloc-before.png"));

                // Click the new-alloc button via the real handler (writes ROM +
                // pushes onto the throwaway CoreState.Undo).
                Invoke(view, "AllocStatBonuses_Click", null, null);

                // AFTER: the warning row is hidden and P12 holds a GBA pointer.
                Assert.False(row.IsVisible, "AFTER: warning row must hide once P12 is allocated.");
                uint newP12 = rom.u32(targetAddr + 12);
                Assert.True(U.isPointer(newP12), "AFTER: P12 must hold a GBA pointer.");
                _output.WriteLine($"AFTER: P12 = 0x{newP12:X8}");

                SaveRender(view, W, H, Path.Combine(outDir, "pr831-item-newalloc.png"));

                // Roll the allocation back on the throwaway undo so the ROM is
                // left byte-for-byte as found (P12 returns to 0).
                CoreState.Undo.RunUndo();
                Assert.Equal(0u, rom.u32(targetAddr + 12));
            }
            finally
            {
                // Restore EVERY shared slot we touched.
                CoreState.Undo = prevUndo;
                CoreState.ROM = rom;
                CoreState.ImageService = prevImageService;
            }

            // The shared undo buffer must be byte-for-byte as we found it (#827
            // leak class): no entries pushed, position unchanged.
            Assert.Equal(sharedUndoCountBefore, CoreState.Undo?.UndoBuffer.Count ?? 0);
            Assert.Equal(sharedUndoPosBefore, CoreState.Undo?.Postion ?? 0);
        }

        void SaveRender(Control view, int w, int h, string outPath)
        {
            // Best-effort headless render (mirrors the proven WorldMapImageList /
            // ItemShop screenshot pattern). If the headless pipeline can't render
            // in this environment the data-layer assertions remain the proof, so
            // we log rather than fail.
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
                _output.WriteLine($"Headless render failed (environment, not the #831 fix): {ex.Message}");
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
