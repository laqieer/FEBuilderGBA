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
        public void Export_ProjectionSuccess_MovesScratchToSource_SanitizesScratchPath()
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
    }
}
