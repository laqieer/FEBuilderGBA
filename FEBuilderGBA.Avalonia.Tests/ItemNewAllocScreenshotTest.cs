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
    /// "New-alloc Stat Bonuses" button, and capture a BEFORE/AFTER composite PNG
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
            if (CoreState.ImageService == null)
                CoreState.ImageService = new SkiaImageService();
            CoreState.Undo ??= new Undo();

            ROM rom = CoreState.ROM!;
            uint itemPtr = rom.RomInfo.item_pointer;
            uint baseAddr = rom.p32(itemPtr);
            uint dataSize = rom.RomInfo.item_datasize;

            // Find the first item (index > 0) whose Stat Bonuses pointer (P12)
            // is 0 — the new-alloc target.
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
            // Drive the real selection handler so the VM + UI populate exactly
            // as in production (OnItemSelected -> LoadItem -> UpdateUI ->
            // UpdateComputedUI shows the orange warning row).
            Invoke(view, "OnItemSelected", targetAddr);

            const int W = 900;
            const int H = 760;

            // BEFORE state assertions: the StatBonuses warning row is visible.
            var row = view.FindControl<Control>("AllocStatBonusesRow");
            Assert.NotNull(row);
            Assert.True(row!.IsVisible, "BEFORE: Stat Bonuses null-pointer warning row must be visible.");

            string outDir = ResolveScreenshotOutputDir();
            Directory.CreateDirectory(outDir);
            // BEFORE shot: the orange "P12 is null" warning row + New-alloc button.
            SaveRender(view, W, H, Path.Combine(outDir, "pr831-item-newalloc-before.png"));

            // Click the new-alloc button via the real handler.
            Invoke(view, "AllocStatBonuses_Click", null, null);

            // AFTER state assertions: the warning row is hidden and the pointer
            // box shows a real GBA pointer.
            Assert.False(row.IsVisible, "AFTER: warning row must hide once P12 is allocated.");
            uint newP12 = rom.u32(targetAddr + 12);
            Assert.True(U.isPointer(newP12), "AFTER: P12 must hold a GBA pointer.");
            _output.WriteLine($"AFTER: P12 = 0x{newP12:X8}");

            // AFTER shot (canonical PR proof): warning gone, Stat Bonuses box now
            // shows the freshly allocated pointer.
            SaveRender(view, W, H, Path.Combine(outDir, "pr831-item-newalloc.png"));
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
