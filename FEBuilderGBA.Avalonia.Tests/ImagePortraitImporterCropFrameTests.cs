// SPDX-License-Identifier: GPL-3.0-or-later
// AXAML / code-behind source-text regression tests for the Portrait Import
// Wizard's crop NUDs + frame selector + status label (#707 Slice A).
//
// These tests verify the Detail expander grew the expected AutomationIds and
// that the code-behind wires FrameInput.ValueChanged through the Core helper.
// They run on every platform (no Avalonia headless host) — the AvaloniaFact
// integration coverage lives in AutomationIdTests.cs.
using System;
using System.IO;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    public class ImagePortraitImporterCropFrameTests
    {
        static string FindRepoRoot()
        {
            string dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
            {
                dir = Path.GetDirectoryName(dir);
            }
            if (dir == null)
                throw new InvalidOperationException(
                    "Could not find FEBuilderGBA.sln from test base directory");
            return dir;
        }

        static string ReadView()
        {
            string root = FindRepoRoot();
            string path = Path.Combine(root, "FEBuilderGBA.Avalonia", "Views",
                "ImagePortraitImporterView.axaml");
            Assert.True(File.Exists(path), $"AXAML not found at {path}");
            return File.ReadAllText(path);
        }

        static string ReadCodeBehind()
        {
            string root = FindRepoRoot();
            string path = Path.Combine(root, "FEBuilderGBA.Avalonia", "Views",
                "ImagePortraitImporterView.axaml.cs");
            Assert.True(File.Exists(path), $"Code-behind not found at {path}");
            return File.ReadAllText(path);
        }

        [Theory]
        // Eye crop X/Y/W/H
        [InlineData("ImagePortraitImporter_EyeCropX_Input")]
        [InlineData("ImagePortraitImporter_EyeCropY_Input")]
        [InlineData("ImagePortraitImporter_EyeCropW_Input")]
        [InlineData("ImagePortraitImporter_EyeCropH_Input")]
        // Mouth crop X/Y/W/H
        [InlineData("ImagePortraitImporter_MouthCropX_Input")]
        [InlineData("ImagePortraitImporter_MouthCropY_Input")]
        [InlineData("ImagePortraitImporter_MouthCropW_Input")]
        [InlineData("ImagePortraitImporter_MouthCropH_Input")]
        // Frame selector + status label
        [InlineData("ImagePortraitImporter_Frame_Input")]
        [InlineData("ImagePortraitImporter_FrameStatus_Label")]
        public void DetailExpander_ContainsExpectedAutomationId(string automationId)
        {
            string source = ReadView();
            Assert.Contains(automationId, source);
        }

        [Fact]
        public void CodeBehind_WiresFrameInputThroughPortraitFrameStrings()
        {
            string source = ReadCodeBehind();

            // The handler must use the Core helper so WF parity is preserved
            // in one place.
            Assert.Contains("PortraitFrameStrings.GetWfModeString", source);
            // The handler must subscribe to FrameInput.ValueChanged.
            Assert.Contains("FrameInput.ValueChanged", source);
            // Status label must be initialized so the user sees frame 0's
            // string before they touch the NUD.
            Assert.Contains("FrameStatusLabel.Text", source);
        }

        // Regression guard: every Eye/Mouth crop NUD must mirror its
        // WinForms counterpart's Minimum / Maximum / default Value
        // attributes (source: WF ImagePortraitImporterForm.Designer.cs).
        // The invariant is that the Avalonia wizard opens with the SAME
        // crop boxes WF does, so the future preview-render port can read
        // these NUDs without re-clamping. If someone widens a NUD bound
        // or drops a default, this test fails immediately with a clear
        // pointer to the WF source.
        [Theory]
        [InlineData("EyeCropX",   "Minimum=\"0\"", "Maximum=\"32\"", "Value=\"6\"")]
        [InlineData("EyeCropY",   "Minimum=\"0\"", "Maximum=\"16\"", "Value=\"1\"")]
        [InlineData("EyeCropW",   "Minimum=\"1\"", "Maximum=\"32\"", "Value=\"23\"")]
        [InlineData("EyeCropH",   "Minimum=\"1\"", "Maximum=\"16\"", "Value=\"10\"")]
        [InlineData("MouthCropX", "Minimum=\"0\"", "Maximum=\"32\"", "Value=\"8\"")]
        [InlineData("MouthCropY", "Minimum=\"0\"", "Maximum=\"16\"", "Value=\"0\"")]
        [InlineData("MouthCropW", "Minimum=\"1\"", "Maximum=\"32\"", "Value=\"14\"")]
        [InlineData("MouthCropH", "Minimum=\"1\"", "Maximum=\"16\"", "Value=\"8\"")]
        public void CropNud_HasWfParityMinMaxDefault(string baseName, string min, string max, string value)
        {
            string source = ReadView();

            // Find the line carrying the AutomationId for this NUD and assert
            // the WF-parity attributes appear in the same NumericUpDown
            // element. We do this by checking that the substring window
            // around the AutomationId contains all three attributes.
            string autoId = $"ImagePortraitImporter_{baseName}_Input";
            int idIdx = source.IndexOf(autoId, StringComparison.Ordinal);
            Assert.True(idIdx >= 0, $"AutomationId {autoId} not found in AXAML.");

            // Look forward at most 500 chars (one NumericUpDown element fits).
            int sliceLen = Math.Min(500, source.Length - idIdx);
            string slice = source.Substring(idIdx, sliceLen);
            Assert.Contains(min,   slice);
            Assert.Contains(max,   slice);
            Assert.Contains(value, slice);
        }

        [Fact]
        public void FrameInput_HasInitialValueZero()
        {
            string source = ReadView();
            int idIdx = source.IndexOf("ImagePortraitImporter_Frame_Input", StringComparison.Ordinal);
            Assert.True(idIdx >= 0, "Frame_Input AutomationId not found");
            int sliceLen = Math.Min(500, source.Length - idIdx);
            string slice = source.Substring(idIdx, sliceLen);
            Assert.Contains("Value=\"0\"", slice);
        }

        // ------------------------------------------------------------------
        // #1985 — FrameInput must be the custom wrapping control (min
        // increase wraps to min, max decrease wraps to max), not the stock
        // Avalonia NumericUpDown which just clamps and stops at the bounds.
        // ------------------------------------------------------------------

        [Fact]
        public void FrameInput_IsWrappingNumericUpDown_WithMinZeroMaxTen()
        {
            string source = ReadView();
            int idIdx = source.IndexOf("ImagePortraitImporter_Frame_Input", StringComparison.Ordinal);
            Assert.True(idIdx >= 0, "Frame_Input AutomationId not found");

            // Find the opening tag that carries this AutomationId — search
            // backwards for the last "<controls:" before the AutomationId.
            int tagStart = source.LastIndexOf("<controls:", idIdx, StringComparison.Ordinal);
            Assert.True(tagStart >= 0, "Could not find the opening tag for FrameInput");
            string tagName = source.Substring(tagStart, Math.Min(40, source.Length - tagStart));
            Assert.StartsWith("<controls:WrappingNumericUpDown", tagName);

            int sliceLen = Math.Min(500, source.Length - idIdx);
            string slice = source.Substring(idIdx, sliceLen);
            Assert.Contains("Minimum=\"0\"", slice);
            Assert.Contains("Maximum=\"10\"", slice);
            Assert.Contains("Name=\"FrameInput\"", slice);
        }
    }
}
