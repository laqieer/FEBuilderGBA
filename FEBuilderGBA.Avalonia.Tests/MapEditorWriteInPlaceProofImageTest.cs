using System;
using System.IO;
using System.Linq;
using FEBuilderGBA;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    public class MapEditorWriteInPlaceProofImageTest
    {
        readonly ITestOutputHelper _output;

        public MapEditorWriteInPlaceProofImageTest(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void RenderMapEditorWriteInPlaceProofImage()
        {
            int oldRelocations = SimulateOldPerStrokeRelocations();
            var after = SimulateFixedWrites();

            const int W = 1040, H = 430;
            using var bmp = new SKBitmap(W, H);
            using (var c = new SKCanvas(bmp))
            {
                var bg = new SKColor(0x1F, 0x24, 0x2A);
                var card = new SKColor(0x2D, 0x35, 0x3D);
                var accent = new SKColor(0x67, 0xE8, 0xA6);
                var warn = new SKColor(0xFF, 0xB8, 0x6B);
                var fg = new SKColor(0xF4, 0xF4, 0xF5);
                var dim = new SKColor(0xA8, 0xB3, 0xC2);
                c.Clear(bg);

                using var title = new SKPaint { Color = accent, IsAntialias = true, TextSize = 30, FakeBoldText = true };
                using var h = new SKPaint { Color = fg, IsAntialias = true, TextSize = 19, FakeBoldText = true };
                using var text = new SKPaint { Color = fg, IsAntialias = true, TextSize = 17, Typeface = SKTypeface.FromFamilyName("Consolas") };
                using var label = new SKPaint { Color = dim, IsAntialias = true, TextSize = 15 };
                using var pass = new SKPaint { Color = accent, IsAntialias = true, TextSize = 18, FakeBoldText = true };
                using var fail = new SKPaint { Color = warn, IsAntialias = true, TextSize = 18, FakeBoldText = true };
                using var cardPaint = new SKPaint { Color = card, IsAntialias = true };

                c.DrawText("Map paint compressed-write hardening — #1846 proof", 28, 44, title);
                c.DrawText("Synthetic ROM: shared pointer preserved, repeated paint-sized writes reuse the same blob", 30, 74, label);

                DrawCard(c, cardPaint, 28, 112, 470, 220);
                c.DrawText("Before fix leak model", 52, 148, h);
                c.DrawText("Write path", 52, 188, label);
                c.DrawText("FindAndWriteData + write_p32", 190, 188, text);
                c.DrawText("Relocations / 24 strokes", 52, 226, label);
                c.DrawText(oldRelocations.ToString(), 280, 226, fail);
                c.DrawText("Old private blobs freed", 52, 264, label);
                c.DrawText("No", 280, 264, fail);

                DrawCard(c, cardPaint, 542, 112, 470, 220);
                c.DrawText("After fix", 566, 148, h);
                c.DrawText("Write path", 566, 188, label);
                c.DrawText("WriteCompressedInPlaceOrRelocate", 704, 188, text);
                c.DrawText("Leaked relocations", 566, 226, label);
                c.DrawText(after.LeakedRelocations.ToString(), 794, 226, pass);
                c.DrawText("Sentinel intact", 566, 264, label);
                c.DrawText(after.SentinelIntact ? "Yes" : "No", 794, 264, after.SentinelIntact ? pass : fail);
                c.DrawText("Shared sibling intact", 566, 302, label);
                c.DrawText(after.SharedSiblingIntact ? "Yes" : "No", 794, 302, after.SharedSiblingIntact ? pass : fail);

                c.DrawText("Result: per-stroke auto-commit keeps UX, but stops draining free space and never zeros shared blobs.", 30, H - 34, label);
            }

            using var img = SKImage.FromBitmap(bmp);
            using var data = img.Encode(SKEncodedImageFormat.Png, 100);
            byte[] pngBytes = data.ToArray();
            Assert.NotNull(pngBytes);
            Assert.True(pngBytes.Length > 0);
            Assert.True(after.SentinelIntact);
            Assert.True(after.SharedSiblingIntact);
            Assert.Equal(0, after.LeakedRelocations);

            try
            {
                string outDir = ResolveScreenshotOutputDir();
                Directory.CreateDirectory(outDir);
                string outPath = Path.Combine(outDir, "pr-map-write-1846.png");
                File.WriteAllBytes(outPath, pngBytes);
                _output.WriteLine($"Saved proof image to: {outPath} ({pngBytes.Length} bytes)");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Proof image not saved to disk (best-effort): {ex.Message}");
            }
        }

        static void DrawCard(SKCanvas c, SKPaint paint, float x, float y, float w, float h)
        {
            c.DrawRoundRect(x, y, w, h, 12, 12, paint);
        }

        static string ResolveScreenshotOutputDir()
        {
            string? overrideDir = Environment.GetEnvironmentVariable("FEBUILDERGBA_SCREENSHOT_DIR");
            if (!string.IsNullOrEmpty(overrideDir)) return overrideDir;
            return Path.Combine(Path.GetTempPath(), "FEBuilderGBA-screenshots");
        }

        static (int LeakedRelocations, bool SentinelIntact, bool SharedSiblingIntact) SimulateFixedWrites()
        {
            var rom = CreateRom();
            const uint ptr = 0x280;
            const uint siblingPtr = 0x284;
            const uint oldAddr = 0x1000;
            const uint sentinelAddr = 0x1800;
            byte[] sentinel = Enumerable.Repeat((byte)0x5C, 80).ToArray();
            byte[] oldBlob = LiteralLz77(0x22, 128);
            rom.write_p32(ptr, oldAddr);
            rom.write_p32(siblingPtr, oldAddr);
            rom.write_range(oldAddr, oldBlob);
            rom.write_range(sentinelAddr, sentinel);

            byte[] siblingBefore = rom.getBinaryData(oldAddr, (uint)oldBlob.Length);
            uint firstAddr = 0;
            int leakedRelocations = 0;
            for (int i = 0; i < 24; i++)
            {
                uint written = ImageImportCore.WriteCompressedInPlaceOrRelocate(rom, ptr, LiteralLz77((byte)(0x40 + i), 96));
                if (i == 0) firstAddr = written;
                else if (written != firstAddr) leakedRelocations++;
            }

            bool sentinelIntact = sentinel.SequenceEqual(rom.getBinaryData(sentinelAddr, (uint)sentinel.Length));
            bool siblingIntact = oldAddr == rom.p32(siblingPtr)
                && siblingBefore.SequenceEqual(rom.getBinaryData(oldAddr, (uint)siblingBefore.Length));
            return (leakedRelocations, sentinelIntact, siblingIntact);
        }

        static int SimulateOldPerStrokeRelocations()
        {
            var rom = CreateRom();
            const uint ptr = 0x288;
            uint previous = 0;
            int relocations = 0;
            for (int i = 0; i < 24; i++)
            {
                uint written = ImageImportCore.FindAndWriteData(rom, LiteralLz77((byte)(0x60 + i), 96));
                rom.write_p32(ptr, written);
                if (i > 0 && written != previous)
                    relocations++;
                previous = written;
            }
            return relocations;
        }

        static ROM CreateRom(int size = 0x8000)
        {
            byte[] data = Enumerable.Repeat((byte)0xAA, size).ToArray();
            for (int i = size / 2; i < size / 2 + 0x1000; i++)
                data[i] = 0x00;

            var rom = new ROM();
            Assert.True(rom.LoadLow("synthetic.gba", data, "NAZO"));
            return rom;
        }

        static byte[] LiteralLz77(byte seed, int uncompressedSize)
        {
            byte[] compressed = new byte[4 + ((uncompressedSize + 7) / 8) + uncompressedSize];
            compressed[0] = 0x10;
            compressed[1] = (byte)(uncompressedSize & 0xFF);
            compressed[2] = (byte)((uncompressedSize >> 8) & 0xFF);
            compressed[3] = (byte)((uncompressedSize >> 16) & 0xFF);
            int dst = 4;
            for (int written = 0; written < uncompressedSize;)
            {
                compressed[dst++] = 0x00;
                int count = Math.Min(8, uncompressedSize - written);
                for (int i = 0; i < count; i++)
                    compressed[dst++] = (byte)(seed + written + i);
                written += count;
            }
            return compressed;
        }

        static string FindRepoRoot()
        {
            string dir = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(dir))
            {
                if (File.Exists(Path.Combine(dir, "FEBuilderGBA.sln"))) return dir;
                string? parent = Directory.GetParent(dir)?.FullName;
                if (parent == dir) break;
                dir = parent ?? "";
            }
            return Directory.GetCurrentDirectory();
        }
    }
}
