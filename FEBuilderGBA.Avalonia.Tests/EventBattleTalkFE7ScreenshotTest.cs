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
    /// Issue #1437: render the Avalonia <see cref="EventBattleTalkFE7View"/> on a
    /// real FE7J ROM, select the first Main (16-byte) battle-talk entry, and
    /// capture a PNG proving the editor is no longer display-only — the detail
    /// pane now shows populated input fields (attacker/defender units, text,
    /// event pointer, achievement flag) + a Write button.
    ///
    /// HEADLESS render (Avalonia.Headless + RenderTargetBitmap) — works even when
    /// the desktop is locked. Output defaults to a per-test temp dir; set
    /// FEBUILDERGBA_SCREENSHOT_DIR to the repo's pr-screenshots/ to regenerate the
    /// canonical PR screenshot. Mirrors <see cref="ItemNewAllocScreenshotTest"/>.
    /// The data-layer assertions (inputs populated + Write button present) remain
    /// the proof regardless of whether the headless raster pipeline succeeds.
    /// </summary>
    [Collection("SharedState")]
    public class EventBattleTalkFE7ScreenshotTest
    {
        readonly ITestOutputHelper _output;

        public EventBattleTalkFE7ScreenshotTest(ITestOutputHelper output)
        {
            _output = output;
        }

        [AvaloniaFact]
        public void BattleTalkFE7Editor_SelectFirst_PopulatesInputs_SavesScreenshot()
        {
            RomTestHelper.WithRom("FE7J", () =>
            {
                var prevImageService = CoreState.ImageService;
                try
                {
                    if (CoreState.ImageService == null)
                        CoreState.ImageService = new SkiaImageService();

                    var view = new EventBattleTalkFE7View();
                    // Drive the real Opened -> LoadList path, then select the
                    // first row through the production selection handler so the
                    // VM + inputs populate exactly as in the running app.
                    Invoke(view, "LoadList");
                    view.SelectFirstItem();

                    var attackerBox = view.FindControl<NumericUpDown>("AttackerUnitBox");
                    var defenderBox = view.FindControl<NumericUpDown>("DefenderUnitBox");
                    var textBox = view.FindControl<NumericUpDown>("TextIdBox");
                    var eventPtrBox = view.FindControl<NumericUpDown>("EventPointerBox");
                    var writeButton = view.FindControl<Button>("WriteButton");
                    var addrLabel = view.FindControl<TextBlock>("AddrLabel");

                    // The new editable surface must exist and be wired.
                    Assert.NotNull(attackerBox);
                    Assert.NotNull(defenderBox);
                    Assert.NotNull(textBox);
                    Assert.NotNull(eventPtrBox);
                    Assert.NotNull(writeButton);

                    // Selecting the first entry must populate the address readout
                    // (proving the detail pane is no longer empty/display-only).
                    Assert.False(string.IsNullOrEmpty(addrLabel?.Text));
                    Assert.StartsWith("0x", addrLabel!.Text);
                    // Main schema → Event Pointer input is enabled.
                    Assert.True(eventPtrBox!.IsEnabled);

                    _output.WriteLine($"Addr={addrLabel.Text} Attacker={attackerBox!.Value} " +
                                      $"Defender={defenderBox!.Value} Text={textBox!.Value} EventPtr={eventPtrBox.Value}");

                    string outDir = ResolveScreenshotOutputDir();
                    Directory.CreateDirectory(outDir);
                    SaveRender(view, 1100, 820, Path.Combine(outDir, "pr1437-battletalk-fe7.png"));
                }
                finally
                {
                    CoreState.ImageService = prevImageService;
                }
            });
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
                _output.WriteLine($"Headless render failed (environment, not the #1437 fix): {ex.Message}");
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
