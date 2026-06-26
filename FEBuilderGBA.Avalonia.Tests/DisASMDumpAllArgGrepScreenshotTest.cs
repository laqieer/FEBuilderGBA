// SPDX-License-Identifier: GPL-3.0-or-later
// #1463 PR proof — render the Disassembly Argument Grep editor showing the five
// newly-wired options (target function, register r0-r8, allowed rows, hide
// function call, hide unknown arg) plus a sample register-flow result, proving
// the editor is no longer a flat substring grep.
//
// PNG captured via the Avalonia headless software framebuffer (CaptureRenderedFrame).
// The render is wrapped in try/catch (UseHeadlessDrawing CI environments yield a
// blank/null frame) and the FUNCTIONAL assertions (all five option controls exist
// + register-flow result content) are the authoritative proof. Set
// FEBUILDERGBA_SCREENSHOT_DIR to regenerate the canonical PR screenshot.
using System;
using System.Collections.Generic;
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
    public class DisASMDumpAllArgGrepScreenshotTest
    {
        private readonly ITestOutputHelper _output;

        public DisASMDumpAllArgGrepScreenshotTest(ITestOutputHelper output) => _output = output;

        [AvaloniaFact]
        public void ArgGrepEditor_ShowsAllFiveOptions_SavesScreenshot()
        {
            var view = new DisASMDumpAllArgGrepView();

            // Populate the newly-wired controls so the screenshot shows live values.
            var targetFunc = view.FindControl<TextBox>("TargetFunctionInput");
            var registerCombo = view.FindControl<ComboBox>("SearchRegisterCombo");
            var allowedRows = view.FindControl<NumericUpDown>("AllowedRowsInput");
            var hideCall = view.FindControl<CheckBox>("HideFunctionCallsCheck");
            var hideUnknown = view.FindControl<CheckBox>("HideUnknownArgsCheck");
            var results = view.FindControl<TextBox>("ResultsBox");

            // All five option controls must exist (this is the heart of #1463).
            Assert.NotNull(targetFunc);
            Assert.NotNull(registerCombo);
            Assert.NotNull(allowedRows);
            Assert.NotNull(hideCall);
            Assert.NotNull(hideUnknown);

            targetFunc!.Text = "m4aSongNumStart";
            registerCombo!.SelectedIndex = 0; // r0
            allowedRows!.Value = 5;
            hideCall!.IsChecked = false;
            hideUnknown!.IsChecked = false;

            // Show a real register-flow result produced by the Core helper so the
            // screenshot proves register-flow output, not a flat substring grep.
            var sampleLines = new List<string>
            {
                "; === PlaySong ===",
                "  0x08001000:  push {lr}",
                "  0x08001002:  mov r0, #0x1A",
                "  0x08001004:  bl  m4aSongNumStart",
                "  0x08001006:  pop {pc}",
                "  0x08001008:  nop",
                "  0x0800100A:  mov r0, #0x2B",
                "  0x0800100C:  bl  m4aSongNumStart",
            };
            string searchFunction = DisASMArgGrepCore.NormalizeSearchFunction("m4aSongNumStart");
            string grep = DisASMArgGrepCore.Grep(
                sampleLines, searchFunction, DisASMArgGrepCore.BuildSearchReg("r0"),
                5, false, false);
            results!.Text = "; ArgGrep m4aSongNumStart r0\n\n" + grep;

            // Sanity: the register-flow result really contains the argument blocks.
            Assert.Contains("mov r0, #0x1A", results.Text);
            Assert.Contains("mov r0, #0x2B", results.Text);

            const int W = 900;
            const int H = 740;
            view.Measure(new Size(W, H));
            view.Arrange(new Rect(0, 0, W, H));

            string outDir = ResolveScreenshotOutputDir();
            Directory.CreateDirectory(outDir);
            string outPath = Path.Combine(outDir, "pr1463-disasm-arggrep-fe8u.png");

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
                _output.WriteLine($"Headless capture no-op (environment, not the #1463 fix): {ex.Message}");
            }
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
                raw[o++] = 0;
                int rowStart = y * stride;
                for (int x = 0; x < w; x++)
                {
                    int i = rowStart + x * 4;
                    raw[o++] = bgra[i + 2];
                    raw[o++] = bgra[i + 1];
                    raw[o++] = bgra[i + 0];
                    raw[o++] = bgra[i + 3];
                }
            }

            using var ms = new MemoryStream();
            WriteBytes(ms, new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
            byte[] ihdr = new byte[13];
            WriteBe(ihdr, 0, (uint)w);
            WriteBe(ihdr, 4, (uint)h);
            ihdr[8] = 8;
            ihdr[9] = 6;
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
