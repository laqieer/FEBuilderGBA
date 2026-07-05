using System;
using System.Collections.Generic;
using System.IO;
using FEBuilderGBA;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// #1824 Slice 1 proof image: renders a faithful card of fe-info code.json
    /// symbols and the shipped-wins merge rule used by DisassemblerCore.
    /// </summary>
    public class FeInfoDisasmProofImageTest
    {
        readonly ITestOutputHelper _output;

        public FeInfoDisasmProofImageTest(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void RenderFeInfoDisasmProofImage()
        {
            string sample = """
[
  {"label":"ColorFadeTick","addr":"8000234","params":null,"return":null},
  {"label":"ClearOam","addr":"8000304","params":null,"return":null},
  {"label":"Checksum32","addr":"8000360","params":[{"type":"const void*"},{"type":"int"}],"return":{"type":"u32"}},
  {"label":"TmFillRect","addr":"80003A8","params":[{"type":"u16*"},{"type":"int"},{"type":"int"},{"type":"u16"}],"return":null}
]
""";
            Dictionary<uint, AsmMapSt> feInfo = FeInfoCodeMap.Parse(sample, "U");
            Assert.Equal("ColorFadeTick", feInfo[U.atoh("8000234")].Name);
            Assert.Equal("RET=u32, r0=const void*, r1=int", feInfo[U.atoh("8000360")].ResultAndArgs);

            var shipped = new Dictionary<uint, AsmMapSt>
            {
                [U.atoh("8000234")] = new AsmMapSt { Name = "Shipped_ColorFadeTick" },
            };
            foreach (var kv in feInfo)
            {
                if (!shipped.ContainsKey(kv.Key))
                    shipped[kv.Key] = kv.Value;
            }
            Assert.Equal("Shipped_ColorFadeTick", shipped[U.atoh("8000234")].Name);
            Assert.Equal("ClearOam", shipped[U.atoh("8000304")].Name);

            const int W = 1100, H = 620;
            using var bmp = new SKBitmap(W, H);
            using (var c = new SKCanvas(bmp))
            {
                var bg = new SKColor(0x24, 0x27, 0x2E);
                var panel = new SKColor(0x30, 0x34, 0x3B);
                var accent = new SKColor(0x7A, 0xD7, 0xFF);
                var green = new SKColor(0x75, 0xD6, 0x83);
                var yellow = new SKColor(0xF5, 0xD0, 0x6F);
                var fg = new SKColor(0xEE, 0xF1, 0xF5);
                var dim = new SKColor(0xA8, 0xB0, 0xBA);
                c.Clear(bg);

                using var title = new SKPaint { Color = accent, IsAntialias = true, TextSize = 30, FakeBoldText = true };
                using var hdr = new SKPaint { Color = fg, IsAntialias = true, TextSize = 20, FakeBoldText = true };
                using var txt = new SKPaint { Color = fg, IsAntialias = true, TextSize = 17, Typeface = SKTypeface.FromFamilyName("Consolas") };
                using var dimTxt = new SKPaint { Color = dim, IsAntialias = true, TextSize = 16 };
                using var okTxt = new SKPaint { Color = green, IsAntialias = true, TextSize = 17, Typeface = SKTypeface.FromFamilyName("Consolas") };
                using var warnTxt = new SKPaint { Color = yellow, IsAntialias = true, TextSize = 17, Typeface = SKTypeface.FromFamilyName("Consolas") };
                using var panelP = new SKPaint { Color = panel, IsAntialias = true };

                c.DrawText("fe-info code.json → Avalonia Disassembler symbols  (#1824 Slice 1)", 28, 44, title);
                c.DrawText("Source: laqieer/fe-info json/fe8/code.json (BSD-3-Clause)", 30, 76, dimTxt);

                c.DrawRoundRect(28, 104, 510, 420, 10, 10, panelP);
                c.DrawText("Parsed fe-info sample", 50, 140, hdr);
                float y = 178;
                foreach (var addr in new[] { "8000234", "8000304", "8000360", "80003A8" })
                {
                    AsmMapSt st = feInfo[U.atoh(addr)];
                    c.DrawText($"0x{U.atoh(addr):X08}", 58, y, txt);
                    c.DrawText(st.Name, 210, y, okTxt);
                    y += 28;
                    if (!string.IsNullOrEmpty(st.ResultAndArgs))
                    {
                        c.DrawText(st.ResultAndArgs, 230, y, dimTxt);
                        y += 26;
                    }
                }

                c.DrawRoundRect(570, 104, 500, 420, 10, 10, panelP);
                c.DrawText("Shipped-wins merge", 592, 140, hdr);
                c.DrawText("existing asmmap_FE8.txt", 604, 186, warnTxt);
                c.DrawText("0x08000234  Shipped_ColorFadeTick", 604, 216, txt);
                c.DrawText("+ fe-info code.json", 604, 266, okTxt);
                c.DrawText("0x08000234  ColorFadeTick   (skipped)", 604, 296, dimTxt);
                c.DrawText("0x08000304  ClearOam        (added)", 604, 326, okTxt);
                c.DrawText("0x08000360  Checksum32      (added)", 604, 356, okTxt);
                c.DrawText("Rule: if (!result.ContainsKey(addr)) result[addr] = feInfoSt;", 604, 416, txt);
                c.DrawText("DisassemblerCore.LoadSymbolMap returns the merged dictionary.", 604, 452, dimTxt);
            }

            string outDir = ResolveScreenshotOutputDir();
            try
            {
                Directory.CreateDirectory(outDir);
                string outPath = Path.Combine(outDir, "pr1824-feinfo-disasm.png");
                using (var img = SKImage.FromBitmap(bmp))
                using (var data = img.Encode(SKEncodedImageFormat.Png, 100))
                using (var fs = File.Open(outPath, FileMode.Create, FileAccess.Write))
                    data.SaveTo(fs);

                _output.WriteLine($"Saved proof image to: {outPath} ({new FileInfo(outPath).Length} bytes)");
            }
            catch (Exception ex)
            {
                _output.WriteLine("Best-effort proof image write failed: " + ex.Message);
            }
        }

        static string ResolveScreenshotOutputDir()
        {
            string? overrideDir = Environment.GetEnvironmentVariable("FEBUILDERGBA_SCREENSHOT_DIR");
            if (!string.IsNullOrEmpty(overrideDir))
                return overrideDir;
            return Path.Combine(Directory.GetCurrentDirectory(), "pr-screenshots");
        }
    }
}
