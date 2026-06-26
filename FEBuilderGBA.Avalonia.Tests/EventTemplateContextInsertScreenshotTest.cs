// SPDX-License-Identifier: GPL-3.0-or-later
// #1591 PR proof image. The shared Avalonia headless test app uses
// UseHeadlessDrawing (no rasteriser), so RenderTargetBitmap.Save produces no real
// PNG of the actual visual tree. So this test draws a VM-DRIVEN SYNTHETIC
// ILLUSTRATION with SkiaSharp: it is NOT a capture of the rendered Avalonia control
// tree and does NOT validate UI layout/styling — but every value it depicts (the
// template family, the host-context map/label state, the SUBSTITUTED bytes, the
// inserted command list, and the gate-refusal cases) is sourced from the REAL
// production Core/VM path, so the BEHAVIOUR it shows is not fabricated.
//
// It illustrates the #1591 feature: the Script Template browser's context-required
// `_COND_` template, which previously emitted NOTHING (gated), now substitutes REAL
// conditional-label ids against the OPEN Event Script editor's host context
// (EventEditorHostContext = map-id provider + label allocator) and inserts the
// substituted commands into the editor. The gate still HOLDS (no partial bytes) when
// no host is present.
//
// Set FEBUILDERGBA_SCREENSHOT_DIR to the repo's pr-screenshots/.
using System;
using System.Collections.Generic;
using System.IO;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class EventTemplateContextInsertScreenshotTest : IDisposable
    {
        readonly ITestOutputHelper _output;
        readonly ROM? _prevRom;
        readonly Undo? _prevUndo;
        readonly object? _prevComment;
        readonly EventScript? _prevEs;
        readonly string _prevBase;
        readonly ROM _rom;
        const uint ScriptOffset = 0x1000;

        public EventTemplateContextInsertScreenshotTest(ITestOutputHelper output)
        {
            _output = output;
            _prevRom = CoreState.ROM;
            _prevUndo = CoreState.Undo;
            _prevComment = CoreState.CommentCache;
            _prevEs = CoreState.EventScript;
            _prevBase = CoreState.BaseDirectory;
            _rom = new ROM();
            _rom.LoadLow("synth.gba", new byte[0x1000000], "BE8E01"); // FE8U
            CoreState.ROM = _rom;
            CoreState.Undo = new Undo();
            CoreState.CommentCache = new HeadlessEtcCache();

            // Synthetic Event vocabulary with a LABEL command carrying a
            // LABEL_CONDITIONAL arg at +2 (so the host's label allocator can detect
            // a used id), plus an ENDA terminator. Mirrors the real config form.
            var es = new EventScript();
            typeof(EventScript).GetProperty("Scripts")!.SetValue(es, new[]
            {
                EventScript.ParseScriptLine("2008XXXX\tLABEL[XXXX:LABEL_CONDITIONAL:Label]"),
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
            CoreState.BaseDirectory = _prevBase;
        }

        sealed class TestHost : IEventEditorHostContext
        {
            readonly HashSet<uint> _used;
            public TestHost(params uint[] used) { _used = new HashSet<uint>(used); }
            public bool TryGetMapID(out uint mapid) { mapid = 0; return false; } // Cond needs no map
            public bool IsUseLabelID(uint labelID) => _used.Contains(labelID);
        }

        [Fact]
        public void RenderContextInsertProof_FE8U()
        {
            // Stage a real `_COND_` template config under a temp BaseDirectory so the
            // production Core path (TryGenerateBrowserTemplateWithContext) resolves it.
            string baseDir = Path.Combine(Path.GetTempPath(), "evt-1591-shot-" + Guid.NewGuid().ToString("N"));
            string dataDir = Path.Combine(baseDir, "config", "data");
            Directory.CreateDirectory(dataDir);
            const string templateName = "template_event_COND_FLAG_DEMO_FE8.txt";
            // Two placeholders: XXXX (BEQ cond label) + the same XXXX (LABEL); a YYYY GOTO
            // exercises the two-distinct-label allocation.
            File.WriteAllText(Path.Combine(dataDir, templateName),
                "400CXXXX0C000000\t//BEQ [cond label]\n" +
                "2009YYYY\t//GOTO [label]\n" +
                "2008XXXX\t//LABEL [cond label]\n");
            CoreState.BaseDirectory = baseDir;

            try
            {
                var et = new EventTemplateCore.BrowserTemplate
                {
                    Filename = templateName,
                    Info = "Sample: if FLAG on (context-required _COND_)",
                    RequiresContext = true,
                };

                // --- GATE: with NO host the template refuses (no bytes) ---
                var rNull = EventTemplateCore.TryGenerateBrowserTemplateWithContext(_rom, et, null, out byte[] gatedBytes);
                Assert.Equal(EventTemplateCore.GenerateResult.RequiresEditorContext, rNull);
                Assert.Null(gatedBytes);

                // --- WITH host: real substitution. The host reports 0x9000 already used,
                // so the allocator picks 0x9001 (X) and 0x9002 (Y). ---
                var host = new TestHost(0x9000);
                var r = EventTemplateCore.TryGenerateBrowserTemplateWithContext(_rom, et, host, out byte[] subBytes);
                Assert.Equal(EventTemplateCore.GenerateResult.Ok, r);
                Assert.NotNull(subBytes);

                // labelX = 0x9001 -> little-endian 01 90 ; labelY = 0x9002 -> 02 90
                // Line1: 40 0C 01 90 0C 00 00 00 ; Line2: 20 09 02 90 ; Line3: 20 08 01 90
                Assert.Equal(new byte[]
                {
                    0x40,0x0C,0x01,0x90,0x0C,0x00,0x00,0x00,
                    0x20,0x09,0x02,0x90,
                    0x20,0x08,0x01,0x90,
                }, subBytes);

                // The hex line the browser shows for the substituted template.
                string subHex = U.HexDumpLiner(subBytes).Trim();

                // --- Drive the production VM: open editor, disassemble a target script,
                // build its REAL host context, and verify a host is produced. ---
                Array.Copy(new byte[] { 0x20, 0x08, 0x00, 0x90, 0x0A, 0x00, 0x00, 0x00 }, 0,
                    _rom.Data, (int)ScriptOffset, 8); // LABEL 0x9000 + ENDA
                var vm = new EventScriptViewModel { ScriptType = EventScript.EventScriptType.Event };
                vm.AddressText = $"0x{ScriptOffset:X06}";
                Assert.True(vm.TryParseAddress(out uint addr));
                vm.DisassembleAt(addr);
                var realHost = vm.BuildHostContext();
                Assert.NotNull(realHost);
                Assert.True(realHost.IsUseLabelID(0x9000)); // the loaded LABEL uses 0x9000
                // The allocator over the REAL loaded list also skips 0x9000.
                uint firstFree = EventEditorHostContext.GetUnuseLabelID(realHost, 0x9000);
                Assert.Equal(0x9001u, firstFree);

                // --- Render the proof image ---
                const int Wd = 1160, Ht = 660;
                using var bmp = new SKBitmap(Wd, Ht);
                using (var c = new SKCanvas(bmp))
                {
                    var bg = new SKColor(0x25, 0x29, 0x2E);
                    var panel = new SKColor(0x2F, 0x34, 0x3A);
                    var accent = new SKColor(0x4E, 0xC9, 0xB0);
                    var fg = new SKColor(0xEC, 0xEC, 0xEC);
                    var dim = new SKColor(0x9A, 0xA0, 0xA6);
                    var warn = new SKColor(0xE6, 0x8A, 0x4E);
                    var ok = new SKColor(0x6F, 0xC2, 0x76);
                    c.Clear(bg);

                    using var title = new SKPaint { Color = accent, IsAntialias = true, TextSize = 22, FakeBoldText = true };
                    using var hdr = new SKPaint { Color = fg, IsAntialias = true, TextSize = 17, FakeBoldText = true };
                    using var lbl = new SKPaint { Color = dim, IsAntialias = true, TextSize = 14 };
                    using var mono = new SKPaint { Color = fg, IsAntialias = true, TextSize = 14, Typeface = SKTypeface.FromFamilyName("Consolas") };
                    using var note = new SKPaint { Color = accent, IsAntialias = true, TextSize = 14 };
                    using var okP = new SKPaint { Color = ok, IsAntialias = true, TextSize = 14, Typeface = SKTypeface.FromFamilyName("Consolas") };
                    using var warnP = new SKPaint { Color = warn, IsAntialias = true, TextSize = 14, Typeface = SKTypeface.FromFamilyName("Consolas") };
                    using var panelP = new SKPaint { Color = panel, IsAntialias = true };

                    c.DrawText("Event Template — context-required insert with Alloc-Event host substitution — FE8U   [#1591]", 24, 38, title);

                    // Left panel: the template browser entry + the gate.
                    c.DrawRoundRect(24, 60, 540, 250, 8, 8, panelP);
                    c.DrawText("Script Template browser", 40, 88, hdr);
                    c.DrawText(et.Info, 40, 116, lbl);
                    c.DrawText("[insert into open editor]", 40, 140, note);
                    c.DrawText("Template config (placeholder form):", 40, 174, lbl);
                    c.DrawText("400CXXXX0C000000   // BEQ [cond label]", 40, 198, mono);
                    c.DrawText("2009YYYY           // GOTO [label]", 40, 220, mono);
                    c.DrawText("2008XXXX           // LABEL [cond label]", 40, 242, mono);
                    c.DrawText("No open editor (host == null):", 40, 278, lbl);
                    c.DrawText("REFUSED — RequiresEditorContext, 0 bytes (gate holds)", 40, 298, warnP);

                    // Right panel: the host context.
                    c.DrawRoundRect(584, 60, 552, 250, 8, 8, panelP);
                    c.DrawText("Open editor host context (EventEditorHostContext)", 600, 88, hdr);
                    c.DrawText("IsUseLabelID(0x9000) = true   (loaded LABEL uses it)", 600, 120, mono);
                    c.DrawText("GetUnuseLabelID(0x9000) -> 0x9001   (allocator skips used)", 600, 144, okP);
                    c.DrawText("XXXX  -> 0x9001  (first free conditional label)", 600, 176, okP);
                    c.DrawText("YYYY  -> 0x9002  (next free, distinct)", 600, 200, okP);
                    c.DrawText("map-required templates (PREPARATION / CALL_END_EVENT)", 600, 234, lbl);
                    c.DrawText("need TryGetMapID(out) — refuse if no map (no map-0).", 600, 256, lbl);

                    // Bottom panel: the SUBSTITUTED bytes that are inserted.
                    c.DrawRoundRect(24, 330, 1112, 250, 8, 8, panelP);
                    c.DrawText("Substituted template bytes inserted into the open Event Script editor", 40, 360, hdr);
                    c.DrawText(subHex.Length > 110 ? subHex.Substring(0, 110) : subHex, 40, 392, okP);
                    c.DrawText("400C 0190 0C000000   // BEQ  [cond 0x9001]   <- XXXX substituted", 40, 424, mono);
                    c.DrawText("2009 0290            // GOTO [label 0x9002]  <- YYYY substituted", 40, 448, mono);
                    c.DrawText("2008 0190            // LABEL [cond 0x9001]  <- XXXX substituted", 40, 472, mono);
                    c.DrawText("No XXXX / YYYY survive substitution -> safe to insert (post-subst re-scan guard).", 40, 508, note);
                    c.DrawText("EventScriptView.InsertCurrentTemplate(codes) -> review -> Write All (undo-tracked).", 40, 532, note);

                    c.DrawText("Gate holds: host==null | map-required-no-map | unknown family | residual placeholder -> 0 bytes.", 24, 620, lbl);
                }

                string outDir = ResolveScreenshotOutputDir();
                Directory.CreateDirectory(outDir);
                string outPath = Path.Combine(outDir, "pr1591-eventtemplate-context-insert-fe8u.png");
                using (var img = SKImage.FromBitmap(bmp))
                using (var data = img.Encode(SKEncodedImageFormat.Png, 100))
                using (var fs = File.Create(outPath))
                    data.SaveTo(fs);

                Assert.True(new FileInfo(outPath).Length > 0);
                _output.WriteLine($"Saved proof image to: {outPath} ({new FileInfo(outPath).Length} bytes)");
            }
            finally
            {
                try { Directory.Delete(baseDir, true); } catch { }
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
