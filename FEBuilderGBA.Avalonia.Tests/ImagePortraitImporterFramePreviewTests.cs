// SPDX-License-Identifier: GPL-3.0-or-later
// Source-text wiring tests for the Portrait Import Wizard's per-frame live
// preview render pane (#975, follow-up to #707 Slice A / #717).
//
// These run on every platform (no Avalonia headless host). The Core render
// math is covered exhaustively by FEBuilderGBA.Core.Tests.PortraitImportPreviewCoreTests;
// here we only assert that the wizard's AXAML grew the preview viewport and the
// code-behind wires the Core seam on the frame + crop + block NUDs, refreshes on
// load, and seeds the screenshot harness.
using System;
using System.IO;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    public class ImagePortraitImporterFramePreviewTests
    {
        static string FindRepoRoot()
        {
            string dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
                dir = Path.GetDirectoryName(dir);
            if (dir == null)
                throw new InvalidOperationException("Could not find FEBuilderGBA.sln from test base directory");
            return dir;
        }

        static string ReadView()
        {
            string path = Path.Combine(FindRepoRoot(), "FEBuilderGBA.Avalonia", "Views",
                "ImagePortraitImporterView.axaml");
            Assert.True(File.Exists(path), $"AXAML not found at {path}");
            return File.ReadAllText(path);
        }

        static string ReadCodeBehind()
        {
            string path = Path.Combine(FindRepoRoot(), "FEBuilderGBA.Avalonia", "Views",
                "ImagePortraitImporterView.axaml.cs");
            Assert.True(File.Exists(path), $"Code-behind not found at {path}");
            return File.ReadAllText(path);
        }

        [Fact]
        public void View_HasFramePreviewViewport()
        {
            string source = ReadView();
            // Dedicated per-frame preview GbaImageControl exists.
            Assert.Contains("ImagePortraitImporter_FramePreview_Image", source);
            Assert.Contains("Name=\"FramePreviewImage\"", source);
            // The note clarifying auto-quantized-source-only scope is present.
            Assert.Contains("ImagePortraitImporter_FramePreview_Note_Label", source);
            // The existing quantized-source preview is still there (not replaced).
            Assert.Contains("ImagePortraitImporter_Preview_Image", source);
        }

        // ------------------------------------------------------------------
        // #1981 — UX clarity: the AXAML must explicitly state (a) the
        // displayed Step-2 image is the SOURCE file and stays fixed until
        // another file is picked, and (b) selecting a list row only chooses
        // the Import target and never mutates ROM data until Import (Step 4).
        // ------------------------------------------------------------------

        [Fact]
        public void View_HasSourceVsTargetClarificationText()
        {
            string source = ReadView();

            Assert.Contains("ImagePortraitImporter_Preview_SourceNote_Label", source);
            Assert.Contains("ImagePortraitImporter_TargetSlot_Note_Label", source);

            // Source-note copy: fixed source preview, unaffected by row pick.
            int sourceNoteIdx = source.IndexOf("ImagePortraitImporter_Preview_SourceNote_Label", StringComparison.Ordinal);
            string sourceSlice = source.Substring(sourceNoteIdx, Math.Min(600, source.Length - sourceNoteIdx));
            Assert.Contains("SOURCE preview", sourceSlice);
            Assert.Contains("stays fixed", sourceSlice);
            Assert.Contains("InfoBannerTextBrush", sourceSlice);
            Assert.DoesNotContain("Foreground=\"DarkCyan\"", sourceSlice);

            // Target-note copy: row selection only picks the write target;
            // no ROM mutation before Import.
            int targetNoteIdx = source.IndexOf("ImagePortraitImporter_TargetSlot_Note_Label", StringComparison.Ordinal);
            string targetSlice = source.Substring(targetNoteIdx, Math.Min(600, source.Length - targetNoteIdx));
            Assert.Contains("target slot", targetSlice);
            Assert.Contains("no ROM data changes until you click Import", targetSlice);
            Assert.DoesNotContain("Foreground=\"Gray\"", targetSlice);

            int frameNoteIdx = source.IndexOf(
                "ImagePortraitImporter_FramePreview_Note_Label",
                StringComparison.Ordinal);
            string frameNoteSlice = source.Substring(
                frameNoteIdx, Math.Min(600, source.Length - frameNoteIdx));
            Assert.DoesNotContain("Foreground=\"Gray\"", frameNoteSlice);
        }

        [Fact]
        public void CodeBehind_CallsCoreRenderSeam()
        {
            string source = ReadCodeBehind();
            Assert.Contains("PortraitImportPreviewCore.RenderFramePreview", source);
            Assert.Contains("RefreshFramePreview", source);
            // Pushes the result into the dedicated preview control.
            Assert.Contains("FramePreviewImage.SetImage", source);
        }

        [Fact]
        public void CodeBehind_RefreshesOnFrameAndCropAndBlockNuds()
        {
            string source = ReadCodeBehind();
            // Frame NUD triggers a re-render.
            Assert.Contains("FrameInput.ValueChanged", source);
            // Crop + block NUDs are hooked to RefreshFramePreview (via the Hook helper).
            Assert.Contains("Hook(EyeCropXInput)", source);
            Assert.Contains("Hook(MouthCropHInput)", source);
            Assert.Contains("Hook(EyeBlockXInput)", source);
            // RefreshFramePreview is invoked after an image loads.
            Assert.Contains("RefreshFramePreview();", source);
        }

        [Fact]
        public void CodeBehind_NoOpsGracefullyWhenNoImageLoaded()
        {
            string source = ReadCodeBehind();
            // The refresh guards on a loaded image and clears the pane otherwise.
            Assert.Contains("_vm.LoadedImage", source);
            Assert.Contains("FramePreviewImage.SetImage(null)", source);
        }

        [Fact]
        public void CodeBehind_FailedImageLoadClearsPreviousImportState()
        {
            string source = ReadCodeBehind();

            int clearStart = source.IndexOf("void ClearLoadedImageState()", StringComparison.Ordinal);
            Assert.True(clearStart >= 0, "ClearLoadedImageState helper not found");
            int clearEnd = source.IndexOf("// #661: batch folder import", clearStart, StringComparison.Ordinal);
            Assert.True(clearEnd > clearStart, "ClearLoadedImageState helper boundary not found");
            string clearBody = source.Substring(clearStart, clearEnd - clearStart);

            Assert.Contains("_vm.LoadedImage = null;", clearBody);
            Assert.Contains("_preparedPreview = null;", clearBody);
            Assert.Contains("PreviewImage?.SetImage(null);", clearBody);
            Assert.Contains("FramePreviewImage?.SetImage(null);", clearBody);
            Assert.Contains("SourceFileLabel.Text = R._(\"(no file selected)\");", clearBody);
            Assert.Contains("ImageSizeLabel.Text = string.Empty;", clearBody);
            Assert.Contains("SheetModeLabel.Text = string.Empty;", clearBody);
            Assert.Contains("RefreshImportButtonState();", clearBody);

            int loadStart = source.IndexOf("void LoadImageFromPath(string filePath)", StringComparison.Ordinal);
            Assert.True(loadStart >= 0, "LoadImageFromPath not found");
            string loadBody = source.Substring(loadStart, clearStart - loadStart);

            // Null result, unsuccessful result, and unexpected exception must all
            // invalidate the previously loaded portrait before returning.
            Assert.Equal(3, CountOccurrences(loadBody, "ClearLoadedImageState();"));
        }

        [Fact]
        public void CodeBehind_SeedsScreenshotHarness()
        {
            string source = ReadCodeBehind();
            // The screenshot seed is gated behind ScreenshotAllMode (never the
            // interactive runtime) and builds a synthetic 128x112 sheet.
            Assert.Contains("App.ScreenshotAllMode", source);
            Assert.Contains("SeedFramePreviewForScreenshot", source);
            Assert.Contains("BuildSyntheticSheetLoadResult", source);
        }

        [Fact]
        public void CodeBehind_PassesFe6FlagToSeam()
        {
            string source = ReadCodeBehind();
            // FE6 path: the seam is told to skip eye overlays via the isFe6 flag,
            // derived from IsFe7Or8EntryLayout.
            Assert.Contains("IsFe7Or8EntryLayout", source);
            Assert.Contains("isFe6", source);
        }

        // ------------------------------------------------------------------
        // #1980 — frame renderer must read the SHARED prepared-preview cache
        // (color-keyed + quantized), not the raw _vm.LoadedImage buffers, and
        // that cache must never reach ImportPortrait (the ROM-write path is
        // unchanged and always uses the original LoadedImage).
        // ------------------------------------------------------------------

        [Fact]
        public void CodeBehind_FrameRendererReadsPreparedPreviewCache_NotRawLoadedImage()
        {
            string source = ReadCodeBehind();

            // RefreshFramePreview must feed the Core seam from the cached
            // prepared result (color-keyed + quantized), matching the Step-2
            // source preview's pipeline (#1980).
            Assert.Contains("_preparedPreview", source);
            Assert.Contains("RebuildPreparedPreview", source);
            Assert.Contains("PortraitImportHelper.BuildPreparedPreviewLoadResult", source);

            // The cache is rebuilt exactly once per image load — not
            // recomputed on every frame/crop NUD tweak.
            int rebuildCalls = CountOccurrences(source, "RebuildPreparedPreview();");
            Assert.Equal(2, rebuildCalls);

            // The render call itself must read from the local `prepared`
            // variable (sourced from _preparedPreview), never straight off
            // `_vm.LoadedImage`.
            int renderIdx = source.IndexOf("PortraitImportPreviewCore.RenderFramePreview(", StringComparison.Ordinal);
            Assert.True(renderIdx >= 0, "RenderFramePreview call not found");
            int sliceLen = Math.Min(300, source.Length - renderIdx);
            string slice = source.Substring(renderIdx, sliceLen);
            Assert.Contains("prepared.IndexedPixels", slice);
            Assert.Contains("prepared.GBAPalette", slice);
            Assert.DoesNotContain("_vm.LoadedImage.IndexedPixels", slice);
            Assert.DoesNotContain("_vm.LoadedImage.GBAPalette", slice);
        }

        [Fact]
        public void CodeBehind_PreparedPreviewCacheNeverPassedToImportPortrait()
        {
            string source = ReadCodeBehind();

            // ImportPortrait must keep writing the ORIGINAL loaded image so
            // ROM-write behavior is unchanged — the prepared/color-keyed
            // cache is a preview-only artifact (#1980 scope guard).
            int importIdx = source.IndexOf("ImportPortrait(", StringComparison.Ordinal);
            Assert.True(importIdx >= 0, "ImportPortrait call not found");
            int sliceLen = Math.Min(400, source.Length - importIdx);
            string slice = source.Substring(importIdx, sliceLen);
            Assert.DoesNotContain("_preparedPreview", slice);
            Assert.DoesNotContain("prepared", slice);
        }

        static int CountOccurrences(string source, string needle)
        {
            int count = 0, idx = 0;
            while ((idx = source.IndexOf(needle, idx, StringComparison.Ordinal)) >= 0)
            {
                count++;
                idx += needle.Length;
            }
            return count;
        }
    }
}
