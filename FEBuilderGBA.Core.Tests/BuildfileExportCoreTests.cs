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
using BoundedPatchReadFailureKind = FEBuilderGBA.PatchMetadataCore.BoundedPatchReadFailureKind;

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
                // ColorzCore 0bca76f ParseFillStatement accepts amount + optional value only.
                string fillLine = ev.Split('\n').Single(
                    line => line.StartsWith("FILL ", StringComparison.Ordinal));
                Assert.Equal("FILL 0x1000000 0xFF", fillLine);
                Assert.Equal(
                    3,
                    fillLine.Split(
                        new[] { ' ' },
                        StringSplitOptions.RemoveEmptyEntries).Length);
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
        public void Export_BuiltInProjection_PropagatesByteLimitToManifestValidation()
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
                        IncludeSourceProjection = true,
                        ProjectionSnapshotMaxBytesForTest = 1,
                        BuiltInProjectionProducerForTest = (_, _, _, manifestPath) =>
                            File.WriteAllText(
                                manifestPath,
                                "@_CRC32 12345678\n"
                                + "@_REBUILDADDRESS 00100000\n"),
                    });

                Assert.True(result.Success, result.Error);
                    Assert.Contains("1-byte validation limit", result.Manifest.Projection.Reason);
                    Assert.Equal("error", result.Manifest.Projection.Status);
                Assert.False(Directory.Exists(Path.Combine(outDir, "source")));
            }
            finally { Cleanup(parent); }
        }

        [Fact]
        public void Export_BuiltInProjection_PropagatesEntryLimitToManifestValidation()
        {
            var clean = new byte[RomSize];
            var target = (byte[])clean.Clone();
            target[0x2000] = 0x75;

            var (outDir, parent) = FreshOut();
            try
            {
                var result = BuildfileExportCore.Export(
                    MakeRom(clean),
                    MakeRom(target),
                    new BuildfileExportOptions
                    {
                        OutputDirectory = outDir,
                        IncludeSourceProjection = true,
                        ProjectionSnapshotMaxEntriesForTest = 1,
                        BuiltInProjectionProducerForTest = (_, _, _, manifestPath) =>
                        {
                            string sidecarDir = Path.Combine(
                                Path.GetDirectoryName(manifestPath),
                                "rebuild_bin");
                            Directory.CreateDirectory(sidecarDir);
                            File.WriteAllBytes(
                                Path.Combine(sidecarDir, "data.bin"),
                                new byte[] { 1 });
                            File.WriteAllText(
                                manifestPath,
                                "@_CRC32 12345678\n"
                                + "@_REBUILDADDRESS 00100000\n"
                                + "@BIN 00100000 rebuild_bin"
                                + Path.DirectorySeparatorChar + "data.bin\n"
                                + "@BIN 00100001 rebuild_bin"
                                + Path.DirectorySeparatorChar + "data.bin\n");
                        },
                    });

                Assert.True(result.Success, result.Error);
                Assert.Equal("error", result.Manifest.Projection.Status);
                Assert.Contains(
                    "1-sidecar directive limit",
                    result.Manifest.Projection.Reason);
                Assert.False(Directory.Exists(Path.Combine(outDir, "source")));
            }
            finally { Cleanup(parent); }
        }

        [Fact]
        public void Export_ProjectionSnapshot_TextFileLimitFailsBeforeSanitization()
        {
            var clean = new byte[RomSize];
            var target = (byte[])clean.Clone();
            target[0x2000] = 0x76;

            var (outDir, parent) = FreshOut();
            try
            {
                var result = BuildfileExportCore.Export(
                    MakeRom(clean),
                    MakeRom(target),
                    new BuildfileExportOptions
                    {
                        OutputDirectory = outDir,
                        ProjectionTextFileMaxBytesForTest = 4,
                        ProjectionRunner = scratch =>
                        {
                            File.WriteAllText(Path.Combine(scratch, "too-large.txt"), "12345");
                            return BuildfileProjectionOutcome.Ok();
                        },
                    });

                Assert.True(result.Success, result.Error);
                Assert.Equal("error", result.Manifest.Projection.Status);
                Assert.Contains(
                    "4-byte text-file limit",
                    result.Manifest.Projection.Reason);
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

        [Fact]
        public void Export_ProjectionSnapshot_Utf16BomTextIsAdvisoryError()
        {
            var clean = new byte[RomSize];
            var target = (byte[])clean.Clone();
            target[0x2000] = 0x77;

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
                                Path.Combine(scratch, "utf16.txt"),
                                new byte[] { 0xFF, 0xFE, 0x41, 0x00 });
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

        [Fact]
        public void Export_ProjectionSnapshot_Utf8BomIsAcceptedAndRemoved()
        {
            var clean = new byte[RomSize];
            var target = (byte[])clean.Clone();
            target[0x2000] = 0x78;

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
                                Path.Combine(scratch, "utf8.txt"),
                                new byte[]
                                {
                                    0xEF, 0xBB, 0xBF,
                                    (byte)'l', (byte)'i', (byte)'n', (byte)'e',
                                    (byte)'\r', (byte)'\n',
                                });
                            return BuildfileProjectionOutcome.Ok();
                        },
                    });

                Assert.True(result.Success, result.Error);
                Assert.Equal("success", result.Manifest.Projection.Status);
                Assert.Equal(
                    Encoding.UTF8.GetBytes("line\n"),
                    File.ReadAllBytes(Path.Combine(outDir, "source", "utf8.txt")));
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

        [Fact]
        public void OpenRegularFileForRead_BrowserFailsClosedBeforePathAccess()
        {
            string path = Path.Combine(
                Path.GetTempPath(),
                "bfx_browser_" + Guid.NewGuid().ToString("N") + ".gba");

            Assert.False(ProjectionFileSystemSafety.TryValidateRegularFile(
                path,
                isBrowser: true,
                out string validationError));
            Assert.Contains("Browser", validationError);

            PlatformNotSupportedException openError =
                Assert.Throws<PlatformNotSupportedException>(() =>
                    ProjectionFileSystemSafety.OpenRegularFileForRead(
                        path,
                        isBrowser: true));
            Assert.Contains("Browser", openError.Message);
            Assert.False(File.Exists(path));
        }

        // ---- #1965/#1936: typed missing-path classification on the native no-follow open,
        // load-bearing for PatchMetadataCore's success-empty contract on a genuinely missing
        // final file / missing parent directory ----

        [Fact]
        public void OpenRegularFileForRead_MissingFinalFile_ThrowsFileNotFoundException()
        {
            string dir = Path.Combine(
                Path.GetTempPath(), "bfx_missingfinal_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string missing = Path.Combine(dir, "does-not-exist.txt");

                // A missing final file (existing parent directory) must map to
                // FileNotFoundException on both Windows (ERROR_FILE_NOT_FOUND) and Unix
                // (ENOENT) — this is the exact classification PatchMetadataCore relies on to
                // resolve a genuinely missing PATCH_*.txt to a success-empty result rather
                // than a propagating fault.
                Assert.Throws<FileNotFoundException>(
                    () => ProjectionFileSystemSafety.OpenRegularFileForRead(missing));
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void OpenRegularFileForRead_MissingParentDirectory_ThrowsTypedMissingException()
        {
            string dir = Path.Combine(
                Path.GetTempPath(), "bfx_missingparent_" + Guid.NewGuid().ToString("N"));
            // Intentionally do NOT create `dir` — its parent-of-target directory is itself
            // missing.
            string missing = Path.Combine(dir, "does-not-exist.txt");

            // Windows' CreateFileW natively distinguishes ERROR_PATH_NOT_FOUND (missing parent)
            // from ERROR_FILE_NOT_FOUND (missing final file). POSIX lstat/open only ever
            // report ENOENT for both cases — there is no OS-level distinction to surface
            // without reintroducing a File.Exists/Directory.Exists precheck (forbidden: would
            // reintroduce TOCTOU). Both outcomes are treated identically as success-empty by
            // PatchMetadataCore, so this divergence is contract-neutral for callers.
            if (OperatingSystem.IsWindows())
            {
                Assert.Throws<DirectoryNotFoundException>(
                    () => ProjectionFileSystemSafety.OpenRegularFileForRead(missing));
            }
            else
            {
                Assert.Throws<FileNotFoundException>(
                    () => ProjectionFileSystemSafety.OpenRegularFileForRead(missing));
            }
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
                Assert.Equal(BuildfileExportCore.AdvisoryPatchInventoryFileSystemReason,
                    result.Manifest.Patches.Reason);

                string json = File.ReadAllText(Path.Combine(outDir, "buildfile.json"));
                Assert.DoesNotContain(patchBase, json);
            }
            finally { Cleanup(parent); try { Directory.Delete(patchBase, true); } catch { } }
        }

        [Fact]
        public void MapAdvisoryPatchFailureKindToReason_ExhaustiveAndPathFree()
        {
            Assert.Equal(
                BuildfileExportCore.AdvisoryResourceBudgetExceededReason,
                BuildfileExportCore.MapAdvisoryPatchFailureKindToReason(
                    BoundedPatchReadFailureKind.ResourceLimit));
            Assert.Equal(
                BuildfileExportCore.AdvisoryPatchInventoryFileSystemReason,
                BuildfileExportCore.MapAdvisoryPatchFailureKindToReason(
                    BoundedPatchReadFailureKind.FileSystem));

            string changed = BuildfileExportCore.MapAdvisoryPatchFailureKindToReason(
                BoundedPatchReadFailureKind.ContentChanged);
            string fixturePath = Path.Combine(Path.GetTempPath(), "fixture-path-1965", "PATCH_Test.txt");
            Assert.Equal(BuildfileExportCore.AdvisorySourceChangedReason, changed);
            Assert.DoesNotContain(fixturePath, changed, StringComparison.Ordinal);
            Assert.DoesNotContain("simulated fault detail", changed, StringComparison.OrdinalIgnoreCase);

            Assert.Throws<InvalidOperationException>(() =>
                BuildfileExportCore.MapAdvisoryPatchFailureKindToReason(
                    BoundedPatchReadFailureKind.None));
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
            Assert.Contains("blocked", error);
        }

        [Fact]
        public void DeleteAndVerifyGone_DeleteThrows_FailsClosed_WithReasonAndPath()
        {
            bool result = BuildfileExportCore.DeleteAndVerifyGone(
                "toxic-dir",
                (_, _) => throw new IOException("disk full"),
                _ => FileAttributes.Directory,
                out string error);

            Assert.False(result);
            Assert.Contains("could not delete path", error);
            Assert.Contains("disk full", error);
            Assert.Contains("toxic-dir", error);
        }

        [Fact]
        public void DeleteAndVerifyGone_PostDeleteVerificationThrows_FailsClosed_WithReasonAndPath()
        {
            bool deleted = false;

            bool result = BuildfileExportCore.DeleteAndVerifyGone(
                "verify-dir",
                (_, _) => { deleted = true; },
                _ => deleted
                    ? throw new UnauthorizedAccessException("locked")
                    : FileAttributes.Directory,
                out string error);

            Assert.False(result);
            Assert.Contains("could not verify path absence", error);
            Assert.Contains("locked", error);
            Assert.Contains("verify-dir", error);
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
            Assert.Contains("path still present after delete", error);
            Assert.Contains("replaced", error);
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

        [SkippableFact]
        public void Export_FinalPatchFileSymlinkOutsideRoot_DegradesAdvisoryWithoutLeakingSentinel()
        {
            // #1965/#1936: a final PATCH_*.txt entry replaced with a symlink pointing OUTSIDE the
            // patch root, at a file containing a unique sentinel key=value. The default bounded
            // reader must reject this via ProjectionFileSystemSafety.OpenRegularFileForRead
            // (no-follow, exact-regular-file) instead of transparently following it — so the
            // sentinel must never surface in the advisory inventory, the manifest reason, or the
            // serialized buildfile.json, while the AUTHORITATIVE recipe/payload export still
            // succeeds in full.
            var clean = new byte[RomSize];
            var target = (byte[])clean.Clone();
            target[0x10] = 0x7;

            var (outDir, parent) = FreshOut();
            string externalRoot = Path.Combine(
                Path.GetTempPath(), "bfx_patchlink_external_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(externalRoot);
            const string SentinelKey = "SENTINEL_1936";
            const string SentinelValue = "leak-me-if-you-can";
            string externalTarget = Path.Combine(externalRoot, "outside-secrets.txt");
            File.WriteAllText(externalTarget,
                "TYPE=ADDR\nNAME=Should Not Appear\n" + SentinelKey + "=" + SentinelValue + "\n");

            try
            {
                string patchBase = Path.Combine(parent, "patch2");
                Directory.CreateDirectory(patchBase);
                string linkPath = Path.Combine(patchBase, "PATCH_Link.txt");
                try
                {
                    File.CreateSymbolicLink(linkPath, externalTarget);
                }
                catch (Exception ex)
                {
                    Skip.If(true, "Cannot create a file symlink here: " + ex.Message);
                    return;
                }

                var result = BuildfileExportCore.Export(MakeRom(clean), MakeRom(target),
                    new BuildfileExportOptions
                    {
                        OutputDirectory = outDir,
                        PatchBaseDirectory = patchBase,
                    });

                // Authoritative export (recipe/payload) remains fully successful — only the
                // advisory patch inventory degrades.
                Assert.True(result.Success, result.Error);
                Assert.Equal("unavailable", result.Manifest.Patches.Status);
                Assert.Empty(result.Manifest.Patches.Installed);
                Assert.DoesNotContain(SentinelValue, result.Manifest.Patches.Reason);
                Assert.DoesNotContain(SentinelKey, result.Manifest.Patches.Reason);
                Assert.DoesNotContain(externalRoot, result.Manifest.Patches.Reason);
                Assert.DoesNotContain(patchBase, result.Manifest.Patches.Reason);
                Assert.True(target.SequenceEqual(ReconstructFromProject(outDir, clean)));

                string json = File.ReadAllText(Path.Combine(outDir, "buildfile.json"));
                Assert.DoesNotContain(SentinelValue, json);
                Assert.DoesNotContain(SentinelKey, json);
                Assert.DoesNotContain(externalRoot.Replace('\\', '/'), json);
                Assert.DoesNotContain(patchBase.Replace('\\', '/'), json);
                Assert.DoesNotContain("Should Not Appear", json);
            }
            finally
            {
                Cleanup(parent);
                try { Directory.Delete(externalRoot, true); } catch { }
            }
        }

        // ---------------------------------------------------------------
        // Shared advisory-item budget (16,384) — bounded exporter producer.
        // Real 16,384 constant, no test-only override, per the approved design.
        // ---------------------------------------------------------------

        [Fact]
        public void Export_PatchListingAtCapPlusOne_DegradesInventoryBeforeParsing()
        {
            var clean = new byte[RomSize];
            var target = (byte[])clean.Clone();
            target[0x10] = 0x5;

            var (outDir, parent) = FreshOut();
            try
            {
                string patchBase = Path.Combine(parent, "patch2");
                Directory.CreateDirectory(patchBase);

                // Every one of these MaxAdvisoryItems + 1 paths does NOT exist on disk. The
                // file-discovery cap must reject the injected listing on length alone, before a
                // single file is opened/parsed (Copilot review finding: unbounded eager listing
                // materialization).
                var fakeFiles = new string[BuildfileExportOptions.MaxAdvisoryItems + 1];
                for (int i = 0; i < fakeFiles.Length; i++)
                    fakeFiles[i] = Path.Combine(patchBase, "PATCH_fake_" + i + ".txt");

                var result = BuildfileExportCore.Export(MakeRom(clean), MakeRom(target),
                    new BuildfileExportOptions
                    {
                        OutputDirectory = outDir,
                        PatchBaseDirectory = patchBase,
                        PatchDirectoryListerForTest = _ => fakeFiles,
                    });

                Assert.True(result.Success, result.Error);
                Assert.Equal("unavailable", result.Manifest.Patches.Status);
                Assert.Empty(result.Manifest.Patches.Installed);
                // This is a genuine resource-BUDGET breach (more files discovered than
                // MaxAdvisoryItems), not a filesystem/permission fault, so it MUST use the same
                // shared, stable AdvisoryResourceBudgetExceededReason every other advisory-budget breach
                // uses — never the generic "check directory permissions" reason (Copilot review
                // finding: BuildPatchInventory previously string-discarded the typed distinction
                // via `out _`, always reporting the generic permission reason here).
                Assert.Equal(BuildfileExportCore.AdvisoryResourceBudgetExceededReason, result.Manifest.Patches.Reason);
                Assert.DoesNotContain(patchBase, result.Manifest.Patches.Reason);

                string json = File.ReadAllText(Path.Combine(outDir, "buildfile.json"));
                Assert.DoesNotContain(patchBase.Replace('\\', '/'), json);
            }
            finally { Cleanup(parent); }
        }

        // Note: the companion "real filesystem/access fault during discovery keeps the generic
        // permission reason, never the budget reason" case is ALREADY covered by the pre-existing
        // Export_PatchDirectoryListingFailure_ManifestReasonIsStableAndPathFree test below (an
        // injected IOException from PatchDirectoryListerForTest) — re-verified against the typed
        // failure-kind plumbing added above (`FileSystem` maps to the same generic reason), so
        // no duplicate test was added.

        [Fact]
        public void Export_PatchFileWithParamsOverCap_DegradesInventoryWithoutPartialRecords()
        {
            var clean = new byte[RomSize];
            var target = (byte[])clean.Clone();
            target[0x10] = 0x9;

            var (outDir, parent) = FreshOut();
            try
            {
                string patchBase = Path.Combine(parent, "patch2");
                string dir = Path.Combine(patchBase, "BIG");
                Directory.CreateDirectory(dir);

                var lines = new List<string> { "TYPE=ADDR", "NAME=Big Patch" };
                int paramCount = BuildfileExportOptions.MaxAdvisoryItems + 1;
                for (int i = 0; i < paramCount; i++)
                    lines.Add("KEY" + i + "=" + i);
                string patchFile = Path.Combine(dir, "PATCH_Big.txt");
                File.WriteAllLines(patchFile, lines);

                var result = BuildfileExportCore.Export(MakeRom(clean), MakeRom(target),
                    new BuildfileExportOptions
                    {
                        OutputDirectory = outDir,
                        PatchBaseDirectory = patchBase,
                    });

                Assert.True(result.Success, result.Error);
                Assert.Equal("unavailable", result.Manifest.Patches.Status);
                // No partial/truncated installed record survives a params-budget breach — the
                // WHOLE inventory degrades (Copilot review finding: unbounded nested params).
                Assert.Empty(result.Manifest.Patches.Installed);
                Assert.DoesNotContain(patchBase, result.Manifest.Patches.Reason);

                string json = File.ReadAllText(Path.Combine(outDir, "buildfile.json"));
                Assert.DoesNotContain(patchBase.Replace('\\', '/'), json);
                Assert.DoesNotContain("Big Patch", json);
            }
            finally { Cleanup(parent); }
        }

        // ---------------------------------------------------------------
        // #1965: byte-bound caps — 16 MiB per PATCH definition, 64 MiB aggregate metadata
        // pass, separate 64 MiB aggregate params pass. Production ALWAYS binds the immutable
        // constants (BuildfileExportOptions.MaxPatchParamsAggregateBytes /
        // PatchMetadataCore.MaxMetadataAggregateBytes) — there is no mutable/nullable options
        // field that can override either cap. Deterministic small-fixture coverage of the
        // params-pass aggregate breach therefore calls the ordinary parameterized internal core
        // helper `BuildfileExportCore.BuildPatchInventoryBounded` directly with a small value,
        // bypassing the public Export() entry point entirely (that helper is unreachable from
        // any production code path with anything other than the immutable constant).
        // ---------------------------------------------------------------

        [Fact]
        public void BuildPatchInventoryBounded_ParamsAggregateByteBudgetBreach_DegradesWholeInventoryPathFree()
        {
            var clean = new byte[RomSize];
            var target = (byte[])clean.Clone();
            target[0x10] = 0x7;

            var (outDir, parent) = FreshOut();
            try
            {
                string patchBase = Path.Combine(parent, "patch2");
                string dirA = Path.Combine(patchBase, "AAA");
                string dirB = Path.Combine(patchBase, "BBB");
                Directory.CreateDirectory(dirA);
                Directory.CreateDirectory(dirB);

                // Two small, individually-tiny patch files whose COMBINED raw-params bytes
                // exceed a deterministic tiny aggregate cap, even though neither breaches the
                // (generous) per-file 16 MiB cap or the per-record advisory item count alone.
                File.WriteAllText(Path.Combine(dirA, "PATCH_A.txt"),
                    "TYPE=ADDR\nNAME=First Patch\nKEY1=value1\n");
                File.WriteAllText(Path.Combine(dirB, "PATCH_B.txt"),
                    "TYPE=ADDR\nNAME=Second Patch\nKEY2=value2\n");
                long firstParamsBytes = new FileInfo(Path.Combine(dirA, "PATCH_A.txt")).Length;
                long secondParamsBytes = new FileInfo(Path.Combine(dirB, "PATCH_B.txt")).Length;

                var options = new BuildfileExportOptions
                {
                    OutputDirectory = outDir,
                    PatchBaseDirectory = patchBase,
                };

                // One byte short of fitting both files' raw-params bytes — deterministic, no
                // real 64 MiB fixture needed. Only this direct test call ever passes anything
                // other than BuildfileExportOptions.MaxPatchParamsAggregateBytes.
                BuildfilePatchInventory inv = BuildfileExportCore.BuildPatchInventoryBounded(
                    MakeRom(clean), MakeRom(target), options, existingAdvisoryItemCount: 0,
                    maxParamsAggregateBytes: firstParamsBytes + secondParamsBytes - 1,
                    out var failureKind);

                Assert.Equal("unavailable", inv.Status);
                Assert.Equal(BoundedPatchReadFailureKind.ResourceLimit, failureKind);
                // No partial/truncated installed record survives an aggregate-budget breach —
                // the WHOLE inventory degrades, not just the second (over-budget) record.
                Assert.Empty(inv.Installed);
                Assert.Equal(BuildfileExportCore.AdvisoryResourceBudgetExceededReason, inv.Reason);
                Assert.DoesNotContain(patchBase, inv.Reason);
            }
            finally { Cleanup(parent); }
        }

        [Fact]
        public void Export_FGrepInstallMarker_ProducesUnknownRecord_WithoutReadingSignatureFile()
        {
            // #1936 bounded-exporter $FGREP escape, END-TO-END. What this test proves and what it
            // does NOT:
            //  - PROVES bounded CLASSIFICATION: the exporter emits an "available" advisory
            //    inventory with the file-backed $FGREP patch as "unknown" (never "installed"),
            //    while the SAME fixture resolves to "installed" through the legacy unbounded
            //    CheckPatchInstalled path (it reads sig.bin and matches the planted ROM bytes) —
            //    so the "unknown" is the deliberate bounded refusal, not a vacuous non-match.
            //  - PROVES authoritative independence: the raw recipe still reconstructs the target
            //    EXACTLY; the advisory record never touches authoritative bytes.
            //  - The "signature body absent from JSON" assertion below is a useful non-leak
            //    sanity check, but by itself it does NOT prove the file was never read (even the
            //    old resolver used the bytes only to classify and never serialized them). The
            //    positive "resolver is never invoked" no-read guarantee is proven directly by the
            //    injected-resolver unit test
            //    TryParsePatchFileStrictBounded_FGrepMarker_ResolverNeverInvoked.
            byte[] signature = Encoding.ASCII.GetBytes("FGSIG1936XY"); // unique external body
            string sigHex = string.Join(" ", signature.Select(b => "0x" + b.ToString("X2")));

            var clean = new byte[RomSize];
            var target = (byte[])clean.Clone();
            target[0x10] = 0x9;
            signature.CopyTo(target, 0x200); // 4-aligned, so legacy FGREP resolves+matches

            var (outDir, parent) = FreshOut();
            try
            {
                string patchBase = Path.Combine(parent, "patch2");
                string patchDir = Path.Combine(patchBase, "FGREPTEST");
                Directory.CreateDirectory(patchDir);
                File.WriteAllBytes(Path.Combine(patchDir, "sig.bin"), signature);
                string patchFile = Path.Combine(patchDir, "PATCH_Fg.txt");
                File.WriteAllLines(patchFile, new[]
                {
                    "TYPE=ADDR",
                    "NAME=FGrep Patch",
                    "PATCHED_IF:$FGREP4 sig.bin=" + sigHex,
                });

                // Non-vacuous guard: the legacy unbounded path resolves this SAME fixture to
                // Installed (it reads sig.bin and matches the planted ROM bytes).
                var legacy = PatchMetadataCore.ParsePatchFile(patchFile, "FGREPTEST", MakeRom(target), "en");
                Assert.Equal(PatchMetadataCore.PatchStatus.Installed, legacy.Status);

                var result = BuildfileExportCore.Export(MakeRom(clean), MakeRom(target),
                    new BuildfileExportOptions
                    {
                        OutputDirectory = outDir,
                        PatchBaseDirectory = patchBase,
                    });

                Assert.True(result.Success, result.Error);
                Assert.Equal("available", result.Manifest.Patches.Status);
                var rec = Assert.Single(result.Manifest.Patches.Installed);
                Assert.Equal("unknown", rec.Status);

                // Non-leak sanity check (see method-level note: NOT itself a no-read proof): the
                // external signature body does not appear in the serialized manifest (payloads
                // are separate data/*.bin files; the manifest holds only paths + hashes).
                string json = File.ReadAllText(Path.Combine(outDir, "buildfile.json"));
                Assert.DoesNotContain("FGSIG1936XY", json);

                // Authoritative reconstruction is exact — the advisory record never touches it.
                Assert.True(target.SequenceEqual(ReconstructFromProject(outDir, clean)));
            }
            finally { Cleanup(parent); }
        }

        [Fact]
        public void Export_MetadataFileLengthOverPerFileCap_DegradesWithGenericResourceReason()
        {
            // #1936 diagnostic-accuracy: a per-file byte Length breach (metadata pass) must map
            // to the SAME generic, path-free resource reason as every other advisory resource
            // budget breach — never a raw path and never the stale "item budget" wording. A
            // sparse SetLength keeps the fixture from materializing 16+ MiB of real bytes.
            var clean = new byte[RomSize];
            var target = (byte[])clean.Clone();
            target[0x10] = 0xB;

            var (outDir, parent) = FreshOut();
            try
            {
                string patchBase = Path.Combine(parent, "patch2");
                Directory.CreateDirectory(patchBase);
                string patchFile = Path.Combine(patchBase, "PATCH_Big.txt");
                using (var fs = new FileStream(patchFile, FileMode.CreateNew, FileAccess.Write))
                    fs.SetLength(PatchMetadataCore.MaxPatchDefinitionBytes + 1); // sparse, no real bytes

                var result = BuildfileExportCore.Export(MakeRom(clean), MakeRom(target),
                    new BuildfileExportOptions
                    {
                        OutputDirectory = outDir,
                        PatchBaseDirectory = patchBase,
                    });

                Assert.True(result.Success, result.Error);
                Assert.Equal("unavailable", result.Manifest.Patches.Status);
                Assert.Empty(result.Manifest.Patches.Installed);
                Assert.Equal(BuildfileExportCore.AdvisoryResourceBudgetExceededReason,
                    result.Manifest.Patches.Reason);
                Assert.DoesNotContain("item budget", result.Manifest.Patches.Reason);
                Assert.DoesNotContain(patchBase, result.Manifest.Patches.Reason);
                Assert.DoesNotContain(patchFile, result.Manifest.Patches.Reason);
            }
            finally { Cleanup(parent); }
        }

        // genuine bytes read from a REAL small backing file, then throws IOException on any
        // subsequent read — simulating a mid-read I/O fault partway through a file (#1965 L2
        // correction: proves the exporter's aggregate byte accounting stays monotonic/path-free
        // even when the underlying read genuinely fails after some bytes were consumed).
        sealed class FaultAfterNBytesFileStream : FileStream
        {
            readonly long _faultAfter;
            long _totalRead;
            public FaultAfterNBytesFileStream(string path, long faultAfter)
                : base(path, FileMode.Open, FileAccess.Read, FileShare.Read)
            {
                _faultAfter = faultAfter;
            }
            public override int Read(byte[] buffer, int offset, int count)
            {
                if (_totalRead >= _faultAfter)
                    throw new IOException("Simulated I/O fault after N bytes (test double).");
                int allowed = (int)Math.Min(count, _faultAfter - _totalRead);
                int read = base.Read(buffer, offset, allowed);
                _totalRead += read;
                return read;
            }
        }

        [Fact]
        public void TryAppendRawParamsBounded_FaultAfterNBytes_PreservesBytesReadAndDegradesRecordPathFree()
        {
            var (outDir, parent) = FreshOut();
            try
            {
                string patchBase = Path.Combine(parent, "patch2");
                Directory.CreateDirectory(patchBase);
                string patchPath = Path.Combine(patchBase, "PATCH_Fault.txt");
                const int n = 12;
                File.WriteAllText(patchPath, "TYPE=ADDR\nNAME=Fault Patch\nKEY1=value_longer_than_n\n");
                long realLength = new FileInfo(patchPath).Length;
                Assert.True(realLength > n); // the fault must trigger before EOF is reached

                var rec = new BuildfilePatchRecord
                {
                    Name = "Fault Patch",
                    Status = "installed",
                    Confidence = "high",
                    Reason = "test-seed",
                };

                bool ok = BuildfileExportCore.TryAppendRawParamsBounded(
                    rec,
                    patchPath,
                    maxEntries: 100,
                    maxBytes: PatchMetadataCore.MaxPatchDefinitionBytes,
                    openFileStreamForTest: p => new FaultAfterNBytesFileStream(p, n),
                    consumedCount: out int consumedCount,
                    bytesRead: out long bytesRead,
                    failureKind: out var failureKind);

                // An expected FS fault resolves to a successful (true) but degraded record —
                // never a silent empty params list, never a thrown exception across this seam.
                Assert.True(ok);
                Assert.Equal(BoundedPatchReadFailureKind.None, failureKind);
                Assert.Equal(0, consumedCount);
                Assert.Equal(n, bytesRead); // genuinely-read bytes must survive the fault path,
                                             // NOT be reset to 0 in the catch block, so the
                                             // caller's aggregate params budget still accounts
                                             // for them.
                Assert.Contains("raw parameters unavailable", rec.Reason, StringComparison.Ordinal);
                Assert.DoesNotContain(patchPath, rec.Reason, StringComparison.Ordinal);
                Assert.DoesNotContain(patchBase, rec.Reason, StringComparison.Ordinal);
            }
            finally { Cleanup(parent); }
        }

        [Theory]
        [InlineData(typeof(UnauthorizedAccessException))]
        [InlineData(typeof(IOException))]
        [InlineData(typeof(System.Security.SecurityException))]
        [InlineData(typeof(PlatformNotSupportedException))]
        public void TryAppendRawParamsBounded_ExpectedFileSystemFault_DegradesRecordPathFreeWithEmptyParams(Type exceptionType)
        {
            // #1965 L3 correction: deterministic opener-injection proving ALL THREE expected
            // filesystem/access fault classes (not just IOException-via-a-mid-read-fault) reach
            // the exporter's catch and produce the SAME stable, path-free degradation — empty
            // params, no thrown exception across this seam, no absolute path in the reason.
            // PlatformNotSupportedException (#1965/#1936 correction) covers the new default
            // opener's Browser classification (ProjectionFileSystemSafety.OpenRegularFileForRead
            // throws PlatformNotSupportedException on Browser instead of an unsafe fallback) —
            // this injected double proves the exporter degrades path-free instead of crashing,
            // without needing an actual Browser runtime.
            var (outDir, parent) = FreshOut();
            try
            {
                string patchBase = Path.Combine(parent, "patch2");
                Directory.CreateDirectory(patchBase);
                string patchPath = Path.Combine(patchBase, "PATCH_Fault.txt");
                File.WriteAllText(patchPath, "TYPE=ADDR\nNAME=Fault Patch\nKEY1=value1\n");

                var rec = new BuildfilePatchRecord
                {
                    Name = "Fault Patch",
                    Status = "installed",
                    Confidence = "high",
                    Reason = "test-seed",
                };

                Func<string, FileStream> throwingOpener = p =>
                    throw (Exception)Activator.CreateInstance(exceptionType, "simulated fault (test double)");

                bool ok = BuildfileExportCore.TryAppendRawParamsBounded(
                    rec,
                    patchPath,
                    maxEntries: 100,
                    maxBytes: PatchMetadataCore.MaxPatchDefinitionBytes,
                    openFileStreamForTest: throwingOpener,
                    consumedCount: out int consumedCount,
                    bytesRead: out long bytesRead,
                    failureKind: out var failureKind);

                Assert.True(ok);
                Assert.Equal(BoundedPatchReadFailureKind.None, failureKind);
                Assert.Equal(0, consumedCount);
                // No bytes were genuinely consumed before this immediate-open fault — the
                // accounting must stay exactly 0, not just "not reset to something wrong".
                Assert.Equal(0, bytesRead);
                Assert.Empty(rec.Params);
                Assert.Contains("raw parameters unavailable", rec.Reason, StringComparison.Ordinal);
                Assert.DoesNotContain(patchPath, rec.Reason, StringComparison.Ordinal);
                Assert.DoesNotContain(patchBase, rec.Reason, StringComparison.Ordinal);
            }
            finally { Cleanup(parent); }
        }

        // Fake FileStream double (duplicated from the equivalent private nested type in
        // PatchMetadataCoreTests — these test fakes are file-local by established convention, no
        // shared test-helper file exists in this repo) that under/over-reports its Length
        // relative to what the handle actually delivers — simulates the file growing OR
        // shrinking between the Length check and the read (or a Length that simply lied).
        sealed class GrowthAfterLengthFileStream : FileStream
        {
            readonly long _reportedLength;
            public GrowthAfterLengthFileStream(string path, long reportedLength)
                : base(path, FileMode.Open, FileAccess.Read, FileShare.Read)
            {
                _reportedLength = reportedLength;
            }
            public override long Length => _reportedLength;
        }

        [Fact]
        public void TryAppendRawParamsBounded_WithinCapGrowthAfterLengthCheck_FailsWithNoConsumedParams()
        {
            // #1965 length-drift correction: proves the exporter-facing false path for a file
            // that grows past the Length captured at open time while staying comfortably within
            // maxBytes — the shared reader must reject this BEFORE any raw parameter is ever
            // appended to `rec.Params`, and `consumedCount` must stay 0 (never partially
            // populated from the rejected surplus bytes). This intentionally does NOT add a new
            // production inventory seam — it exercises the existing `openFileStreamForTest`
            // fault-injection seam already used by the fault-after-N-bytes coverage above.
            var (outDir, parent) = FreshOut();
            try
            {
                string patchBase = Path.Combine(parent, "patch2");
                Directory.CreateDirectory(patchBase);
                string patchPath = Path.Combine(patchBase, "PATCH_WithinCapGrowth.txt");
                const int reportedLength = 8;
                const long maxBytes = 1024; // comfortably above both the reported length AND the real data below
                File.WriteAllText(patchPath, "TYPE=ADDR\nNAME=Growth Patch\nKEY1=value1\n"); // > reportedLength, well within maxBytes
                long realLength = new FileInfo(patchPath).Length;
                Assert.True(realLength > reportedLength);

                var rec = new BuildfilePatchRecord
                {
                    Name = "Growth Patch",
                    Status = "installed",
                    Confidence = "high",
                    Reason = "test-seed",
                };

                bool ok = BuildfileExportCore.TryAppendRawParamsBounded(
                    rec,
                    patchPath,
                    maxEntries: 100,
                    maxBytes: maxBytes,
                    openFileStreamForTest: p => new GrowthAfterLengthFileStream(p, reportedLength),
                    consumedCount: out int consumedCount,
                    bytesRead: out long bytesRead,
                    failureKind: out var failureKind);

                Assert.False(ok);
                Assert.Equal(BoundedPatchReadFailureKind.ContentChanged, failureKind);
                Assert.Equal(0, consumedCount);
                Assert.Empty(rec.Params); // no partial params ever appended from rejected bytes
                Assert.True(bytesRead > reportedLength,
                    $"Expected bytesRead ({bytesRead}) to exceed the reported length ({reportedLength}).");
            }
            finally { Cleanup(parent); }
        }

        [Fact]
        public void BuildPatchInventoryBounded_ParamsContentChanged_DegradesWholeInventoryPathFree()
        {
            var clean = new byte[RomSize];
            var target = (byte[])clean.Clone();
            target[0x10] = 0x17;

            var (outDir, parent) = FreshOut();
            try
            {
                string patchBase = Path.Combine(parent, "patch2");
                Directory.CreateDirectory(patchBase);
                string patchPath = Path.Combine(patchBase, "PATCH_WithinCapGrowth.txt");
                const int reportedLength = 8;
                File.WriteAllText(patchPath, "TYPE=ADDR\nNAME=Growth Patch\nKEY1=value1\n");
                Assert.True(new FileInfo(patchPath).Length > reportedLength);

                var options = new BuildfileExportOptions
                {
                    OutputDirectory = outDir,
                    PatchBaseDirectory = patchBase,
                };

                BuildfilePatchInventory inv = BuildfileExportCore.BuildPatchInventoryBounded(
                    MakeRom(clean),
                    MakeRom(target),
                    options,
                    existingAdvisoryItemCount: 0,
                    maxParamsAggregateBytes: BuildfileExportOptions.MaxPatchParamsAggregateBytes,
                    openPatchParamsFileStreamForTest: p => new GrowthAfterLengthFileStream(p, reportedLength),
                    out var failureKind);

                Assert.Equal("unavailable", inv.Status);
                Assert.Equal(BoundedPatchReadFailureKind.ContentChanged, failureKind);
                Assert.Empty(inv.Installed);
                Assert.Equal(BuildfileExportCore.AdvisorySourceChangedReason, inv.Reason);
                Assert.DoesNotContain(patchBase, inv.Reason, StringComparison.Ordinal);
                Assert.DoesNotContain(patchPath, inv.Reason, StringComparison.Ordinal);
            }
            finally { Cleanup(parent); }
        }

        [Fact]
        public void BuildPatchInventoryBounded_MaxParamsAggregateBytesAboveImmutableCeiling_Throws()
        {
            // #1965 L3 correction ("opposite hypothesis" check): a caller invoking the extracted
            // core helper directly with an aggregate cap wider than the immutable
            // BuildfileExportOptions.MaxPatchParamsAggregateBytes ceiling must be rejected
            // outright, never silently honored as a widened budget.
            var clean = new byte[RomSize];
            var target = (byte[])clean.Clone();

            var options = new BuildfileExportOptions
            {
                OutputDirectory = Path.GetTempPath(),
                PatchBaseDirectory = null,
            };

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                BuildfileExportCore.BuildPatchInventoryBounded(
                    MakeRom(clean), MakeRom(target), options, existingAdvisoryItemCount: 0,
                    maxParamsAggregateBytes: BuildfileExportOptions.MaxPatchParamsAggregateBytes + 1,
                    out _));
        }

        [Fact]
        public void Export_LateProjectionWarning_DegradesPatchesWhenCombinedTotalExceedsCap()
        {
            var clean = new byte[RomSize]; // all-zero clean => exactly one "non-canonical" warning
            var target = (byte[])clean.Clone();
            target[0x10] = 0x3;

            var (outDir, parent) = FreshOut();
            try
            {
                string patchBase = Path.Combine(parent, "patch2");
                string dir = Path.Combine(patchBase, "NEARCAP");
                Directory.CreateDirectory(dir);

                // One record whose own item (1) + params exactly fills the budget once the
                // pre-existing "non-canonical clean" warning (1) is accounted for at Plan()
                // time — i.e. it is exactly AT the cap and available.
                var lines = new List<string> { "TYPE=ADDR", "NAME=Near Cap Patch" };
                int paramCount = BuildfileExportOptions.MaxAdvisoryItems - 2;
                for (int i = 0; i < paramCount; i++)
                    lines.Add("KEY" + i + "=" + i);
                File.WriteAllLines(Path.Combine(dir, "PATCH_NearCap.txt"), lines);

                var result = BuildfileExportCore.Export(MakeRom(clean), MakeRom(target),
                    new BuildfileExportOptions
                    {
                        OutputDirectory = outDir,
                        PatchBaseDirectory = patchBase,
                        // Forces a SECOND warning to be appended AFTER BuildPatchInventory's
                        // budget was already reserved during Plan() (Copilot review finding:
                        // late warning mutation not accounted for by the original reservation).
                        ProjectionRunner = scratch => BuildfileProjectionOutcome.Refuse("forcing a late warning"),
                    });

                Assert.True(result.Success, result.Error);
                Assert.Contains(result.Manifest.Warnings, w => w.Contains("Source projection refused"));
                // The combined total (2 warnings + 1 record + (cap-2) params == cap+1) now
                // exceeds the shared budget, so the patch inventory (never the warnings) is
                // degraded — the authoritative export itself remains fully usable.
                Assert.Equal("unavailable", result.Manifest.Patches.Status);
                Assert.Empty(result.Manifest.Patches.Installed);
                Assert.DoesNotContain(patchBase, result.Manifest.Patches.Reason);

                Assert.True(File.Exists(Path.Combine(outDir, "buildfile.json")));
                Assert.True(target.SequenceEqual(ReconstructFromProject(outDir, clean)));
            }
            finally { Cleanup(parent); }
        }

        // ---------------------------------------------------------------
        // Tri-state fail-closed path-attribute inspection (Copilot review finding:
        // File.Exists silently reports "gone" for a directory/reparse replacement or an
        // inspection fault).
        // ---------------------------------------------------------------

        [Fact]
        public void ProbePathAttributes_RegularFileDirectoryOrReparse_IsPresent()
        {
            Assert.Equal(BuildfileExportCore.PathAttributeProbeResult.Present,
                BuildfileExportCore.ProbePathAttributes("f", _ => FileAttributes.Normal, out _, out string e1));
            Assert.Equal("", e1);

            Assert.Equal(BuildfileExportCore.PathAttributeProbeResult.Present,
                BuildfileExportCore.ProbePathAttributes("d", _ => FileAttributes.Directory, out _, out string e2));
            Assert.Equal("", e2);

            Assert.Equal(BuildfileExportCore.PathAttributeProbeResult.Present,
                BuildfileExportCore.ProbePathAttributes("r", _ => FileAttributes.ReparsePoint, out _, out string e3));
            Assert.Equal("", e3);
        }

        [Fact]
        public void ProbePathAttributes_NotFound_IsAbsent()
        {
            Assert.Equal(BuildfileExportCore.PathAttributeProbeResult.Absent,
                BuildfileExportCore.ProbePathAttributes(
                    "missing-file", _ => throw new FileNotFoundException("gone"), out _, out string e1));
            Assert.Equal("", e1);

            Assert.Equal(BuildfileExportCore.PathAttributeProbeResult.Absent,
                BuildfileExportCore.ProbePathAttributes(
                    "missing-dir", _ => throw new DirectoryNotFoundException("gone"), out _, out string e2));
            Assert.Equal("", e2);
        }

        [Fact]
        public void ProbePathAttributes_ExpectedInspectionFault_IsUnknown_FailClosed()
        {
            var result = BuildfileExportCore.ProbePathAttributes(
                "blocked-path", _ => throw new UnauthorizedAccessException("denied"),
                out _, out string fault);

            Assert.Equal(BuildfileExportCore.PathAttributeProbeResult.Unknown, result);
            Assert.Contains("denied", fault);
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

        // ---------------------------------------------------------------
        // #1936/#1935: shared 16 MiB producer/consumer manifest byte cap. BuildfileExportCore
        // must never publish a buildfile.json its own independent consumer (BuildfileBuildCore)
        // would refuse to open (root cause: the exporter previously had no byte cap of its own).
        // ---------------------------------------------------------------

        [Fact]
        public void ManifestByteCap_IsSharedAcrossFormatBuildAndExportOptions()
        {
            Assert.Equal(16 * 1024 * 1024, BuildfileFormat.MaxManifestBytes);
            // The consumer's own constant must remain exactly 16 MiB and must never drift from
            // the shared source of truth.
            Assert.Equal(BuildfileFormat.MaxManifestBytes, BuildfileBuildOptions.MaxManifestBytes);
            Assert.Equal(BuildfileFormat.MaxManifestBytes, BuildfileExportOptions.MaxManifestBytes);
        }

        [Fact]
        public void TryPrepareManifestForByteCap_ExactCap_Succeeds()
        {
            var manifest = new BuildfileManifest();
            long exact = Encoding.UTF8.GetByteCount(BuildfileExportCore.SerializeManifest(manifest));

            Assert.True(BuildfileExportCore.TryPrepareManifestForByteCap(manifest, exact));
        }

        [Fact]
        public void TryPrepareManifestForByteCap_OneByteOverWithNoDegradableInventory_Fails()
        {
            var manifest = new BuildfileManifest();
            long exact = Encoding.UTF8.GetByteCount(BuildfileExportCore.SerializeManifest(manifest));

            // One byte below the manifest's actual size ("cap+1" relative to the content) —
            // nothing installed to degrade, so the manifest is left exactly as-is and this must
            // fail outright, never truncate/partially serialize.
            Assert.False(BuildfileExportCore.TryPrepareManifestForByteCap(manifest, exact - 1));
            Assert.Empty(manifest.Patches.Installed);
            Assert.Equal("unavailable", manifest.Patches.Status); // default, untouched
        }

        [Fact]
        public void TryPrepareManifestForByteCap_MultibyteContent_MeasuresUtf8BytesNotChars()
        {
            var manifest = new BuildfileManifest();
            // Each '\u65E5' is one UTF-16 char but 3 UTF-8 bytes — repeated enough times that the
            // byte/char gap is large and unambiguous.
            manifest.Warnings.Add(new string('\u65E5', 200));
            string json = BuildfileExportCore.SerializeManifest(manifest);
            int charLength = json.Length;
            long byteLength = Encoding.UTF8.GetByteCount(json);
            Assert.True(byteLength > charLength); // sanity: the fixture really is multibyte

            // A char-counting (WRONG) implementation would consider this within budget; a
            // byte-counting (CORRECT) implementation must reject it (nothing installed to
            // degrade, so this proves rejection, not silent truncation).
            Assert.False(BuildfileExportCore.TryPrepareManifestForByteCap(manifest, charLength));
            // The true byte count must be accepted.
            Assert.True(BuildfileExportCore.TryPrepareManifestForByteCap(manifest, byteLength));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void ResolveManifestByteCap_ZeroOrNegativeOverride_Throws(long invalid)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                BuildfileExportCore.ResolveManifestByteCap(invalid));
        }

        [Fact]
        public void ResolveManifestByteCap_WideningOverride_Throws()
        {
            // #1936/#1935 non-widening guarantee: a test-only override can only ever NARROW the
            // cap below the immutable production ceiling, mirroring
            // BuildPatchInventoryBounded_MaxParamsAggregateBytesAboveImmutableCeiling_Throws.
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                BuildfileExportCore.ResolveManifestByteCap(BuildfileExportOptions.MaxManifestBytes + 1L));
        }

        [Fact]
        public void ResolveManifestByteCap_NoOverride_ResolvesToProductionConstant()
        {
            Assert.Equal(BuildfileExportOptions.MaxManifestBytes, BuildfileExportCore.ResolveManifestByteCap(null));
        }

        [Fact]
        public void Export_SmallManifestByteCap_DegradesAvailablePatchInventoryAllOrNothing_ThenBuildRoundTripsSuccessfully()
        {
            var clean = new byte[RomSize];
            var target = (byte[])clean.Clone();
            target[0x10] = 0x21;

            string patchParent = Path.Combine(
                Path.GetTempPath(), "bfx_capC_patch_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(patchParent);
            string patchBase = Path.Combine(patchParent, "patch2");
            string dir = Path.Combine(patchBase, "CAP");
            Directory.CreateDirectory(dir);
            // A modestly sized (not real-16-MiB-cap-sized) single param value — big enough that
            // the WITH-patches manifest is measurably larger than the degraded (no-patches)
            // manifest, but small enough to keep this test cheap/fast (the real-cap magnitude is
            // covered separately below).
            string value = new string('B', 4000);
            File.WriteAllText(
                Path.Combine(dir, "PATCH_Cap.txt"),
                "TYPE=ADDR\nNAME=Cap Patch\nKEY1=" + value + "\n");

            var (outDirCalib, parentCalib) = FreshOut();
            var (outDir, parent) = FreshOut();
            try
            {
                var calibResult = BuildfileExportCore.Export(MakeRom(clean), MakeRom(target),
                    new BuildfileExportOptions { OutputDirectory = outDirCalib, PatchBaseDirectory = patchBase });
                Assert.True(calibResult.Success, calibResult.Error);
                Assert.Equal("available", calibResult.Manifest.Patches.Status);
                Assert.Single(calibResult.Manifest.Patches.Installed);

                BuildfileManifest calibManifest = calibResult.Manifest;
                long withPatchesBytes = Encoding.UTF8.GetByteCount(BuildfileExportCore.SerializeManifest(calibManifest));

                // Compute the EXACT degraded size analytically — the same mutation
                // TryPrepareManifestForByteCap itself performs — instead of guessing a
                // threshold or running a second uncontrolled export.
                calibManifest.Patches.Status = "unavailable";
                calibManifest.Patches.Reason = BuildfileExportCore.ManifestByteBudgetExceededReason;
                calibManifest.Patches.Installed.Clear();
                long degradedBytes = Encoding.UTF8.GetByteCount(BuildfileExportCore.SerializeManifest(calibManifest));

                Assert.True(degradedBytes < withPatchesBytes,
                    "Test fixture assumption: degrading the patch inventory must shrink the manifest.");

                // A cap strictly between the degraded size and the with-patches size: too small
                // for the full inventory, comfortably large enough after degrade.
                long cap = degradedBytes + ((withPatchesBytes - degradedBytes) / 2);
                Assert.InRange(cap, degradedBytes, withPatchesBytes - 1);

                var result = BuildfileExportCore.Export(MakeRom(clean), MakeRom(target),
                    new BuildfileExportOptions
                    {
                        OutputDirectory = outDir,
                        PatchBaseDirectory = patchBase,
                        MaxManifestBytesForTest = cap,
                    });

                Assert.True(result.Success, result.Error);
                Assert.Equal("unavailable", result.Manifest.Patches.Status);
                Assert.Equal(BuildfileExportCore.ManifestByteBudgetExceededReason, result.Manifest.Patches.Reason);
                Assert.Empty(result.Manifest.Patches.Installed);
                Assert.DoesNotContain(patchBase, result.Manifest.Patches.Reason);

                long publishedBytes = new FileInfo(Path.Combine(outDir, "buildfile.json")).Length;
                Assert.True(publishedBytes <= cap);
                // NOTE: GenerateReadme does not enumerate patch names/status/reason text (it is
                // generic boilerplate), so asserting on README content here would not actually
                // exercise or prove anything about degradation ordering/coherence — the real
                // ordering guarantee (byte-cap check runs before the README write) is reviewed
                // directly in Export(...)/TryPrepareManifestForByteCap, not asserted via README
                // text. The one guarantee this test DOES prove textually is that the raw patch
                // param VALUE never reaches the published buildfile.json:
                Assert.DoesNotContain(value, File.ReadAllText(Path.Combine(outDir, "buildfile.json")));

                // Authoritative fields (ranges/payload hashes/identity) are never touched by the
                // degrade — the fully independent consumer must still reconstruct the target.
                BuildfileBuildResult buildResult =
                    BuildfileBuildCore.Build(MakeRom(clean), outDir, new BuildfileBuildOptions());
                Assert.True(buildResult.Success, buildResult.Error);
                Assert.True(buildResult.TargetIdentityMatches);
                Assert.True(target.SequenceEqual(buildResult.TargetBytes));
            }
            finally
            {
                Cleanup(parentCalib);
                Cleanup(parent);
                try { Directory.Delete(patchParent, true); } catch { }
            }
        }

        [SkippableFact]
        public void Export_LateRewriteAfterMaterializationError_PushesManifestOverByteCap_FailsWithoutPublishOrResidue()
        {
            var clean = new byte[RomSize];
            var target = (byte[])clean.Clone();
            target[0x2000] = 0x51;

            string external = Path.Combine(
                Path.GetTempPath(), "bfx_capD_external_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(external);
            File.WriteAllText(Path.Combine(external, "outside.txt"), "external-content\n");
            try
            {
                // Phase 1 (calibration): the SAME real scenario with NO cap override — the
                // production 16 MiB cap is comfortably large enough that this run is
                // unaffected, but it tells us EXACTLY how many bytes the real,
                // materialization-error-rewritten manifest needs (no guessed threshold).
                Exception linkErrorA = null;
                var (outDirA, parentA) = FreshOut();
                long finalManifestBytes;
                try
                {
                    var optionsA = new BuildfileExportOptions
                    {
                        OutputDirectory = outDirA,
                        ProjectionRunner = scratch =>
                        {
                            File.WriteAllText(Path.Combine(scratch, "projection.txt"), "complete\n");
                            return BuildfileProjectionOutcome.Ok();
                        },
                        AfterProjectionMoveForTest = source =>
                        {
                            Directory.Delete(source, true);
                            try { Directory.CreateSymbolicLink(source, external); }
                            catch (Exception ex) { linkErrorA = ex; }
                        },
                    };
                    var resultA = BuildfileExportCore.Export(MakeRom(clean), MakeRom(target), optionsA);
                    if (linkErrorA != null)
                    {
                        Skip.If(true, "Cannot create a fresh-source symlink here: " + linkErrorA.Message);
                        return;
                    }
                    Assert.True(resultA.Success, resultA.Error);
                    Assert.Equal("error", resultA.Manifest.Projection.Status);
                    Assert.Empty(resultA.Manifest.Patches.Installed); // nothing degradable, by design
                    finalManifestBytes = new FileInfo(Path.Combine(outDirA, "buildfile.json")).Length;
                }
                finally { Cleanup(parentA); }

                // Phase 2 (real route under test): the identical scenario, but with a test-only
                // cap ONE BYTE below the known real post-rewrite size. The EARLIER (pre-error,
                // "success") manifest lacks the later-appended error warning and is therefore
                // smaller, so the FIRST byte-cap check (before README) passes; only the SECOND
                // check — immediately before the real RewriteProjectionMetadata call — can fail,
                // since there is still no advisory patch inventory installed to degrade.
                Exception linkError = null;
                var (outDir, parent) = FreshOut();
                try
                {
                    var options = new BuildfileExportOptions
                    {
                        OutputDirectory = outDir,
                        MaxManifestBytesForTest = finalManifestBytes - 1,
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
                    };
                    var result = BuildfileExportCore.Export(MakeRom(clean), MakeRom(target), options);
                    if (linkError != null)
                    {
                        Skip.If(true, "Cannot create a fresh-source symlink here: " + linkError.Message);
                        return;
                    }

                    Assert.False(result.Success);
                    Assert.Contains("byte", result.Error, StringComparison.OrdinalIgnoreCase);
                    Assert.False(Directory.Exists(outDir)); // no destination published
                    // No stage/scratch sibling residue left behind either — the existing outer
                    // catch's cleanup ran, no NEW cleanup logic was added for this failure.
                    Assert.Empty(Directory.GetDirectories(parent));
                }
                finally { Cleanup(parent); }
            }
            finally { try { Directory.Delete(external, true); } catch { } }
        }

        [Fact]
        public void Export_RealCapExceeded_DegradesPatchInventory_PublishesUnderCap_AndBuildSucceeds()
        {
            // REQUIRED real-cap regression (#1936/#1935): NO manifest cap override anywhere in
            // this test. A single accepted, at-per-file-cap (16 MiB) patch param payload alone
            // makes the serialized buildfile.json exceed the REAL 16 MiB producer/consumer cap
            // on the pre-fix baseline (which has no byte-cap check at all, so it would publish
            // an oversized manifest BuildfileBuildCore.Build then refuses to open). Kept to a
            // single ~16 MiB string allocation/file (bounded, deterministic cleanup).
            //
            // Blue-team review: this method deliberately references ONLY members that already
            // exist on the pre-fix baseline (BuildfileBuildOptions.MaxManifestBytes,
            // BuildfilePatchInventory.Status/Reason/Installed) so it COMPILES unchanged at
            // baseline, and asserts the actual published byte count against the consumer's own
            // cap IMMEDIATELY after Export.Success — BEFORE any degradation-specific
            // assertion — so it fails FIRST at that exact byte-count defect
            // (16,779,064 > 16,777,216) on baseline rather than at a missing-member compile
            // error or an unrelated/later assertion. The new-constant-specific assertions
            // (BuildfileExportOptions.MaxManifestBytes, ManifestByteBudgetExceededReason) are
            // deliberately NOT used here; a stable semantic substring stands in for the exact
            // reason constant instead.
            var clean = new byte[RomSize];
            var target = (byte[])clean.Clone();
            target[0x10] = 0x37;

            string patchParent = Path.Combine(
                Path.GetTempPath(), "bfx_capE_patch_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(patchParent);
            string patchBase = Path.Combine(patchParent, "patch2");
            string dir = Path.Combine(patchBase, "CAP");
            Directory.CreateDirectory(dir);

            // Sized so the RAW patch file itself sits EXACTLY at the immutable per-file 16 MiB
            // cap (PatchMetadataCore.MaxPatchDefinitionBytes) — comfortably under the separate
            // 64 MiB metadata/params aggregate caps and the 16,384-item advisory cap (one
            // record + one param + at most one warning) — so ONLY the new manifest byte cap is
            // exercised here, nothing else rejects/degrades this fixture first.
            const string header = "TYPE=ADDR\nNAME=CapPatch\nKEY1=";
            int headerBytes = Encoding.UTF8.GetByteCount(header);
            long totalFileBytes = PatchMetadataCore.MaxPatchDefinitionBytes;
            long valueLength = totalFileBytes - headerBytes - 1; // reserve exactly 1 byte for the trailing LF
            string value = new string('A', (int)valueLength);
            string patchFile = Path.Combine(dir, "PATCH_Cap.txt");
            File.WriteAllText(patchFile, header + value + "\n");
            Assert.Equal(totalFileBytes, new FileInfo(patchFile).Length);

            var (outDir, parent) = FreshOut();
            try
            {
                var result = BuildfileExportCore.Export(MakeRom(clean), MakeRom(target),
                    new BuildfileExportOptions
                    {
                        OutputDirectory = outDir,
                        PatchBaseDirectory = patchBase,
                        // NO MaxManifestBytesForTest override — this is the real production cap.
                    });

                Assert.True(result.Success, result.Error);

                // The proof-critical assertion: the actual published byte count must fit the
                // REAL consumer cap. On the pre-fix baseline this is the FIRST thing that fails
                // (a real 16,779,064-byte manifest against the real 16,777,216-byte cap) — no
                // degradation ever ran, so checking status/reason first would still fail here,
                // but checking the byte count FIRST pins the exact defect this regression closes
                // rather than a downstream symptom of it.
                var manifestFile = new FileInfo(Path.Combine(outDir, "buildfile.json"));
                Assert.True(manifestFile.Length <= BuildfileBuildOptions.MaxManifestBytes,
                    $"Published buildfile.json ({manifestFile.Length} bytes) must fit the real {BuildfileBuildOptions.MaxManifestBytes}-byte consumer cap.");

                // Only reachable once the byte-cap defect above is actually fixed: the advisory
                // patch inventory must have been the thing that degraded (never a partial list,
                // never a path leaking into the reason).
                Assert.Equal("unavailable", result.Manifest.Patches.Status);
                Assert.Contains("manifest byte budget", result.Manifest.Patches.Reason, StringComparison.OrdinalIgnoreCase);
                Assert.Empty(result.Manifest.Patches.Installed);
                Assert.DoesNotContain(patchBase, result.Manifest.Patches.Reason);

                BuildfileBuildResult buildResult =
                    BuildfileBuildCore.Build(MakeRom(clean), outDir, new BuildfileBuildOptions());
                Assert.True(buildResult.Success, buildResult.Error);
                Assert.True(buildResult.TargetIdentityMatches);
                Assert.True(target.SequenceEqual(buildResult.TargetBytes));
            }
            finally
            {
                Cleanup(parent);
                try { Directory.Delete(patchParent, true); } catch { }
            }
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
