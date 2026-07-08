// SPDX-License-Identifier: GPL-3.0-or-later
using System.IO;
using System.Reflection;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// #1917 source-scan wiring guards: the 128x112 sheet import must run the
    /// eye/mouth cell reconstruction (gated on supplied crops), and the wizard's
    /// Import button must pass the Detail-panel crop rects. The behavioural proof
    /// that the reconstruction is correct lives in the Core test
    /// <c>PortraitSheetReconstructTests</c>; these guards ensure the fix stays
    /// wired into the import path and cannot silently regress to the raw-slice
    /// behaviour that caused the in-game smear.
    /// </summary>
    public class PortraitSheetReconstructWiringTests
    {
        [Fact]
        public void ImportSheet_ReconstructsCells_GatedOnCrops()
        {
            string src = ReadSource("FEBuilderGBA.Avalonia", "Services", "PortraitImportHelper.cs");
            // The reconstruction is invoked...
            Assert.Contains("PortraitImportPreviewCore.ReconstructSheetCellsRgba(", src);
            // ...gated on crops + block coords (so drag-drop / Import PNG, which
            // pass null, keep their existing behaviour — board B2).
            Assert.Contains("crops.HasValue", src);
            // ...and BEFORE the sheet is split into D0/D4/D12.
            int recon = src.IndexOf("ReconstructSheetCellsRgba(", System.StringComparison.Ordinal);
            int split = src.IndexOf("SplitPortraitSheet(rgbaPixels", System.StringComparison.Ordinal);
            Assert.True(recon >= 0 && split >= 0 && recon < split,
                "reconstruction must run before SplitPortraitSheet");
        }

        [Fact]
        public void Wizard_ImportClick_PassesCropRects()
        {
            string src = ReadSource("FEBuilderGBA.Avalonia", "Views", "ImagePortraitImporterView.axaml.cs");
            Assert.Contains("new PortraitImportHelper.PortraitEyeMouthCrops(", src);
            Assert.Contains("EyeCropXInput", src);
            Assert.Contains("MouthCropWInput", src);
            // The crops are threaded into the import call.
            Assert.Contains("eyeBlockX, eyeBlockY, crops)", src);
        }

        private static string ReadSource(params string[] pathSegments)
        {
            string? dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            for (int i = 0; i < 10 && dir != null; i++)
            {
                if (File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
                {
                    string full = Path.Combine(dir, Path.Combine(pathSegments));
                    if (File.Exists(full))
                        return File.ReadAllText(full);
                    Assert.Fail($"Source file not found: {full}");
                }
                dir = Path.GetDirectoryName(dir);
            }
            Assert.Fail("Could not locate FEBuilderGBA.sln from test assembly");
            return string.Empty;
        }
    }
}
