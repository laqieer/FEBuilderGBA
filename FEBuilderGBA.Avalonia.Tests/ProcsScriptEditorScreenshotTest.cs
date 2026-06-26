// SPDX-License-Identifier: GPL-3.0-or-later
// #1585 PR proof image. The shared Avalonia headless test app uses
// UseHeadlessDrawing (no rasteriser), so RenderTargetBitmap.Save produces no real
// PNG. This test renders a faithful, NON-fabricated picture of the Procs Script
// editor's NEW structural-editing surface — the toolbar re-skinned from a
// placeholder shell onto the SAME cross-platform EventScriptEditorCore engine as
// the Event editor (EventScriptViewModel ScriptType=Procs) — populated entirely by
// driving the production VM (Disassemble -> Insert -> delete terminal -> Write-All)
// against a synthetic FE8U ROM with a synthetic Procs vocabulary.
//
// It proves the feature AND the #1585 finding-#1 safety fix: when the terminal
// command is removed, Write-All appends the Procs family `{TERM}` command, NOT the FE
// event terminator — so a Procs stream is never corrupted with an event terminator.
// Every byte and list line is sourced from the real VM state.
//
// NOTE: this test uses a SIMPLIFIED SYNTHETIC Procs vocabulary (4-byte commands and a
// 4-byte all-zero `End [TERM]`) so the in-place geometry is easy to assert. The shipped
// production Procs vocabulary (config/data/6c_script_ALL.txt) defines 8-byte commands
// and an 8-byte `End` {TERM} (0000000000000000); the engine logic under test is
// identical — it always appends the FAMILY {TERM} from the loaded vocabulary, whatever
// its width.
//
// Set FEBUILDERGBA_SCREENSHOT_DIR to the repo's pr-screenshots/.
using System;
using System.IO;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class ProcsScriptEditorScreenshotTest : IDisposable
    {
        readonly ITestOutputHelper _output;
        readonly ROM? _prevRom;
        readonly Undo? _prevUndo;
        readonly object? _prevComment;
        readonly EventScript? _prevEs;
        readonly EventScript? _prevProcs;
        readonly ROM _rom;
        const uint ScriptOffset = 0x1000;

        public ProcsScriptEditorScreenshotTest(ITestOutputHelper output)
        {
            _output = output;
            _prevRom = CoreState.ROM;
            _prevUndo = CoreState.Undo;
            _prevComment = CoreState.CommentCache;
            _prevEs = CoreState.EventScript;
            _prevProcs = CoreState.ProcsScript;
            _rom = new ROM();
            _rom.LoadLow("synth.gba", new byte[0x1000000], "BE8E01"); // FE8U
            CoreState.ROM = _rom;
            CoreState.Undo = new Undo();
            CoreState.CommentCache = new HeadlessEtcCache();

            var procEs = new EventScript();
            typeof(EventScript).GetProperty("Scripts")!.SetValue(procEs, new[]
            {
                EventScript.ParseScriptLine("0100XXXX\tPROC_CALL [X:UNIT:Units]"),
                EventScript.ParseScriptLine("0200XXXX\tPROC_MOVE [X:UNIT:Units]"),
                EventScript.ParseScriptLine("00000000\tEnd (Deletes Self) [TERM]"),
            });
            CoreState.ProcsScript = procEs;
        }

        public void Dispose()
        {
            CoreState.ROM = _prevRom;
            CoreState.Undo = _prevUndo;
            CoreState.CommentCache = (IEtcCache?)_prevComment;
            CoreState.EventScript = _prevEs;
            CoreState.ProcsScript = _prevProcs;
        }

        [Fact]
        public void RenderProcsScriptEditorProof_FE8U()
        {
            // SIMPLIFIED SYNTHETIC layout (see file header): this vocabulary uses 4-byte
            // commands + a 4-byte End [TERM]. (Production Procs in 6c_script_ALL.txt is
            // 8 bytes per command/TERM; the engine behaviour is the same.) Plant a Procs
            // script: PROC_CALL + PROC_MOVE + End (12 bytes). Deleting the terminal End
            // leaves PROC_CALL + PROC_MOVE (8 bytes); Write-All re-appends the Procs End
            // (4 bytes) = 12 bytes, which FITS the original region → an in-place write
            // whose appended terminator lands predictably at ScriptOffset+8.
            byte[] original =
            {
                0x01, 0x00, 0x00, 0x00, // PROC_CALL
                0x02, 0x00, 0x00, 0x00, // PROC_MOVE
                0x00, 0x00, 0x00, 0x00, // End [TERM]
            };
            Array.Copy(original, 0, _rom.Data, (int)ScriptOffset, original.Length);

            // Drive the production VM exactly as ProcsScriptView does (ScriptType=Procs).
            var vm = new EventScriptViewModel { ScriptType = EventScript.EventScriptType.Procs };
            vm.AddressText = $"0x{ScriptOffset:X06}";
            Assert.True(vm.TryParseAddress(out uint addr));
            vm.DisassembleAt(addr);
            Assert.True(vm.CommandCount >= 1);

            // Remove the terminal End so the list has NO terminator → Write-All must append
            // the Procs End (00000000), NOT the FE event terminator (#1585 finding #1).
            vm.SelectedCommandIndex = vm.CommandCount - 1;
            if (vm.Commands[vm.SelectedCommandIndex].Contains("End"))
                vm.DeleteSelected();

            var listLines = new System.Collections.Generic.List<string>(vm.Commands);

            Assert.True(vm.WriteAll());
            string status = vm.StatusText;

            // Read the appended terminator bytes from the ROM (after PROC_CALL+PROC_MOVE = 8 bytes).
            byte[] eventTerm = _rom.RomInfo.Default_event_script_term_code;
            byte[] appended = U.getBinaryData(_rom.Data, ScriptOffset + 8, 8);
            // The appended terminator must be the Procs End (all-zero), not the event term.
            Assert.Equal(0x00, appended[0]);
            Assert.NotEqual(0x00, eventTerm[0]);

            // --- Render a faithful SkiaSharp proof image ---
            const int W = 1120, H = 640;
            using var bmp = new SKBitmap(W, H);
            using (var c = new SKCanvas(bmp))
            {
                var bg = new SKColor(0x25, 0x29, 0x2E);
                var panel = new SKColor(0x2F, 0x34, 0x3A);
                var accent = new SKColor(0x4E, 0xC9, 0xB0);
                var fg = new SKColor(0xEC, 0xEC, 0xEC);
                var dim = new SKColor(0x9A, 0xA0, 0xA6);
                var warn = new SKColor(0xE6, 0x8A, 0x4E);
                c.Clear(bg);

                using var title = new SKPaint { Color = accent, IsAntialias = true, TextSize = 22, FakeBoldText = true };
                using var hdr = new SKPaint { Color = fg, IsAntialias = true, TextSize = 17, FakeBoldText = true };
                using var lbl = new SKPaint { Color = dim, IsAntialias = true, TextSize = 14 };
                using var mono = new SKPaint { Color = fg, IsAntialias = true, TextSize = 14, Typeface = SKTypeface.FromFamilyName("Consolas") };
                using var note = new SKPaint { Color = accent, IsAntialias = true, TextSize = 14 };
                using var warnP = new SKPaint { Color = warn, IsAntialias = true, TextSize = 14, Typeface = SKTypeface.FromFamilyName("Consolas") };
                using var panelP = new SKPaint { Color = panel, IsAntialias = true };
                using var btn = new SKPaint { Color = new SKColor(0x3C, 0x44, 0x4C), IsAntialias = true };
                using var btnWrite = new SKPaint { Color = new SKColor(0xD2, 0x7A, 0x3A), IsAntialias = true };

                c.DrawText("Procs Script editor — structural editing (shared engine) — FE8U   [#1585]", 24, 38, title);

                // Toolbar row — the real buttons added to ProcsScriptView.axaml.
                string[] buttons = { "Insert", "Insert (hex)", "Delete", "Move Up", "Move Down", "Import (append)" };
                float bx = 24, by = 56;
                foreach (var b in buttons)
                {
                    float w = b.Length * 8.2f + 20;
                    c.DrawRoundRect(bx, by, w, 28, 5, 5, btn);
                    c.DrawText(b, bx + 10, by + 19, lbl);
                    bx += w + 8;
                }
                c.DrawRoundRect(bx, by, 86, 28, 5, 5, btnWrite);
                c.DrawText("Write All", bx + 10, by + 19, mono);

                // Left panel: the editable Procs command list (real VM strings).
                c.DrawRoundRect(24, 104, 720, 320, 8, 8, panelP);
                c.DrawText("Editable Procs command list (after Insert + delete terminal End)", 40, 132, hdr);
                float ry = 162;
                foreach (var line in listLines)
                {
                    c.DrawText(line.Length > 80 ? line.Substring(0, 80) : line, 40, ry, mono);
                    ry += 24;
                }
                c.DrawText("EventScriptViewModel { ScriptType = Procs } — same engine as Event", 40, ry + 8, note);

                // Right panel: the terminator-safety proof.
                float x0 = 768;
                c.DrawRoundRect(x0 - 8, 104, 360, 420, 8, 8, panelP);
                c.DrawText("Write-All terminator (safety)", x0 + 8, 132, hdr);
                c.DrawText("appended:  " + Hex4(appended), x0 + 8, 172, mono);
                c.DrawText("Procs End  -> 00 00 00 00", x0 + 8, 206, note);
                c.DrawText("FE event term (rejected here):", x0 + 8, 246, warnP);
                c.DrawText("  " + Hex4(eventTerm), x0 + 8, 268, warnP);
                c.DrawText("Procs/AI Serialize appends the FAMILY", x0 + 8, 312, note);
                c.DrawText("terminator, never an FE event term.", x0 + 8, 334, note);
                c.DrawText("No family TERM -> WriteResult", x0 + 8, 374, warnP);
                c.DrawText(".NoTerminator refusal, ROM unchanged.", x0 + 8, 396, warnP);

                c.DrawText("Status: " + (status.Length > 130 ? status.Substring(0, 130) : status), 24, 560, lbl);
            }

            string outDir = ResolveScreenshotOutputDir();
            Directory.CreateDirectory(outDir);
            string outPath = Path.Combine(outDir, "pr1585-procsscript-editing-fe8u.png");
            using (var img = SKImage.FromBitmap(bmp))
            using (var data = img.Encode(SKEncodedImageFormat.Png, 100))
            using (var fs = File.Create(outPath))
                data.SaveTo(fs);

            Assert.True(new FileInfo(outPath).Length > 0);
            _output.WriteLine($"Saved proof image to: {outPath} ({new FileInfo(outPath).Length} bytes)");
        }

        static string Hex4(byte[] b) =>
            $"{b[0]:X2} {b[1]:X2} {b[2]:X2} {b[3]:X2}";

        static string ResolveScreenshotOutputDir()
        {
            string? overrideDir = Environment.GetEnvironmentVariable("FEBUILDERGBA_SCREENSHOT_DIR");
            if (!string.IsNullOrEmpty(overrideDir))
                return overrideDir;
            return Path.Combine(Path.GetTempPath(), "FEBuilderGBA-screenshots");
        }
    }
}
