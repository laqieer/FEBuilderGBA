using System;
using System.IO;
using System.Linq;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Headless;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.Media.Imaging;
using global::Avalonia.VisualTree;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// Issue #1438: functional proof that the Avalonia <see cref="EventBattleTalkFE6View"/>
    /// surfaces the FE6-only second (boss generic-conversation) battle-talk table
    /// (event_ballte_talk2_pointer). Switches the Table combo to "Boss conversation
    /// (16-byte)", asserts the secondary list loads (16-byte stride) and the
    /// event-pointer field becomes visible, then opportunistically renders a PNG.
    ///
    /// <para>The PNG is captured via the Avalonia headless software framebuffer
    /// (<see cref="HeadlessWindowExtensions.CaptureRenderedFrame"/>) and written with a
    /// dependency-free PNG encoder (no SkiaSharp). The shared TestApp uses
    /// <c>UseHeadlessDrawing</c> (no rasteriser) in CI/locked environments, so the frame
    /// may be blank/null there — the render is wrapped in try/catch and the FUNCTIONAL
    /// assertions are the authoritative proof. Set <c>FEBUILDERGBA_SCREENSHOT_DIR</c> to
    /// pick the output directory on a machine with a real rasteriser.</para>
    /// </summary>
    [Collection("SharedState")]
    public class EventBattleTalkFE6SecondaryScreenshotTest
    {
        readonly ITestOutputHelper _output;

        public EventBattleTalkFE6SecondaryScreenshotTest(ITestOutputHelper output)
        {
            _output = output;
        }

        [AvaloniaFact]
        public void EventBattleTalkFE6View_SecondaryTable_SavesScreenshot()
        {
            bool ran = false;
            RomTestHelper.WithRom("FE6", () =>
            {
                ran = true;
                CoreState.Services ??= new HeadlessAppServices();
                CoreState.Undo ??= new Undo();

                var view = new EventBattleTalkFE6View();

                // Headless does not raise Opened on the same timeline, so drive the
                // initial list load directly.
                Invoke(view, "LoadList");

                const int W = 1200;
                const int H = 760;
                view.Measure(new Size(W, H));
                view.Arrange(new Rect(0, 0, W, H));

                // Switch the Table combo to the secondary (boss-conversation) table.
                var combo = view.FindControl<ComboBox>("TableFilter");
                Assert.NotNull(combo);
                combo!.SelectedIndex = 1; // 0=Main(12-byte), 1=Boss conversation(16-byte)
                view.Measure(new Size(W, H));
                view.Arrange(new Rect(0, 0, W, H));

                var entryList = view.FindControl<AddressListControl>("EntryList");
                Assert.NotNull(entryList);
                int secondaryRows = entryList!.ItemCount;
                _output.WriteLine($"Secondary (boss-conversation) rows: {secondaryRows}");
                Assert.True(secondaryRows > 0, "secondary battle-talk table must have rows in FE6");

                // The event-pointer field must now be visible in secondary mode.
                var epLabel = view.FindControl<TextBlock>("EventPointerLabel");
                var epBox = view.FindControl<NumericUpDown>("EventPointerBox");
                Assert.NotNull(epLabel);
                Assert.NotNull(epBox);
                Assert.True(epLabel!.IsVisible, "Event Pointer label must be visible in secondary mode");
                Assert.True(epBox!.IsVisible, "Event Pointer box must be visible in secondary mode");

                // In secondary mode the second field is a chapter id (章ID), not a
                // defender unit (Copilot PR review): the label is relabeled and its
                // unit-name preview is suppressed.
                var defLabel = view.FindControl<TextBlock>("DefenderLabel");
                var defNameLabel = view.FindControl<TextBlock>("DefenderNameLabel");
                Assert.NotNull(defLabel);
                Assert.NotNull(defNameLabel);
                Assert.Contains("Chapter", defLabel!.Text ?? "");
                Assert.False(defNameLabel!.IsVisible, "Defender unit-name preview must be hidden in secondary mode");

                // Select the first secondary row so the editor panel is populated.
                entryList.SelectFirst();
                view.Measure(new Size(W, H));
                view.Arrange(new Rect(0, 0, W, H));

                string outDir = ResolveScreenshotOutputDir();
                Directory.CreateDirectory(outDir);
                string outPath = Path.Combine(outDir, "pr1438-battletalk2-fe6.png");

                // Render via the Avalonia headless software framebuffer
                // (CaptureRenderedFrame) rather than RenderTargetBitmap.Save —
                // the latter routes through SkiaSharp's PNG encoder which is
                // unavailable in some headless/locked environments. The frame is
                // copied to a managed BGRA buffer and written with a dependency-
                // free PNG encoder so the proof image is produced regardless of
                // the Skia native availability.
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
                    _output.WriteLine($"Headless capture failed (environment, not the #1438 fix): {ex.Message}");
                }
            });

            if (!ran)
                _output.WriteLine("SKIP: FE6 ROM not available (set ROMS_DIR or place roms/FE6.gba)");
        }

        // Save a WriteableBitmap (BGRA8888 / premultiplied) to a PNG using a
        // dependency-free encoder so the proof image is produced even when the
        // SkiaSharp native PNG encoder is unavailable.
        static void SavePng(global::Avalonia.Media.Imaging.WriteableBitmap bmp, string path)
        {
            int w = bmp.PixelSize.Width;
            int h = bmp.PixelSize.Height;
            int stride = w * 4;
            byte[] bgra = new byte[stride * h];
            using (var fb = bmp.Lock())
            {
                System.Runtime.InteropServices.Marshal.Copy(fb.Address, bgra, 0, bgra.Length);
            }

            // Build raw RGBA scanlines (PNG filter byte 0 per row), BGRA -> RGBA.
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
            // IHDR
            byte[] ihdr = new byte[13];
            WriteBe(ihdr, 0, (uint)w);
            WriteBe(ihdr, 4, (uint)h);
            ihdr[8] = 8;  // bit depth
            ihdr[9] = 6;  // color type RGBA
            WriteChunk(ms, "IHDR", ihdr);
            // IDAT (zlib-wrapped deflate; store via System.IO.Compression then prepend zlib header + adler)
            byte[] compressed = ZlibCompress(raw);
            WriteChunk(ms, "IDAT", compressed);
            WriteChunk(ms, "IEND", Array.Empty<byte>());
            File.WriteAllBytes(path, ms.ToArray());
        }

        static byte[] ZlibCompress(byte[] data)
        {
            using var outMs = new MemoryStream();
            outMs.WriteByte(0x78); // zlib header CMF
            outMs.WriteByte(0x01); // FLG (no dict, fastest)
            using (var ds = new System.IO.Compression.DeflateStream(outMs, System.IO.Compression.CompressionLevel.Fastest, true))
            {
                ds.Write(data, 0, data.Length);
            }
            // Adler-32 of the uncompressed data.
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

        static void Invoke(EventBattleTalkFE6View view, string method)
        {
            var m = typeof(EventBattleTalkFE6View).GetMethod(method,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                null, Type.EmptyTypes, null);
            Assert.NotNull(m);
            m!.Invoke(view, Array.Empty<object?>());
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
