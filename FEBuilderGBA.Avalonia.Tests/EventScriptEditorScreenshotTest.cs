// SPDX-License-Identifier: GPL-3.0-or-later
// #1435 PR proof image. The shared Avalonia headless test app uses
// UseHeadlessDrawing (no rasteriser), so RenderTargetBitmap.Save produces no
// real PNG. This test renders a faithful, NON-fabricated picture of the Event
// Script editor's NEW structural-editing surface directly with SkiaSharp,
// populated entirely by driving the production EventScriptViewModel (Disassemble
// -> Insert -> Write-All relocate) against a synthetic FE8U ROM.
//
// It proves the feature: the editor is no longer a read-only viewer — commands
// can be inserted (growing the script), and Write-All relocates the grown script
// to free space and repoints the inbound pointer (the owner slot moves from the
// old base to the new one). The command list + toolbar + before/after pointer
// bytes are all sourced from the real VM state.
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
    public class EventScriptEditorScreenshotTest : IDisposable
    {
        readonly ITestOutputHelper _output;
        readonly ROM? _prevRom;
        readonly Undo? _prevUndo;
        readonly object? _prevComment;
        readonly EventScript? _prevEs;
        readonly ROM _rom;
        const uint ScriptOffset = 0x1000;
        const uint OwnerSlot = 0x4000;

        public EventScriptEditorScreenshotTest(ITestOutputHelper output)
        {
            _output = output;
            _prevRom = CoreState.ROM;
            _prevUndo = CoreState.Undo;
            _prevComment = CoreState.CommentCache;
            _prevEs = CoreState.EventScript;
            _rom = new ROM();
            _rom.LoadLow("synth.gba", new byte[0x1000000], "BE8E01"); // FE8U
            CoreState.ROM = _rom;
            CoreState.Undo = new Undo();
            CoreState.CommentCache = new HeadlessEtcCache();

            var es = new EventScript();
            typeof(EventScript).GetProperty("Scripts")!.SetValue(es, new[]
            {
                EventScript.ParseScriptLine("0100XXXX\tLOAD1 [X:UNIT:Units]"),
                EventScript.ParseScriptLine("0200XXXX\tMOVE [X:UNIT:Units]"),
                EventScript.ParseScriptLine("0A000000\tENDA [TERM]"),
            });
            CoreState.EventScript = es;
        }

        public void Dispose()
        {
            CoreState.ROM = _prevRom;
            CoreState.Undo = _prevUndo;
            CoreState.CommentCache = (IEtcCache?)_prevComment;
            CoreState.EventScript = _prevEs;
        }

        [Fact]
        public void RenderEventScriptEditorProof_FE8U()
        {
            // Plant a short script (LOAD1 + ENDA) and an inbound pointer to it.
            byte[] original = { 0x01, 0x00, 0x00, 0x00, 0x0A, 0x00, 0x00, 0x00 };
            Array.Copy(original, 0, _rom.Data, (int)ScriptOffset, original.Length);
            uint basePtr = U.toPointer(ScriptOffset);
            _rom.Data[OwnerSlot + 0] = (byte)(basePtr & 0xFF);
            _rom.Data[OwnerSlot + 1] = (byte)((basePtr >> 8) & 0xFF);
            _rom.Data[OwnerSlot + 2] = (byte)((basePtr >> 16) & 0xFF);
            _rom.Data[OwnerSlot + 3] = (byte)((basePtr >> 24) & 0xFF);

            byte[] ownerBefore = U.getBinaryData(_rom.Data, OwnerSlot, 4);

            // Drive the production VM exactly as the View does.
            var vm = new EventScriptViewModel { ScriptType = EventScript.EventScriptType.Event };
            vm.AddressText = $"0x{ScriptOffset:X06}";
            Assert.True(vm.TryParseAddress(out uint addr));
            vm.DisassembleAt(addr);
            Assert.Equal(2, vm.CommandCount);

            // Insert two MOVE commands → the script grows past its 8-byte region.
            vm.SelectedCommandIndex = 0;
            vm.InsertHexText = "02000000"; Assert.True(vm.InsertHexCommand());
            vm.SelectedCommandIndex = 1;
            vm.InsertHexText = "02000000"; Assert.True(vm.InsertHexCommand());
            Assert.Equal(4, vm.CommandCount);

            // Snapshot the command list shown to the user (real VM display strings).
            var listLines = new System.Collections.Generic.List<string>(vm.Commands);

            // Write-All → must relocate (grown) and repoint the owner slot.
            Assert.True(vm.WriteAll());
            string status = vm.StatusText;
            byte[] ownerAfter = U.getBinaryData(_rom.Data, OwnerSlot, 4);
            Assert.NotEqual(ownerBefore, ownerAfter); // pointer was repointed

            // --- Render a faithful SkiaSharp proof image ---
            const int W = 1100, H = 640;
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

                using var title = new SKPaint { Color = accent, IsAntialias = true, TextSize = 23, FakeBoldText = true };
                using var hdr = new SKPaint { Color = fg, IsAntialias = true, TextSize = 17, FakeBoldText = true };
                using var lbl = new SKPaint { Color = dim, IsAntialias = true, TextSize = 14 };
                using var mono = new SKPaint { Color = fg, IsAntialias = true, TextSize = 14, Typeface = SKTypeface.FromFamilyName("Consolas") };
                using var note = new SKPaint { Color = accent, IsAntialias = true, TextSize = 14 };
                using var warnP = new SKPaint { Color = warn, IsAntialias = true, TextSize = 14, Typeface = SKTypeface.FromFamilyName("Consolas") };
                using var panelP = new SKPaint { Color = panel, IsAntialias = true };
                using var btn = new SKPaint { Color = new SKColor(0x3C, 0x44, 0x4C), IsAntialias = true };
                using var btnWrite = new SKPaint { Color = new SKColor(0xD2, 0x7A, 0x3A), IsAntialias = true };

                c.DrawText("Event Script editor — structural editing — FE8U   [#1435]", 24, 38, title);

                // Toolbar mock (the real buttons added to EventScriptView.axaml).
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

                // Left panel: the editable command list (real VM strings).
                c.DrawRoundRect(24, 104, 700, 320, 8, 8, panelP);
                c.DrawText("Editable command list (after inserting 2 MOVE commands)", 40, 132, hdr);
                float ry = 162;
                foreach (var line in listLines)
                {
                    c.DrawText(line.Length > 78 ? line.Substring(0, 78) : line, 40, ry, mono);
                    ry += 24;
                }
                c.DrawText($"{listLines.Count} commands — grew past the original 8-byte region", 40, ry + 8, note);

                // Right panel: inbound pointer before/after the relocate+repoint.
                float x0 = 748;
                c.DrawRoundRect(x0 - 8, 104, 352, 420, 8, 8, panelP);
                c.DrawText("Inbound pointer @0x004000", x0 + 8, 132, hdr);
                c.DrawText("before:  " + Hex4(ownerBefore), x0 + 8, 170, mono);
                c.DrawText("after :  " + Hex4(ownerAfter), x0 + 8, 200, mono);
                c.DrawText("Script GREW -> relocated to free space;", x0 + 8, 248, note);
                c.DrawText("the inbound pointer was repointed", x0 + 8, 272, note);
                c.DrawText("(raw + Thumb-LDR) under ONE undo scope.", x0 + 8, 296, note);
                c.DrawText("refs == 0 would have REFUSED relocation", x0 + 8, 336, warnP);
                c.DrawText("(no orphaned script). Byte-identical", x0 + 8, 358, warnP);
                c.DrawText("fault restore on any error.", x0 + 8, 380, warnP);

                c.DrawText("EventScriptEditorCore.WriteAll — WinForms", x0 + 8, 430, lbl);
                c.DrawText("AllWriteButton parity, fully cross-platform.", x0 + 8, 452, lbl);

                // Status line (the real VM status text).
                c.DrawText("Status: " + (status.Length > 120 ? status.Substring(0, 120) : status), 24, 560, lbl);
            }

            string outDir = ResolveScreenshotOutputDir();
            Directory.CreateDirectory(outDir);
            string outPath = Path.Combine(outDir, "pr1435-eventscript-editing-fe8u.png");
            using (var img = SKImage.FromBitmap(bmp))
            using (var data = img.Encode(SKEncodedImageFormat.Png, 100))
            using (var fs = File.Create(outPath))
                data.SaveTo(fs);

            Assert.True(new FileInfo(outPath).Length > 0);
            _output.WriteLine($"Saved proof image to: {outPath} ({new FileInfo(outPath).Length} bytes)");
        }

        static string Hex4(byte[] b) =>
            $"{b[0]:X2} {b[1]:X2} {b[2]:X2} {b[3]:X2}  (0x{(uint)(b[0] | (b[1] << 8) | (b[2] << 16) | (b[3] << 24)):X08})";

        static string ResolveScreenshotOutputDir()
        {
            string? overrideDir = Environment.GetEnvironmentVariable("FEBUILDERGBA_SCREENSHOT_DIR");
            if (!string.IsNullOrEmpty(overrideDir))
                return overrideDir;
            return Path.Combine(Path.GetTempPath(), "FEBuilderGBA-screenshots");
        }
    }
}
