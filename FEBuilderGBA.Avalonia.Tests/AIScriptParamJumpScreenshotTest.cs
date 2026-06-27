// SPDX-License-Identifier: GPL-3.0-or-later
// #1600 PR proof — render the AIScript editor with a real POINTER_AI* opcode
// loaded and its disassembly row selected, so the detail-panel parameter rows
// populate and the AI-pointer parameter label shows its clickable jump
// affordance (the "->" prefix). Headless RenderTargetBitmap (works locked / in
// CI). Default output is a temp dir; set FEBUILDERGBA_SCREENSHOT_DIR to
// regenerate the canonical PR screenshot into the repo's pr-screenshots/.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.Media.Imaging;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class AIScriptParamJumpScreenshotTest
    {
        readonly ITestOutputHelper _output;
        public AIScriptParamJumpScreenshotTest(ITestOutputHelper output) => _output = output;

        // FE8U coordinate opcode: 01 00 FF 00 00 00 00 00 A9 F9 03 08 VVVVVVVV
        static byte[] CoordinateFE8U(uint gbaPtr)
        {
            var b = new byte[16];
            b[0] = 0x01; b[1] = 0x00; b[2] = 0xFF;
            b[8] = 0xA9; b[9] = 0xF9; b[10] = 0x03; b[11] = 0x08;
            b[12] = (byte)(gbaPtr & 0xFF);
            b[13] = (byte)((gbaPtr >> 8) & 0xFF);
            b[14] = (byte)((gbaPtr >> 16) & 0xFF);
            b[15] = (byte)((gbaPtr >> 24) & 0xFF);
            return b;
        }

        [AvaloniaFact]
        public void AIScript_CoordinateParamRow_ShowsJumpAffordance_SavesScreenshot()
        {
            using var env = new AiDisasmEnv();

            // A valid coordinate block so the opcode decodes and the param row
            // shows the coordinate arg (with the "->" jump affordance).
            uint blockOff = 0x300000;
            env.Rom.write_u8(blockOff + 2, 0); // non-broken coordinate
            uint coordPtr = U.toPointer(blockOff);

            env.PlantBody(CoordinateFE8U(coordPtr), out uint pointerSlotAddr);
            CoreState.ROM = env.Rom;
            CoreState.AIScript = env.AiScript;

            var view = new AIScriptView();
            var vmField = typeof(AIScriptView).GetField("_vm",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var vm = (AIScriptViewModel)vmField!.GetValue(view)!;
            vm.LoadEntry(pointerSlotAddr);

            var list = view.FindControl<ListBox>("DisassemblyList");
            var addressBox = view.FindControl<NumericUpDown>("AddressBox");
            var byteCountBox = view.FindControl<NumericUpDown>("ReadByteCountBox");
            addressBox!.Value = vm.CurrentAddr;
            byteCountBox!.Value = vm.ReadByteCount;

            var reload = typeof(AIScriptView).GetMethod("ReloadList_Click",
                BindingFlags.Instance | BindingFlags.NonPublic);
            reload!.Invoke(view, new object?[] { null, new global::Avalonia.Interactivity.RoutedEventArgs() });

            // Select the coordinate row so the detail param rows populate with the
            // jump affordance (the SelectionChanged handler calls UpdateParamRows).
            list!.SelectedIndex = 0;

            // Sanity: the coordinate param-1 label exists and is the AI-pointer jump.
            Assert.Equal(AiPointerKind.Coordinate, vm.ClassifyParam(0, 1));
            var param1 = view.FindControl<TextBlock>("Param1Label");
            Assert.NotNull(param1);
            Assert.StartsWith("->", (param1!.Text ?? "").Replace("→", "->"));

            // Best-effort visual proof (mirrors the #1414 screenshot test — the PNG
            // is not asserted; headless render may no-op in some environments).
            try
            {
                const int W = 680, H = 760;
                view.Measure(new Size(W, H));
                view.Arrange(new Rect(0, 0, W, H));
                using var bitmap = new RenderTargetBitmap(new PixelSize(W, H));
                bitmap.Render(view);

                string outDir = ResolveScreenshotOutputDir();
                Directory.CreateDirectory(outDir);
                string outPath = Path.Combine(outDir, "pr1600-aiscript-fe8u.png");
                bitmap.Save(outPath);
                _output.WriteLine($"Saved screenshot to: {outPath} ({new FileInfo(outPath).Length} bytes)");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Headless render failed (environment, not the #1600 change): {ex.Message}");
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
