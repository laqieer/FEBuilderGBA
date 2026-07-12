// SPDX-License-Identifier: GPL-3.0-or-later
// Core tests for the buildfile recipe exporter (#1935).
//
// Synthetic-first: two in-memory FE8U ROMs (a clean baseline and a modded target
// differing at KNOWN offsets, optionally extended) drive the pure planner and the
// staged publication. The tests prove the governing losslessness invariant (clean +
// declared fill + payloads reconstruct the target exactly), determinism (two runs
// produce byte-identical trees), the ordered/non-overlapping maxGap-0 ranges, sparse
// extension handling (one fill rule + only non-fill payloads, no giant padding file),
// identity/size rejections, the non-canonical warning, JSON/path containment, the
// derived Event Assembler balance, and the atomic no-partial-on-failure guarantees.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class BuildfileExportCoreTests
    {
        const string FE8U_CODE = "BE8E01";
        const string FE7U_CODE = "AE7E01";
        const int RomSize = 0x1000000; // 16 MiB, a normal FE8U size

        [DllImport("libc", EntryPoint = "mkfifo", SetLastError = true)]
        static extern int CreateFifoUnix(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
            uint mode);

        static ROM MakeRom(byte[] data, string code = FE8U_CODE)
        {
            var rom = new ROM();
            Assert.True(rom.LoadLow("buildfile-test.gba", data, code));
            return rom;
        }

        // A fresh, unique, empty output directory path under a temp parent (the path
        // does NOT exist yet — Export requires that). Caller cleans the parent up.
        static (string OutDir, string Parent) FreshOut()
        {
            string parent = Path.Combine(Path.GetTempPath(), "bfx_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(parent);
            return (Path.Combine(parent, "project"), parent);
        }

        static void Cleanup(string parent)
        {
            try { if (Directory.Exists(parent)) Directory.Delete(parent, true); } catch { }
        }

        static string FindBuiltColorzCore()
        {
            string dir = AppContext.BaseDirectory;
            for (int i = 0; i < 12 && dir != null; i++)
            {
                foreach (string config in new[] { "Release", "Debug" })
                {
                    foreach (string name in new[] { "ColorzCore.exe", "ColorzCore" })
                    {
                        string path = Path.Combine(dir, "tools", "ColorzCore", "ColorzCore",
                            "bin", "Core", config, "net6.0", name);
                        if (File.Exists(path))
                            return path;
                    }
                }

                dir = Path.GetDirectoryName(dir);
            }
            return null;
        }

        static string Sha256Hex(byte[] data)
        {
            var sb = new StringBuilder();
            foreach (byte b in SHA256.HashData(data)) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        // Reconstruct the target purely from the published manifest + payloads, exactly
        // as an independent #1936-style consumer would, and return the bytes.
        static byte[] ReconstructFromProject(string projectDir, byte[] clean)
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
                byte fill = Convert.ToByte(ext.GetProperty("fillByte").GetString().Substring(2), 16);
                for (uint i = 0; i < len; i++) recon[start + i] = fill;
            }

            foreach (JsonElement r in root.GetProperty("ranges").EnumerateArray())
            {
                uint offset = r.GetProperty("offset").GetUInt32();
                string payload = r.GetProperty("payload").GetString();
                Assert.StartsWith("data/", payload);
                byte[] bytes = File.ReadAllBytes(Path.Combine(projectDir, payload.Replace('/', Path.DirectorySeparatorChar)));
                Array.Copy(bytes, 0, recon, offset, bytes.Length);
            }
            return recon;
        }

        // ---------------------------------------------------------------- planning

        [Fact]
        public void Plan_DisjointEdits_ProducesOrderedNonOverlappingMaxGapZeroRanges()
        {
            var clean = new byte[RomSize];
            var target = (byte[])clean.Clone();
            // Two separated single-byte edits + one 4-byte run.
            target[0x100] = 0xAA;
            target[0x200] = 0xBB; target[0x201] = 0xBC;
            target[0x300] = 0xCC; target[0x301] = 0xCD; target[0x302] = 0xCE; target[0x303] = 0xCF;

            var plan = BuildfileExportCore.Plan(MakeRom(clean), MakeRom(target), new BuildfileExportOptions { OutputDirectory = "unused" });

            Assert.Equal(3, plan.Manifest.Ranges.Count);
            Assert.Equal((uint)0x100, plan.Manifest.Ranges[0].Offset);
            Assert.Equal((uint)0x200, plan.Manifest.Ranges[1].Offset);
            Assert.Equal((uint)0x300, plan.Manifest.Ranges[2].Offset);
            // maxGap 0: span == changed bytes for every range.
            foreach (var r in plan.Manifest.Ranges)
                Assert.Equal(r.Length, r.ChangedBytes);
            // Ordered + non-overlapping.
            for (int i = 1; i < plan.Manifest.Ranges.Count; i++)
            {
                var prev = plan.Manifest.Ranges[i - 1];
                Assert.True(prev.Offset + prev.Length <= plan.Manifest.Ranges[i].Offset);
            }
            Assert.Equal((uint)7, plan.Manifest.TotalChangedBytes);
            // GBA address is offset + 0x08000000.
            Assert.Equal("0x08000100", plan.Manifest.Ranges[0].GbaAddress);
        }

        [Theory]
        [InlineData(0, "0x08000000")]
        [InlineData(1, "0x08000001")]
        public void Plan_HeaderOffset_AlwaysUsesMappedGbaAddress(int offset, string expectedAddress)
        {
            var clean = new byte[RomSize];
            var target = (byte[])clean.Clone();
            target[offset] = 0xA5;

            var plan = BuildfileExportCore.Plan(MakeRom(clean), MakeRom(target),
                new BuildfileExportOptions { OutputDirectory = "unused" });

            var range = Assert.Single(plan.Manifest.Ranges);
            Assert.Equal((uint)offset, range.Offset);
            Assert.Equal(expectedAddress, range.GbaAddress);
        }

        [Fact]
        public void Plan_FaultingClassifierOverride_DoesNotDropOrReorderAuthoritativeRanges()
        {
            // A faulting/bad advisory classifier must NEVER omit, reorder, or resize the
            // authoritative payload ranges — only its own advisory category/confidence/
            // suggestion for the affected range degrades to a stable Unknown/Low/manual-review
            // record (Copilot review finding: authoritative AnalyzeWithFill partial report).
            var clean = new byte[RomSize];
            var target = (byte[])clean.Clone();
            target[0x100] = 0xAA;
            target[0x200] = 0xBB;
            target[0x300] = 0xCC;

            var plan = BuildfileExportCore.Plan(MakeRom(clean), MakeRom(target), new BuildfileExportOptions
            {
                OutputDirectory = "unused",
                ClassifierOverrideForTest = (rom, built, offset, span, changed, map, resolver) =>
                    throw new InvalidOperationException("injected advisory classifier fault"),
            });

            // All three authoritative ranges are still present, in order, unmodified.
            Assert.Equal(3, plan.Manifest.Ranges.Count);
            Assert.Equal((uint)0x100, plan.Manifest.Ranges[0].Offset);
            Assert.Equal((uint)0x200, plan.Manifest.Ranges[1].Offset);
            Assert.Equal((uint)0x300, plan.Manifest.Ranges[2].Offset);
            foreach (var r in plan.Manifest.Ranges)
            {
                Assert.Equal(1u, r.Length);
                Assert.Equal("unknown", r.Category);
                Assert.Equal("low", r.Confidence);
            }
        }

        [Fact]
        public void Plan_ExceedsMaxPayloadRanges_ThrowsExplicitFragmentationError_NoHugeAllocation()
        {
            // A worst-case alternating-byte diff must be rejected the instant the next range
            // would exceed MaxPayloadRanges — proving the bound is enforced BEFORE any
            // downstream materialization (payload files, manifest entries), never allocating
            // millions of ranges/files (Copilot review finding: unbounded 16M ranges/files).
            // Uses a normal-size (16 MiB) ROM buffer — only the first ~33 KiB alternates — so
            // the limit is hit almost immediately without a slow/huge test.
            int limit = BuildfileExportOptions.MaxPayloadRanges;
            int alternatingLength = (limit + 1) * 2;
            var clean = new byte[RomSize];
            var target = (byte[])clean.Clone();
            for (int i = 0; i < alternatingLength; i += 2)
                target[i] = 0x01;

            var ex = Assert.Throws<RomDiffCore.DiffRangeLimitExceededException>(() =>
                BuildfileExportCore.Plan(MakeRom(clean, FE8U_CODE), MakeRom(target, FE8U_CODE),
                    new BuildfileExportOptions { OutputDirectory = "unused" }));
            Assert.Equal(limit, ex.Limit);
        }

        [Fact]
        public void Export_ExceedsMaxPayloadRanges_FailsExplicitly_NoDestinationNoHugeAllocation()
        {
            // End-to-end: Export must surface the SAME bounded rejection through the public
            // Export() API (via Plan()'s internal try/catch), never publish a destination, and
            // never materialize the huge range/file set the limit exists to prevent.
            int limit = BuildfileExportOptions.MaxPayloadRanges;
            int alternatingLength = (limit + 1) * 2;
            var clean = new byte[RomSize];
            var target = (byte[])clean.Clone();
            for (int i = 0; i < alternatingLength; i += 2)
                target[i] = 0x01;

            var (outDir, parent) = FreshOut();
            try
            {
                var result = BuildfileExportCore.Export(MakeRom(clean, FE8U_CODE), MakeRom(target, FE8U_CODE),
                    new BuildfileExportOptions { OutputDirectory = outDir });
                Assert.False(result.Success);
                Assert.Contains("resource-safety limit", result.Error);
                Assert.False(Directory.Exists(outDir));
            }
            finally { Cleanup(parent); }
        }

        [Fact]
        public void Plan_ShorterTarget_IsRejected()
        {
            var clean = new byte[RomSize * 2]; // 32 MiB clean
            var target = new byte[RomSize];    // 16 MiB target (shorter → rejected)
            var ex = Assert.Throws<ArgumentException>(() =>
                BuildfileExportCore.Plan(MakeRom(clean), MakeRom(target), new BuildfileExportOptions { OutputDirectory = "unused" }));
            Assert.Contains("shorter", ex.Message);
        }

        [Fact]
        public void Plan_VersionMismatch_IsRejected()
        {
            var clean = new byte[RomSize];
            var target = new byte[RomSize];
            var ex = Assert.Throws<ArgumentException>(() =>
                BuildfileExportCore.Plan(MakeRom(clean, FE8U_CODE), MakeRom(target, FE7U_CODE), new BuildfileExportOptions { OutputDirectory = "unused" }));
            Assert.Contains("different versions", ex.Message);
        }

        [Fact]
        public void Plan_NonCanonicalClean_EmitsWarningAndRecordsHashes()
        {
            var clean = new byte[RomSize]; // all-zero is NOT the canonical FE8U original
            var target = (byte[])clean.Clone();
            target[0x40] = 0x7;

            var plan = BuildfileExportCore.Plan(MakeRom(clean), MakeRom(target), new BuildfileExportOptions { OutputDirectory = "unused" });

            Assert.False(plan.Manifest.Clean.IsCanonicalOriginal);
            Assert.Contains(plan.Manifest.Warnings, w => w.Contains("not the known canonical original"));
            Assert.Equal(Sha256Hex(clean), plan.Manifest.Clean.Sha256);
            Assert.Equal(Sha256Hex(target), plan.Manifest.Target.Sha256);
        }

        [Fact]
        public void Plan_IdenticalRoms_ProducesZeroRangeRecipe()
        {
            var clean = new byte[RomSize];
            var target = (byte[])clean.Clone();
            var plan = BuildfileExportCore.Plan(MakeRom(clean), MakeRom(target), new BuildfileExportOptions { OutputDirectory = "unused" });
            Assert.Empty(plan.Manifest.Ranges);
            Assert.Null(plan.Manifest.Extension);
            Assert.Equal((uint)0, plan.Manifest.TotalChangedBytes);
        }

        [Fact]
        public void Plan_SparseExtension_OneFillRulePlusOnlyNonFillPayloads()
        {
            var clean = new byte[RomSize];             // 16 MiB
            var target = new byte[RomSize * 2];        // 32 MiB
            Array.Copy(clean, target, RomSize);
            // Fill the extension mostly with 0xFF, plus a few non-FF override bytes.
            for (int i = RomSize; i < target.Length; i++) target[i] = 0xFF;
            target[RomSize + 0x10] = 0x01;
            target[RomSize + 0x11] = 0x02;
            target[target.Length - 1] = 0x03;

            var plan = BuildfileExportCore.Plan(MakeRom(clean), MakeRom(target), new BuildfileExportOptions { OutputDirectory = "unused" });

            Assert.NotNull(plan.Manifest.Extension);
            Assert.Equal((uint)RomSize, plan.Manifest.Extension.Start);
            Assert.Equal((uint)RomSize, plan.Manifest.Extension.Length);
            Assert.Equal("0xFF", plan.Manifest.Extension.FillByte);
            // Only the non-fill overrides become payloads — never a 16 MiB padding file.
            Assert.All(plan.Payloads, p => Assert.True(p.Length < 1024));
            uint totalPayloadBytes = (uint)plan.Payloads.Sum(p => (long)p.Length);
            Assert.Equal((uint)3, totalPayloadBytes);
        }

        // ------------------------------------------------------------- publication

        [Fact]
        public void Export_TwoRuns_ProduceByteIdenticalDeterministicTree()
        {
            var clean = new byte[RomSize];
            var target = (byte[])clean.Clone();
            target[0x1000] = 0x11; target[0x1001] = 0x22;
            target[0x5000] = 0x33;

            var (out1, parent1) = FreshOut();
            var (out2, parent2) = FreshOut();
            try
            {
                var r1 = BuildfileExportCore.Export(MakeRom(clean), MakeRom(target), new BuildfileExportOptions { OutputDirectory = out1 });
                var r2 = BuildfileExportCore.Export(MakeRom(clean), MakeRom(target), new BuildfileExportOptions { OutputDirectory = out2 });
                Assert.True(r1.Success, r1.Error);
                Assert.True(r2.Success, r2.Error);

                var files1 = RelativeFileSet(out1);
                var files2 = RelativeFileSet(out2);
                Assert.Equal(files1, files2);
                foreach (string rel in files1)
                {
                    byte[] a = File.ReadAllBytes(Path.Combine(out1, rel.Replace('/', Path.DirectorySeparatorChar)));
                    byte[] b = File.ReadAllBytes(Path.Combine(out2, rel.Replace('/', Path.DirectorySeparatorChar)));
                    Assert.True(a.SequenceEqual(b), "Mismatch in " + rel);
                }
            }
            finally { Cleanup(parent1); Cleanup(parent2); }
        }

        [Fact]
        public void Export_ManifestReconstructsTargetExactly_WithSparseExtension()
        {
            var clean = new byte[RomSize];
            for (int i = 0; i < clean.Length; i += 0x1000) clean[i] = (byte)(i & 0xFF); // some baseline content
            var target = new byte[RomSize + 0x8000];
            Array.Copy(clean, target, RomSize);
            for (int i = RomSize; i < target.Length; i++) target[i] = 0x00; // extension fill = 0x00
            // disjoint edits inside the clean region + non-fill extension overrides
            target[0x2000] = 0xEE;
            target[0x2001] = 0xEF;
            target[RomSize + 0x40] = 0x99;

            var (outDir, parent) = FreshOut();
            try
            {
                var result = BuildfileExportCore.Export(MakeRom(clean), MakeRom(target), new BuildfileExportOptions { OutputDirectory = outDir });
                Assert.True(result.Success, result.Error);

                // Every payload hash/length matches its declared record.
                foreach (var r in result.Manifest.Ranges)
                {
                    byte[] bytes = File.ReadAllBytes(Path.Combine(outDir, r.Payload.Replace('/', Path.DirectorySeparatorChar)));
                    Assert.Equal(r.Length, (uint)bytes.Length);
                    Assert.Equal(r.PayloadSha256, Sha256Hex(bytes));
                }

                byte[] recon = ReconstructFromProject(outDir, clean);
                Assert.True(recon.SequenceEqual(target), "Reconstruction did not match the target exactly.");
            }
            finally { Cleanup(parent); }
        }

        [Fact]
        public void Export_JsonParses_AllRelativePathsStayBeneathRoot_ForwardSlashes()
        {
            var clean = new byte[RomSize];
            var target = (byte[])clean.Clone();
            target[0x1234] = 0x5;

            var (outDir, parent) = FreshOut();
            try
            {
                var result = BuildfileExportCore.Export(MakeRom(clean), MakeRom(target), new BuildfileExportOptions { OutputDirectory = outDir });
                Assert.True(result.Success, result.Error);

                string json = File.ReadAllText(Path.Combine(outDir, "buildfile.json"));
                using var doc = JsonDocument.Parse(json); // parses
                Assert.DoesNotContain("\r\n", json);       // LF endings

                foreach (JsonElement r in doc.RootElement.GetProperty("ranges").EnumerateArray())
                {
                    string payload = r.GetProperty("payload").GetString();
                    Assert.DoesNotContain("\\", payload);   // forward slashes only
                    Assert.DoesNotContain("..", payload);   // no traversal
                    Assert.StartsWith("data/", payload);
                }
                // No absolute input paths leaked.
                Assert.DoesNotContain(":\\", json);
                Assert.DoesNotContain(outDir, json);
            }
            finally { Cleanup(parent); }
        }

        [Fact]
        public void Export_MainEvent_HasBalancedPushPop_OrgFillIncbin()
        {
            var clean = new byte[RomSize];
            var target = new byte[RomSize * 2];
            Array.Copy(clean, target, RomSize);
            for (int i = RomSize; i < target.Length; i++) target[i] = 0xFF;
            target[0x400] = 0xA1;
            target[RomSize + 0x8] = 0x55;

            var (outDir, parent) = FreshOut();
            try
            {
                var result = BuildfileExportCore.Export(MakeRom(clean), MakeRom(target), new BuildfileExportOptions { OutputDirectory = outDir });
                Assert.True(result.Success, result.Error);

                string ev = File.ReadAllText(Path.Combine(outDir, "main.event"));
                Assert.Contains("PUSH", ev);
                Assert.Contains("POP", ev);
                Assert.Equal(1, CountOccurrences(ev, "PUSH"));
                Assert.Equal(1, CountOccurrences(ev, "POP"));
                Assert.Contains("FILL 0x1000000 1 0xFF", ev);
                // one ORG + #incbin per payload range
                int incbin = CountOccurrences(ev, "#incbin");
                Assert.Equal(result.Manifest.Ranges.Count, incbin);
                foreach (var r in result.Manifest.Ranges)
                    Assert.Contains("#incbin \"" + r.Payload + "\"", ev);
                Assert.DoesNotContain("\r\n", ev);
            }
            finally { Cleanup(parent); }
        }

        [SkippableFact]
        public void Export_MainEvent_RealColorzCore_ReconstructsTarget()
        {
            string exe = FindBuiltColorzCore();
            Skip.If(exe == null,
                "ColorzCore is not built; deterministic main.event assertions still run.");

            var clean = new byte[RomSize];
            var target = new byte[RomSize * 2];
            Array.Copy(clean, target, RomSize);
            Array.Fill(target, (byte)0xFF, RomSize, RomSize);
            target[0x400] = 0xA1;
            target[RomSize + 0x8] = 0x55;

            var (outDir, parent) = FreshOut();
            Config savedConfig = CoreState.Config;
            Undo savedUndo = CoreState.Undo;
            ROM savedRom = CoreState.ROM;
            try
            {
                var export = BuildfileExportCore.Export(
                    MakeRom(clean),
                    MakeRom(target),
                    new BuildfileExportOptions { OutputDirectory = outDir });
                Assert.True(export.Success, export.Error);

                CoreState.Config = new Config { ["event_assembler"] = exe };

                string probeFile = Path.Combine(parent, "probe.event");
                File.WriteAllText(probeFile, "ORG 0x200\nBYTE 0x12 0x34\n");
                ROM probeRom = MakeRom((byte[])clean.Clone());
                CoreState.ROM = probeRom;
                CoreState.Undo = new Undo();
                var probe = EventAssemblerCompileCore.CompileAndInsert(
                    probeRom,
                    probeFile,
                    EventAssemblerCompileCore.FreeAreaMode.None,
                    CoreState.Undo.NewUndoData("buildfile-ea-probe"),
                    SymbolUtil.DebugSymbol.None);
                Skip.IfNot(probe.Success,
                    "A complete Event Assembler toolchain is unavailable: " + probe.ErrorMessage);

                ROM rebuilt = MakeRom((byte[])clean.Clone());
                CoreState.ROM = rebuilt;
                CoreState.Undo = new Undo();
                var result = EventAssemblerCompileCore.CompileAndInsert(
                    rebuilt,
                    Path.Combine(outDir, "main.event"),
                    EventAssemblerCompileCore.FreeAreaMode.None,
                    CoreState.Undo.NewUndoData("buildfile-main-event"),
                    SymbolUtil.DebugSymbol.None);

                Assert.True(result.Success, result.ErrorMessage);
                Assert.Equal(target, rebuilt.Data);
            }
            finally
            {
                CoreState.Config = savedConfig;
                CoreState.Undo = savedUndo;
                CoreState.ROM = savedRom;
                Cleanup(parent);
            }
        }

        [Fact]
        public void Export_ExistingDestination_IsRefused_NoStageLeftBehind()
        {
            var clean = new byte[RomSize];
            var target = (byte[])clean.Clone();
            target[0x10] = 0x9;

            var (outDir, parent) = FreshOut();
            try
            {
                Directory.CreateDirectory(outDir);
                File.WriteAllText(Path.Combine(outDir, "keepme.txt"), "precious");

                var result = BuildfileExportCore.Export(MakeRom(clean), MakeRom(target), new BuildfileExportOptions { OutputDirectory = outDir });
                Assert.False(result.Success);
                Assert.Contains("already exists", result.Error);
                // Existing content untouched.
                Assert.True(File.Exists(Path.Combine(outDir, "keepme.txt")));
                Assert.Equal("precious", File.ReadAllText(Path.Combine(outDir, "keepme.txt")));
                // No staging sibling left behind in the parent.
                Assert.DoesNotContain(Directory.GetDirectories(parent), d => Path.GetFileName(d).Contains(".stage-"));
            }
            finally { Cleanup(parent); }
        }

        [Fact]
        public void Export_EmptyDestinationCreatedAtPublish_IsNotReplaced()
        {
            var clean = new byte[RomSize];
            var target = (byte[])clean.Clone();
            target[0x10] = 0xA;

            var (outDir, parent) = FreshOut();
            try
            {
                var options = new BuildfileExportOptions
                {
                    OutputDirectory = outDir,
                    BeforePublishForTest = path => Directory.CreateDirectory(path),
                };

                var result = BuildfileExportCore.Export(MakeRom(clean), MakeRom(target), options);

                Assert.False(result.Success);
                Assert.Contains("already exists", result.Error);
                Assert.True(Directory.Exists(outDir));
                Assert.Empty(Directory.GetFileSystemEntries(outDir));
                Assert.DoesNotContain(Directory.GetDirectories(parent), d =>
                    Path.GetFileName(d).Contains(".stage-"));
            }
            finally { Cleanup(parent); }
        }

        [Fact]
        public void Export_StagedPayloadWriteFailure_RemovesStageAndDestination()
        {
            var clean = new byte[RomSize];
            var target = (byte[])clean.Clone();
            target[0x10] = 0x9;

            var (outDir, parent) = FreshOut();
            try
            {
                bool injected = false;
                var options = new BuildfileExportOptions
                {
                    OutputDirectory = outDir,
                    BeforePayloadWriteForTest = payloadPath =>
                    {
                        injected = true;
                        Directory.CreateDirectory(payloadPath);
                    },
                };

                var result = BuildfileExportCore.Export(MakeRom(clean), MakeRom(target), options);

                Assert.True(injected);
                Assert.False(result.Success);
                Assert.Contains("Export failed", result.Error);
                Assert.False(Directory.Exists(outDir));
                Assert.DoesNotContain(Directory.GetDirectories(parent), d =>
                    Path.GetFileName(d).Contains(".stage-") || Path.GetFileName(d).Contains(".psrc-"));
            }
            finally { Cleanup(parent); }
        }

        [SkippableFact]
        public void Export_StagedCleanupFailure_IsReportedWithResidualPath()
        {
            Skip.IfNot(OperatingSystem.IsWindows(), "Open-handle delete-lock is reliable only on Windows");

            var clean = new byte[RomSize];
            var target = (byte[])clean.Clone();
            target[0x10] = 0x9;

            var (outDir, parent) = FreshOut();
            FileStream held = null;
            try
            {
                var options = new BuildfileExportOptions
                {
                    OutputDirectory = outDir,
                    BeforePayloadWriteForTest = payloadPath =>
                    {
                        held = new FileStream(Path.Combine(Path.GetDirectoryName(payloadPath), "locked.bin"),
                            FileMode.Create, FileAccess.ReadWrite, FileShare.None);
                        held.WriteByte(1);
                        throw new IOException("injected payload write failure");
                    },
                };

                var result = BuildfileExportCore.Export(MakeRom(clean), MakeRom(target), options);

                Assert.False(result.Success);
                Assert.Contains("injected payload write failure", result.Error);
                Assert.Contains("Cleanup incomplete", result.Error);
                Assert.Contains(".stage-", result.Error);
                Assert.False(Directory.Exists(outDir));
                Assert.Contains(Directory.GetDirectories(parent),
                    d => Path.GetFileName(d).Contains(".stage-"));
            }
            finally
            {
                held?.Dispose();
                Cleanup(parent);
            }
        }

        [Fact]
        public void Export_MissingParent_Fails_NoPartialOutput()
        {
            var clean = new byte[RomSize];
            var target = (byte[])clean.Clone();
            target[0x10] = 0x9;

            string parent = Path.Combine(Path.GetTempPath(), "bfx_missing_" + Guid.NewGuid().ToString("N"));
            string outDir = Path.Combine(parent, "nope", "project"); // grandparent missing
            var result = BuildfileExportCore.Export(MakeRom(clean), MakeRom(target), new BuildfileExportOptions { OutputDirectory = outDir });
            Assert.False(result.Success);
            Assert.False(Directory.Exists(outDir));
            Assert.False(Directory.Exists(parent));
        }

        [Fact]
        public void Export_ProjectionRefusal_IsWarning_RawRecipeStillComplete()
        {
            var clean = new byte[RomSize];
            var target = (byte[])clean.Clone();
            target[0x2000] = 0x77;

            var (outDir, parent) = FreshOut();
            try
            {
                var options = new BuildfileExportOptions
                {
                    OutputDirectory = outDir,
                    ProjectionRunner = _ => BuildfileProjectionOutcome.Refuse("installed EA/BIN patch"),
                };
                var result = BuildfileExportCore.Export(MakeRom(clean), MakeRom(target), options);
                Assert.True(result.Success, result.Error);
                Assert.Equal("refused", result.Manifest.Projection.Status);
                Assert.Contains(result.Manifest.Warnings, w => w.Contains("Source projection refused"));
                // Raw recipe is still complete and reconstructs.
                Assert.False(Directory.Exists(Path.Combine(outDir, "source")));
                // No projection scratch sibling leaked next to the destination, and no stage.
                Assert.DoesNotContain(Directory.GetDirectories(parent), d =>
                    Path.GetFileName(d).Contains(".psrc-") || Path.GetFileName(d).Contains(".stage-"));
                byte[] recon = ReconstructFromProject(outDir, clean);
                Assert.True(recon.SequenceEqual(target));
            }
            finally { Cleanup(parent); }
        }

        [Fact]
        public void Export_ProjectionSuccess_SnapshotsToFreshSource_SanitizesScratchPath()
        {
            var clean = new byte[RomSize];
            var target = (byte[])clean.Clone();
            target[0x2000] = 0x77;

            var (outDir, parent) = FreshOut();
            try
            {
                var options = new BuildfileExportOptions
                {
                    OutputDirectory = outDir,
                    ProjectionRunner = scratch =>
                    {
                        // Emit CRLF (must become LF) and embed the scratch absolute path (must be
                        // sanitized to "source" so no environment/scratch location leaks).
                        File.WriteAllText(Path.Combine(scratch, "rom.rebuild"),
                            "PUSH\r\n#incbin \"" + scratch + "/rebuild_bin/x.bin\"\r\nPOP\r\n");
                        Directory.CreateDirectory(Path.Combine(scratch, "rebuild_bin"));
                        File.WriteAllBytes(Path.Combine(scratch, "rebuild_bin", "x.bin"), new byte[4]);
                        return BuildfileProjectionOutcome.Ok();
                    },
                };
                var result = BuildfileExportCore.Export(MakeRom(clean), MakeRom(target), options);
                Assert.True(result.Success, result.Error);
                Assert.Equal("success", result.Manifest.Projection.Status);
                string projected = Path.Combine(outDir, "source", "rom.rebuild");
                Assert.True(File.Exists(projected));
                string txt = File.ReadAllText(projected);
                Assert.DoesNotContain("\r\n", txt);                    // LF endings
                Assert.DoesNotContain(parent, txt);                    // no scratch/parent abs path leaked
                Assert.Contains("source/rebuild_bin/x.bin", txt);      // sanitized to the final rel dir
                // No scratch sibling remains next to the destination.
                Assert.DoesNotContain(Directory.GetDirectories(parent), d =>
                    Path.GetFileName(d).Contains(".psrc-") || Path.GetFileName(d).Contains(".stage-"));
                Assert.DoesNotContain(Directory.GetDirectories(outDir), d => Path.GetFileName(d).StartsWith("."));
            }
            finally { Cleanup(parent); }
        }

        [Fact]
        public void Export_ProjectionRunsBeforePublishStageExists()
        {
            var clean = new byte[RomSize];
            var target = (byte[])clean.Clone();
            target[0x2000] = 0x70;

            var (outDir, parent) = FreshOut();
            bool sawPublishStage = true;
            try
            {
                var result = BuildfileExportCore.Export(
                    MakeRom(clean),
                    MakeRom(target),
                    new BuildfileExportOptions
                    {
                        OutputDirectory = outDir,
                        ProjectionRunner = scratch =>
                        {
                            sawPublishStage = Directory.GetDirectories(
                                Path.GetDirectoryName(scratch)).Any(
                                    path => Path.GetFileName(path).Contains(".stage-"));
                            return BuildfileProjectionOutcome.Refuse("test refusal");
                        },
                    });

                Assert.True(result.Success, result.Error);
                Assert.False(sawPublishStage);
                Assert.Equal("refused", result.Manifest.Projection.Status);
            }
            finally { Cleanup(parent); }
        }

        [Fact]
        public void Export_ProjectionSnapshot_PreservesEmptyDirectories()
        {
            var clean = new byte[RomSize];
            var target = (byte[])clean.Clone();
            target[0x2000] = 0x71;

            var (outDir, parent) = FreshOut();
            try
            {
                var result = BuildfileExportCore.Export(
                    MakeRom(clean),
                    MakeRom(target),
                    new BuildfileExportOptions
                    {
                        OutputDirectory = outDir,
                        ProjectionRunner = scratch =>
                        {
                            Directory.CreateDirectory(
                                Path.Combine(scratch, "empty", "nested"));
                            return BuildfileProjectionOutcome.Ok();
                        },
                    });

                Assert.True(result.Success, result.Error);
                Assert.Equal("success", result.Manifest.Projection.Status);
                string nested = Path.Combine(outDir, "source", "empty", "nested");
                Assert.True(Directory.Exists(nested));
                Assert.Empty(Directory.GetFileSystemEntries(nested));
            }
            finally { Cleanup(parent); }
        }

        [Fact]
        public void Export_ProjectionSnapshot_EntryLimitFailsAdvisoryProjectionClosed()
        {
            var clean = new byte[RomSize];
            var target = (byte[])clean.Clone();
            target[0x2000] = 0x72;

            var (outDir, parent) = FreshOut();
            try
            {
                var result = BuildfileExportCore.Export(
                    MakeRom(clean),
                    MakeRom(target),
                    new BuildfileExportOptions
                    {
                        OutputDirectory = outDir,
                        ProjectionSnapshotMaxEntriesForTest = 2,
                        ProjectionRunner = scratch =>
                        {
                            File.WriteAllText(Path.Combine(scratch, "a.bin"), "a");
                            File.WriteAllText(Path.Combine(scratch, "b.bin"), "b");
                            File.WriteAllText(Path.Combine(scratch, "c.bin"), "c");
                            return BuildfileProjectionOutcome.Ok();
                        },
                    });

                Assert.True(result.Success, result.Error);
                Assert.Equal("error", result.Manifest.Projection.Status);
                Assert.Contains("2-entry limit", result.Manifest.Projection.Reason);
                Assert.False(Directory.Exists(Path.Combine(outDir, "source")));
            }
            finally { Cleanup(parent); }
        }

        [Fact]
        public void Export_ProjectionSnapshot_ByteLimitFailsAdvisoryProjectionClosed()
        {
            var clean = new byte[RomSize];
            var target = (byte[])clean.Clone();
            target[0x2000] = 0x73;

            var (outDir, parent) = FreshOut();
            try
            {
                var result = BuildfileExportCore.Export(
                    MakeRom(clean),
                    MakeRom(target),
                    new BuildfileExportOptions
                    {
                        OutputDirectory = outDir,
                        ProjectionSnapshotMaxBytesForTest = 4,
                        ProjectionRunner = scratch =>
                        {
                            File.WriteAllBytes(
                                Path.Combine(scratch, "too-large.bin"),
                                new byte[] { 1, 2, 3, 4, 5 });
                            return BuildfileProjectionOutcome.Ok();
                        },
                    });

                Assert.True(result.Success, result.Error);
                Assert.Equal("error", result.Manifest.Projection.Status);
                Assert.Contains("4-byte limit", result.Manifest.Projection.Reason);
                Assert.False(Directory.Exists(Path.Combine(outDir, "source")));
            }
            finally { Cleanup(parent); }
        }

        [Fact]
        public void Export_ProjectionSnapshot_InvalidUtf8TextIsAdvisoryError()
        {
            var clean = new byte[RomSize];
            var target = (byte[])clean.Clone();
            target[0x2000] = 0x74;

            var (outDir, parent) = FreshOut();
            try
            {
                var result = BuildfileExportCore.Export(
                    MakeRom(clean),
                    MakeRom(target),
                    new BuildfileExportOptions
                    {
                        OutputDirectory = outDir,
                        ProjectionRunner = scratch =>
                        {
                            File.WriteAllBytes(
                                Path.Combine(scratch, "invalid.txt"),
                                new byte[] { 0xC3, 0x28 });
                            return BuildfileProjectionOutcome.Ok();
                        },
                    });

                Assert.True(result.Success, result.Error);
                Assert.Equal("error", result.Manifest.Projection.Status);
                Assert.Contains("not valid UTF-8", result.Manifest.Projection.Reason);
                Assert.False(Directory.Exists(Path.Combine(outDir, "source")));
            }
            finally { Cleanup(parent); }
        }

        [Theory]
        [InlineData(0x8000, true)]
        [InlineData(0x81A4, true)]
        [InlineData(0x1000, false)]
        [InlineData(0x2000, false)]
        [InlineData(0x4000, false)]
        [InlineData(0x6000, false)]
        [InlineData(0xA000, false)]
        [InlineData(0xC000, false)]
        public void ProjectionFileSystemSafety_RegularMode_RejectsEverySpecialType(
            int mode,
            bool expected)
        {
            Assert.Equal(expected, ProjectionFileSystemSafety.IsRegularFileMode(mode));
        }

        [SkippableFact]
        public void TryEnumeratePlainProjectionTree_UnixFifo_IsRejectedWithoutOpening()
        {
            Skip.IfNot(
                OperatingSystem.IsLinux() || OperatingSystem.IsMacOS(),
                "Unix FIFO inode types are available only on Linux/macOS CI.");

            string root = Path.Combine(
                Path.GetTempPath(), "bff" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(root);
            try
            {
                string fifo = Path.Combine(root, "p");
                Assert.True(
                    CreateFifoUnix(fifo, 0x180) == 0,
                    "mkfifo failed with native error " + Marshal.GetLastWin32Error());

                bool success = BuildfileExportCore.TryEnumeratePlainProjectionTree(
                    root, root, out List<string> files, out string error);

                Assert.False(success);
                Assert.Empty(files);
                Assert.Contains("non-regular file", error);
                Assert.Contains("0x1000", error);
            }
            finally { Cleanup(root); }
        }

        [SkippableFact]
        public void TryEnumeratePlainProjectionTree_UnixSocket_IsRejectedWithoutOpening()
        {
            Skip.IfNot(
                OperatingSystem.IsLinux() || OperatingSystem.IsMacOS(),
                "Unix socket inode types are available only on Linux/macOS CI.");

            // Darwin limits filesystem-backed Unix socket paths to 104 bytes.
            string root = Path.Combine(
                OperatingSystem.IsMacOS() ? "/tmp" : Path.GetTempPath(),
                "bfs" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(root);
            string socketPath = Path.Combine(root, "s");
            if (OperatingSystem.IsMacOS())
                Assert.InRange(Encoding.UTF8.GetByteCount(socketPath), 1, 104);
            Socket socket = null;
            try
            {
                socket = new Socket(
                    AddressFamily.Unix,
                    SocketType.Stream,
                    ProtocolType.Unspecified);
                socket.Bind(new UnixDomainSocketEndPoint(socketPath));

                bool success = BuildfileExportCore.TryEnumeratePlainProjectionTree(
                    root, root, out List<string> files, out string error);

                Assert.False(success);
                Assert.Empty(files);
                Assert.Contains("non-regular file", error);
                Assert.Contains("0xC000", error);
            }
            finally
            {
                socket?.Dispose();
                try { File.Delete(socketPath); } catch { }
                Cleanup(root);
            }
        }

        [SkippableFact]
        public void ProjectionFileSystemSafety_UnixCharacterDevice_IsRejectedWithoutOpening()
        {
            Skip.IfNot(
                OperatingSystem.IsLinux() || OperatingSystem.IsMacOS(),
                "Unix device inode types are available only on Linux/macOS CI.");

            Assert.False(ProjectionFileSystemSafety.TryValidateRegularFile(
                "/dev/null", out string error));
            Assert.Contains("0x2000", error);
        }

        [SkippableFact]
        public void OpenRegularFileForRead_UnixCharacterDevice_IsRejected()
        {
            Skip.IfNot(
                OperatingSystem.IsLinux() || OperatingSystem.IsMacOS(),
                "Unix device inode types are available only on Linux/macOS CI.");

            IOException error = Assert.Throws<IOException>(
                () => ProjectionFileSystemSafety.OpenRegularFileForRead("/dev/null"));
            Assert.Contains("regular file", error.Message);
        }

        [SkippableFact]
        public void Export_ProjectionFileReplacedWithFifoBeforeOpen_IsRejectedWithoutBlocking()
        {
            Skip.IfNot(
                OperatingSystem.IsLinux() || OperatingSystem.IsMacOS(),
                "Unix FIFO inode types are available only on Linux/macOS CI.");

            var clean = new byte[RomSize];
            var target = (byte[])clean.Clone();
            target[0x2000] = 0x75;
            var (outDir, parent) = FreshOut();
            int fifoResult = 0;
            bool replaced = false;
            try
            {
                var options = new BuildfileExportOptions
                {
                    OutputDirectory = outDir,
                    ProjectionRunner = scratch =>
                    {
                        File.WriteAllText(Path.Combine(scratch, "victim.event"), "safe\n");
                        return BuildfileProjectionOutcome.Ok();
                    },
                    BeforeProjectionFileOpenForTest = file =>
                    {
                        if (replaced || Path.GetFileName(file) != "victim.event")
                            return;
                        replaced = true;
                        File.Delete(file);
                        fifoResult = CreateFifoUnix(file, 0x180);
                    },
                };

                BuildfileExportResult result =
                    BuildfileExportCore.Export(MakeRom(clean), MakeRom(target), options);

                if (fifoResult != 0)
                {
                    Skip.If(true, "mkfifo failed with native error "
                        + Marshal.GetLastWin32Error());
                    return;
                }
                Assert.True(replaced);
                Assert.True(result.Success, result.Error);
                Assert.Equal("error", result.Manifest.Projection.Status);
                Assert.Contains("regular file", result.Manifest.Projection.Reason);
                Assert.False(Directory.Exists(Path.Combine(outDir, "source")));
            }
            finally { Cleanup(parent); }
        }

        [SkippableFact]
        public void Export_ProjectionFileReplacedWithSymlinkBeforeOpen_DoesNotReadTarget()
        {
            var clean = new byte[RomSize];
            var target = (byte[])clean.Clone();
            target[0x2000] = 0x76;
            var (outDir, parent) = FreshOut();
            string externalRoot = Path.Combine(
                Path.GetTempPath(), "bfx_external_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(externalRoot);
            string external = Path.Combine(externalRoot, "outside.event");
            const string ExternalContent = "outside-secret-content\n";
            File.WriteAllText(external, ExternalContent);
            Exception linkError = null;
            bool replaced = false;
            try
            {
                var options = new BuildfileExportOptions
                {
                    OutputDirectory = outDir,
                    ProjectionRunner = scratch =>
                    {
                        File.WriteAllText(Path.Combine(scratch, "victim.event"), "safe\n");
                        return BuildfileProjectionOutcome.Ok();
                    },
                    BeforeProjectionFileOpenForTest = file =>
                    {
                        if (replaced || Path.GetFileName(file) != "victim.event")
                            return;
                        replaced = true;
                        File.Delete(file);
                        try { File.CreateSymbolicLink(file, external); }
                        catch (Exception ex) { linkError = ex; }
                    },
                };

                BuildfileExportResult result =
                    BuildfileExportCore.Export(MakeRom(clean), MakeRom(target), options);

                if (linkError != null)
                {
                    Skip.If(true, "Cannot create a file symlink here: " + linkError.Message);
                    return;
                }
                Assert.True(replaced);
                Assert.True(result.Success, result.Error);
                Assert.Equal("error", result.Manifest.Projection.Status);
                // The exact diagnostic text differs by platform here: Windows opens the reparse
                // point (FILE_FLAG_OPEN_REPARSE_POINT) and rejects it by attribute inspection
                // ("... is not a plain regular file"), while Unix rejects the symlink earlier, at
                // the O_NOFOLLOW open() call itself ("Cannot open projection entry without
                // following links"). Both wrap through the same "capture failed: " prefix, so
                // assert that stable, platform-independent contract instead of either message.
                Assert.Contains("capture failed", result.Manifest.Projection.Reason);
                Assert.False(Directory.Exists(Path.Combine(outDir, "source")));
                Assert.Equal(ExternalContent, File.ReadAllText(external));
            }
            finally
            {
                Cleanup(parent);
                try { Directory.Delete(externalRoot, true); } catch { }
            }
        }

        [SkippableFact]
        public void Export_ProjectionAncestorReplacedDuringCapture_UsesHeldDirectory()
        {
            var clean = new byte[RomSize];
            var target = (byte[])clean.Clone();
            target[0x2000] = 0x76;
            var (outDir, parent) = FreshOut();
            string externalRoot = Path.Combine(
                Path.GetTempPath(), "bfx_ancestor_external_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(externalRoot);
            string externalFile = Path.Combine(externalRoot, "victim.event");
            const string ExternalContent = "external-secret\n";
            const string OriginalContent = "held-original\n";
            File.WriteAllText(externalFile, ExternalContent);
            Exception swapError = null;
            bool replaced = false;
            try
            {
                var options = new BuildfileExportOptions
                {
                    OutputDirectory = outDir,
                    ProjectionRunner = scratch =>
                    {
                        string nested = Path.Combine(scratch, "nested");
                        Directory.CreateDirectory(nested);
                        File.WriteAllText(
                            Path.Combine(nested, "victim.event"),
                            OriginalContent);
                        return BuildfileProjectionOutcome.Ok();
                    },
                    BeforeProjectionFileOpenForTest = file =>
                    {
                        if (replaced || Path.GetFileName(file) != "victim.event")
                            return;
                        replaced = true;
                        string nested = Path.GetDirectoryName(file);
                        string moved = nested + "-moved";
                        try
                        {
                            Directory.Move(nested, moved);
                            Directory.CreateSymbolicLink(nested, externalRoot);
                        }
                        catch (Exception ex)
                        {
                            swapError = ex;
                        }
                    },
                };

                BuildfileExportResult result =
                    BuildfileExportCore.Export(MakeRom(clean), MakeRom(target), options);

                if (swapError != null)
                {
                    Skip.If(true, "Cannot replace an opened projection ancestor here: "
                        + swapError.Message);
                    return;
                }
                Assert.True(replaced);
                Assert.True(result.Success, result.Error);
                Assert.Equal("success", result.Manifest.Projection.Status);
                Assert.Equal(
                    OriginalContent,
                    File.ReadAllText(
                        Path.Combine(outDir, "source", "nested", "victim.event")));
                Assert.Equal(ExternalContent, File.ReadAllText(externalFile));
            }
            finally
            {
                Cleanup(parent);
                try { Directory.Delete(externalRoot, true); } catch { }
            }
        }

        [SkippableFact]
        public void Export_ProjectionHardLinkedTextAndBinary_AreRematerialized()
        {
            var clean = new byte[RomSize];
            var target = (byte[])clean.Clone();
            target[0x2000] = 0x77;
            var (outDir, parent) = FreshOut();
            string externalRoot = Path.Combine(
                Path.GetTempPath(), "bfx_links_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(externalRoot);
            string externalText = Path.Combine(externalRoot, "outside.event");
            string externalBinary = Path.Combine(externalRoot, "outside.bin");
            const string OriginalText = "external-text\n";
            byte[] originalBinary = { 1, 3, 5, 7 };
            File.WriteAllText(externalText, OriginalText);
            File.WriteAllBytes(externalBinary, originalBinary);
            string linkFailure = null;
            try
            {
                var options = new BuildfileExportOptions
                {
                    OutputDirectory = outDir,
                    ProjectionRunner = scratch =>
                    {
                        if (!CreateHardLink(
                            Path.Combine(scratch, "linked.event"),
                            externalText))
                        {
                            linkFailure = "text hard link failed with native error "
                                + Marshal.GetLastWin32Error();
                            return BuildfileProjectionOutcome.Fail(linkFailure);
                        }
                        if (!CreateHardLink(
                            Path.Combine(scratch, "linked.bin"),
                            externalBinary))
                        {
                            linkFailure = "binary hard link failed with native error "
                                + Marshal.GetLastWin32Error();
                            return BuildfileProjectionOutcome.Fail(linkFailure);
                        }
                        return BuildfileProjectionOutcome.Ok();
                    },
                };

                BuildfileExportResult result =
                    BuildfileExportCore.Export(MakeRom(clean), MakeRom(target), options);
                if (linkFailure != null)
                {
                    Skip.If(true, linkFailure);
                    return;
                }

                Assert.True(result.Success, result.Error);
                Assert.Equal("success", result.Manifest.Projection.Status);
                string publishedText =
                    Path.Combine(outDir, "source", "linked.event");
                string publishedBinary =
                    Path.Combine(outDir, "source", "linked.bin");

                File.WriteAllText(externalText, "external-mutated\n");
                File.WriteAllBytes(externalBinary, new byte[] { 9, 9, 9 });
                Assert.Equal(OriginalText, File.ReadAllText(publishedText));
                Assert.Equal(originalBinary, File.ReadAllBytes(publishedBinary));

                File.WriteAllText(publishedText, "published-mutated\n");
                File.WriteAllBytes(publishedBinary, new byte[] { 2, 4, 6 });
                Assert.Equal("external-mutated\n", File.ReadAllText(externalText));
                Assert.Equal(new byte[] { 9, 9, 9 }, File.ReadAllBytes(externalBinary));
            }
            finally
            {
                Cleanup(parent);
                try { Directory.Delete(externalRoot, true); } catch { }
            }
        }

        [SkippableFact]
        public void Export_ProjectionRunnerReplacesScratchRootWithLink_ExternalTreeUntouched()
        {
            var clean = new byte[RomSize];
            var target = (byte[])clean.Clone();
            target[0x2000] = 0x78;

            var (outDir, parent) = FreshOut();
            string external = Path.Combine(
                Path.GetTempPath(), "bfx_projection_external_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(external);
            string externalFile = Path.Combine(external, "outside.txt");
            const string Original = "outside\r\nmust remain unchanged\r\n";
            File.WriteAllText(externalFile, Original);
            Exception linkError = null;
            try
            {
                var options = new BuildfileExportOptions
                {
                    OutputDirectory = outDir,
                    ProjectionRunner = scratch =>
                    {
                        Directory.Delete(scratch);
                        try { Directory.CreateSymbolicLink(scratch, external); }
                        catch (Exception ex) { linkError = ex; }
                        return BuildfileProjectionOutcome.Ok();
                    },
                };

                var result = BuildfileExportCore.Export(MakeRom(clean), MakeRom(target), options);
                if (linkError != null)
                {
                    Skip.If(true, "Cannot create a directory symlink here: " + linkError.Message);
                    return;
                }
                Assert.True(result.Success, result.Error);
                Assert.Equal("error", result.Manifest.Projection.Status);
                // The exact diagnostic text differs by platform here: Windows opens the reparse
                // point (FILE_FLAG_OPEN_REPARSE_POINT) and rejects it by attribute inspection
                // ("... is not a plain directory"), while Unix rejects the symlinked root earlier,
                // at the O_NOFOLLOW open() call itself ("Cannot open projection root without
                // following links"). Both wrap through the same "capture failed: " prefix, so
                // assert that stable, platform-independent contract instead of either message.
                Assert.Contains("capture failed", result.Manifest.Projection.Reason);
                Assert.False(Directory.Exists(Path.Combine(outDir, "source")));
                Assert.Equal(Original, File.ReadAllText(externalFile));
                Assert.True(ReconstructFromProject(outDir, clean).SequenceEqual(target));
            }
            finally
            {
                Cleanup(parent);
                try { Directory.Delete(external, true); } catch { }
            }
        }

        [SkippableFact]
        public void Export_FreshProjectionDescendantReplacedBeforeValidation_IsRemovedWithoutExternalMutation()
        {
            var clean = new byte[RomSize];
            var target = (byte[])clean.Clone();
            target[0x2000] = 0x79;

            var (outDir, parent) = FreshOut();
            string external = Path.Combine(
                Path.GetTempPath(), "bfx_moved_child_external_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(external);
            string externalFile = Path.Combine(external, "outside.txt");
            const string Original = "external-child-content\n";
            File.WriteAllText(externalFile, Original);
            Exception linkError = null;
            try
            {
                var options = new BuildfileExportOptions
                {
                    OutputDirectory = outDir,
                    ProjectionRunner = scratch =>
                    {
                        string child = Path.Combine(scratch, "nested");
                        Directory.CreateDirectory(child);
                        File.WriteAllText(Path.Combine(child, "inside.txt"), "complete\n");
                        return BuildfileProjectionOutcome.Ok();
                    },
                    AfterProjectionMoveForTest = source =>
                    {
                        string child = Path.Combine(source, "nested");
                        Directory.Delete(child, true);
                        try { Directory.CreateSymbolicLink(child, external); }
                        catch (Exception ex) { linkError = ex; }
                    },
                };

                var result = BuildfileExportCore.Export(MakeRom(clean), MakeRom(target), options);
                if (linkError != null)
                {
                    Skip.If(true, "Cannot create a moved-source child symlink here: " + linkError.Message);
                    return;
                }

                Assert.True(result.Success, result.Error);
                Assert.Equal("error", result.Manifest.Projection.Status);
                Assert.Contains("symlink/junction", result.Manifest.Projection.Reason);
                Assert.False(Directory.Exists(Path.Combine(outDir, "source")));
                Assert.Equal(Original, File.ReadAllText(externalFile));
                Assert.True(ReconstructFromProject(outDir, clean).SequenceEqual(target));
                Assert.DoesNotContain(Directory.GetDirectories(parent), d =>
                    Path.GetFileName(d).Contains(".psrc-") || Path.GetFileName(d).Contains(".stage-"));
            }
            finally
            {
                Cleanup(parent);
                try { Directory.Delete(external, true); } catch { }
            }
        }

        [SkippableFact]
        public void Export_FreshProjectionFileReplacedWithHardLink_RematerializesOwnedInode()
        {
            var clean = new byte[RomSize];
            var target = (byte[])clean.Clone();
            target[0x2000] = 0x79;

            var (outDir, parent) = FreshOut();
            string external = Path.Combine(
                Path.GetTempPath(), "bfx_fresh_hardlink_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(external);
            string externalFile = Path.Combine(external, "outside.txt");
            const string ExternalContent = "external-content\n";
            File.WriteAllText(externalFile, ExternalContent);
            string linkError = null;
            try
            {
                var options = new BuildfileExportOptions
                {
                    OutputDirectory = outDir,
                    ProjectionRunner = scratch =>
                    {
                        File.WriteAllText(
                            Path.Combine(scratch, "projection.txt"),
                            "snapshot-content\n");
                        return BuildfileProjectionOutcome.Ok();
                    },
                    AfterProjectionMoveForTest = source =>
                    {
                        string projectedFile = Path.Combine(source, "projection.txt");
                        File.Delete(projectedFile);
                        if (!CreateHardLink(projectedFile, externalFile))
                        {
                            linkError = "Cannot create a hard link here; native error "
                                + Marshal.GetLastPInvokeError();
                        }
                    },
                };

                var result = BuildfileExportCore.Export(
                    MakeRom(clean),
                    MakeRom(target),
                    options);
                Skip.If(linkError != null, linkError);

                Assert.True(result.Success, result.Error);
                Assert.Equal("success", result.Manifest.Projection.Status);
                string publishedFile = Path.Combine(
                    outDir,
                    "source",
                    "projection.txt");
                Assert.Equal("snapshot-content\n", File.ReadAllText(publishedFile));
                using (FileStream publishedStream =
                    ProjectionFileSystemSafety.OpenRegularFileForRead(publishedFile))
                using (FileStream externalStream =
                    ProjectionFileSystemSafety.OpenRegularFileForRead(externalFile))
                {
                    Assert.False(ProjectionFileSystemSafety.SameOpenedFile(
                        publishedStream,
                        externalStream));
                }

                File.WriteAllText(publishedFile, "published-change\n");
                Assert.Equal(ExternalContent, File.ReadAllText(externalFile));
            }
            finally
            {
                Cleanup(parent);
                try { Directory.Delete(external, true); } catch { }
            }
        }

        [SkippableFact]
        public void Export_FreshProjectionRootReplacedBeforeValidation_PublishesAdvisoryError()
        {
            var clean = new byte[RomSize];
            var target = (byte[])clean.Clone();
            target[0x2000] = 0x79;

            var (outDir, parent) = FreshOut();
            string external = Path.Combine(
                Path.GetTempPath(), "bfx_fresh_external_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(external);
            string externalFile = Path.Combine(external, "outside.txt");
            const string Original = "external-content\n";
            File.WriteAllText(externalFile, Original);
            Exception linkError = null;
            try
            {
                var options = new BuildfileExportOptions
                {
                    OutputDirectory = outDir,
                    ProjectionRunner = scratch =>
                    {
                        File.WriteAllText(
                            Path.Combine(scratch, "projection.txt"),
                            "complete\n");
                        return BuildfileProjectionOutcome.Ok();
                    },
                    AfterProjectionMoveForTest = source =>
                    {
                        Directory.Delete(source, true);
                        try { Directory.CreateSymbolicLink(source, external); }
                        catch (Exception ex) { linkError = ex; }
                    },
                };

                var result = BuildfileExportCore.Export(
                    MakeRom(clean),
                    MakeRom(target),
                    options);
                if (linkError != null)
                {
                    Skip.If(true, "Cannot create a fresh-source symlink here: "
                        + linkError.Message);
                    return;
                }

                Assert.True(result.Success, result.Error);
                Assert.Equal("error", result.Manifest.Projection.Status);
                Assert.Empty(result.Manifest.Projection.Directory);
                Assert.False(Directory.Exists(Path.Combine(outDir, "source")));
                Assert.Equal(Original, File.ReadAllText(externalFile));
                Assert.Contains(
                    "Source projection error",
                    File.ReadAllText(Path.Combine(outDir, "README.md")));
                Assert.Contains(
                    "\"status\": \"error\"",
                    File.ReadAllText(Path.Combine(outDir, "buildfile.json")));
            }
            finally
            {
                Cleanup(parent);
                try { Directory.Delete(external, true); } catch { }
            }
        }

        [SkippableFact]
        public void Export_UnsafeMaterializedProjectionCannotBeRemoved_AbortsWithoutExternalMutation()
        {
            var clean = new byte[RomSize];
            var target = (byte[])clean.Clone();
            target[0x2000] = 0x79;

            var (outDir, parent) = FreshOut();
            string external = Path.Combine(
                Path.GetTempPath(), "bfx_moved_external_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(external);
            string externalFile = Path.Combine(external, "outside.txt");
            const string Original = "external-content\n";
            File.WriteAllText(externalFile, Original);
            Exception linkError = null;
            try
            {
                var options = new BuildfileExportOptions
                {
                    OutputDirectory = outDir,
                    ProjectionRunner = scratch =>
                    {
                        File.WriteAllText(Path.Combine(scratch, "projection.txt"), "complete\n");
                        return BuildfileProjectionOutcome.Ok();
                    },
                    AfterProjectionMoveForTest = source =>
                    {
                        Directory.Delete(source, true);
                        try { Directory.CreateSymbolicLink(source, external); }
                        catch (Exception ex) { linkError = ex; }
                    },
                    UnsafeMovedProjectionCleanupForTest = _ => false,
                };

                var result = BuildfileExportCore.Export(MakeRom(clean), MakeRom(target), options);
                if (linkError != null)
                {
                    Skip.If(true, "Cannot create a moved-source symlink here: " + linkError.Message);
                    return;
                }

                Assert.False(result.Success);
                Assert.Contains("Unsafe materialized projection could not be removed", result.Error);
                Assert.False(Directory.Exists(outDir));
                Assert.Equal(Original, File.ReadAllText(externalFile));
                Assert.DoesNotContain(Directory.GetDirectories(parent), d =>
                    Path.GetFileName(d).Contains(".psrc-") || Path.GetFileName(d).Contains(".stage-"));
            }
            finally
            {
                Cleanup(parent);
                try { Directory.Delete(external, true); } catch { }
            }
        }

        [Fact]
        public void Export_ProjectionError_IsWarning_NoSourcePublished_NoScratchSibling()
        {
            var clean = new byte[RomSize];
            var target = (byte[])clean.Clone();
            target[0x30] = 0x44;

            var (outDir, parent) = FreshOut();
            try
            {
                var options = new BuildfileExportOptions
                {
                    OutputDirectory = outDir,
                    ProjectionRunner = scratch =>
                    {
                        File.WriteAllText(Path.Combine(scratch, "partial.txt"), "half-written");
                        return BuildfileProjectionOutcome.Fail("producer crashed");
                    },
                };
                var result = BuildfileExportCore.Export(MakeRom(clean), MakeRom(target), options);
                Assert.True(result.Success, result.Error);           // raw recipe still complete
                Assert.Equal("error", result.Manifest.Projection.Status);
                Assert.Contains(result.Manifest.Warnings, w => w.Contains("Source projection error"));
                Assert.False(Directory.Exists(Path.Combine(outDir, "source")));
                Assert.DoesNotContain(Directory.GetDirectories(parent), d =>
                    Path.GetFileName(d).Contains(".psrc-") || Path.GetFileName(d).Contains(".stage-"));
            }
            finally { Cleanup(parent); }
        }

        [Fact]
        public void Export_ProjectionOutcomeReasonEmbeddingScratchPath_IsSanitizedInManifestAndReadme()
        {
            // A caller-supplied ProjectionRunner (or a runner exception message) could otherwise
            // echo the absolute scratch path back through its OWN outcome.Reason — not just
            // through projected file content. That must be sanitized before it ever reaches
            // m.Projection.Reason / m.Warnings / buildfile.json / README.md (Copilot review
            // finding: projection scratch reason paths).
            var clean = new byte[RomSize];
            var target = (byte[])clean.Clone();
            target[0x30] = 0x44;

            var (outDir, parent) = FreshOut();
            try
            {
                var options = new BuildfileExportOptions
                {
                    OutputDirectory = outDir,
                    ProjectionRunner = scratch =>
                        BuildfileProjectionOutcome.Fail("could not write " + scratch + "\\rebuild_bin\\x.bin"),
                };
                var result = BuildfileExportCore.Export(MakeRom(clean), MakeRom(target), options);
                Assert.True(result.Success, result.Error);
                Assert.Equal("error", result.Manifest.Projection.Status);
                Assert.DoesNotContain(parent, result.Manifest.Projection.Reason);
                Assert.Contains("source", result.Manifest.Projection.Reason);
                foreach (string w in result.Manifest.Warnings)
                    Assert.DoesNotContain(parent, w);

                string json = File.ReadAllText(Path.Combine(outDir, "buildfile.json"));
                Assert.DoesNotContain(parent, json);
                string readme = File.ReadAllText(Path.Combine(outDir, "README.md"));
                Assert.DoesNotContain(parent, readme);
            }
            finally { Cleanup(parent); }
        }

        [Fact]
        public void Export_ProjectionRunnerThrows_ExceptionMessageWithScratchPath_IsSanitizedInManifest()
        {
            // The plugin-boundary catch around the ProjectionRunner call turns ANY thrown
            // exception into an advisory failure reason — its Message must be sanitized exactly
            // like a normal outcome.Reason (same Copilot review finding as above).
            var clean = new byte[RomSize];
            var target = (byte[])clean.Clone();
            target[0x30] = 0x44;

            var (outDir, parent) = FreshOut();
            try
            {
                var options = new BuildfileExportOptions
                {
                    OutputDirectory = outDir,
                    ProjectionRunner = scratch =>
                        throw new IOException("access denied: " + scratch),
                };
                var result = BuildfileExportCore.Export(MakeRom(clean), MakeRom(target), options);
                Assert.True(result.Success, result.Error);
                Assert.Equal("error", result.Manifest.Projection.Status);
                Assert.DoesNotContain(parent, result.Manifest.Projection.Reason);

                string json = File.ReadAllText(Path.Combine(outDir, "buildfile.json"));
                Assert.DoesNotContain(parent, json);
            }
            finally { Cleanup(parent); }
        }

        [Fact]
        public void Export_ReadmeWrittenAfterProjection_ReflectsFinalStatusAndWarning()
        {
            // The README must be generated AFTER RunProjection finalizes manifest projection
            // status/warnings — otherwise it would always show the initial "skipped" status,
            // even when the projection actually succeeds/fails/refuses (Copilot review finding:
            // README written before the projection warning was known).
            var clean = new byte[RomSize];
            var target = (byte[])clean.Clone();
            target[0x2000] = 0x66;

            var (outDir, parent) = FreshOut();
            try
            {
                var options = new BuildfileExportOptions
                {
                    OutputDirectory = outDir,
                    ProjectionRunner = _ => BuildfileProjectionOutcome.Refuse("installed EA/BIN patch"),
                };
                var result = BuildfileExportCore.Export(MakeRom(clean), MakeRom(target), options);
                Assert.True(result.Success, result.Error);
                Assert.Equal("refused", result.Manifest.Projection.Status);
                Assert.Contains(result.Manifest.Warnings, w => w.Contains("Source projection refused"));

                string readme = File.ReadAllText(Path.Combine(outDir, "README.md"));
                Assert.Contains("## Warnings", readme);
                Assert.Contains("Source projection refused", readme);
            }
            finally { Cleanup(parent); }
        }

        [Fact]
        public void Export_PatchDirectoryListingFailure_ManifestReasonIsStableAndPathFree()
        {
            // An existing-but-inaccessible patch base directory must surface as a stable,
            // PATH-FREE manifest reason — never the raw exception message or the absolute
            // patch base directory (Copilot review finding: enumError absolute path / per-record
            // raw exception path). Deterministic injection via the internal
            // PatchDirectoryListerForTest seam (no flaky real permission changes needed).
            var clean = new byte[RomSize];
            var target = (byte[])clean.Clone();
            target[0x2000] = 0x66;

            string patchBase = Path.Combine(Path.GetTempPath(), "bfx_patch_fail_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(patchBase);
            var (outDir, parent) = FreshOut();
            try
            {
                var options = new BuildfileExportOptions
                {
                    OutputDirectory = outDir,
                    PatchBaseDirectory = patchBase,
                    PatchDirectoryListerForTest = _ => throw new IOException("Access to the path '" + patchBase + "' is denied."),
                };
                var result = BuildfileExportCore.Export(MakeRom(clean), MakeRom(target), options);
                Assert.True(result.Success, result.Error);
                Assert.Equal("unavailable", result.Manifest.Patches.Status);
                Assert.DoesNotContain(patchBase, result.Manifest.Patches.Reason);
                Assert.DoesNotContain("Access to the path", result.Manifest.Patches.Reason);
                Assert.Equal("patch enumeration failed; check patch library directory permissions",
                    result.Manifest.Patches.Reason);

                string json = File.ReadAllText(Path.Combine(outDir, "buildfile.json"));
                Assert.DoesNotContain(patchBase, json);
            }
            finally { Cleanup(parent); try { Directory.Delete(patchBase, true); } catch { } }
        }

        [Fact]
        public void DeleteAndVerifyGone_MissingPath_RequiresDirectAbsenceVerification()
        {
            bool result = BuildfileExportCore.DeleteAndVerifyGone(
                "missing",
                (_, _) => throw new DirectoryNotFoundException("missing"),
                _ => throw new FileNotFoundException("missing"),
                out string error);

            Assert.True(result);
            Assert.Equal("", error);
        }

        [Fact]
        public void DeleteAndVerifyGone_AttributeAccessFailure_FailsClosed()
        {
            bool result = BuildfileExportCore.DeleteAndVerifyGone(
                "blocked",
                (_, _) => { },
                _ => throw new UnauthorizedAccessException("denied"),
                out string error);

            Assert.False(result);
            Assert.Contains("could not inspect path before delete", error);
            Assert.Contains("denied", error);
        }

        [Fact]
        public void DeleteAndVerifyGone_ReparseRoot_DeletesOnlyLink()
        {
            bool deleted = false;
            bool? recursive = null;

            bool result = BuildfileExportCore.DeleteAndVerifyGone(
                "linked-root",
                (_, recurse) =>
                {
                    recursive = recurse;
                    deleted = true;
                },
                _ => deleted
                    ? throw new DirectoryNotFoundException("deleted")
                    : FileAttributes.Directory | FileAttributes.ReparsePoint,
                out string error);

            Assert.True(result);
            Assert.Equal("", error);
            Assert.False(recursive);
        }

        [Fact]
        public void DeleteAndVerifyGone_DeleteReportsMissingButPathRemains_FailsClosed()
        {
            bool result = BuildfileExportCore.DeleteAndVerifyGone(
                "replaced",
                (_, _) => throw new DirectoryNotFoundException("directory missing"),
                _ => FileAttributes.Archive,
                out string error);

            Assert.False(result);
            Assert.Equal("path still present after delete", error);
        }

        [SkippableFact]
        public void Export_ProjectionCleanupFailure_AbortsExport_NoDestination()
        {
            // On refusal/error the external scratch is deleted and verified gone; if it CANNOT be
            // removed, the export must abort with no destination (never publish a partial scratch).
            // Simulated by holding an open handle to a file inside the scratch (locks it on Windows).
            Skip.IfNot(OperatingSystem.IsWindows(), "Open-handle delete-lock is reliable only on Windows");

            var clean = new byte[RomSize];
            var target = (byte[])clean.Clone();
            target[0x30] = 0x55;

            var (outDir, parent) = FreshOut();
            FileStream held = null;
            try
            {
                var options = new BuildfileExportOptions
                {
                    OutputDirectory = outDir,
                    ProjectionRunner = scratch =>
                    {
                        // Open (and keep open) a file inside the scratch so Directory.Delete fails.
                        held = new FileStream(Path.Combine(scratch, "locked.bin"),
                            FileMode.Create, FileAccess.ReadWrite, FileShare.None);
                        held.WriteByte(1);
                        return BuildfileProjectionOutcome.Refuse("refused with a locked scratch");
                    },
                };
                var result = BuildfileExportCore.Export(MakeRom(clean), MakeRom(target), options);
                Assert.False(result.Success);
                Assert.Contains("refusing to publish", result.Error);
                Assert.False(Directory.Exists(outDir));
            }
            finally
            {
                held?.Dispose();
                Cleanup(parent);
            }
        }

        [Fact]
        public void Export_EmptyPatchDir_IsUnavailable_NotInitialized()
        {
            // Fresh installations contain empty version stub directories. A successful scan
            // with no PATCH_*.txt files therefore means the advisory library is unavailable.
            var clean = new byte[RomSize];
            var target = (byte[])clean.Clone();
            target[0x10] = 0x3;

            var (outDir, parent) = FreshOut();
            try
            {
                string emptyPatchDir = Path.Combine(parent, "empty-patch2");
                Directory.CreateDirectory(emptyPatchDir);
                var options = new BuildfileExportOptions
                {
                    OutputDirectory = outDir,
                    PatchBaseDirectory = emptyPatchDir,
                };
                var result = BuildfileExportCore.Export(MakeRom(clean), MakeRom(target), options);
                Assert.True(result.Success, result.Error);
                Assert.Equal("unavailable", result.Manifest.Patches.Status);
                Assert.Contains("empty or not initialized", result.Manifest.Patches.Reason);
                Assert.Empty(result.Manifest.Patches.Installed);
            }
            finally { Cleanup(parent); }
        }

        [Fact]
        public void Export_PatchInventoryUsesMetadataNamesForFilesInSharedFolder()
        {
            var clean = new byte[RomSize];
            var target = (byte[])clean.Clone();
            target[0x10] = 0x3;

            var (outDir, parent) = FreshOut();
            try
            {
                string patchBase = Path.Combine(parent, "patch2");
                string systemDir = Path.Combine(patchBase, "SYSTEM");
                Directory.CreateDirectory(systemDir);
                File.WriteAllLines(Path.Combine(systemDir, "PATCH_Eirika.txt"),
                    new[] { "TYPE=ADDR", "NAME=Eirika Patch" });
                File.WriteAllLines(Path.Combine(systemDir, "PATCH_Ephraim.txt"),
                    new[] { "TYPE=ADDR", "NAME=Ephraim Patch" });

                var result = BuildfileExportCore.Export(MakeRom(clean), MakeRom(target),
                    new BuildfileExportOptions
                    {
                        OutputDirectory = outDir,
                        PatchBaseDirectory = patchBase,
                    });

                Assert.True(result.Success, result.Error);
                Assert.Equal("available", result.Manifest.Patches.Status);
                Assert.Equal(2, result.Manifest.Patches.Installed.Count);
                Assert.Contains(result.Manifest.Patches.Installed, p => p.Name == "Eirika Patch");
                Assert.Contains(result.Manifest.Patches.Installed, p => p.Name == "Ephraim Patch");
                Assert.DoesNotContain(result.Manifest.Patches.Installed, p => p.Name == "SYSTEM");
            }
            finally { Cleanup(parent); }
        }

        [Fact]
        public void TryEnumeratePatches_DistinguishesMissingAndEmptyFromFailure()
        {
            // Missing directory → true + empty (NOT a failure).
            string missing = Path.Combine(Path.GetTempPath(), "bfx_pm_missing_" + Guid.NewGuid().ToString("N"));
            Assert.True(PatchMetadataCore.TryEnumeratePatches(missing, null, "en", out var m, out var mErr));
            Assert.Empty(m);
            Assert.Equal("", mErr);

            // Existing empty directory → true + empty (NOT a failure).
            string empty = Path.Combine(Path.GetTempPath(), "bfx_pm_empty_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(empty);
            try
            {
                Assert.True(PatchMetadataCore.TryEnumeratePatches(empty, null, "en", out var e, out var eErr));
                Assert.Empty(e);
                Assert.Equal("", eErr);
                // The legacy EnumeratePatches API still returns an (empty) list for the same input.
                Assert.Empty(PatchMetadataCore.EnumeratePatches(empty, null, "en"));
                Assert.True(PatchMetadataCore.IsExpectedFileSystemException(new ArgumentException()));
                Assert.False(PatchMetadataCore.IsExpectedFileSystemException(new ArgumentNullException()));
                Assert.False(PatchMetadataCore.IsExpectedFileSystemException(new ArgumentOutOfRangeException()));
            }
            finally { try { Directory.Delete(empty, true); } catch { } }
        }

        [Fact]
        public void TryEnumeratePatches_ReadFailure_ReturnsExplicitFailure()
        {
            string patchBase = Path.Combine(Path.GetTempPath(), "bfx_pm_read_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(patchBase);
            try
            {
                File.WriteAllText(Path.Combine(patchBase, "PATCH_Test.txt"), "NAME=Test");

                bool success = PatchMetadataCore.TryEnumeratePatches(
                    patchBase, null, "en", _ => throw new IOException("injected read failure"),
                    out var patches, out string error);

                Assert.False(success);
                Assert.Empty(patches);
                Assert.Contains("injected read failure", error);
            }
            finally { try { Directory.Delete(patchBase, true); } catch { } }
        }

        [Fact]
        public void TryEnumeratePatches_ListingFailureDistinguishesMissingFromAccessDenied()
        {
            bool missing = PatchMetadataCore.TryEnumeratePatches(
                "missing", null, "en", File.ReadAllLines,
                _ => throw new DirectoryNotFoundException("missing"),
                out var missingPatches, out string missingError);
            Assert.True(missing);
            Assert.Empty(missingPatches);
            Assert.Equal("", missingError);

            bool denied = PatchMetadataCore.TryEnumeratePatches(
                "denied", null, "en", File.ReadAllLines,
                _ => throw new UnauthorizedAccessException("denied"),
                out var deniedPatches, out string deniedError);
            Assert.False(denied);
            Assert.Empty(deniedPatches);
            Assert.Equal("denied", deniedError);
        }

        [Fact]
        public void Export_ProjectionSanitizesNativeForwardAndJsonEscapedScratchSpellings()
        {
            var clean = new byte[RomSize];
            var target = (byte[])clean.Clone();
            target[0x2000] = 0x66;

            var (outDir, parent) = FreshOut();
            try
            {
                var options = new BuildfileExportOptions
                {
                    OutputDirectory = outDir,
                    ProjectionRunner = scratch =>
                    {
                        string fwd = scratch.Replace('\\', '/');
                        string esc = scratch.Replace("\\", "\\\\");
                        // Emit all three spellings across text files, plus CRLF to be normalized.
                        File.WriteAllText(Path.Combine(scratch, "a.txt"), "native=" + scratch + "\r\n");
                        File.WriteAllText(Path.Combine(scratch, "b.txt"), "fwd=" + fwd + "\r\n");
                        File.WriteAllText(Path.Combine(scratch, "c.json"), "{\"p\":\"" + esc + "/x.bin\"}\r\n");
                        return BuildfileProjectionOutcome.Ok();
                    },
                };
                var result = BuildfileExportCore.Export(MakeRom(clean), MakeRom(target), options);
                Assert.True(result.Success, result.Error);
                Assert.Equal("success", result.Manifest.Projection.Status);

                string sourceDir = Path.Combine(outDir, "source");
                foreach (string f in Directory.GetFiles(sourceDir))
                {
                    string txt = File.ReadAllText(f);
                    Assert.DoesNotContain("\r\n", txt);        // LF normalization
                    Assert.DoesNotContain(parent, txt);        // native spelling gone
                    Assert.DoesNotContain(parent.Replace('\\', '/'), txt);          // forward gone
                    Assert.DoesNotContain(parent.Replace("\\", "\\\\"), txt);       // JSON-escaped gone
                    Assert.Contains("source", txt);            // replaced with the final rel dir
                }
            }
            finally { Cleanup(parent); }
        }

        [Fact]
        public void Export_ProjectionSanitizesCaseVariantsOnCaseInsensitivePlatforms()
        {
            if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS())
                return;

            var clean = new byte[RomSize];
            var target = (byte[])clean.Clone();
            target[0x2000] = 0x67;

            var (outDir, parent) = FreshOut();
            string scratchPath = "";
            try
            {
                var options = new BuildfileExportOptions
                {
                    OutputDirectory = outDir,
                    ProjectionRunner = scratch =>
                    {
                        scratchPath = scratch;
                        string alternateCase = scratch.ToUpperInvariant();
                        string forward = alternateCase.Replace('\\', '/');
                        string escaped = alternateCase.Replace("\\", "\\\\");
                        File.WriteAllText(Path.Combine(scratch, "paths.txt"),
                            alternateCase + "\n" + forward + "\n" + escaped + "\n");
                        return new BuildfileProjectionOutcome
                        {
                            Status = BuildfileProjectionStatus.Success,
                            Reason = "generated under " + alternateCase,
                        };
                    },
                };

                var result = BuildfileExportCore.Export(MakeRom(clean), MakeRom(target), options);
                Assert.True(result.Success, result.Error);
                string projected = File.ReadAllText(Path.Combine(outDir, "source", "paths.txt"));
                Assert.DoesNotContain(scratchPath.ToUpperInvariant(), projected.ToUpperInvariant());
                Assert.Contains("source", projected);
                Assert.DoesNotContain(
                    scratchPath.ToUpperInvariant(),
                    result.Manifest.Projection.Reason.ToUpperInvariant());
            }
            finally { Cleanup(parent); }
        }

        [Fact]
        public void Export_StageAndScratchNameCollisions_RetryWithoutTouchingExistingTrees()
        {
            var clean = new byte[RomSize];
            var target = (byte[])clean.Clone();
            target[0x2000] = 0x68;

            var (outDir, parent) = FreshOut();
            try
            {
                string name = Path.GetFileName(outDir);
                string stagePrefix = BuildfileExportCore.MakeTemporaryDirectoryPrefix(name, "stage");
                string scratchPrefix = BuildfileExportCore.MakeTemporaryDirectoryPrefix(name, "psrc");
                Guid stageCollisionId = Guid.Parse("11111111-1111-1111-1111-111111111111");
                Guid stageId = Guid.Parse("22222222-2222-2222-2222-222222222222");
                Guid scratchCollisionId = Guid.Parse("33333333-3333-3333-3333-333333333333");
                Guid scratchId = Guid.Parse("44444444-4444-4444-4444-444444444444");
                string stageCollision = Path.Combine(
                    parent, stagePrefix + stageCollisionId.ToString("N"));
                string scratchCollision = Path.Combine(
                    parent, scratchPrefix + scratchCollisionId.ToString("N"));
                Directory.CreateDirectory(stageCollision);
                Directory.CreateDirectory(scratchCollision);
                File.WriteAllText(Path.Combine(stageCollision, "owner.txt"), "stage-owner");
                File.WriteAllText(Path.Combine(scratchCollision, "owner.txt"), "scratch-owner");

                var ids = new Queue<Guid>(new[]
                {
                    scratchCollisionId, scratchId, stageCollisionId, stageId,
                });
                var options = new BuildfileExportOptions
                {
                    OutputDirectory = outDir,
                    GuidFactoryForTest = () => ids.Dequeue(),
                    ProjectionRunner = scratch =>
                    {
                        File.WriteAllText(Path.Combine(scratch, "projection.txt"), "complete\n");
                        return BuildfileProjectionOutcome.Ok();
                    },
                };

                var result = BuildfileExportCore.Export(MakeRom(clean), MakeRom(target), options);
                Assert.True(result.Success, result.Error);
                Assert.Equal("stage-owner", File.ReadAllText(Path.Combine(stageCollision, "owner.txt")));
                Assert.Equal("scratch-owner", File.ReadAllText(Path.Combine(scratchCollision, "owner.txt")));
                Assert.True(File.Exists(Path.Combine(outDir, "source", "projection.txt")));
                Assert.Empty(ids);
            }
            finally { Cleanup(parent); }
        }

        [Fact]
        public void Export_LongValidOutputBasename_UsesBoundedTemporaryComponents()
        {
            var clean = new byte[RomSize];
            var target = (byte[])clean.Clone();
            target[0x2000] = 0x69;

            string parent = Path.Combine(
                Path.GetTempPath(), "feb_buildfile_component_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(parent);
            string outputName = new string('o', 230);
            string outDir = Path.Combine(parent, outputName);
            try
            {
                string stagePrefix = BuildfileExportCore.MakeTemporaryDirectoryPrefix(outputName, "stage");
                string scratchPrefix = BuildfileExportCore.MakeTemporaryDirectoryPrefix(outputName, "psrc");
                Assert.True((stagePrefix + Guid.NewGuid().ToString("N")).Length < 255);
                Assert.True((scratchPrefix + Guid.NewGuid().ToString("N")).Length < 255);

                var result = BuildfileExportCore.Export(
                    MakeRom(clean),
                    MakeRom(target),
                    new BuildfileExportOptions
                    {
                        OutputDirectory = outDir,
                        ProjectionRunner = scratch =>
                        {
                            File.WriteAllText(Path.Combine(scratch, "projection.txt"), "complete\n");
                            return BuildfileProjectionOutcome.Ok();
                        },
                    });

                Assert.True(result.Success, result.Error);
                Assert.True(File.Exists(Path.Combine(outDir, "source", "projection.txt")));
            }
            finally { Cleanup(parent); }
        }

        [Fact]
        public void TryCreateDirectoryExclusive_WindowsLongPath_PreservesManagedPathSupport()
        {
            if (!OperatingSystem.IsWindows())
                return;

            string root = Path.Combine(
                Path.GetTempPath(), "feb_buildfile_long_" + Guid.NewGuid().ToString("N"));
            try
            {
                string parent = root;
                while (parent.Length < 280)
                    parent = Path.Combine(parent, new string('a', 40));
                Directory.CreateDirectory(parent);

                string candidate = Path.Combine(
                    parent, "stage-" + Guid.NewGuid().ToString("N"));
                Assert.True(candidate.Length > 260);
                Assert.True(BuildfileExportCore.TryCreateDirectoryExclusive(candidate));
                Assert.True(Directory.Exists(candidate));
                Assert.False(BuildfileExportCore.TryCreateDirectoryExclusive(candidate));
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { }
            }
        }

        [Fact]
        public void AtomicDirectoryOperations_BrowserFailClosedBeforeFilesystemAccess()
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "bfx-browser-" + Guid.NewGuid().ToString("N"));
            string source = Path.Combine(root, "source");
            string destination = Path.Combine(root, "destination");

            PlatformNotSupportedException reservationError =
                Assert.Throws<PlatformNotSupportedException>(() =>
                    BuildfileExportCore.TryCreateDirectoryExclusive(
                        source,
                        isBrowser: true));
            Assert.Contains("atomic directory reservation", reservationError.Message);

            PlatformNotSupportedException publicationError =
                Assert.Throws<PlatformNotSupportedException>(() =>
                    BuildfileExportCore.PublishDirectoryNoReplace(
                        source,
                        destination,
                        isBrowser: true));
            Assert.Contains("atomic no-replace publication", publicationError.Message);
            Assert.False(Directory.Exists(root));
        }

        [Fact]
        public void ToWindowsExtendedPath_ConvertsDriveAndUncPaths()
        {
            if (!OperatingSystem.IsWindows())
                return;

            Assert.Equal(
                @"\\?\C:\repo\stage",
                BuildfileExportCore.ToWindowsExtendedPath(@"C:\repo\stage"));
            Assert.Equal(
                @"\\?\UNC\server\share\stage",
                BuildfileExportCore.ToWindowsExtendedPath(@"\\server\share\stage"));
        }

        [Fact]
        public void Export_PatchDirectoryIsAFile_IsUnavailable_NotAFailure()
        {
            // A patch base that is a FILE (or otherwise not a directory) must yield an advisory
            // "unavailable" inventory, never abort the authoritative export.
            var clean = new byte[RomSize];
            var target = (byte[])clean.Clone();
            target[0x10] = 0x2;

            var (outDir, parent) = FreshOut();
            try
            {
                string fakePatchDir = Path.Combine(parent, "not-a-dir.txt");
                File.WriteAllText(fakePatchDir, "this is a file, not a patch directory");
                var options = new BuildfileExportOptions
                {
                    OutputDirectory = outDir,
                    PatchBaseDirectory = fakePatchDir,
                };
                var result = BuildfileExportCore.Export(MakeRom(clean), MakeRom(target), options);
                Assert.True(result.Success, result.Error);
                Assert.Equal("unavailable", result.Manifest.Patches.Status);
            }
            finally { Cleanup(parent); }
        }

        [Fact]
        public void Export_PatchLibraryMissing_IsUnavailable_NotAFailure()
        {
            var clean = new byte[RomSize];
            var target = (byte[])clean.Clone();
            target[0x10] = 0x1;

            var (outDir, parent) = FreshOut();
            try
            {
                var options = new BuildfileExportOptions
                {
                    OutputDirectory = outDir,
                    PatchBaseDirectory = Path.Combine(parent, "does-not-exist"),
                };
                var result = BuildfileExportCore.Export(MakeRom(clean), MakeRom(target), options);
                Assert.True(result.Success, result.Error);
                Assert.Equal("unavailable", result.Manifest.Patches.Status);
                Assert.NotEqual("", result.Manifest.Patches.Reason);
            }
            finally { Cleanup(parent); }
        }

        // ------------------------------------------------------------- path safety

        [Fact]
        public void Export_TrailingForwardSlashOut_PublishesAtIntendedDirectory()
            => AssertTrailingSeparatorPublishes("/");

        [SkippableFact]
        public void Export_TrailingBackslashOut_WindowsOnly_PublishesAtIntendedDirectory()
        {
            // '\' is a directory separator only on Windows; on Unix it is a legal filename
            // character, so a trailing '\' is NOT a separator there.
            Skip.IfNot(OperatingSystem.IsWindows(), "Backslash separator only on Windows");
            AssertTrailingSeparatorPublishes("\\");
        }

        static void AssertTrailingSeparatorPublishes(string sep)
        {
            var clean = new byte[RomSize];
            var target = (byte[])clean.Clone();
            target[0x1234] = 0x9;

            var (outDir, parent) = FreshOut();
            try
            {
                // A correct, not-yet-existing destination spelled WITH a trailing separator
                // must succeed and publish exactly at the trimmed directory.
                var result = BuildfileExportCore.Export(MakeRom(clean), MakeRom(target),
                    new BuildfileExportOptions { OutputDirectory = outDir + sep });
                Assert.True(result.Success, result.Error);
                Assert.Equal(BuildfilePathSafety.NormalizeFullPath(outDir), result.PublishedPath);
                Assert.True(File.Exists(Path.Combine(outDir, "buildfile.json")));
                Assert.False(Directory.Exists(outDir + sep + Path.GetFileName(outDir))); // no nested dup
            }
            finally { Cleanup(parent); }
        }

        [Fact]
        public void PathSafety_NormalizeAndEquals_AreTrailingSeparatorIndependent()
        {
            string baseDir = Path.Combine(Path.GetTempPath(), "bfx_norm_" + Guid.NewGuid().ToString("N"));
            string withSep = baseDir + Path.DirectorySeparatorChar;
            Assert.Equal(BuildfilePathSafety.NormalizeFullPath(baseDir), BuildfilePathSafety.NormalizeFullPath(withSep));
            Assert.True(BuildfilePathSafety.PathsEqual(baseDir, withSep));
            // A filesystem root is preserved (not trimmed to empty).
            string root = Path.GetPathRoot(Path.GetTempPath());
            Assert.False(string.IsNullOrEmpty(BuildfilePathSafety.NormalizeFullPath(root)));
        }

        [SkippableFact]
        public void PathSafety_WindowsDeviceNamespaces_AreRejectedBeforeInspection()
        {
            Skip.IfNot(OperatingSystem.IsWindows(), "Windows device namespaces only exist on Windows");
            string ordinary = Path.Combine(
                Path.GetPathRoot(Path.GetTempPath())!,
                "bfx-device",
                "rom.gba");
            string[] devicePaths =
            {
                @"\\?\" + ordinary,
                @"\\.\" + ordinary,
                "//?/" + ordinary.Replace('\\', '/'),
                "//./" + ordinary.Replace('\\', '/'),
                @"\\?\UNC\server\share\rom.gba",
                @"\\.\UNC\server\share\rom.gba",
                @"\??\" + ordinary,
                @"\??\UNC\server\share\rom.gba",
            };

            foreach (string devicePath in devicePaths)
            {
                IOException normalizeError = Assert.Throws<IOException>(
                    () => BuildfilePathSafety.NormalizeFullPath(devicePath));
                Assert.Contains("device-namespace", normalizeError.Message);

                bool inspected = false;
                IOException resolveError = Assert.Throws<IOException>(() =>
                    BuildfilePathSafety.ResolvePhysicalPath(devicePath, _ =>
                    {
                        inspected = true;
                        return FileAttributes.Normal;
                    }));
                Assert.Contains("device-namespace", resolveError.Message);
                Assert.False(inspected);
            }

            string ordinaryUnc = @"\\server\share\rom.gba";
            Assert.Equal(ordinaryUnc, BuildfilePathSafety.NormalizeFullPath(ordinaryUnc));
        }

        [SkippableFact]
        public void PathSafety_SamePhysicalFile_WindowsHardLink_UsesFileIdentity()
        {
            Skip.IfNot(OperatingSystem.IsWindows(), "Windows file identity only asserted on Windows");
            string root = Path.Combine(Path.GetTempPath(), "bfx-hardlink-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string original = Path.Combine(root, "original.gba");
            string alias = Path.Combine(root, "alias.gba");
            File.WriteAllBytes(original, new byte[16]);
            try
            {
                if (!CreateHardLinkWindows(alias, original, IntPtr.Zero))
                {
                    Skip.If(true, "Cannot create a hard link here; Win32 error "
                        + Marshal.GetLastWin32Error());
                    return;
                }

                Assert.False(BuildfilePathSafety.PathsEqual(original, alias));
                Assert.True(BuildfilePathSafety.SamePhysicalFile(original, alias));
                Assert.True(BuildfilePathSafety.SameWindowsFileIdentity(
                    original,
                    alias,
                    try128BitIdentity: false));
            }
            finally { try { Directory.Delete(root, true); } catch { } }
        }

        [SkippableFact]
        public void PathSafety_SamePhysicalFile_WindowsLongPathHardLink_UsesExtendedIdentityPath()
        {
            Skip.IfNot(OperatingSystem.IsWindows(), "Windows long-path identity only asserted on Windows");
            string root = Path.Combine(
                Path.GetTempPath(), "bfx-long-identity-" + Guid.NewGuid().ToString("N"));
            string parent = root;
            while (parent.Length < 280)
                parent = Path.Combine(parent, new string('i', 40));
            Directory.CreateDirectory(parent);
            string original = Path.Combine(parent, "original.gba");
            string alias = Path.Combine(parent, "alias.gba");
            File.WriteAllBytes(original, new byte[16]);
            try
            {
                if (!CreateHardLinkWindows(
                    BuildfileExportCore.ToWindowsExtendedPath(alias),
                    BuildfileExportCore.ToWindowsExtendedPath(original),
                    IntPtr.Zero))
                {
                    Skip.If(true, "Cannot create a long-path hard link here; Win32 error "
                        + Marshal.GetLastWin32Error());
                    return;
                }

                Assert.True(original.Length > 260);
                Assert.True(BuildfilePathSafety.SamePhysicalFile(original, alias));
                Assert.True(BuildfilePathSafety.SameWindowsFileIdentity(
                    original,
                    alias,
                    try128BitIdentity: false));
            }
            finally { try { Directory.Delete(root, true); } catch { } }
        }

        [SkippableFact]
        public void ProjectionFileSystemSafety_SameOpenedFile_DetectsHardLinks()
        {
            Skip.If(
                OperatingSystem.IsBrowser(),
                "Browser does not expose native opened-file identities.");

            string root = Path.Combine(
                Path.GetTempPath(),
                "bfx-opened-identity-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string original = Path.Combine(root, "original.gba");
            string alias = Path.Combine(root, "alias.gba");
            string distinct = Path.Combine(root, "distinct.gba");
            File.WriteAllBytes(original, new byte[16]);
            File.WriteAllBytes(distinct, new byte[16]);
            try
            {
                if (!CreateHardLink(alias, original))
                {
                    Skip.If(true, "Cannot create a hard link here; native error "
                        + Marshal.GetLastWin32Error());
                    return;
                }

                using FileStream originalStream =
                    ProjectionFileSystemSafety.OpenRegularFileForRead(original);
                using FileStream aliasStream =
                    ProjectionFileSystemSafety.OpenRegularFileForRead(alias);
                using FileStream distinctStream =
                    ProjectionFileSystemSafety.OpenRegularFileForRead(distinct);

                Assert.True(ProjectionFileSystemSafety.SameOpenedFile(
                    originalStream,
                    aliasStream));
                Assert.False(ProjectionFileSystemSafety.SameOpenedFile(
                    originalStream,
                    distinctStream));
            }
            finally { try { Directory.Delete(root, true); } catch { } }
        }

        [Fact]
        public void PathSafety_SamePhysicalFile_DistinctDirectories_AreNotSame()
        {
            string root = Path.Combine(Path.GetTempPath(), "bfx-dirs-" + Guid.NewGuid().ToString("N"));
            string first = Path.Combine(root, "first");
            string second = Path.Combine(root, "second");
            Directory.CreateDirectory(first);
            Directory.CreateDirectory(second);
            try
            {
                Assert.False(BuildfilePathSafety.SamePhysicalFile(first, second));
                if (OperatingSystem.IsWindows())
                {
                    Assert.False(BuildfilePathSafety.SameWindowsFileIdentity(
                        first,
                        second,
                        try128BitIdentity: false));
                }
            }
            finally { try { Directory.Delete(root, true); } catch { } }
        }

        [Fact]
        public void PathSafety_ContainsParentTraversal_ForwardSlashDotDot_True()
        {
            Assert.True(BuildfilePathSafety.ContainsParentTraversal("a/../b.gba"));
            Assert.True(BuildfilePathSafety.ContainsParentTraversal("../x.gba"));
            Assert.True(BuildfilePathSafety.ContainsParentTraversal("dir/.."));
            Assert.True(BuildfilePathSafety.ContainsParentTraversal("/abs/dir/../f.gba"));
        }

        [Fact]
        public void PathSafety_ContainsParentTraversal_DottedFilenamesAndDotSegments_False()
        {
            // A filename that merely CONTAINS dots is not a traversal segment.
            Assert.False(BuildfilePathSafety.ContainsParentTraversal("my..rom.gba"));
            Assert.False(BuildfilePathSafety.ContainsParentTraversal("..config/x.gba")); // segment "..config" != ".."
            Assert.False(BuildfilePathSafety.ContainsParentTraversal("a/b/c.gba"));
            Assert.False(BuildfilePathSafety.ContainsParentTraversal("./rel.gba"));       // '.' is allowed
            Assert.False(BuildfilePathSafety.ContainsParentTraversal(""));
        }

        [SkippableFact]
        public void PathSafety_ContainsParentTraversal_BackslashDotDot_WindowsOnly()
        {
            Skip.IfNot(OperatingSystem.IsWindows(), "Backslash is a separator only on Windows");
            Assert.True(BuildfilePathSafety.ContainsParentTraversal(@"a\..\b.gba"));
            Assert.True(BuildfilePathSafety.ContainsParentTraversal(@"C:\dir\..\f.gba"));
            Assert.True(BuildfilePathSafety.ContainsParentTraversal(@"C:..\f.gba"));
            Assert.True(BuildfilePathSafety.ContainsParentTraversal(@"C:dir\..\f.gba"));
            Assert.False(BuildfilePathSafety.ContainsParentTraversal(@"C:..config\f.gba"));
            // A backslash inside a filename on Unix would NOT be a separator; on Windows it is.
        }

        [Fact]
        public void PathSafety_ResolvePhysical_DotSegmentAndOrdinaryRelative_ResolveCleanly()
        {
            // A '.' segment and ordinary components are supported (unambiguous no-op), and
            // resolution returns the real physical file without throwing.
            string dir = Path.Combine(Path.GetTempPath(), "bfx_dot_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string real = Path.Combine(dir, "rom.gba");
                File.WriteAllBytes(real, new byte[16]);
                string withDot = Path.Combine(dir, ".", "rom.gba");
                Assert.False(BuildfilePathSafety.ContainsParentTraversal(withDot));
                // Compare physically-resolved forms (macOS temp resolves /var -> /private/var).
                Assert.Equal(BuildfilePathSafety.ResolvePhysicalPath(real), BuildfilePathSafety.ResolvePhysicalPath(withDot));
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        [Fact]
        public void PathSafety_ResolvePhysical_NonexistentTail_AppendedLexically_NoThrow()
        {
            // The destination may not exist yet; missing trailing components are appended
            // lexically (they cannot be links) and resolution must not throw.
            string baseDir = Path.Combine(Path.GetTempPath(), "bfx_res_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(baseDir);
            try
            {
                string missing = Path.Combine(baseDir, "does", "not", "exist.gba");
                string resolved = BuildfilePathSafety.ResolvePhysicalPath(missing);
                // Expected = physically-resolved existing base + the lexical missing tail
                // (macOS temp resolves /var -> /private/var, so the base must be resolved too).
                string expected = Path.TrimEndingDirectorySeparator(
                    Path.Combine(BuildfilePathSafety.ResolvePhysicalPath(baseDir), "does", "not", "exist.gba"));
                Assert.Equal(expected, resolved);
            }
            finally { try { Directory.Delete(baseDir, true); } catch { } }
        }

        [Fact]
        public void PathSafety_ResolvePhysical_ParentTraversalRejectedBeforeInspection()
        {
            string traversed = Path.Combine(Path.GetTempPath(), "link", "..", "secret.gba");
            bool inspected = false;

            IOException ex = Assert.Throws<IOException>(() =>
                BuildfilePathSafety.ResolvePhysicalPath(traversed, _ =>
                {
                    inspected = true;
                    return FileAttributes.Normal;
                }));

            Assert.Contains("parent-directory", ex.Message);
            Assert.False(inspected);
            Assert.Throws<IOException>(() =>
                BuildfilePathSafety.SamePhysicalFile(traversed, traversed));
        }

        [SkippableFact]
        public void PathSafety_ResolvePhysical_LinkTargetTraversal_MatchesPlatformFilesystemOrder()
        {
            string root = Path.Combine(
                Path.GetTempPath(), "bfx_link_traversal_" + Guid.NewGuid().ToString("N"));
            string real = Path.Combine(root, "real");
            string realChild = Path.Combine(real, "child");
            Directory.CreateDirectory(realChild);
            string physicalTarget = Path.Combine(real, "mod.gba");
            string lexicalTarget = Path.Combine(root, "mod.gba");
            File.WriteAllBytes(physicalTarget, new byte[] { 0x11 });
            File.WriteAllBytes(lexicalTarget, new byte[] { 0x22 });
            string pivot = Path.Combine(root, "pivot");
            string link = Path.Combine(root, "link.gba");
            try
            {
                try
                {
                    Directory.CreateSymbolicLink(pivot, realChild);
                    File.CreateSymbolicLink(
                        link,
                        Path.Combine("pivot", "..", "mod.gba"));
                }
                catch (Exception ex)
                {
                    Skip.If(true, "Cannot create symbolic links here: " + ex.Message);
                    return;
                }

                // Unix resolves pivot before '..' (real/child -> real); Win32 lexically folds the
                // target first (pivot/../mod -> the sibling mod). Canonicalization must agree with
                // the platform's actual file-open behavior so the checked path is the loaded path.
                string expectedTarget = OperatingSystem.IsWindows()
                    ? lexicalTarget
                    : physicalTarget;
                Assert.Equal(File.ReadAllBytes(expectedTarget), File.ReadAllBytes(link));
                Assert.Equal(
                    BuildfilePathSafety.ResolvePhysicalPath(expectedTarget),
                    BuildfilePathSafety.ResolvePhysicalPath(link));
            }
            finally { try { Directory.Delete(root, true); } catch { } }
        }

        [Fact]
        public void PathSafety_ResolvePhysical_AttributeAccessFailure_FailsClosed()
        {
            string blocked = Path.Combine(
                Path.GetTempPath(),
                "bfx_blocked_" + Guid.NewGuid().ToString("N"),
                "target.gba");

            IOException ex = Assert.Throws<IOException>(() =>
                BuildfilePathSafety.ResolvePhysicalPath(blocked, candidate =>
                {
                    if (candidate.Contains("bfx_blocked_", StringComparison.Ordinal))
                        throw new UnauthorizedAccessException("denied");
                    return File.GetAttributes(candidate);
                }));

            Assert.Contains("Cannot inspect path component", ex.Message);
            Assert.IsType<UnauthorizedAccessException>(ex.InnerException);
        }

        [Fact]
        public void PathSafety_InspectorArgumentNull_PropagatesProgrammerDefect()
        {
            string path = Path.Combine(
                Path.GetTempPath(),
                "bfx_argnull_" + Guid.NewGuid().ToString("N"),
                "target.gba");

            Assert.Throws<ArgumentNullException>(() =>
                BuildfilePathSafety.ResolvePhysicalPath(
                    path,
                    _ => throw new ArgumentNullException("candidate")));
            Assert.Throws<ArgumentNullException>(() =>
                BuildfilePathSafety.IsReparsePoint(
                    path,
                    _ => throw new ArgumentNullException("candidate")));
        }

        [Fact]
        public void PathSafety_IsReparsePoint_DistinguishesMissingFromInspectionFailure()
        {
            Assert.False(BuildfilePathSafety.IsReparsePoint(
                "missing-file",
                _ => throw new FileNotFoundException("missing")));
            Assert.False(BuildfilePathSafety.IsReparsePoint(
                "missing-directory",
                _ => throw new DirectoryNotFoundException("missing")));

            IOException ex = Assert.Throws<IOException>(() =>
                BuildfilePathSafety.IsReparsePoint(
                    "blocked",
                    _ => throw new UnauthorizedAccessException("denied")));
            Assert.Contains("Could not inspect path for reparse point", ex.Message);
            Assert.IsType<UnauthorizedAccessException>(ex.InnerException);
        }

        [SkippableFact]
        public void PathSafety_SamePhysicalFile_AncestorLink_SameFileTrue_DistinctFileFalse()
        {
            // linkRoot -> realRoot. Two DISTINCT files under the shared (symlinked) ancestor
            // must NOT be considered the same (this is the macOS /var -> /private/var case);
            // the SAME file reached through the ancestor link MUST be the same.
            string root = Path.Combine(Path.GetTempPath(), "bfx_anc_" + Guid.NewGuid().ToString("N"));
            string realRoot = Path.Combine(root, "real");
            string linkRoot = Path.Combine(root, "link");
            Directory.CreateDirectory(realRoot);
            File.WriteAllBytes(Path.Combine(realRoot, "a.gba"), new byte[16]);
            File.WriteAllBytes(Path.Combine(realRoot, "b.gba"), new byte[16]);
            try
            {
                try { Directory.CreateSymbolicLink(linkRoot, realRoot); }
                catch (Exception ex) { Skip.If(true, "Cannot create a directory symlink here: " + ex.Message); return; }

                string realA = Path.Combine(realRoot, "a.gba");
                string linkedA = Path.Combine(linkRoot, "a.gba");   // ancestor-link alias of realA
                string linkedB = Path.Combine(linkRoot, "b.gba");   // distinct file, shared link ancestor

                // Same physical file through an ancestor link → rejected as identical.
                Assert.True(BuildfilePathSafety.SamePhysicalFile(realA, linkedA));
                // Lexical equality alone would have MISSED this (proves realpath is required).
                Assert.False(BuildfilePathSafety.PathsEqual(realA, linkedA));
                // Two distinct files under the shared symlinked ancestor → accepted (not same).
                Assert.False(BuildfilePathSafety.SamePhysicalFile(realA, linkedB));
                // Physical resolution collapses the ancestor link to the SAME physical file.
                Assert.Equal(BuildfilePathSafety.ResolvePhysicalPath(realA), BuildfilePathSafety.ResolvePhysicalPath(linkedA));
            }
            finally { try { Directory.Delete(root, true); } catch { } }
        }

        [SkippableFact]
        public void PathSafety_SamePhysicalFile_FinalLink_SameFileTrue_DistinctFileFalse()
        {
            string dir = Path.Combine(Path.GetTempPath(), "bfx_fin_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string realFile = Path.Combine(dir, "real.gba");
            string other = Path.Combine(dir, "other.gba");
            File.WriteAllBytes(realFile, new byte[16]);
            File.WriteAllBytes(other, new byte[16]);
            string linkToReal = Path.Combine(dir, "link.gba");
            try
            {
                try { File.CreateSymbolicLink(linkToReal, realFile); }
                catch (Exception ex) { Skip.If(true, "Cannot create a file symlink here: " + ex.Message); return; }

                // Final symlink pointing at the SAME physical file → identical.
                Assert.True(BuildfilePathSafety.SamePhysicalFile(realFile, linkToReal));
                // Final symlink vs a DISTINCT file → not identical (allowed by identity).
                Assert.False(BuildfilePathSafety.SamePhysicalFile(linkToReal, other));
                // Both spellings resolve to the SAME physical file (compare resolved forms).
                Assert.Equal(BuildfilePathSafety.ResolvePhysicalPath(realFile), BuildfilePathSafety.ResolvePhysicalPath(linkToReal));
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        [SkippableFact]
        public void PathSafety_ResolvePhysical_MacTempDistinctFiles_AreNotSame()
        {
            // macOS Path.GetTempPath() typically resolves through /var -> /private/var. Two
            // DISTINCT temp files must resolve without throwing and must not be judged identical.
            Skip.IfNot(OperatingSystem.IsMacOS(), "macOS /var symlink behavior only asserted on macOS");
            string a = Path.Combine(Path.GetTempPath(), "bfx_mac_a_" + Guid.NewGuid().ToString("N") + ".gba");
            string b = Path.Combine(Path.GetTempPath(), "bfx_mac_b_" + Guid.NewGuid().ToString("N") + ".gba");
            File.WriteAllBytes(a, new byte[16]);
            File.WriteAllBytes(b, new byte[16]);
            try
            {
                Assert.False(BuildfilePathSafety.SamePhysicalFile(a, b));
                Assert.True(BuildfilePathSafety.SamePhysicalFile(a, a));
                Assert.NotNull(BuildfilePathSafety.ResolvePhysicalPath(a));
            }
            finally { try { File.Delete(a); File.Delete(b); } catch { } }
        }

        [SkippableFact]
        public void Export_ReparseParent_IsRefused_NoDestination()
        {
            var clean = new byte[RomSize];
            var target = (byte[])clean.Clone();
            target[0x10] = 0x5;

            string root = Path.Combine(Path.GetTempPath(), "bfx_reparse_" + Guid.NewGuid().ToString("N"));
            string realParent = Path.Combine(root, "real");
            string linkParent = Path.Combine(root, "link");
            Directory.CreateDirectory(realParent);
            try
            {
                try { Directory.CreateSymbolicLink(linkParent, realParent); }
                catch (Exception ex) { Skip.If(true, "Cannot create a directory symlink here: " + ex.Message); return; }

                string outDir = Path.Combine(linkParent, "project");
                var result = BuildfileExportCore.Export(MakeRom(clean), MakeRom(target),
                    new BuildfileExportOptions { OutputDirectory = outDir });
                Assert.False(result.Success);
                Assert.Contains("reparse point", result.Error);
                Assert.False(Directory.Exists(outDir));
                // No stage sibling leaked into the (real) target directory either.
                Assert.DoesNotContain(Directory.GetDirectories(realParent), d => Path.GetFileName(d).Contains(".stage-"));
            }
            finally { try { Directory.Delete(root, true); } catch { } }
        }

        // --------------------------------------------------------------- utilities

        static SortedSet<string> RelativeFileSet(string root)
        {
            var set = new SortedSet<string>(StringComparer.Ordinal);
            foreach (string f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                set.Add(Path.GetRelativePath(root, f).Replace('\\', '/'));
            return set;
        }

        static int CountOccurrences(string haystack, string needle)
        {
            int count = 0, idx = 0;
            while ((idx = haystack.IndexOf(needle, idx, StringComparison.Ordinal)) >= 0) { count++; idx += needle.Length; }
            return count;
        }

        static bool CreateHardLink(string linkPath, string existingPath)
        {
            if (OperatingSystem.IsWindows())
                return CreateHardLinkWindows(linkPath, existingPath, IntPtr.Zero);
            return CreateHardLinkUnix(existingPath, linkPath) == 0;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true,
            EntryPoint = "CreateHardLinkW")]
        static extern bool CreateHardLinkWindows(
            string fileName,
            string existingFileName,
            IntPtr securityAttributes);

        [DllImport("libc", SetLastError = true, EntryPoint = "link")]
        static extern int CreateHardLinkUnix(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string existingPath,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string linkPath);
    }
}
