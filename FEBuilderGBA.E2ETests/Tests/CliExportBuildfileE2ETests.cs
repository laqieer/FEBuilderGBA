using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FEBuilderGBA.E2ETests.Helpers;
using Xunit;

namespace FEBuilderGBA.E2ETests.Tests
{
    /// <summary>
    /// E2E coverage for #1935 — <c>--export-buildfile --rom=&lt;modded&gt; --clean=&lt;clean&gt;
    /// --out=&lt;project&gt;</c> on <c>FEBuilderGBA.CLI</c>.
    ///
    /// The reconstruction scenario is ROM-gated (a temporary copy of <c>roms/FE8U.gba</c>
    /// via <see cref="RomLocator"/>) and skips via <see cref="SkippableFact"/> when the ROM
    /// is unavailable — the same pattern as <c>CliDataJsonE2ETests</c>/<c>RomCliTests</c>.
    /// It applies known disjoint edits plus a sparse extension to a modded copy, invokes the
    /// CLI, then INDEPENDENTLY reconstructs the target from clean + the declared extension
    /// fill + the JSON payload records to prove the recipe describes the target exactly.
    /// This reconstruction is a test helper — not the public apply/build verb, which is #1936.
    /// The argument-validation and help scenarios need no ROM and run as plain facts.
    /// </summary>
    public class CliExportBuildfileE2ETests : IDisposable
    {
        private static readonly string CliExe = AppRunner.FindCliExePath();
        private readonly List<string> _tempPaths = new();

        public void Dispose()
        {
            foreach (var p in _tempPaths)
            {
                try
                {
                    if (Directory.Exists(p)) Directory.Delete(p, true);
                    else if (File.Exists(p)) File.Delete(p);
                }
                catch { }
            }
        }

        private string TempPath(string suffix)
        {
            string p = Path.Combine(Path.GetTempPath(), $"febuilder_bfx_{Guid.NewGuid():N}{suffix}");
            _tempPaths.Add(p);
            return p;
        }

        private string CopyFE8U()
        {
            string tempRom = TempPath(".gba");
            File.Copy(RomLocator.FE8U!, tempRom);
            return tempRom;
        }

        // ------------------------------------------------------- argument validation (no ROM)

        [Fact]
        public void ExportBuildfile_MissingRom_Returns1()
        {
            var (code, _, stderr) = AppRunner.Run(CliExe,
                "--export-buildfile --clean=clean.gba --out=proj", timeoutMs: 15_000);
            Assert.Equal(1, code);
            Assert.Contains("--rom", stderr);
        }

        [Fact]
        public void ExportBuildfile_MissingClean_Returns1()
        {
            var (code, _, stderr) = AppRunner.Run(CliExe,
                "--export-buildfile --rom=modded.gba --out=proj", timeoutMs: 15_000);
            Assert.Equal(1, code);
            Assert.Contains("--clean", stderr);
        }

        [Fact]
        public void ExportBuildfile_MissingOut_Returns1()
        {
            var (code, _, stderr) = AppRunner.Run(CliExe,
                "--export-buildfile --rom=modded.gba --clean=clean.gba", timeoutMs: 15_000);
            Assert.Equal(1, code);
            Assert.Contains("--out", stderr);
        }

        [Fact]
        public void ExportBuildfile_NonexistentRom_Returns1_NoOutput()
        {
            string outDir = TempPath("_proj");
            var (code, _, stderr) = AppRunner.Run(CliExe,
                $"--export-buildfile --rom=\"{Path.Combine(Path.GetTempPath(), "nope.gba")}\" --clean=\"{Path.Combine(Path.GetTempPath(), "nope2.gba")}\" --out=\"{outDir}\"",
                timeoutMs: 15_000);
            Assert.Equal(1, code);
            Assert.False(Directory.Exists(outDir));
        }

        [Fact]
        public void ExportBuildfile_UnknownFlag_Returns1_NoOutput()
        {
            string outDir = TempPath("_proj");
            // A typo like `--with-soruce` must be rejected before any load/write.
            var (code, _, stderr) = AppRunner.Run(CliExe,
                $"--export-buildfile --rom=modded.gba --clean=clean.gba --out=\"{outDir}\" --with-soruce",
                timeoutMs: 15_000);
            Assert.Equal(1, code);
            Assert.Contains("unknown option", stderr);
            Assert.False(Directory.Exists(outDir));
        }

        [Fact]
        public void ExportBuildfile_SameCleanAndModded_Returns1()
        {
            // Both inputs exist and are the SAME file → alias rejection fires before loading,
            // so no ROM is needed. This prevents a misleading zero-diff recipe.
            string shared = TempPath(".gba");
            File.WriteAllBytes(shared, new byte[64]);
            string outDir = TempPath("_proj");

            var (code, _, stderr) = AppRunner.Run(CliExe,
                $"--export-buildfile --rom=\"{shared}\" --clean=\"{shared}\" --out=\"{outDir}\"",
                timeoutMs: 15_000);
            Assert.Equal(1, code);
            Assert.Contains("same file", stderr);
            Assert.False(Directory.Exists(outDir));
        }

        [Fact]
        public void Help_DocumentsExportBuildfile()
        {
            var (_, stdout, _) = AppRunner.Run(CliExe, "--help", timeoutMs: 15_000);
            Assert.Contains("--export-buildfile", stdout);
            Assert.Contains("--clean", stdout);
        }

        // --------------------------------------------------------------- ROM-gated scenarios

        [SkippableFact]
        public void ExportBuildfile_ReconstructsTargetExactly_FromCleanPlusManifest()
        {
            Skip.If(RomLocator.FE8U == null, "FE8U ROM not available");

            string cleanPath = CopyFE8U();
            byte[] clean = File.ReadAllBytes(cleanPath);

            // Build a modded copy: known disjoint edits inside the clean region + a sparse
            // extension (mostly 0xFF with a couple of non-fill overrides). Keep the header /
            // game-code region (0xA0..0xC0) untouched so it still detects as FE8U.
            var modded = new byte[clean.Length * 2];
            Array.Copy(clean, modded, clean.Length);
            modded[0x100000] = 0xA1;
            modded[0x100001] = 0xA2;
            modded[0x200000] = 0xB7;
            for (int i = clean.Length; i < modded.Length; i++) modded[i] = 0xFF;
            modded[clean.Length + 0x10] = 0x01;
            modded[clean.Length + 0x11] = 0x02;
            modded[modded.Length - 1] = 0x03;

            string moddedPath = TempPath(".gba");
            File.WriteAllBytes(moddedPath, modded);

            string outDir = TempPath("_proj");

            var (code, stdout, stderr) = AppRunner.Run(CliExe,
                $"--export-buildfile --rom=\"{moddedPath}\" --clean=\"{cleanPath}\" --out=\"{outDir}\"",
                timeoutMs: 120_000);

            Assert.True(code == 0, $"--export-buildfile exited {code}\nStdout: {stdout}\nStderr: {stderr}");
            Assert.True(File.Exists(Path.Combine(outDir, "buildfile.json")), "buildfile.json missing");
            Assert.True(File.Exists(Path.Combine(outDir, "main.event")), "main.event missing");
            Assert.False(Directory.Exists(Path.Combine(outDir, "source")), "projection must be off by default");

            byte[] recon = ReconstructFromProject(outDir, clean);
            Assert.True(recon.SequenceEqual(modded), "Independent reconstruction did not match the modded ROM exactly.");
        }

        [SkippableFact]
        public void ExportBuildfile_PreexistingOut_Returns1_NoOverwrite()
        {
            Skip.If(RomLocator.FE8U == null, "FE8U ROM not available");
            string cleanPath = CopyFE8U();
            string moddedPath = CopyFE8U();

            string outDir = TempPath("_proj");
            Directory.CreateDirectory(outDir);
            File.WriteAllText(Path.Combine(outDir, "keepme.txt"), "precious");

            var (code, _, stderr) = AppRunner.Run(CliExe,
                $"--export-buildfile --rom=\"{moddedPath}\" --clean=\"{cleanPath}\" --out=\"{outDir}\"",
                timeoutMs: 60_000);

            Assert.Equal(1, code);
            Assert.True(File.Exists(Path.Combine(outDir, "keepme.txt")));
            Assert.Equal("precious", File.ReadAllText(Path.Combine(outDir, "keepme.txt")));
            Assert.False(File.Exists(Path.Combine(outDir, "buildfile.json")));
        }

        [SkippableFact]
        public void ExportBuildfile_WrongVersionClean_Returns1()
        {
            Skip.If(RomLocator.FE8U == null || RomLocator.FE7U == null, "FE8U and FE7U ROMs required");

            string moddedPath = CopyFE8U();               // FE8U modded
            string cleanPath = TempPath(".gba");
            File.Copy(RomLocator.FE7U!, cleanPath);       // FE7U clean → version mismatch

            string outDir = TempPath("_proj");
            var (code, _, stderr) = AppRunner.Run(CliExe,
                $"--export-buildfile --rom=\"{moddedPath}\" --clean=\"{cleanPath}\" --out=\"{outDir}\"",
                timeoutMs: 60_000);

            Assert.Equal(1, code);
            Assert.False(Directory.Exists(outDir));
            Assert.Contains("different versions", stderr);
        }

        [SkippableFact]
        public void ExportBuildfile_TrailingSlashOut_Succeeds_PublishesAtIntendedDirectory()
        {
            Skip.If(RomLocator.FE8U == null, "FE8U ROM not available");
            string cleanPath = CopyFE8U();
            byte[] clean = File.ReadAllBytes(cleanPath);

            var modded = (byte[])clean.Clone();
            modded[0x100000] = 0xC1;
            string moddedPath = TempPath(".gba");
            File.WriteAllBytes(moddedPath, modded);

            string outDir = TempPath("_proj");
            // Spell the (not-yet-existing) destination WITH a trailing separator. Use '/' (which
            // Windows accepts) rather than '\', because a trailing backslash before the closing
            // quote would escape the quote in the process command line.
            var (code, stdout, stderr) = AppRunner.Run(CliExe,
                $"--export-buildfile --rom=\"{moddedPath}\" --clean=\"{cleanPath}\" --out=\"{outDir}/\"",
                timeoutMs: 120_000);

            Assert.True(code == 0, $"trailing-separator out exited {code}\nStdout: {stdout}\nStderr: {stderr}");
            Assert.True(File.Exists(Path.Combine(outDir, "buildfile.json")), "published at the intended directory");
            byte[] recon = ReconstructFromProject(outDir, clean);
            Assert.True(recon.SequenceEqual(modded));
        }

        [Fact]
        public void ExportBuildfile_GlobalHelpAndVersion_TakePrecedence()
        {
            // Global dispatch (--help/--version) is handled in Main BEFORE the verb, so it must
            // win over --export-buildfile regardless of ordering, and the command-specific
            // allowlist must never interfere with these global switches.
            var (hCode, hOut, _) = AppRunner.Run(CliExe, "--export-buildfile --help", timeoutMs: 15_000);
            Assert.Equal(0, hCode);
            Assert.Contains("--export-buildfile", hOut);

            var (h2Code, h2Out, _) = AppRunner.Run(CliExe, "--help --export-buildfile", timeoutMs: 15_000);
            Assert.Equal(0, h2Code);
            Assert.Contains("--export-buildfile", h2Out);

            var (vCode, vOut, _) = AppRunner.Run(CliExe, "--export-buildfile --version", timeoutMs: 15_000);
            Assert.Equal(0, vCode);
            Assert.Contains("Version", vOut, StringComparison.OrdinalIgnoreCase);
        }

        [SkippableFact]
        public void ExportBuildfile_JunctionParentAliasesSameFile_Returns1_NoOutput()
        {
            // C:\real\mod.gba  vs  C:\link\mod.gba  where C:\link -> C:\real. Both resolve to
            // the SAME physical file, so physical-identity resolution rejects the alias that a
            // lexical comparison would miss. No ROM is needed — the identity check precedes load.
            string root = TempPath("_aliasroot");
            string real = Path.Combine(root, "real");
            string link = Path.Combine(root, "link");
            Directory.CreateDirectory(real);
            File.WriteAllBytes(Path.Combine(real, "mod.gba"), new byte[64]);
            try { Directory.CreateSymbolicLink(link, real); }
            catch (Exception ex) { Skip.If(true, "Cannot create a directory symlink here: " + ex.Message); return; }

            string moddedReal = Path.Combine(real, "mod.gba");
            string cleanLinked = Path.Combine(link, "mod.gba");
            string outDir = TempPath("_proj");

            var (code, _, stderr) = AppRunner.Run(CliExe,
                $"--export-buildfile --rom=\"{moddedReal}\" --clean=\"{cleanLinked}\" --out=\"{outDir}\"",
                timeoutMs: 15_000);

            Assert.Equal(1, code);
            Assert.Contains("same file", stderr);
            Assert.False(Directory.Exists(outDir));
            // No stage sibling leaked into the real target dir either.
            Assert.DoesNotContain(Directory.GetDirectories(real), d => Path.GetFileName(d).Contains(".stage-"));
        }

        [SkippableFact]
        public void ExportBuildfile_FinalSymlinkToSameFile_Returns1()
        {
            // A final-component symlink that points at the SAME physical file as the other input
            // must be rejected via identity (intent, not a blanket reparse ban).
            string dir = TempPath("_symin");
            Directory.CreateDirectory(dir);
            string realFile = Path.Combine(dir, "real.gba");
            string linkFile = Path.Combine(dir, "link.gba");
            File.WriteAllBytes(realFile, new byte[64]);
            try { File.CreateSymbolicLink(linkFile, realFile); }
            catch (Exception ex) { Skip.If(true, "Cannot create a file symlink here: " + ex.Message); return; }

            string outDir = TempPath("_proj");
            var (code, _, stderr) = AppRunner.Run(CliExe,
                $"--export-buildfile --rom=\"{realFile}\" --clean=\"{linkFile}\" --out=\"{outDir}\"",
                timeoutMs: 15_000);

            Assert.Equal(1, code);
            Assert.Contains("same file", stderr);
            Assert.False(Directory.Exists(outDir));
        }

        [SkippableFact]
        public void ExportBuildfile_CaseAliasedCleanAndModded_Returns1()
        {
            Skip.IfNot(OperatingSystem.IsWindows(), "Case-insensitive alias only asserted on Windows");
            string shared = TempPath(".gba");
            File.WriteAllBytes(shared, new byte[64]);
            string upper = shared.ToUpperInvariant();
            string outDir = TempPath("_proj");

            var (code, _, stderr) = AppRunner.Run(CliExe,
                $"--export-buildfile --rom=\"{shared}\" --clean=\"{upper}\" --out=\"{outDir}\"",
                timeoutMs: 15_000);

            Assert.Equal(1, code);
            Assert.Contains("same file", stderr);
            Assert.False(Directory.Exists(outDir));
        }

        [Fact]
        public void ExportBuildfile_ParentTraversalInRom_ForwardSlash_Returns1_NoOutput()
        {
            string dir = TempPath("_pt");
            Directory.CreateDirectory(dir);
            Directory.CreateDirectory(Path.Combine(dir, "sub"));
            File.WriteAllBytes(Path.Combine(dir, "a.gba"), new byte[64]);
            File.WriteAllBytes(Path.Combine(dir, "clean.gba"), new byte[64]);
            string outDir = TempPath("_proj");

            // sub/../a.gba is a parent-traversal input → rejected before existence/load.
            string traversed = dir.Replace('\\', '/') + "/sub/../a.gba";
            var (code, _, stderr) = AppRunner.Run(CliExe,
                $"--export-buildfile --rom=\"{traversed}\" --clean=\"{Path.Combine(dir, "clean.gba")}\" --out=\"{outDir}\"",
                timeoutMs: 15_000);

            Assert.Equal(1, code);
            Assert.Contains("parent-directory", stderr);
            Assert.False(Directory.Exists(outDir));
        }

        [SkippableFact]
        public void ExportBuildfile_ParentTraversalInClean_Backslash_Returns1_NoOutput()
        {
            Skip.IfNot(OperatingSystem.IsWindows(), "Backslash separator only on Windows");
            string dir = TempPath("_pt2");
            Directory.CreateDirectory(dir);
            Directory.CreateDirectory(Path.Combine(dir, "sub"));
            File.WriteAllBytes(Path.Combine(dir, "clean.gba"), new byte[64]);
            string modded = Path.Combine(dir, "mod.gba");
            File.WriteAllBytes(modded, new byte[64]);
            string outDir = TempPath("_proj");

            string traversed = dir + @"\sub\..\clean.gba";
            var (code, _, stderr) = AppRunner.Run(CliExe,
                $"--export-buildfile --rom=\"{modded}\" --clean=\"{traversed}\" --out=\"{outDir}\"",
                timeoutMs: 15_000);

            Assert.Equal(1, code);
            Assert.Contains("parent-directory", stderr);
            Assert.False(Directory.Exists(outDir));
        }

        [SkippableFact]
        public void ExportBuildfile_SymlinkAncestorPlusDotDot_Returns1_NoOutput()
        {
            // Minimal reproduction of the lexical-vs-physical divergence: linkRoot -> realRoot,
            // and the input threads a '..' through a symlinked ancestor. Path.GetFullPath would
            // collapse the '..' lexically before resolving the link; the safe contract rejects it.
            string root = TempPath("_symdd");
            string realRoot = Path.Combine(root, "real");
            string linkRoot = Path.Combine(root, "link");
            Directory.CreateDirectory(Path.Combine(realRoot, "sub"));
            File.WriteAllBytes(Path.Combine(realRoot, "mod.gba"), new byte[64]);
            File.WriteAllBytes(Path.Combine(realRoot, "clean.gba"), new byte[64]);
            try { Directory.CreateSymbolicLink(linkRoot, realRoot); }
            catch (Exception ex) { Skip.If(true, "Cannot create a directory symlink here: " + ex.Message); return; }

            string outDir = TempPath("_proj");
            string traversed = linkRoot.Replace('\\', '/') + "/sub/../mod.gba";
            var (code, _, stderr) = AppRunner.Run(CliExe,
                $"--export-buildfile --rom=\"{traversed}\" --clean=\"{Path.Combine(realRoot, "clean.gba")}\" --out=\"{outDir}\"",
                timeoutMs: 15_000);

            Assert.Equal(1, code);
            Assert.Contains("parent-directory", stderr);
            Assert.False(Directory.Exists(outDir));
            Assert.DoesNotContain(Directory.GetDirectories(realRoot), d => Path.GetFileName(d).Contains(".stage-"));
        }

        // --------------------------------------------------------------------- helper

        private static byte[] ReconstructFromProject(string projectDir, byte[] clean)
        {
            string json = File.ReadAllText(Path.Combine(projectDir, "buildfile.json"));
            using var doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            int targetSize = (int)root.GetProperty("target").GetProperty("size").GetUInt32();
            var recon = new byte[targetSize];
            Array.Copy(clean, 0, recon, 0, Math.Min(clean.Length, targetSize));

            if (root.TryGetProperty("extension", out JsonElement ext) && ext.ValueKind == JsonValueKind.Object)
            {
                uint start = ext.GetProperty("start").GetUInt32();
                uint len = ext.GetProperty("length").GetUInt32();
                byte fill = Convert.ToByte(ext.GetProperty("fillByte").GetString()!.Substring(2), 16);
                for (uint i = 0; i < len; i++) recon[start + i] = fill;
            }

            foreach (JsonElement r in root.GetProperty("ranges").EnumerateArray())
            {
                uint offset = r.GetProperty("offset").GetUInt32();
                string payload = r.GetProperty("payload").GetString()!;
                Assert.StartsWith("data/", payload);
                Assert.DoesNotContain("\\", payload);
                byte[] bytes = File.ReadAllBytes(Path.Combine(projectDir, payload.Replace('/', Path.DirectorySeparatorChar)));
                Array.Copy(bytes, 0, recon, offset, bytes.Length);
            }
            return recon;
        }
    }
}
