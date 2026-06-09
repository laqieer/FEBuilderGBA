// #1023 — Palette Editor live bitmap preview + Zoom.
// Two layers of proof:
//   1. Headless [AvaloniaFact]: a render-delegate supplied to JumpTo populates
//      PreviewImage.Source, and a Zoom change rescales WITHOUT re-invoking the
//      delegate (zoom only scales the already-rendered bitmap). The no-delegate
//      path leaves the preview blank.
//   2. Source-text [Fact]: the wiring is present and uses the CORRECT 8-bit
//      packer (PaletteCore.PackToBytes), NOT the 5-bit UnitPaletteWriteCore.PackRgb555.
using System;
using System.IO;
using System.Linq;
using global::Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Views;
using FEBuilderGBA.SkiaSharp;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    public class ImagePalletPreviewTests
    {
        static void EnsureImageService()
        {
            if (CoreState.ImageService == null)
                CoreState.ImageService = new SkiaImageService();
        }

        [AvaloniaFact]
        public void JumpTo_WithRenderDelegate_PopulatesPreview_And_ZoomRescalesWithoutRerender()
        {
            EnsureImageService();
            var view = new ImagePalletView();

            int renderCalls = 0;
            Func<byte[], global::FEBuilderGBA.IImage?> stub = _ =>
            {
                renderCalls++;
                return CoreState.ImageService!.CreateImage(8, 8);
            };

            // JumpTo with the delegate renders the preview at least once. The
            // render path packs the in-memory NUDs and calls the delegate; it
            // does not require a loaded ROM (LoadEntry is best-effort/guarded).
            view.JumpTo(0x0u, maxPaletteCount: 1, defaultSelectPalette: 0,
                        paletteNames: null, renderPreview: stub);

            var preview = view.FindControl<Image>("PreviewImage");
            Assert.NotNull(preview);
            Assert.True(renderCalls >= 1, "JumpTo with a delegate must invoke the render delegate.");
            Assert.NotNull(preview!.Source);

            int callsAfterJump = renderCalls;

            // Changing Zoom must rescale the already-rendered bitmap WITHOUT
            // re-invoking the render delegate.
            var zoom = view.FindControl<ComboBox>("ZoomComboBox");
            Assert.NotNull(zoom);
            zoom!.SelectedIndex = 2; // "2x"
            Assert.Equal(callsAfterJump, renderCalls);
        }

        [AvaloniaFact]
        public void JumpTo_DoesNotInvokeRenderDelegate_PerNud_DuringSeed()
        {
            EnsureImageService();
            var view = new ImagePalletView();

            // Pre-dirty all 48 R/G/B NUDs so the subsequent JumpTo bulk-seed
            // actually CHANGES them (a no-op seed wouldn't fire Nud_ValueChanged
            // and wouldn't exercise the _seedingNuds suppression).
            foreach (int slot in Enumerable.Range(1, 16))
                foreach (string ch in new[] { "R", "G", "B" })
                {
                    var nud = view.FindControl<NumericUpDown>($"{ch}{slot}Box");
                    if (nud != null) nud.Value = 99;
                }

            int calls = 0;
            view.JumpTo(0x0u, maxPaletteCount: 1, defaultSelectPalette: 0, paletteNames: null,
                renderPreview: _ => { calls++; return CoreState.ImageService!.CreateImage(4, 4); });

            // With the _seedingNuds guard the 48-NUD seed must NOT invoke the
            // render delegate per field — exactly one render happens at the end
            // of UpdateUI. Without the fix this would be ~48 (Copilot #1087).
            Assert.True(calls >= 1, "JumpTo must render the preview at least once.");
            Assert.True(calls <= 2, $"Bulk NUD seed must not invoke the delegate per field; got {calls} calls.");
        }

        [AvaloniaFact]
        public void JumpTo_WithoutDelegate_LeavesPreviewBlank()
        {
            EnsureImageService();
            var view = new ImagePalletView();
            view.JumpTo(0x0u); // no renderPreview
            var preview = view.FindControl<Image>("PreviewImage");
            Assert.NotNull(preview);
            Assert.Null(preview!.Source);
        }

        // ---- source-text wiring (CI-safe, reformat-tolerant) ----

        static string FindProjectRoot()
        {
            foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
            {
                string dir = start;
                for (int i = 0; i < 12; i++)
                {
                    if (File.Exists(Path.Combine(dir, "FEBuilderGBA.sln"))) return dir;
                    string? parent = Path.GetDirectoryName(dir);
                    if (parent == null || parent == dir) break;
                    dir = parent;
                }
            }
            throw new InvalidOperationException("Could not find project root (FEBuilderGBA.sln)");
        }

        static string ViewFile(string name)
            => File.ReadAllText(Path.Combine(FindProjectRoot(), "FEBuilderGBA.Avalonia", "Views", name));

        [Fact]
        public void PaletteView_Uses_PackToBytes_Not_PackRgb555()
        {
            string cs = ViewFile("ImagePalletView.axaml.cs");
            Assert.Contains("PaletteCore.PackToBytes", cs);
            // The 5-bit PackRgb555 must not be CALLED (a comment may mention it
            // to explain why it's avoided — that's fine; an invocation is not).
            Assert.DoesNotContain("PackRgb555(", cs);
            Assert.Contains("RenderPreview", cs);
            Assert.Contains("renderPreview", cs);
            Assert.Contains("ApplyZoom", cs);
        }

        [Fact]
        public void PortraitView_Passes_RenderPreview_Delegate()
        {
            string cs = ViewFile("ImagePortraitView.axaml.cs");
            Assert.Contains("renderPreview:", cs);
            Assert.Contains("DrawPortraitMap", cs);
        }
    }
}
