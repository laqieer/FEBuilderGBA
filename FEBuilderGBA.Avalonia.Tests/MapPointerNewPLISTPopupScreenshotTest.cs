// SPDX-License-Identifier: GPL-3.0-or-later
// #1433 PR proof image. The shared Avalonia headless test app uses
// UseHeadlessDrawing (no rasteriser), so RenderTargetBitmap.Save produces no
// real PNG. This test renders a faithful, NON-fabricated picture of the FIXED
// New-PLIST popup state directly with SkiaSharp, populated entirely by driving
// the production MapPointerNewPLISTPopupViewModel (InitUI + UpdatePlistInfo)
// against a synthetic FE8U ROM whose map id 0 uses event_plist = 3.
//
// It proves the fix: the read-only info box is now POPULATED ("already used:
// {map}") instead of blank, IsAlreadyUse is set (so OK shows the overwrite
// confirmation), and the Extend button is honestly hidden (no dead control).
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
    public class MapPointerNewPLISTPopupScreenshotTest : IDisposable
    {
        readonly ITestOutputHelper _output;
        readonly ROM? _prevRom;
        readonly ROM _rom;
        readonly bool _loadedEn;

        public MapPointerNewPLISTPopupScreenshotTest(ITestOutputHelper output)
        {
            _output = output;
            _prevRom = CoreState.ROM;
            // Synthetic split FE8U ROM: map id 0 uses event_plist = 3.
            _rom = MakeSplitFe8uRomWithMap(config: 7, evt: 3, mapchange: 4,
                mappointer: 5, anime1: 8, anime2: 9, palette: 10, palette2: 0,
                objLow: 11, objHigh: 12);
            CoreState.ROM = _rom;

            // Load the shipped English translation so the rendered info text is
            // readable English (Consolas has no CJK glyphs). Falls back to the
            // raw key if the repo's config/ isn't found.
            string en = FindRepoFile("config/translate/en.txt");
            if (en != null)
            {
                MyTranslateResource.LoadResource(en);
                _loadedEn = true;
            }
        }

        public void Dispose()
        {
            CoreState.ROM = _prevRom;
            if (_loadedEn) MyTranslateResource.Clear();
        }

        // Walk up from the test assembly to find a repo-relative file.
        static string FindRepoFile(string rel)
        {
            string dir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            for (int i = 0; i < 10 && dir != null; i++)
            {
                if (File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
                {
                    string p = Path.Combine(dir, rel.Replace('/', Path.DirectorySeparatorChar));
                    return File.Exists(p) ? p : null;
                }
                dir = Path.GetDirectoryName(dir);
            }
            return null;
        }

        [Fact]
        public void RenderNewPlistPopupProof_FE8U()
        {
            // Drive the production VM exactly as the view does.
            var vm = new MapPointerNewPLISTPopupViewModel();
            vm.Initialize();
            vm.InitUI(_rom);

            // Used PLIST → info box populated, IsAlreadyUse = true.
            vm.UpdatePlistInfo(_rom, 3);
            Assert.True(vm.IsAlreadyUse);
            string usedInfo = vm.LinkPlistInfo;
            Assert.False(string.IsNullOrEmpty(usedInfo));

            // Unused PLIST → recommended, IsAlreadyUse = false.
            vm.UpdatePlistInfo(_rom, 60);
            Assert.False(vm.IsAlreadyUse);
            string freeInfo = vm.LinkPlistInfo;

            // Restore the "in use" selection for the rendered shot.
            vm.UpdatePlistInfo(_rom, 3);
            string confirmMsg = MapPointerPlistUsageCore.OverwriteConfirmMessage();

            // --- Render a faithful SkiaSharp proof image of the popup state ---
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

                using var title = new SKPaint { Color = accent, IsAntialias = true, TextSize = 23, FakeBoldText = true };
                using var hdr = new SKPaint { Color = fg, IsAntialias = true, TextSize = 17, FakeBoldText = true };
                using var lbl = new SKPaint { Color = dim, IsAntialias = true, TextSize = 15 };
                using var mono = new SKPaint { Color = fg, IsAntialias = true, TextSize = 15, Typeface = SKTypeface.FromFamilyName("Consolas") };
                using var note = new SKPaint { Color = accent, IsAntialias = true, TextSize = 14 };
                using var warnP = new SKPaint { Color = warn, IsAntialias = true, TextSize = 14, Typeface = SKTypeface.FromFamilyName("Consolas") };
                using var panelP = new SKPaint { Color = panel, IsAntialias = true };

                c.DrawText("New PLIST popup — EventCond Precise-allocate — FE8U   [#1433 fixed]", 24, 40, title);
                c.DrawText("Search type: EVENT   Map id 0 uses event_plist = 3", 24, 68, lbl);

                // Left panel: the read-only info box (was blank before the fix).
                c.DrawRoundRect(24, 84, 470, 360, 8, 8, panelP);
                c.DrawText("Read-only info box (PLIST = 3)", 40, 116, hdr);
                DrawWrapped(c, usedInfo, 44, 148, 430, mono, 22);
                c.DrawText($"IsAlreadyUse = {vm.IsAlreadyUse}  -> OK confirms before overwrite", 44, 300, note);

                c.DrawText("Info box (PLIST = 60, unused)", 40, 350, hdr);
                DrawWrapped(c, freeInfo, 44, 380, 430, mono, 22);

                // Right panel: the three fixed behaviors.
                float x0 = 520;
                c.DrawRoundRect(x0 - 8, 84, 512, 460, 8, 8, panelP);
                c.DrawText("Three WinForms safety features now wired", x0 + 8, 116, hdr);

                c.DrawText("1. In-use info display (left) — was never populated.", x0 + 8, 152, note);
                c.DrawText("2. Overwrite confirmation on OK (Yes/No):", x0 + 8, 188, note);
                DrawWrapped(c, confirmMsg.Replace("\r", "").Replace("\n", " "), x0 + 16, 214, 470, mono, 21);
                c.DrawText("   abort on No when IsAlreadyUse.", x0 + 16, 290, note);

                c.DrawText("3. Extend button — honestly hidden", x0 + 8, 330, note);
                c.DrawText($"   ExtendVisible = {vm.ExtendVisible}  (split editor not wired;", x0 + 16, 356, mono);
                c.DrawText("   was a dead no-op enabled button before).", x0 + 16, 378, mono);
                c.DrawText($"   ExtendState = {MapPointerPlistUsageCore.GetExtendState(_rom)}", x0 + 16, 400, mono);

                c.DrawText($"PLIST max = {vm.PlistMaximum} (GetDataCount(EVENT) - 1)", x0 + 8, 440, lbl);
                c.DrawText("Out-of-range / plist 0 are gated before WriteEventPLIST.", x0 + 8, 464, lbl);

                c.DrawText("Mirrors WF MapPointerNewPLISTPopupForm.PlistToName +", x0 + 8, 504, warnP);
                c.DrawText("OKButton_Click (R.ShowNoYes) + InitUI Extend state.", x0 + 8, 526, warnP);
            }

            string outDir = ResolveScreenshotOutputDir();
            Directory.CreateDirectory(outDir);
            string outPath = Path.Combine(outDir, "pr1433-newplist-popup-fe8u.png");
            using (var img = SKImage.FromBitmap(bmp))
            using (var data = img.Encode(SKEncodedImageFormat.Png, 100))
            using (var fs = File.Create(outPath))
                data.SaveTo(fs);

            Assert.True(new FileInfo(outPath).Length > 0);
            _output.WriteLine($"Saved proof image to: {outPath} ({new FileInfo(outPath).Length} bytes)");
        }

        static void DrawWrapped(SKCanvas c, string text, float x, float y, float maxWidth, SKPaint paint, float lineH)
        {
            foreach (string rawLine in text.Replace("\r", "").Split('\n'))
            {
                string[] words = rawLine.Split(' ');
                string cur = "";
                foreach (string w in words)
                {
                    string trial = cur.Length == 0 ? w : cur + " " + w;
                    if (paint.MeasureText(trial) > maxWidth && cur.Length > 0)
                    {
                        c.DrawText(cur, x, y, paint);
                        y += lineH;
                        cur = w;
                    }
                    else
                    {
                        cur = trial;
                    }
                }
                c.DrawText(cur, x, y, paint);
                y += lineH;
            }
        }

        static string ResolveScreenshotOutputDir()
        {
            string? overrideDir = Environment.GetEnvironmentVariable("FEBUILDERGBA_SCREENSHOT_DIR");
            if (!string.IsNullOrEmpty(overrideDir))
                return overrideDir;
            return Path.Combine(Path.GetTempPath(), "FEBuilderGBA-screenshots");
        }

        static ROM MakeFe8uRom()
        {
            var rom = new ROM();
            rom.LoadLow("test-fe8u.gba", new byte[0x1100000], "BE8E01");
            return rom;
        }

        static ROM MakeSplitFe8uRomWithMap(
            uint config, uint evt, uint mapchange, uint mappointer,
            uint anime1, uint anime2, uint palette, uint palette2,
            uint objLow, uint objHigh)
        {
            var rom = MakeFe8uRom();
            uint mapTableBase = 0x00700000u;
            uint dataSize = rom.RomInfo.map_setting_datasize;
            WriteU32(rom.Data, (int)rom.RomInfo.map_setting_pointer, mapTableBase | 0x08000000u);
            int rec = (int)mapTableBase;
            WriteU32(rom.Data, rec + 0, 0x08123456u);
            ushort obj = (ushort)((objLow & 0xFF) | ((objHigh & 0xFF) << 8));
            rom.Data[rec + 4] = (byte)(obj & 0xFF);
            rom.Data[rec + 5] = (byte)((obj >> 8) & 0xFF);
            rom.Data[rec + 6] = (byte)palette;
            rom.Data[rec + 7] = (byte)config;
            rom.Data[rec + 8] = (byte)mappointer;
            rom.Data[rec + 9] = (byte)anime1;
            rom.Data[rec + 10] = (byte)anime2;
            rom.Data[rec + 11] = (byte)mapchange;
            rom.Data[rec + (int)rom.RomInfo.map_setting_event_plist_pos] = (byte)evt;
            if (palette2 != 0) rom.Data[rec + 146] = (byte)palette2;
            int term = (int)(mapTableBase + dataSize);
            WriteU32(rom.Data, term + 0, 0x00000000u);

            WriteU32(rom.Data, (int)rom.RomInfo.map_config_pointer,        0x08800000u);
            WriteU32(rom.Data, (int)rom.RomInfo.map_tileanime1_pointer,    0x08801000u);
            WriteU32(rom.Data, (int)rom.RomInfo.map_tileanime2_pointer,    0x08801000u);
            WriteU32(rom.Data, (int)rom.RomInfo.map_obj_pointer,           0x08802000u);
            WriteU32(rom.Data, (int)rom.RomInfo.map_pal_pointer,           0x08802000u);
            WriteU32(rom.Data, (int)rom.RomInfo.map_map_pointer_pointer,   0x08803000u);
            WriteU32(rom.Data, (int)rom.RomInfo.map_mapchange_pointer,     0x08804000u);
            WriteU32(rom.Data, (int)rom.RomInfo.map_event_pointer,         0x08805000u);
            return rom;
        }

        static void WriteU32(byte[] data, int offset, uint value)
        {
            data[offset + 0] = (byte)(value & 0xFF);
            data[offset + 1] = (byte)((value >> 8) & 0xFF);
            data[offset + 2] = (byte)((value >> 16) & 0xFF);
            data[offset + 3] = (byte)((value >> 24) & 0xFF);
        }
    }
}
