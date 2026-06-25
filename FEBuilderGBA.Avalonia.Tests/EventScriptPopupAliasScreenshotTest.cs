// SPDX-License-Identifier: GPL-3.0-or-later
// #1422 PR proof image. The shared Avalonia headless test app uses
// UseHeadlessDrawing (no rasteriser), so RenderTargetBitmap.Save produces no
// real PNG. This test renders a faithful, NON-fabricated picture of the FIXED
// Event Script popup editor state directly with SkiaSharp, populated entirely
// by driving the production EventScriptPopupViewModel (OnCommandSelected +
// WriteCommand) against a synthetic FE8U ROM holding an ALIAS command.
//
// It proves the fix: for an alias command (a symbol char repeated at two byte
// positions — the shape installed EVENTSCRIPT_* patches use), the popup shows
// ONLY the primary operand rows (alias rows hidden), and a primary edit
// propagates to every aliased byte-position on Write (no stale alias byte).
//
// Set FEBUILDERGBA_SCREENSHOT_DIR to the repo's pr-screenshots/.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class EventScriptPopupAliasScreenshotTest : IDisposable
    {
        readonly ITestOutputHelper _output;
        readonly ROM? _prevRom;
        readonly Undo? _prevUndo;
        readonly object? _prevComment;
        readonly ROM _rom;
        const uint CmdOffset = 0x200000;

        public EventScriptPopupAliasScreenshotTest(ITestOutputHelper output)
        {
            _output = output;
            _prevRom = CoreState.ROM;
            _prevUndo = CoreState.Undo;
            _prevComment = CoreState.CommentCache;
            _rom = new ROM();
            _rom.LoadLow("synth.gba", new byte[0x1000000], "BE8E01"); // FE8U
            CoreState.ROM = _rom;
            CoreState.Undo = new Undo();
            CoreState.CommentCache = new HeadlessEtcCache();
        }

        public void Dispose()
        {
            CoreState.ROM = _prevRom;
            CoreState.Undo = _prevUndo;
            CoreState.CommentCache = (IEtcCache?)_prevComment;
        }

        [Fact]
        public void RenderEventScriptAliasProof_FE8U()
        {
            // X(byte2) Y(byte3) ... X(byte10) Y(byte11): the second X/Y are aliases.
            var script = EventScript.ParseScriptLine(
                "400DXXYY00010000400DXXYY\tGetSupportLevel [X:UNIT:Unit1][Y:UNIT:Unit2]");
            Assert.NotNull(script);

            // Plant the command bytes; give X/Y an old value so propagation is visible.
            var bytes = new byte[script.Size];
            bytes[0] = 0x40; bytes[1] = 0x0D;
            bytes[2] = 0x07; bytes[3] = 0x09;   // primary X / Y old values
            bytes[10] = 0x07; bytes[11] = 0x09; // alias X / Y old values (mirror)
            Array.Copy(bytes, 0, _rom.Data, (int)CmdOffset, bytes.Length);

            var code = new EventScript.OneCode
            {
                Script = script,
                ByteData = (byte[])bytes.Clone(),
                Comment = "",
            };

            var vm = new EventScriptPopupViewModel();
            SetPrivateList(vm, "_disassembledCodes", new List<EventScript.OneCode> { code });
            SetPrivateList(vm, "_commandOffsets", new List<uint> { CmdOffset });
            vm.Commands.Add("0x200000: GetSupportLevel  [40 0D 07 09 ...]");

            // Select -> alias rows hidden: exactly 2 primary rows (X + Y), not 4.
            vm.SelectedCommandIndex = 0;
            Assert.Equal(2, vm.CommandArgs.Count);
            var rows = vm.CommandArgs.ToList();

            byte[] before = U.getBinaryData(_rom.Data, CmdOffset, script.Size);

            // Edit the primary X row only (the alias X row isn't even shown), Write.
            var xRow = vm.CommandArgs.First(a => script.Args[a.SourceArgIndex].Symbol == 'X');
            xRow.Value = 0x42;
            Assert.True(vm.WriteCommand());

            byte[] after = U.getBinaryData(_rom.Data, CmdOffset, script.Size);
            // Proof: both primary (byte 2) and alias (byte 10) now hold 0x42.
            Assert.Equal(0x42, after[2]);
            Assert.Equal(0x42, after[10]);

            // --- Render a faithful SkiaSharp proof image of the editor state ---
            const int W = 1024, H = 600;
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

                using var title = new SKPaint { Color = accent, IsAntialias = true, TextSize = 24, FakeBoldText = true };
                using var hdr = new SKPaint { Color = fg, IsAntialias = true, TextSize = 18, FakeBoldText = true };
                using var lbl = new SKPaint { Color = dim, IsAntialias = true, TextSize = 15 };
                using var mono = new SKPaint { Color = fg, IsAntialias = true, TextSize = 15, Typeface = SKTypeface.FromFamilyName("Consolas") };
                using var note = new SKPaint { Color = accent, IsAntialias = true, TextSize = 14 };
                using var warnP = new SKPaint { Color = warn, IsAntialias = true, TextSize = 14, Typeface = SKTypeface.FromFamilyName("Consolas") };
                using var panelP = new SKPaint { Color = panel, IsAntialias = true };

                c.DrawText("Event Script popup — alias command — FE8U   [#1422 fixed]", 24, 40, title);
                c.DrawText($"Command: {vm.SelectedCommandName}", 24, 70, lbl);

                // Left panel: editable args (only primaries — alias rows hidden).
                c.DrawRoundRect(24, 84, 470, 260, 8, 8, panelP);
                c.DrawText("Editable operands (alias rows hidden)", 40, 116, hdr);
                float ry = 150;
                foreach (var r in rows)
                {
                    char sym = script.Args[r.SourceArgIndex].Symbol;
                    c.DrawText($"{r.Name}", 44, ry, mono);
                    c.DrawText($"symbol '{sym}'  @byte {r.ByteOffset}  = 0x{r.Value:X2}", 200, ry, mono);
                    ry += 30;
                }
                c.DrawText($"{rows.Count} primary rows shown (was 4 incl. 2 stale alias rows)", 44, ry + 6, note);

                // Right panel: byte view before/after proving alias propagation.
                float x0 = 520;
                c.DrawRoundRect(x0 - 8, 84, 512, 420, 8, 8, panelP);
                c.DrawText("Command bytes  (primary X @2, alias X @10)", x0 + 8, 116, hdr);
                c.DrawText("offset:", x0 + 8, 150, lbl);
                DrawBytes(c, x0 + 90, 150, before, mono, lbl);
                c.DrawText("before:", x0 + 8, 180, lbl);
                DrawBytes(c, x0 + 90, 180, before, mono, null);
                c.DrawText("after :", x0 + 8, 210, lbl);
                DrawBytes(c, x0 + 90, 210, after, mono, null, highlight: new[] { 2, 10 });

                c.DrawText("Edited PRIMARY X (byte 2) -> 0x42.", x0 + 8, 260, note);
                c.DrawText("ALIAS X (byte 10) auto-updated -> 0x42 (no stale byte).", x0 + 8, 286, note);
                c.DrawText("Before the fix the alias byte kept its old value 0x07,", x0 + 8, 320, warnP);
                c.DrawText("corrupting the command. WinForms parity restored.", x0 + 8, 342, warnP);

                c.DrawText("Mirrors EventScript.IsFixedArg (hide) +", x0 + 8, 386, lbl);
                c.DrawText("EventScriptForm.WriteAliasScriptEditSetTables (propagate).", x0 + 8, 410, lbl);
            }

            string outDir = ResolveScreenshotOutputDir();
            Directory.CreateDirectory(outDir);
            string outPath = Path.Combine(outDir, "pr1422-eventscript-alias-fe8u.png");
            using (var img = SKImage.FromBitmap(bmp))
            using (var data = img.Encode(SKEncodedImageFormat.Png, 100))
            using (var fs = File.OpenWrite(outPath))
                data.SaveTo(fs);

            Assert.True(new FileInfo(outPath).Length > 0);
            _output.WriteLine($"Saved proof image to: {outPath} ({new FileInfo(outPath).Length} bytes)");
        }

        static void DrawBytes(SKCanvas c, float x, float y, byte[] data, SKPaint mono, SKPaint? offsetPaint, int[]? highlight = null)
        {
            const float step = 26;
            for (int i = 0; i < data.Length && i < 16; i++)
            {
                float bx = x + i * step;
                if (offsetPaint != null)
                {
                    c.DrawText($"{i:X2}", bx, y, offsetPaint);
                }
                else
                {
                    bool hi = highlight != null && Array.IndexOf(highlight, i) >= 0;
                    using var p = hi
                        ? new SKPaint { Color = new SKColor(0x4E, 0xC9, 0xB0), IsAntialias = true, TextSize = mono.TextSize, FakeBoldText = true, Typeface = mono.Typeface }
                        : null;
                    c.DrawText($"{data[i]:X2}", bx, y, p ?? mono);
                }
            }
        }

        static void SetPrivateList<T>(EventScriptPopupViewModel vm, string field, List<T> value)
        {
            var f = typeof(EventScriptPopupViewModel).GetField(field,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(f);
            var list = (System.Collections.IList)f!.GetValue(vm)!;
            list.Clear();
            foreach (var item in value) list.Add(item);
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
