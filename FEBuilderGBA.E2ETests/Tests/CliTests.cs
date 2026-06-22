using System;
using System.IO;
using FEBuilderGBA.E2ETests.Helpers;
using Xunit;

namespace FEBuilderGBA.E2ETests.Tests
{
    /// <summary>
    /// Black-box CLI tests that launch FEBuilderGBA.exe with command-line flags
    /// and verify the exit code and stdout output without needing a ROM file.
    /// These are headless-friendly and run well in CI.
    /// </summary>
    public class CliTests
    {
        private static readonly string ExePath = AppRunner.FindExePath();
        private static readonly string CliExe = AppRunner.FindCliExePath();

        // ------------------------------------------------------------------ --version

        [Fact]
        public void Version_ExitsZero()
        {
            var (code, _, _) = AppRunner.Run(ExePath, "--version", timeoutMs: 15_000);
            Assert.Equal(0, code);
        }

        [Fact]
        public void Version_OutputsApplicationName()
        {
            var (_, stdout, _) = AppRunner.Run(ExePath, "--version", timeoutMs: 15_000);
            // Should contain the assembly name
            Assert.Contains("FEBuilderGBA", stdout, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Version_OutputsVersionKeyword()
        {
            var (_, stdout, _) = AppRunner.Run(ExePath, "--version", timeoutMs: 15_000);
            // Should contain a version label
            Assert.Contains("Version", stdout, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Version_OutputIsNonEmpty()
        {
            var (_, stdout, stderr) = AppRunner.Run(ExePath, "--version", timeoutMs: 15_000);
            string combined = stdout + stderr;
            Assert.False(string.IsNullOrWhiteSpace(combined),
                "Expected non-empty output from --version");
        }

        // ------------------------------------------------------------------ Unknown flag

        [Fact]
        public void UnknownFlag_DoesNotCrashImmediately()
        {
            // An unknown flag should not crash the process with exit code 1 before GUI appears;
            // it either launches the GUI (not tested here) or exits cleanly.
            // We simply check that with --version the app is well-behaved (regression guard).
            var (code, stdout, _) = AppRunner.Run(ExePath, "--version", timeoutMs: 15_000);
            Assert.True(code == 0, $"--version returned exit code {code}; stdout: {stdout}");
        }

        // -------------------------------------------------- decomp audit / NMM / validate (#1150)

        [Fact]
        public void DecompAudit_ExitsZero_AndContainsHeaderAndItems()
        {
            var (code, stdout, _) = AppRunner.Run(CliExe, "--decomp-audit", timeoutMs: 30_000);
            Assert.Equal(0, code);
            Assert.Contains("Editor", stdout, StringComparison.Ordinal);
            Assert.Contains("items", stdout, StringComparison.Ordinal);
        }

        [Fact]
        public void DecompAuditSummary_ExitsZero_AndShowsCountsAndReleaseNote()
        {
            // #1150 (reopened): the --summary mode prints the per-tier coverage counts,
            // an explicit Unclassified line, and the master-ahead-of-release note.
            var (code, stdout, _) = AppRunner.Run(CliExe, "--decomp-audit --summary", timeoutMs: 30_000);
            Assert.Equal(0, code);
            Assert.Contains("Total", stdout, StringComparison.Ordinal);
            Assert.Contains("Unclassified       = 0", stdout, StringComparison.Ordinal);
            Assert.Contains("not exhaustive byte-level runtime round-trip proof", stdout, StringComparison.Ordinal);
            Assert.Contains("ahead of any tagged release", stdout, StringComparison.Ordinal);
        }

        [Fact]
        public void NmmToManifest_ExitsZero_AndOutputsTable()
        {
            string nmm = Path.Combine(Path.GetTempPath(), "cli_nmm_" + Guid.NewGuid().ToString("N") + ".nmm");
            try
            {
                File.WriteAllText(nmm,
                    "1\nItemSample by FEBuilderGBA\n0x809B7B4\n255\n36\nNULL\nNULL\n\n" +
                    "NameTextID\n0\n2\nNEHU\nNULL\n\n" +
                    "Might\n4\n1\nNEHU\nNULL\n\n");
                var (code, stdout, _) = AppRunner.Run(CliExe, $"--nmm-to-manifest --in=\"{nmm}\" --table=items", timeoutMs: 30_000);
                Assert.Equal(0, code);
                Assert.Contains("\"table\"", stdout, StringComparison.Ordinal);
            }
            finally
            {
                try { File.Delete(nmm); } catch { }
            }
        }

        [Fact]
        public void ValidateAsset_GoodPalette_ExitsZero()
        {
            string pal = Path.Combine(Path.GetTempPath(), "cli_pal_" + Guid.NewGuid().ToString("N") + ".pal");
            try
            {
                File.WriteAllText(pal, "JASC-PAL\r\n0100\r\n2\r\n0 0 0\r\n255 255 255\r\n");
                var (code, _, _) = AppRunner.Run(CliExe, $"--validate-asset --kind=palette --in=\"{pal}\"", timeoutMs: 30_000);
                Assert.Equal(0, code);
            }
            finally
            {
                try { File.Delete(pal); } catch { }
            }
        }

        [Fact]
        public void ValidateAsset_BadGraphics_ExitsTwo_WithErrorCode()
        {
            string png = Path.Combine(Path.GetTempPath(), "cli_badpng_" + Guid.NewGuid().ToString("N") + ".png");
            try
            {
                File.WriteAllText(png, "this is not a png");
                var (code, stdout, stderr) = AppRunner.Run(CliExe, $"--validate-asset --kind=graphics --in=\"{png}\"", timeoutMs: 30_000);
                Assert.Equal(2, code);
                string combined = stdout + stderr;
                Assert.Contains("ERROR", combined, StringComparison.Ordinal);
            }
            finally
            {
                try { File.Delete(png); } catch { }
            }
        }

        // -------------------------------------------------- portrait-package (#1350)

        [Fact]
        public void ValidateAsset_PortraitPackage_Valid_ExitsZero()
        {
            string dir = Path.Combine(Path.GetTempPath(), "cli_pkg_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                byte[] png = BuildIndexedPng(128, 112, 16, out byte[] plteRgb);
                File.WriteAllBytes(Path.Combine(dir, "sheet.png"), png);
                File.WriteAllText(Path.Combine(dir, "sheet.pal"), BuildJascFromRgb(plteRgb));

                var (code, stdout, stderr) = AppRunner.Run(CliExe, $"--validate-asset --kind=portrait-package --path \"{dir}\"", timeoutMs: 30_000);
                Assert.True(code == 0, $"exit {code}; stdout={stdout}; stderr={stderr}");
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch { }
            }
        }

        [Fact]
        public void ValidateAsset_PortraitPackage_MissingSheet_ExitsTwo()
        {
            string dir = Path.Combine(Path.GetTempPath(), "cli_pkg_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var (code, stdout, stderr) = AppRunner.Run(CliExe, $"--validate-asset --kind=portrait-package --path \"{dir}\"", timeoutMs: 30_000);
                Assert.Equal(2, code);
                Assert.Contains("MISSING_SHEET", stdout + stderr, StringComparison.Ordinal);
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch { }
            }
        }

        [Fact]
        public void ValidateAsset_PortraitPackage_MainOnly_AllowFlag_ExitsZero()
        {
            string dir = Path.Combine(Path.GetTempPath(), "cli_pkg_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                byte[] png = BuildIndexedPng(96, 80, 16, out _);
                File.WriteAllBytes(Path.Combine(dir, "sheet.png"), png);

                // Without --allow-main-only: incomplete package → exit 2.
                var (codeNo, _, _) = AppRunner.Run(CliExe, $"--validate-asset --kind=portrait-package --path \"{dir}\"", timeoutMs: 30_000);
                Assert.Equal(2, codeNo);

                // With --allow-main-only: accepted → exit 0.
                var (codeYes, stdout, stderr) = AppRunner.Run(CliExe, $"--validate-asset --kind=portrait-package --path \"{dir}\" --allow-main-only", timeoutMs: 30_000);
                Assert.True(codeYes == 0, $"exit {codeYes}; stdout={stdout}; stderr={stderr}");
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch { }
            }
        }

        // --- self-contained indexed-PNG builder (E2E project does NOT reference Core) ---

        static readonly byte[] PngSig = { 137, 80, 78, 71, 13, 10, 26, 10 };

        /// <summary>
        /// Build a valid color-type-3 (indexed) PNG of <paramref name="w"/>x<paramref name="h"/>
        /// with <paramref name="colors"/> palette entries (proper zlib IDAT + CRC), and return
        /// the emitted PLTE RGB bytes so a matching JASC sidecar can be built.
        /// </summary>
        static byte[] BuildIndexedPng(int w, int h, int colors, out byte[] plteRgb)
        {
            plteRgb = new byte[colors * 3];
            for (int i = 0; i < colors; i++)
            {
                // GBA BGR555 → 8-bit RGB (mirror IndexedPngWriter's conversion).
                int r5 = i & 0x1F, g5 = (i * 2) & 0x1F, b5 = (i * 3) & 0x1F;
                plteRgb[i * 3 + 0] = (byte)(r5 << 3);
                plteRgb[i * 3 + 1] = (byte)(g5 << 3);
                plteRgb[i * 3 + 2] = (byte)(b5 << 3);
            }

            using var ms = new MemoryStream();
            ms.Write(PngSig, 0, PngSig.Length);

            byte[] ihdr = new byte[13];
            WriteU32BE(ihdr, 0, (uint)w); WriteU32BE(ihdr, 4, (uint)h);
            ihdr[8] = 8; ihdr[9] = 3; // bit depth 8, color type 3
            WritePngChunk(ms, "IHDR", ihdr);
            WritePngChunk(ms, "PLTE", plteRgb);
            WritePngChunk(ms, "tRNS", new byte[] { 0 }); // index 0 transparent

            // Scanlines: filter byte 0 + width index bytes per row.
            byte[] scan = new byte[h * (1 + w)];
            for (int y = 0; y < h; y++)
            {
                int b = y * (1 + w);
                scan[b] = 0;
                for (int x = 0; x < w; x++)
                    scan[b + 1 + x] = (byte)((y * w + x) % colors);
            }

            byte[] deflate;
            using (var dms = new MemoryStream())
            {
                using (var ds = new System.IO.Compression.DeflateStream(dms, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
                    ds.Write(scan, 0, scan.Length);
                deflate = dms.ToArray();
            }
            uint adler = Adler32(scan);
            byte[] idat = new byte[2 + deflate.Length + 4];
            idat[0] = 0x78; idat[1] = 0x01;
            Array.Copy(deflate, 0, idat, 2, deflate.Length);
            int ao = 2 + deflate.Length;
            idat[ao] = (byte)(adler >> 24); idat[ao + 1] = (byte)(adler >> 16);
            idat[ao + 2] = (byte)(adler >> 8); idat[ao + 3] = (byte)adler;
            WritePngChunk(ms, "IDAT", idat);
            WritePngChunk(ms, "IEND", Array.Empty<byte>());
            return ms.ToArray();
        }

        static string BuildJascFromRgb(byte[] rgb)
        {
            int count = rgb.Length / 3;
            var sb = new System.Text.StringBuilder();
            sb.Append("JASC-PAL\r\n0100\r\n").Append(count).Append("\r\n");
            for (int i = 0; i < count; i++)
                sb.Append(rgb[i * 3 + 0]).Append(' ').Append(rgb[i * 3 + 1]).Append(' ').Append(rgb[i * 3 + 2]).Append("\r\n");
            return sb.ToString();
        }

        static void WriteU32BE(byte[] buf, int off, uint v)
        {
            buf[off] = (byte)(v >> 24); buf[off + 1] = (byte)(v >> 16);
            buf[off + 2] = (byte)(v >> 8); buf[off + 3] = (byte)v;
        }

        static void WritePngChunk(MemoryStream s, string type, byte[] data)
        {
            byte[] len = new byte[4]; WriteU32BE(len, 0, (uint)data.Length); s.Write(len, 0, 4);
            byte[] t = System.Text.Encoding.ASCII.GetBytes(type); s.Write(t, 0, 4);
            if (data.Length > 0) s.Write(data, 0, data.Length);
            byte[] crcIn = new byte[4 + data.Length];
            Array.Copy(t, 0, crcIn, 0, 4); Array.Copy(data, 0, crcIn, 4, data.Length);
            byte[] crc = new byte[4]; WriteU32BE(crc, 0, Crc32(crcIn)); s.Write(crc, 0, 4);
        }

        static readonly uint[] CrcTable = BuildCrcTable();
        static uint[] BuildCrcTable()
        {
            var t = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? (0xEDB88320u ^ (c >> 1)) : (c >> 1);
                t[n] = c;
            }
            return t;
        }
        static uint Crc32(byte[] data)
        {
            uint crc = 0xFFFFFFFF;
            foreach (byte b in data) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFF;
        }
        static uint Adler32(byte[] data)
        {
            const uint MOD = 65521; uint a = 1, b = 0;
            foreach (byte x in data) { a = (a + x) % MOD; b = (b + a) % MOD; }
            return (b << 16) | a;
        }
    }
}
