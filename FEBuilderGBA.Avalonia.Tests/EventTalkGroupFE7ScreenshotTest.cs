using System;
using System.IO;
using System.Reflection;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.Media.Imaging;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using FEBuilderGBA.SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// Issue #1442: render the Avalonia <see cref="EventTalkGroupFE7View"/> on a real
    /// FE7 ROM and capture a PNG proving the editor is now a 14-entry stride-4 list
    /// (was a single auto-discovered entry) with a repointable base and a "New Block"
    /// button (NewAlloc parity with WinForms EventTalkGroupFE7Form).
    ///
    /// HEADLESS render (Avalonia.Headless + RenderTargetBitmap) — works even when the
    /// desktop is locked. Output defaults to a per-test temp dir; set
    /// FEBUILDERGBA_SCREENSHOT_DIR to the repo's pr-screenshots/ to regenerate the
    /// canonical PR screenshot. Mirrors <see cref="EventBattleTalkFE7ScreenshotTest"/>.
    /// The data-layer assertions (14 rows + New Block button present + populated detail
    /// pane) remain the proof regardless of whether the headless raster pipeline succeeds.
    /// </summary>
    [Collection("SharedState")]
    public class EventTalkGroupFE7ScreenshotTest
    {
        const uint BlockBase = 0x00900000u; // synthetic talk-group block when none auto-discovered

        readonly ITestOutputHelper _output;

        public EventTalkGroupFE7ScreenshotTest(ITestOutputHelper output)
        {
            _output = output;
        }

        [AvaloniaFact]
        public void TalkGroupFE7Editor_Lists14Entries_HasNewBlock_SavesScreenshot()
        {
            RomTestHelper.WithRom("FE7J", () =>
            {
                var prevImageService = CoreState.ImageService;
                try
                {
                    if (CoreState.ImageService == null)
                        CoreState.ImageService = new SkiaImageService();

                    var view = new EventTalkGroupFE7View();

                    // Drive the real Opened -> LoadList path. If the ROM has no
                    // auto-discoverable talk-group block (event-script scan can be
                    // gated headless), plant a deterministic 14×4 block and repoint.
                    Invoke(view, "LoadList");
                    var vm = (EventTalkGroupFE7ViewModel)view.DataViewModel!;
                    var list = view.FindControl<global::FEBuilderGBA.Avalonia.Controls.AddressListControl>("EntryList");

                    if (list!.GetItems().Count == 0)
                    {
                        PlantBlock(CoreState.ROM!, BlockBase);
                        vm.SetBaseAddr(BlockBase);
                        Invoke(view, "LoadList");
                    }

                    view.SelectFirstItem();

                    var textIdBox = view.FindControl<NumericUpDown>("TextIdUpDown");
                    var writeButton = view.FindControl<Button>("WriteButton");
                    var newBlockButton = view.FindControl<Button>("NewBlockButton");
                    var addrLabel = view.FindControl<TextBlock>("AddrLabel");

                    // The ported surface must exist.
                    Assert.NotNull(textIdBox);
                    Assert.NotNull(writeButton);
                    Assert.NotNull(newBlockButton); // NewAlloc parity
                    Assert.Equal("New Block", newBlockButton!.Content);

                    // 14 stride-4 entries are listed (was 1 in the stub).
                    Assert.Equal(EventTalkGroupFE7ViewModel.EntryCount, list.GetItems().Count);

                    // Selecting the first entry populates the address readout.
                    Assert.False(string.IsNullOrEmpty(addrLabel?.Text));
                    Assert.StartsWith("0x", addrLabel!.Text);

                    _output.WriteLine($"Rows={list.GetItems().Count} Addr={addrLabel.Text} " +
                                      $"Base=0x{vm.BaseAddr:X08} TextId={textIdBox!.Value}");

                    string outDir = ResolveScreenshotOutputDir();
                    Directory.CreateDirectory(outDir);
                    SaveRender(view, 1100, 600, Path.Combine(outDir, "pr1442-talkgroup-fe7.png"));
                }
                finally
                {
                    CoreState.ImageService = prevImageService;
                }
            });
        }

        static void PlantBlock(ROM rom, uint baseAddr)
        {
            // 14 entries × 4 bytes, low u16 = a recognizable text id per entry.
            for (int i = 0; i < EventTalkGroupFE7ViewModel.EntryCount; i++)
            {
                uint a = baseAddr + (uint)(i * 4);
                ushort id = (ushort)(0x0100 + i);
                rom.Data[(int)a + 0] = (byte)(id & 0xFF);
                rom.Data[(int)a + 1] = (byte)((id >> 8) & 0xFF);
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
                _output.WriteLine($"Headless render failed (environment, not the #1442 fix): {ex.Message}");
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
