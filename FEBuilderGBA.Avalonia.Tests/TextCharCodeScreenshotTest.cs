// #1446 PR proof — render the real TextCharCodeView with a loaded char-code entry
// so the screenshot shows the editable Char Code / Terminator spinners AND the new
// Write button (previously the editor had only a Close button, so edits never
// persisted).
//
// The PNG is captured via the Avalonia headless software framebuffer
// (HeadlessWindowExtensions.CaptureRenderedFrame) and written with a dependency-free
// PNG encoder (no SkiaSharp native PNG encoder). The shared TestApp uses
// UseHeadlessDrawing in some CI/locked environments, so the frame may be blank/null
// there — the render is wrapped in try/catch and the FUNCTIONAL assertions (Write
// button present + populated entry) are the authoritative proof. Set
// FEBUILDERGBA_SCREENSHOT_DIR to regenerate the canonical PR screenshot.
using System;
using System.IO;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Headless;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.Media.Imaging;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Views;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class TextCharCodeScreenshotTest
    {
        readonly ITestOutputHelper _output;

        public TextCharCodeScreenshotTest(ITestOutputHelper output) => _output = output;

        [AvaloniaFact]
        public void TextCharCodeView_HasWriteButton_SavesScreenshot()
        {
            bool ran = false;
            RomTestHelper.WithRom("FE8U", () =>
            {
                ran = true;

                var view = new TextCharCodeView();
                // The ctor selects the first entry, so the editor panel is populated.

                // FUNCTIONAL proof (authoritative regardless of rasteriser):
                // the Write button exists and the editor is loaded with an entry.
                var write = view.FindControl<Button>("WriteButton");
                Assert.NotNull(write);
                Assert.True(view.IsLoaded, "editor must have a selected char-code entry");

                const int W = 1172;
                const int H = 519;
                view.Measure(new Size(W, H));
                view.Arrange(new Rect(0, 0, W, H));

                string outDir = ResolveScreenshotOutputDir();
                Directory.CreateDirectory(outDir);
                string outPath = Path.Combine(outDir, "pr1446-textcharcode-fe8u.png");

                try
                {
                    view.Show();
                    global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    using var frame = view.CaptureRenderedFrame();
                    Assert.NotNull(frame);
                    SavePng(frame!, outPath);
                    _output.WriteLine($"Saved screenshot to: {outPath} ({new FileInfo(outPath).Length} bytes)");
                }
                catch (Exception ex)
                {
                    _output.WriteLine($"Headless capture no-op (environment, not the #1446 fix): {ex.Message}");
                }
            });

            if (!ran)
                _output.WriteLine("SKIP: FE8U ROM not available (set ROMS_DIR or place roms/FE8U.gba)");
        }

        // ---- dependency-free BGRA8888 WriteableBitmap -> PNG encoder ----

        static void SavePng(WriteableBitmap bmp, string path)
        {
            int w = bmp.PixelSize.Width;
            int h = bmp.PixelSize.Height;
            int stride = w * 4;
            byte[] bgra = new byte[stride * h];
            using (var fb = bmp.Lock())
            {
                int srcStride = fb.RowBytes;
                IntPtr basePtr = fb.Address;
                for (int y = 0; y < h; y++)
                {
                    IntPtr rowPtr = IntPtr.Add(basePtr, y * srcStride);
                    System.Runtime.InteropServices.Marshal.Copy(rowPtr, bgra, y * stride, stride);
                }
            }

            byte[] raw = new byte[(stride + 1) * h];
            int o = 0;
            for (int y = 0; y < h; y++)
            {
                raw[o++] = 0; // filter: none
                int rowStart = y * stride;
                for (int x = 0; x < w; x++)
                {
                    int i = rowStart + x * 4;
                    raw[o++] = bgra[i + 2]; // R
                    raw[o++] = bgra[i + 1]; // G
                    raw[o++] = bgra[i + 0]; // B
                    raw[o++] = bgra[i + 3]; // A
                }
            }

            using var ms = new MemoryStream();
            WriteBytes(ms, new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
            byte[] ihdr = new byte[13];
            WriteBe(ihdr, 0, (uint)w);
            WriteBe(ihdr, 4, (uint)h);
            ihdr[8] = 8;  // bit depth
            ihdr[9] = 6;  // color type RGBA
            WriteChunk(ms, "IHDR", ihdr);
            byte[] compressed = ZlibCompress(raw);
            WriteChunk(ms, "IDAT", compressed);
            WriteChunk(ms, "IEND", Array.Empty<byte>());
            File.WriteAllBytes(path, ms.ToArray());
        }

        static byte[] ZlibCompress(byte[] data)
        {
            using var outMs = new MemoryStream();
            outMs.WriteByte(0x78);
            outMs.WriteByte(0x01);
            using (var ds = new System.IO.Compression.DeflateStream(outMs, System.IO.Compression.CompressionLevel.Fastest, true))
            {
                ds.Write(data, 0, data.Length);
            }
            uint a = 1, b = 0;
            foreach (byte by in data) { a = (a + by) % 65521; b = (b + a) % 65521; }
            uint adler = (b << 16) | a;
            outMs.WriteByte((byte)(adler >> 24));
            outMs.WriteByte((byte)(adler >> 16));
            outMs.WriteByte((byte)(adler >> 8));
            outMs.WriteByte((byte)adler);
            return outMs.ToArray();
        }

        static void WriteChunk(Stream s, string type, byte[] data)
        {
            byte[] len = new byte[4];
            WriteBe(len, 0, (uint)data.Length);
            s.Write(len, 0, 4);
            byte[] typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
            s.Write(typeBytes, 0, 4);
            s.Write(data, 0, data.Length);
            uint crc = Crc32(typeBytes, data);
            byte[] crcb = new byte[4];
            WriteBe(crcb, 0, crc);
            s.Write(crcb, 0, 4);
        }

        static uint[]? _crcTable;
        static uint Crc32(byte[] type, byte[] data)
        {
            if (_crcTable == null)
            {
                _crcTable = new uint[256];
                for (uint n = 0; n < 256; n++)
                {
                    uint c = n;
                    for (int k = 0; k < 8; k++)
                        c = ((c & 1) != 0) ? (0xEDB88320 ^ (c >> 1)) : (c >> 1);
                    _crcTable[n] = c;
                }
            }
            uint crc = 0xFFFFFFFF;
            foreach (byte by in type) crc = _crcTable[(crc ^ by) & 0xFF] ^ (crc >> 8);
            foreach (byte by in data) crc = _crcTable[(crc ^ by) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFF;
        }

        static void WriteBe(byte[] buf, int off, uint val)
        {
            buf[off] = (byte)(val >> 24);
            buf[off + 1] = (byte)(val >> 16);
            buf[off + 2] = (byte)(val >> 8);
            buf[off + 3] = (byte)val;
        }

        static void WriteBytes(Stream s, byte[] b) => s.Write(b, 0, b.Length);

        static string ResolveScreenshotOutputDir()
        {
            string? overrideDir = Environment.GetEnvironmentVariable("FEBUILDERGBA_SCREENSHOT_DIR");
            if (!string.IsNullOrEmpty(overrideDir))
                return overrideDir;
            return Path.Combine(Path.GetTempPath(), "FEBuilderGBA-screenshots");
        }
    }
}
