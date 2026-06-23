using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using FEBuilderGBA.E2ETests.Helpers;
using Xunit;

namespace FEBuilderGBA.E2ETests.Tests
{
    /// <summary>
    /// Black-box E2E tests for the decomp portrait PACKAGE source-tree write-back +
    /// round-trip CLI verbs (#1374):
    /// <list type="bullet">
    ///   <item><description><c>--import-asset --kind=portrait-package</c> — validate + identity write-back into an unambiguous owner dir</description></item>
    ///   <item><description><c>--roundtrip-asset --kind=portrait-package</c> — prove byte-identical to a REQUIRED <c>--expect</c> baseline</description></item>
    /// </list>
    ///
    /// All paths are ROM-FREE. PNGs are hand-built (indexed, color type 3, filter-0
    /// scanlines) so the validator/reader accept them without referencing Core.
    /// </summary>
    public class PortraitPackageWriteBackE2ETests
    {
        static readonly string CliExe = AppRunner.FindCliExePath();

        static (int ExitCode, string Stdout, string Stderr) RunWithRetry(
            string args, int timeoutMs = 60_000, int maxAttempts = 2)
        {
            (int ExitCode, string Stdout, string Stderr) result = default;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                result = AppRunner.Run(CliExe, args, timeoutMs);
                if (result.ExitCode >= 0)
                    return result;
            }
            return result;
        }

        static string NewTempDir(string tag)
        {
            string dir = Path.Combine(Path.GetTempPath(), $"pp_e2e_{tag}_{Guid.NewGuid():N}");
            Directory.CreateDirectory(dir);
            return dir;
        }

        // ----------------------------------------------------------- hand-built indexed PNG

        static void WriteU32BE(MemoryStream ms, uint v)
        {
            ms.WriteByte((byte)(v >> 24)); ms.WriteByte((byte)(v >> 16));
            ms.WriteByte((byte)(v >> 8)); ms.WriteByte((byte)v);
        }

        static uint Crc32(byte[] data)
        {
            uint crc = 0xFFFFFFFFu;
            for (int n = 0; n < data.Length; n++)
            {
                crc ^= data[n];
                for (int k = 0; k < 8; k++)
                    crc = (crc & 1) != 0 ? (0xEDB88320u ^ (crc >> 1)) : (crc >> 1);
            }
            return crc ^ 0xFFFFFFFFu;
        }

        static void WriteChunk(MemoryStream ms, string type, byte[] data)
        {
            WriteU32BE(ms, (uint)data.Length);
            byte[] t = Encoding.ASCII.GetBytes(type);
            var crcInput = new byte[4 + data.Length];
            Array.Copy(t, 0, crcInput, 0, 4);
            Array.Copy(data, 0, crcInput, 4, data.Length);
            ms.Write(t, 0, 4);
            ms.Write(data, 0, data.Length);
            WriteU32BE(ms, Crc32(crcInput));
        }

        /// <summary>Build an indexed (color type 3) PNG, filter 0, deterministic palette + pixels.</summary>
        static byte[] BuildIndexedPng(int w, int h, int colors, int seed)
        {
            // PLTE: distinct RGB per entry, varied by seed so two packages can differ.
            var plte = new byte[colors * 3];
            for (int i = 0; i < colors; i++)
            {
                plte[i * 3 + 0] = (byte)((i * 8 + seed) & 0xFF);
                plte[i * 3 + 1] = (byte)((i * 4 + seed * 2) & 0xFF);
                plte[i * 3 + 2] = (byte)((i * 2 + seed * 3) & 0xFF);
            }

            // Raw scanlines: each row prefixed by a filter byte 0 (None), then w index bytes.
            var raw = new byte[h * (w + 1)];
            for (int y = 0; y < h; y++)
            {
                raw[y * (w + 1)] = 0; // filter None
                for (int x = 0; x < w; x++)
                    raw[y * (w + 1) + 1 + x] = (byte)(((x + y + seed) % colors) & 0xFF);
            }

            byte[] idat;
            using (var comp = new MemoryStream())
            {
                using (var zlib = new ZLibStream(comp, CompressionLevel.Optimal, leaveOpen: true))
                    zlib.Write(raw, 0, raw.Length);
                idat = comp.ToArray();
            }

            var ihdr = new byte[13];
            ihdr[0] = (byte)(w >> 24); ihdr[1] = (byte)(w >> 16); ihdr[2] = (byte)(w >> 8); ihdr[3] = (byte)w;
            ihdr[4] = (byte)(h >> 24); ihdr[5] = (byte)(h >> 16); ihdr[6] = (byte)(h >> 8); ihdr[7] = (byte)h;
            ihdr[8] = 8;   // bit depth
            ihdr[9] = 3;   // color type 3 (indexed)
            ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;

            using var ms = new MemoryStream();
            ms.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, 0, 8);
            WriteChunk(ms, "IHDR", ihdr);
            WriteChunk(ms, "PLTE", plte);
            // tRNS so index 0 is transparent (the validator's index-0 transparency expectation).
            WriteChunk(ms, "tRNS", new byte[] { 0 });
            WriteChunk(ms, "IDAT", idat);
            WriteChunk(ms, "IEND", Array.Empty<byte>());
            return ms.ToArray();
        }

        /// <summary>Build a JASC .pal whose RGB triples EXACTLY match the PNG's PLTE (seed-derived).</summary>
        static string BuildMatchingJasc(int colors, int seed)
        {
            var sb = new StringBuilder();
            sb.Append("JASC-PAL\r\n0100\r\n").Append(colors).Append("\r\n");
            for (int i = 0; i < colors; i++)
            {
                int r = (i * 8 + seed) & 0xFF;
                int g = (i * 4 + seed * 2) & 0xFF;
                int b = (i * 2 + seed * 3) & 0xFF;
                sb.Append(r).Append(' ').Append(g).Append(' ').Append(b).Append("\r\n");
            }
            return sb.ToString();
        }

        /// <summary>Write a valid 128x112 portrait package (sheet + matching sidecar) into a fresh dir.</summary>
        static string MakeValidPackageDir(string tag, int seed = 0, string sheetName = "portrait")
        {
            string dir = NewTempDir(tag);
            File.WriteAllBytes(Path.Combine(dir, sheetName + ".png"), BuildIndexedPng(128, 112, 16, seed));
            File.WriteAllText(Path.Combine(dir, sheetName + ".pal"), BuildMatchingJasc(16, seed));
            return dir;
        }

        // ----------------------------------------------------------- --import-asset --kind=portrait-package

        [Fact]
        public void ImportPortraitPackage_ValidPackage_ExitsZero_WritesOwner()
        {
            string src = MakeValidPackageDir("imp_src");
            string dest = NewTempDir("imp_dest_empty"); // empty → clean owner
            try
            {
                string args = $"--import-asset --kind=portrait-package --path=\"{src}\" --out=\"{dest}\"";
                var (code, stdout, stderr) = RunWithRetry(args);
                Assert.True(code == 0,
                    $"--import-asset --kind=portrait-package exited with {code}\nStdout: {stdout}\nStderr: {stderr}");
                Assert.True(File.Exists(Path.Combine(dest, "portrait.png")), "sheet must be written");
                Assert.True(File.Exists(Path.Combine(dest, "portrait.pal")), "sidecar must be written");
            }
            finally { TryDelete(src); TryDelete(dest); }
        }

        [Fact]
        public void ImportPortraitPackage_BadGeometry_ExitsTwo_NoWrite()
        {
            // 64x64 sheet → slots out of bounds → validator errors → refuse (exit 2).
            string src = NewTempDir("imp_bad_src");
            File.WriteAllBytes(Path.Combine(src, "portrait.png"), BuildIndexedPng(64, 64, 16, 0));
            string dest = NewTempDir("imp_bad_dest");
            try
            {
                string args = $"--import-asset --kind=portrait-package --path=\"{src}\" --out=\"{dest}\"";
                var (code, _, _) = RunWithRetry(args);
                Assert.Equal(2, code);
                Assert.False(File.Exists(Path.Combine(dest, "portrait.png")), "nothing must be written on refusal");
            }
            finally { TryDelete(src); TryDelete(dest); }
        }

        [Fact]
        public void ImportPortraitPackage_ExistingOwner_WithoutOverwrite_ExitsTwo()
        {
            string src = MakeValidPackageDir("imp_ovr_src", seed: 1);
            string dest = MakeValidPackageDir("imp_ovr_dest", seed: 2); // already an owner
            try
            {
                string args = $"--import-asset --kind=portrait-package --path=\"{src}\" --out=\"{dest}\"";
                var (code, _, stderr) = RunWithRetry(args);
                Assert.Equal(2, code);
                Assert.Contains("OWNER_EXISTS", stderr);
            }
            finally { TryDelete(src); TryDelete(dest); }
        }

        [Fact]
        public void ImportPortraitPackage_ExistingOwner_WithOverwrite_ExitsZero()
        {
            string src = MakeValidPackageDir("imp_ovr2_src", seed: 1);
            string dest = MakeValidPackageDir("imp_ovr2_dest", seed: 2);
            try
            {
                string args = $"--import-asset --kind=portrait-package --path=\"{src}\" --out=\"{dest}\" --overwrite";
                var (code, stdout, stderr) = RunWithRetry(args);
                Assert.True(code == 0,
                    $"--overwrite import exited with {code}\nStdout: {stdout}\nStderr: {stderr}");
                // After overwrite the dest sheet equals the src sheet.
                Assert.Equal(
                    File.ReadAllBytes(Path.Combine(src, "portrait.png")),
                    File.ReadAllBytes(Path.Combine(dest, "portrait.png")));
            }
            finally { TryDelete(src); TryDelete(dest); }
        }

        [Fact]
        public void ImportPortraitPackage_Overwrite_DifferentSheetName_LeavesSingleOwner()
        {
            // Source "portrait.png" overwriting an owner "old.png" must leave exactly one PNG (#1379).
            string src = MakeValidPackageDir("imp_rename_src", seed: 1, sheetName: "portrait");
            string dest = MakeValidPackageDir("imp_rename_dest", seed: 2, sheetName: "old");
            try
            {
                string args = $"--import-asset --kind=portrait-package --path=\"{src}\" --out=\"{dest}\" --overwrite";
                var (code, stdout, stderr) = RunWithRetry(args);
                Assert.True(code == 0,
                    $"--overwrite rename import exited with {code}\nStdout: {stdout}\nStderr: {stderr}");
                Assert.Single(Directory.GetFiles(dest, "*.png"));
                Assert.True(File.Exists(Path.Combine(dest, "portrait.png")));
                Assert.False(File.Exists(Path.Combine(dest, "old.png")));
            }
            finally { TryDelete(src); TryDelete(dest); }
        }

        [Fact]
        public void ImportPortraitPackage_OutEscapesProject_ExitsTwo()
        {
            string projectDir = NewTempDir("imp_escape_proj");
            try
            {
                File.WriteAllText(Path.Combine(projectDir, "febuilder.project.json"),
                    "{ \"schemaVersion\": 1, \"builtRom\": \"synth.gba\" }");
                string src = MakeValidPackageDir("imp_escape_src");
                try
                {
                    string escapingOut = "../escaped_portraits";
                    string args = $"--import-asset --kind=portrait-package --project=\"{projectDir}\" --path=\"{src}\" --out={escapingOut}";
                    var (code, _, _) = RunWithRetry(args);
                    Assert.Equal(2, code);
                    Assert.False(Directory.Exists(Path.Combine(projectDir, "..", "escaped_portraits")),
                        "no package must be written outside the project root");
                }
                finally { TryDelete(src); }
            }
            finally { TryDelete(projectDir); }
        }

        // ----------------------------------------------------------- --roundtrip-asset --kind=portrait-package

        [Fact]
        public void RoundtripPortraitPackage_IdenticalBaseline_ExitsZero()
        {
            string src = MakeValidPackageDir("rt_src", seed: 5);
            // Baseline = identical seed → byte-identical package.
            string baseline = MakeValidPackageDir("rt_baseline", seed: 5);
            try
            {
                string args = $"--roundtrip-asset --kind=portrait-package --path=\"{src}\" --expect=\"{baseline}\"";
                var (code, stdout, stderr) = RunWithRetry(args);
                Assert.True(code == 0,
                    $"portrait-package roundtrip exited with {code}\nStdout: {stdout}\nStderr: {stderr}");
                Assert.Contains("Round-trip OK", stdout);
            }
            finally { TryDelete(src); TryDelete(baseline); }
        }

        [Fact]
        public void RoundtripPortraitPackage_TamperedSource_ExitsTwo()
        {
            // Source and baseline both VALIDATE but differ in bytes (different seed) → mismatch.
            string src = MakeValidPackageDir("rt_tamper_src", seed: 7);
            string baseline = MakeValidPackageDir("rt_tamper_base", seed: 8);
            try
            {
                string args = $"--roundtrip-asset --kind=portrait-package --path=\"{src}\" --expect=\"{baseline}\"";
                var (code, _, _) = RunWithRetry(args);
                Assert.Equal(2, code);
            }
            finally { TryDelete(src); TryDelete(baseline); }
        }

        [Fact]
        public void RoundtripPortraitPackage_MissingExpect_ExitsOne()
        {
            string src = MakeValidPackageDir("rt_noexpect_src");
            try
            {
                string args = $"--roundtrip-asset --kind=portrait-package --path=\"{src}\"";
                var (code, _, _) = RunWithRetry(args);
                Assert.Equal(1, code); // usage error: --expect is required
            }
            finally { TryDelete(src); }
        }

        static void TryDelete(string dir)
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
