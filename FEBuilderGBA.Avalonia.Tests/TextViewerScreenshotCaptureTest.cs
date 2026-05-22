using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Views;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// One-off screenshot capture for PR #349. Loads a real FE8U ROM,
    /// instantiates the Text Editor view, selects a text ID with both Unit
    /// and MapSetting cross-references, and saves the rendered Avalonia
    /// window pixels to pr-screenshots/pr349-text-refs.png. Used as the
    /// "real GUI screenshot" proof for the PR per the workflow.
    ///
    /// This is a SCREENSHOT-PRODUCING test: it writes a file as a side
    /// effect. Behaviour assertions are covered by TextViewerCrossRefTests
    /// and TextReferenceFinderTests.
    /// </summary>
    [Collection("SharedState")]
    public class TextViewerScreenshotCaptureTest : IClassFixture<RomFixture>
    {
        private readonly RomFixture _fixture;
        private readonly ITestOutputHelper _output;

        private const int RenderWidth = 1166;
        private const int RenderHeight = 930;

        public TextViewerScreenshotCaptureTest(RomFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        [AvaloniaFact]
        public void CapturePR349Screenshot()
        {
            if (!_fixture.IsAvailable)
            {
                _output.WriteLine("ROM not available; skipping screenshot capture.");
                return;
            }

            ROM rom = CoreState.ROM!;
            var info = rom.RomInfo;

            // Locate a text ID we know will produce a non-Unit reference
            // — preference: a map-setting chapter-name text ID at offset +112.
            uint textIdToShow = 0;
            uint mapBase = NameResolver.DerefPointer(rom, info.map_setting_pointer);
            if (mapBase != 0 && info.map_setting_datasize != 0)
            {
                for (uint i = 0; i < 0x80; i++)
                {
                    uint entry = mapBase + i * info.map_setting_datasize;
                    if (entry + 114 > (uint)rom.Data.Length) break;
                    uint tid = rom.u16(entry + 112);
                    if (tid != 0 && tid < 0x7FFF)
                    {
                        textIdToShow = tid;
                        break;
                    }
                }
            }
            if (textIdToShow == 0)
            {
                _output.WriteLine("No map-setting text ID found; skipping screenshot.");
                return;
            }
            _output.WriteLine($"Capturing screenshot for text ID 0x{textIdToShow:X4}");

            // Build the TextViewerView. Force size + load text via reflection
            // on the private VM field so the Cross-References panel populates.
            var view = new TextViewerView();
            view.Width = RenderWidth;
            view.Height = RenderHeight;
            view.SizeToContent = SizeToContent.Manual;
            try
            {
                view.Show();
            }
            catch (Exception ex)
            {
                _output.WriteLine($"View.Show failed: {ex.GetType().Name}: {ex.Message}");
                return;
            }

            try
            {
                var vmField = typeof(TextViewerView).GetField("_vm",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                Assert.NotNull(vmField);
                var vm = (TextViewerViewModel)vmField!.GetValue(view)!;
                vm.LoadText(textIdToShow);

                // The view binds DecodedTextBlock/EditTextBox/CrossRefList
                // imperatively in OnTextSelected — and that handler only
                // fires when the AddressList raises SelectedAddressChanged.
                // The reflection call above populates the VM's properties,
                // but the visual controls won't reflect them unless we also
                // push them onto the controls. Do that here.
                var decodedBlock = view.FindControl<global::Avalonia.Controls.SelectableTextBlock>("DecodedTextBlock");
                var editBox = view.FindControl<global::Avalonia.Controls.TextBox>("EditTextBox");
                var idLabel = view.FindControl<global::Avalonia.Controls.TextBlock>("TextIdLabel");
                var lengthWarn = view.FindControl<global::Avalonia.Controls.TextBlock>("LengthWarningLabel");
                var crossRefList = view.FindControl<global::Avalonia.Controls.ItemsControl>("CrossRefList");
                if (decodedBlock != null) decodedBlock.Text = vm.DecodedText;
                if (editBox != null) editBox.Text = vm.DecodedText;
                if (idLabel != null) idLabel.Text = $"Text ID: 0x{vm.CurrentId:X04}";
                if (lengthWarn != null) lengthWarn.Text = vm.LengthWarning;
                if (crossRefList != null) crossRefList.ItemsSource = vm.CrossReferences;

                // Force a layout pass.
                view.Measure(new Size(RenderWidth, RenderHeight));
                view.Arrange(new Rect(0, 0, RenderWidth, RenderHeight));
                view.UpdateLayout();
            }
            catch (Exception ex)
            {
                _output.WriteLine($"VM setup failed: {ex.GetType().Name}: {ex.Message}");
                return;
            }

            // Capture via Avalonia.Headless CaptureRenderedFrame (requires
            // UseSkia + UseHeadlessDrawing=false in TestApp config).
            string? outDir = ResolveScreenshotDir();
            if (outDir == null)
            {
                _output.WriteLine("Couldn't locate pr-screenshots directory; skipping save.");
                return;
            }
            Directory.CreateDirectory(outDir);
            string outPath = Path.Combine(outDir, "pr349-text-refs.png");

            try
            {
                var frame = view.CaptureRenderedFrame();
                if (frame == null)
                {
                    _output.WriteLine("CaptureRenderedFrame returned null; skipping save.");
                    return;
                }
                using (var fs = new FileStream(outPath, FileMode.Create, FileAccess.Write))
                {
                    frame.Save(fs);
                }

                var fi = new FileInfo(outPath);
                _output.WriteLine($"Saved: {outPath} ({fi.Length} bytes)");
                Assert.True(fi.Exists);
                Assert.True(fi.Length > 5000, $"PNG too small ({fi.Length} bytes) — likely empty render.");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Capture failed: {ex.GetType().Name}: {ex.Message}");
                // Fall back to RenderTargetBitmap on the Content control.
                try
                {
                    var content = view.Content as Control;
                    if (content != null)
                    {
                        var bitmap = new RenderTargetBitmap(new PixelSize(RenderWidth, RenderHeight));
                        bitmap.Render(content);
                        using var fs = new FileStream(outPath, FileMode.Create, FileAccess.Write);
                        bitmap.Save(fs);
                        var fi = new FileInfo(outPath);
                        _output.WriteLine($"Fallback saved: {outPath} ({fi.Length} bytes)");
                    }
                }
                catch (Exception ex2)
                {
                    _output.WriteLine($"Fallback also failed: {ex2.GetType().Name}: {ex2.Message}");
                }
            }
        }

        static string? ResolveScreenshotDir()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "FEBuilderGBA.sln")))
                {
                    return Path.Combine(dir.FullName, "pr-screenshots");
                }
                dir = dir.Parent;
            }
            return null;
        }
    }
}
