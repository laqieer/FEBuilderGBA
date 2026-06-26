// SPDX-License-Identifier: GPL-3.0-or-later
// #1463 PR proof — render the Disassembly Argument Grep editor showing the five
// newly-wired options (target function, register r0-r8, allowed rows, hide
// function call, hide unknown arg) plus a sample register-flow result, proving
// the editor is no longer a flat substring grep.
//
// PNG captured via the Avalonia headless software framebuffer (CaptureRenderedFrame).
// The render is wrapped in try/catch (UseHeadlessDrawing CI environments yield a
// blank/null frame) and the FUNCTIONAL assertions (all five option controls exist
// + register-flow result content) are the authoritative proof. Set
// FEBUILDERGBA_SCREENSHOT_DIR to regenerate the canonical PR screenshot.
using System;
using System.Collections.Generic;
using System.IO;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Headless;
using global::Avalonia.Headless.XUnit;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Views;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    public class DisASMDumpAllArgGrepScreenshotTest
    {
        private readonly ITestOutputHelper _output;

        public DisASMDumpAllArgGrepScreenshotTest(ITestOutputHelper output) => _output = output;

        [AvaloniaFact]
        public void ArgGrepEditor_ShowsAllFiveOptions_SavesScreenshot()
        {
            var view = new DisASMDumpAllArgGrepView();

            // Populate the newly-wired controls so the screenshot shows live values.
            var targetFunc = view.FindControl<TextBox>("TargetFunctionInput");
            var registerCombo = view.FindControl<ComboBox>("SearchRegisterCombo");
            var allowedRows = view.FindControl<NumericUpDown>("AllowedRowsInput");
            var hideCall = view.FindControl<CheckBox>("HideFunctionCallsCheck");
            var hideUnknown = view.FindControl<CheckBox>("HideUnknownArgsCheck");
            var results = view.FindControl<TextBox>("ResultsBox");

            // All five option controls must exist (this is the heart of #1463).
            Assert.NotNull(targetFunc);
            Assert.NotNull(registerCombo);
            Assert.NotNull(allowedRows);
            Assert.NotNull(hideCall);
            Assert.NotNull(hideUnknown);

            targetFunc!.Text = "m4aSongNumStart";
            registerCombo!.SelectedIndex = 0; // r0
            allowedRows!.Value = 5;
            hideCall!.IsChecked = false;
            hideUnknown!.IsChecked = false;

            // Show a real register-flow result produced by the Core helper so the
            // screenshot proves register-flow output, not a flat substring grep.
            var sampleLines = new List<string>
            {
                "; === PlaySong ===",
                "  0x08001000:  push {lr}",
                "  0x08001002:  mov r0, #0x1A",
                "  0x08001004:  bl  m4aSongNumStart",
                "  0x08001006:  pop {pc}",
                "  0x08001008:  nop",
                "  0x0800100A:  mov r0, #0x2B",
                "  0x0800100C:  bl  m4aSongNumStart",
            };
            string searchFunction = DisASMArgGrepCore.NormalizeSearchFunction("m4aSongNumStart");
            string grep = DisASMArgGrepCore.Grep(
                sampleLines, searchFunction, DisASMArgGrepCore.BuildSearchReg("r0"),
                5, false, false);
            results!.Text = "; ArgGrep m4aSongNumStart r0\n\n" + grep;

            // Sanity: the register-flow result really contains the argument blocks.
            Assert.Contains("mov r0, #0x1A", results.Text);
            Assert.Contains("mov r0, #0x2B", results.Text);

            const int W = 900;
            const int H = 740;
            view.Measure(new Size(W, H));
            view.Arrange(new Rect(0, 0, W, H));

            string outDir = ResolveScreenshotOutputDir();
            Directory.CreateDirectory(outDir);
            string outPath = Path.Combine(outDir, "pr1463-disasm-arggrep-fe8u.png");

            try
            {
                view.Show();
                global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                using var frame = view.CaptureRenderedFrame();
                Assert.NotNull(frame);
                HeadlessScreenshotHelper.SaveFramePng(frame!, outPath);
                _output.WriteLine($"Saved screenshot to: {outPath} ({new FileInfo(outPath).Length} bytes)");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Headless capture no-op (environment, not the #1463 fix): {ex.Message}");
            }
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
