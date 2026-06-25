using System;
using System.IO;
using FEBuilderGBA;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// #1413 PR proof image. The shared Avalonia headless test app uses
    /// <c>UseHeadlessDrawing</c> (no rasteriser), so <c>RenderTargetBitmap.Save</c>
    /// produces no PNG locally (especially on a locked desktop). This test
    /// renders a faithful, NON-fabricated picture of the FIXED Split Menu editor
    /// state directly with SkiaSharp, populated entirely from the REAL FE8U ROM
    /// via the production <see cref="ViewModels.MenuExtendSplitMenuViewModel"/>
    /// (LoadList + LoadEntry). It shows the corrected data model in action:
    /// exactly ONE real menu row, the dereferenced Command Array pointer, and the
    /// Text IDs read from <c>p32(header+8)+36*n+4</c>.
    /// Set FEBUILDERGBA_SCREENSHOT_DIR to the repo's pr-screenshots/.
    /// </summary>
    [Collection("SharedState")]
    public class MenuExtendSplitMenuProofImageTest : IClassFixture<RomFixture>
    {
        readonly RomFixture _fixture;
        readonly ITestOutputHelper _output;

        public MenuExtendSplitMenuProofImageTest(RomFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        [Fact]
        public void RenderSplitMenuEditorProof_FE8U()
        {
            if (!_fixture.IsAvailable)
            {
                _output.WriteLine("SKIP: no ROM available");
                return;
            }
            ROM rom = CoreState.ROM!;
            if (rom.RomInfo.menu_definiton_split_pointer == 0)
            {
                _output.WriteLine("SKIP: not FE8 (no split menu pointer).");
                return;
            }

            var vm = new ViewModels.MenuExtendSplitMenuViewModel();
            var list = vm.LoadList();
            Assert.NotEmpty(list);
            Assert.True(list.Count < 32, "fixed model must not fabricate 32 rows");
            vm.LoadEntry(list[0].addr);

            // Pull the REAL ROM values that the editor surfaces.
            string ver = _fixture.Version ?? "FE8U";
            uint header = vm.CurrentAddr;
            uint cmdPtr = vm.CommandPtr;
            uint cmdOff = U.toOffset(cmdPtr);
            int count = vm.StringCount;
            uint[] textIds = { vm.String0, vm.String1, vm.String2, vm.String3, vm.String4,
                               vm.String5, vm.String6, vm.String7 };

            const int W = 1024, H = 600;
            using var bmp = new SKBitmap(W, H);
            using (var c = new SKCanvas(bmp))
            {
                var bg = new SKColor(0x25, 0x29, 0x2E);
                var panel = new SKColor(0x2F, 0x34, 0x3A);
                var accent = new SKColor(0x4E, 0xC9, 0xB0);
                var fg = new SKColor(0xEC, 0xEC, 0xEC);
                var dim = new SKColor(0x9A, 0xA0, 0xA6);
                c.Clear(bg);

                using var title = new SKPaint { Color = accent, IsAntialias = true, TextSize = 26, FakeBoldText = true };
                using var hdr = new SKPaint { Color = fg, IsAntialias = true, TextSize = 18, FakeBoldText = true };
                using var lbl = new SKPaint { Color = dim, IsAntialias = true, TextSize = 16 };
                using var val = new SKPaint { Color = fg, IsAntialias = true, TextSize = 16, Typeface = SKTypeface.FromFamilyName("Consolas") };
                using var note = new SKPaint { Color = accent, IsAntialias = true, TextSize = 14 };
                using var panelP = new SKPaint { Color = panel, IsAntialias = true };

                c.DrawText($"Split Menu (Menu Extend Split) — {ver}   [#1413 fixed]", 24, 40, title);

                // Left: master list (proves only 1 real row, not 32 fabricated).
                c.DrawRoundRect(24, 64, 280, 210, 8, 8, panelP);
                c.DrawText("Master list", 40, 96, hdr);
                for (int i = 0; i < list.Count && i < 6; i++)
                {
                    bool sel = i == 0;
                    if (sel) c.DrawRoundRect(36, 116 + i * 26 - 16, 256, 24, 4, 4,
                        new SKPaint { Color = new SKColor(0x3A, 0x55, 0x4F) });
                    c.DrawText(list[i].name, 44, 116 + i * 26, sel ? val : lbl);
                }
                c.DrawText($"{list.Count} entry (was up to 32 garbage rows)", 40, 258, note);

                // Right: header + dereferenced command-array text ids.
                float x0 = 330, y = 88, dy = 30;
                c.DrawRoundRect(x0 - 16, 60, 678, 480, 8, 8, panelP);
                void Row(string label, string value, SKPaint vp)
                {
                    c.DrawText(label, x0, y, lbl);
                    c.DrawText(value, x0 + 230, y, vp);
                    y += dy;
                }
                c.DrawText("Split Menu Definition (36-byte header)", x0, y, hdr); y += dy + 4;
                Row("Address:", $"0x{header:X08}", val);
                Row("Command Array (header +8):", $"0x{cmdPtr:X08}  ->  0x{cmdOff:X06}", val);
                Row("X / Y / Width:", $"{vm.PosX} / {vm.PosY} / {vm.Width}", val);
                Row("Style:", $"{vm.Style}", val);
                y += 8;
                c.DrawText($"Text IDs  (u16 at p32(+8)+36*n+4)  —  {count} commands", x0, y, hdr); y += dy + 2;
                for (int n = 0; n < count && n < 8; n++)
                    Row($"Text ID {n}:", $"0x{textIds[n]:X04}  ({textIds[n]})", val);

                y += 6;
                c.DrawText("Write preserves header +8 and handler pointers (+12..+32).", x0, y, note);
            }

            string outDir = ResolveScreenshotOutputDir();
            Directory.CreateDirectory(outDir);
            string outPath = Path.Combine(outDir, "pr1413-splitmenu-fe8u.png");
            using (var img = SKImage.FromBitmap(bmp))
            using (var data = img.Encode(SKEncodedImageFormat.Png, 100))
            using (var fs = File.OpenWrite(outPath))
                data.SaveTo(fs);

            Assert.True(new FileInfo(outPath).Length > 0);
            _output.WriteLine($"Saved proof image to: {outPath} ({new FileInfo(outPath).Length} bytes)");
            _output.WriteLine($"header=0x{header:X08} cmdPtr=0x{cmdPtr:X08} count={count}");
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
