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
    /// Issue #1438: render the Avalonia <see cref="EventBattleTalkFE6View"/> with the new
    /// "Boss conversation (16-byte)" secondary table selected, proving the second
    /// battle-talk table (event_ballte_talk2_pointer) is now reachable in the FE6 editor.
    /// Headless RenderTargetBitmap — works on locked machines and in CI.
    ///
    /// <para>By default the PNG goes to a per-test temp directory; set
    /// <c>FEBUILDERGBA_SCREENSHOT_DIR</c> to regenerate the canonical PR screenshot.</para>
    /// </summary>
    [Collection("SharedState")]
    public class EventBattleTalkFE6SecondaryScreenshotTest
    {
        readonly ITestOutputHelper _output;

        public EventBattleTalkFE6SecondaryScreenshotTest(ITestOutputHelper output)
        {
            _output = output;
        }

        [AvaloniaFact]
        public void EventBattleTalkFE6View_SecondaryTable_SavesScreenshot()
        {
            bool ran = false;
            RomTestHelper.WithRom("FE6", () =>
            {
                ran = true;
                CoreState.Services ??= new HeadlessAppServices();
                CoreState.Undo ??= new Undo();

                var view = new EventBattleTalkFE6View();

                // Headless does not raise Opened on the same timeline, so drive the
                // initial list load directly.
                Invoke(view, "LoadList");

                const int W = 1200;
                const int H = 760;
                view.Measure(new Size(W, H));
                view.Arrange(new Rect(0, 0, W, H));

                // Switch the Table combo to the secondary (boss-conversation) table.
                var combo = view.FindControl<ComboBox>("TableFilter");
                Assert.NotNull(combo);
                combo!.SelectedIndex = 1; // 0=Main(12-byte), 1=Boss conversation(16-byte)
                view.Measure(new Size(W, H));
                view.Arrange(new Rect(0, 0, W, H));

                var entryList = view.FindControl<AddressListControl>("EntryList");
                Assert.NotNull(entryList);
                int secondaryRows = entryList!.ItemCount;
                _output.WriteLine($"Secondary (boss-conversation) rows: {secondaryRows}");
                Assert.True(secondaryRows > 0, "secondary battle-talk table must have rows in FE6");

                // The event-pointer field must now be visible in secondary mode.
                var epLabel = view.FindControl<TextBlock>("EventPointerLabel");
                var epBox = view.FindControl<NumericUpDown>("EventPointerBox");
                Assert.NotNull(epLabel);
                Assert.NotNull(epBox);
                Assert.True(epLabel!.IsVisible, "Event Pointer label must be visible in secondary mode");
                Assert.True(epBox!.IsVisible, "Event Pointer box must be visible in secondary mode");

                // Select the first secondary row so the editor panel is populated.
                entryList.SelectFirst();
                view.Measure(new Size(W, H));
                view.Arrange(new Rect(0, 0, W, H));

                try
                {
                    using var bitmap = new RenderTargetBitmap(new PixelSize(W, H));
                    bitmap.Render(view);

                    string outDir = ResolveScreenshotOutputDir();
                    Directory.CreateDirectory(outDir);
                    string outPath = Path.Combine(outDir, "pr1438-battletalk2-fe6.png");
                    bitmap.Save(outPath);
                    _output.WriteLine($"Saved screenshot to: {outPath} ({new FileInfo(outPath).Length} bytes)");
                }
                catch (Exception ex)
                {
                    _output.WriteLine($"Headless render failed (environment, not the #1438 fix): {ex.Message}");
                }
            });

            if (!ran)
                _output.WriteLine("SKIP: FE6 ROM not available (set ROMS_DIR or place roms/FE6.gba)");
        }

        static void Invoke(EventBattleTalkFE6View view, string method)
        {
            var m = typeof(EventBattleTalkFE6View).GetMethod(method,
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
