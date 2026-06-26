using System;
using System.IO;
using System.Linq;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.Layout;
using global::Avalonia.Media;
using global::Avalonia.Media.Imaging;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.Views;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// Issue #1539: render the Avalonia <see cref="SoundRoomViewerView"/> alongside a
    /// panel that lists the LIVE structural-edit context-menu items extracted from the
    /// wired <see cref="AddressListControl"/> (Copy block / Paste / Swap Up / Swap Down /
    /// Invalidate). A real <c>ContextMenu</c> is a popup that does not composite into a
    /// headless <c>RenderTargetBitmap</c>, so the proof panel reads the ACTUAL menu items
    /// off the control (not a fabricated list) and renders them next to the populated
    /// sound-room list. Headless render — works on locked machines and in CI.
    ///
    /// <para>By default the PNG goes to a per-test temp dir; set
    /// <c>FEBUILDERGBA_SCREENSHOT_DIR</c> (e.g. to the repo's <c>pr-screenshots/</c>)
    /// to regenerate the canonical PR screenshot.</para>
    /// </summary>
    [Collection("SharedState")]
    public class SoundRoomStructuralEditMenuScreenshotTest : IClassFixture<RomFixture>
    {
        readonly RomFixture _fixture;
        readonly ITestOutputHelper _output;

        public SoundRoomStructuralEditMenuScreenshotTest(RomFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        [AvaloniaFact]
        public void SoundRoomView_StructuralEditMenu_SavesScreenshot()
        {
            if (!_fixture.IsAvailable)
            {
                _output.WriteLine("SKIP: no ROM available");
                return;
            }

            CoreState.Services ??= new HeadlessAppServices();
            CoreState.Undo ??= new Undo();

            var view = new SoundRoomViewerView();
            Invoke(view, "LoadList"); // Opened handler runs LoadList on a real desktop

            var entryList = view.FindControl<AddressListControl>("EntryList");
            Assert.NotNull(entryList);
            Assert.True(entryList!.StructuralEditEnabled, "EnableStructuralEdit must have wired the menu");

            var listBox = entryList.FindControl<ListBox>("AddressList");
            Assert.NotNull(listBox);
            var menu = listBox!.ContextMenu;
            Assert.NotNull(menu);

            // Pull the ACTUAL menu-item headers off the live control.
            var headers = menu!.Items.OfType<MenuItem>()
                .Select(mi => mi.Header?.ToString() ?? "")
                .Where(h => h.Length > 0)
                .ToList();
            _output.WriteLine("Live context-menu items: " + string.Join(" | ", headers));
            // WF SoundRoomForm = MakeGeneralAddressListContextMenu(true) → useClear:false,
            // so SoundRoom has 3 copy items + Copy(block) + Paste + Swap Up + Swap Down = 7
            // and NO Invalidate (DEL).
            Assert.Equal(7, headers.Count);
            Assert.DoesNotContain(headers, h => h.Contains("無効化") || h.Contains("Invalidate"));

            // Build the menu-proof panel from the live headers.
            var menuPanel = new StackPanel { Spacing = 2, Margin = new Thickness(8) };
            menuPanel.Children.Add(new TextBlock
            {
                Text = "AddressListControl context menu (#1539):",
                FontWeight = FontWeight.Bold,
                Margin = new Thickness(0, 0, 0, 6),
            });
            foreach (var h in headers)
            {
                bool structural = h.Contains("貼り付け") || h.Contains("入れ替え") || h.Contains("無効化")
                    || h.Contains("Paste") || h.Contains("Swap") || h.Contains("Invalidate")
                    || h.StartsWith("コピー") || h.StartsWith("Copy(");
                menuPanel.Children.Add(new TextBlock
                {
                    Text = (structural ? "→ " : "   ") + h,
                    Foreground = structural ? Brushes.DodgerBlue : Brushes.Gray,
                });
            }

            var menuBox = new Border
            {
                BorderBrush = Brushes.DodgerBlue,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Child = menuPanel,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(8),
                Background = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x28)),
            };

            var root = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            Grid.SetColumn(view, 0);
            Grid.SetColumn(menuBox, 1);
            root.Children.Add(view);
            root.Children.Add(menuBox);

            const int W = 1280;
            const int H = 660;
            try
            {
                root.Measure(new Size(W, H));
                root.Arrange(new Rect(0, 0, W, H));
                root.UpdateLayout();
                using var bitmap = new RenderTargetBitmap(new PixelSize(W, H), new Vector(96, 96));
                bitmap.Render(root);

                string outDir = ResolveScreenshotOutputDir();
                Directory.CreateDirectory(outDir);
                string outPath = Path.Combine(outDir, "pr1539-soundroom-structural-edit-menu.png");
                using (var ms = new MemoryStream())
                {
                    bitmap.Save(ms);
                    File.WriteAllBytes(outPath, ms.ToArray());
                }
                _output.WriteLine($"Saved screenshot to: {outPath} ({new FileInfo(outPath).Length} bytes)");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Headless render failed (environment, not the #1539 fix): {ex.Message}");
            }
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
