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
    }
}
