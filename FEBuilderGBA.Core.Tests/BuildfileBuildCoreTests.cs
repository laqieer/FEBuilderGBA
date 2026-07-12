// SPDX-License-Identifier: GPL-3.0-or-later
// Core tests for the strict, independent schema-v1 buildfile CONSUMER (#1936).
//
// Synthetic-first: an in-memory FE8U clean baseline plus modded targets drive the real
// exporter (BuildfileExportCore.Export) to produce a KNOWN-GOOD project, which the
// independent consumer (BuildfileBuildCore.Build) must reconstruct byte-for-byte. The
// tests then mutate the manifest/payloads/clean ROM to prove every fail-closed contract:
// clean/version/size/hash identity, malformed/duplicate/non-UTF-8/oversized manifest,
// schema/dataDirectory/extension geometry, range totals/order/overlap/bounds/index/gba/
// changed-byte/path/hash invariants, missing/extra/subdirectory/symlink payloads, zero-range
// empty data, boundary-spanning payloads, declared-target-identity drift (Success but no
// match), and the atomic no-replace publication (new-file / no-overwrite / race cleanup).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class BuildfileBuildCoreTests : IDisposable
    {
        const string FE8U_CODE = "BE8E01";
        const string FE7U_CODE = "AE7E01";
        const int RomSize = 0x1000000; // 16 MiB — a normal FE8U size

        // Shared read-only clean baseline (never mutated; ROM.Data wraps it without copying,
        // and neither Export nor Build writes to it).
        static readonly byte[] SharedClean = new byte[RomSize];

        readonly List<string> _tempParents = new();

        public void Dispose()
        {
            foreach (string p in _tempParents)
            {
                try { if (Directory.Exists(p)) Directory.Delete(p, true); } catch { }
                try { if (File.Exists(p)) File.Delete(p); } catch { }
            }
        }

        static ROM MakeRom(byte[] data, string code = FE8U_CODE)
        {
            var rom = new ROM();
            Assert.True(rom.LoadLow("buildfile-consumer-test.gba", data, code));
            return rom;
        }

        string FreshParent()
        {
            string parent = Path.Combine(Path.GetTempPath(), "bfb_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(parent);
            _tempParents.Add(parent);
            return parent;
        }

        // Export a KNOWN-GOOD project for the given target and return its directory.
        string ExportValidProject(byte[] target, byte[] clean = null)
        {
            clean ??= SharedClean;
            string parent = FreshParent();
            string projectDir = Path.Combine(parent, "project");
            var result = BuildfileExportCore.Export(
                MakeRom(clean), MakeRom(target),
                new BuildfileExportOptions { OutputDirectory = projectDir });
            Assert.True(result.Success, "export failed: " + result.Error);
            return projectDir;
        }

        static void MutateManifest(string projectDir, Action<JsonObject> mutate)
        {
            string path = Path.Combine(projectDir, "buildfile.json");
            JsonNode root = JsonNode.Parse(File.ReadAllText(path));
            mutate(root.AsObject());
            File.WriteAllText(path, root.ToJsonString());
        }

        static string FirstPayloadFile(string projectDir)
            => Directory.GetFiles(Path.Combine(projectDir, "data"))
                .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal)
                .First();

        static string Sha256Hex(byte[] data)
        {
            var sb = new StringBuilder();
            foreach (byte b in SHA256.HashData(data)) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        static byte[] TargetEqualSize()
        {
            var t = (byte[])SharedClean.Clone();
            t[0x100] = 0xAA;
            t[0x200] = 0xBB; t[0x201] = 0xBC;
            t[0x30000] = 0xC7;
            return t;
        }

        static byte[] TargetExtended()
        {
            var t = new byte[RomSize + 0x1000];
            Array.Copy(SharedClean, t, RomSize);
            t[0x100] = 0xAA;
            for (int i = RomSize; i < t.Length; i++) t[i] = 0xFF; // most frequent extension byte
            t[RomSize + 0x10] = 0x01; // sparse non-fill overrides inside the extension
            t[RomSize + 0x11] = 0x02;
            t[t.Length - 1] = 0x03;
            return t;
        }

        // ------------------------------------------------------------- happy-path rebuild

        [Fact]
        public void Build_EqualSizeProject_ReconstructsExactly()
        {
            byte[] target = TargetEqualSize();
            string projectDir = ExportValidProject(target);

            BuildfileBuildResult result =
                BuildfileBuildCore.Build(MakeRom(SharedClean), projectDir, new BuildfileBuildOptions());

            Assert.True(result.Success, result.Error);
            Assert.True(result.TargetIdentityMatches);
            Assert.NotNull(result.TargetBytes);
            Assert.True(target.SequenceEqual(result.TargetBytes));
            Assert.Null(result.Manifest.Extension);
        }

        [Fact]
        public void Build_ExtendedProject_ReconstructsExactly_WithExtensionOverriddenByPayloads()
        {
            byte[] target = TargetExtended();
            string projectDir = ExportValidProject(target);

            BuildfileBuildResult result =
                BuildfileBuildCore.Build(MakeRom(SharedClean), projectDir, new BuildfileBuildOptions());

            Assert.True(result.Success, result.Error);
            Assert.True(result.TargetIdentityMatches);
            Assert.True(target.SequenceEqual(result.TargetBytes));
            Assert.NotNull(result.Manifest.Extension);
            Assert.Equal((uint)RomSize, result.Manifest.Extension.Start);
        }

        [Fact]
        public void Build_BoundarySpanningPayload_ReconstructsExactly()
        {
            int n = RomSize;
            var target = new byte[n + 0x1000];
            Array.Copy(SharedClean, target, n);
            for (int i = n; i < target.Length; i++) target[i] = 0x00; // fill = 0x00
            // One contiguous changed run crossing the clean/extension boundary:
            target[n - 2] = 0xF1;
            target[n - 1] = 0xF2;
            target[n] = 0x11;
            target[n + 1] = 0x22;
            string projectDir = ExportValidProject(target);

            BuildfileBuildResult result =
                BuildfileBuildCore.Build(MakeRom(SharedClean), projectDir, new BuildfileBuildOptions());

            Assert.True(result.Success, result.Error);
            Assert.True(result.TargetIdentityMatches);
            Assert.True(target.SequenceEqual(result.TargetBytes));
            // A single range spans the boundary (offset < clean size, end > clean size).
            BuildfileRange spanning = result.Manifest.Ranges.Single(r => r.Offset < (uint)n && r.Offset + r.Length > (uint)n);
            Assert.True(spanning.Length >= 4);
        }

        [Fact]
        public void Build_ZeroRangeEqualSize_ReconstructsCleanExactly()
        {
            byte[] target = (byte[])SharedClean.Clone(); // identical content, no edits
            string projectDir = ExportValidProject(target);

            BuildfileBuildResult result =
                BuildfileBuildCore.Build(MakeRom(SharedClean), projectDir, new BuildfileBuildOptions());

            Assert.True(result.Success, result.Error);
            Assert.True(result.TargetIdentityMatches);
            Assert.Equal(0, result.Manifest.TotalRanges);
            Assert.True(target.SequenceEqual(result.TargetBytes));
        }

        [Fact]
        public void Build_DoesNotMutateCleanRomOrProjectFiles()
        {
            byte[] target = TargetExtended();
            string projectDir = ExportValidProject(target);
            byte[] manifestBefore = File.ReadAllBytes(Path.Combine(projectDir, "buildfile.json"));
            var cleanProbe = (byte[])SharedClean.Clone();

            var rom = MakeRom(cleanProbe);
            BuildfileBuildResult result = BuildfileBuildCore.Build(rom, projectDir, new BuildfileBuildOptions());

            Assert.True(result.Success, result.Error);
            Assert.True(cleanProbe.SequenceEqual(SharedClean)); // clean untouched
            Assert.True(manifestBefore.SequenceEqual(File.ReadAllBytes(Path.Combine(projectDir, "buildfile.json"))));
        }

        // --------------------------------------------------------- clean / version identity

        [Fact]
        public void Build_WrongCleanVersion_Fails()
        {
            byte[] target = TargetEqualSize();
            string projectDir = ExportValidProject(target);

            // Same bytes, but detected as FE7U → version identity mismatch.
            BuildfileBuildResult result =
                BuildfileBuildCore.Build(MakeRom(SharedClean, FE7U_CODE), projectDir, new BuildfileBuildOptions());

            Assert.False(result.Success);
            Assert.Contains("version", result.Error, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Build_WrongCleanBytes_Fails()
        {
            byte[] target = TargetEqualSize();
            string projectDir = ExportValidProject(target);

            var wrongClean = (byte[])SharedClean.Clone();
            wrongClean[0x40000] ^= 0x5A; // differs from the recipe's declared clean identity
            BuildfileBuildResult result =
                BuildfileBuildCore.Build(MakeRom(wrongClean), projectDir, new BuildfileBuildOptions());

            Assert.False(result.Success);
            Assert.Contains("clean", result.Error, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Build_WrongCleanSize_Fails()
        {
            byte[] target = TargetEqualSize();
            string projectDir = ExportValidProject(target);

            var biggerClean = new byte[RomSize + 0x1000];
            Array.Copy(SharedClean, biggerClean, RomSize);
            BuildfileBuildResult result =
                BuildfileBuildCore.Build(MakeRom(biggerClean), projectDir, new BuildfileBuildOptions());

            Assert.False(result.Success);
        }

        // ----------------------------------------------------- declared target-identity drift

        [Fact]
        public void Build_DeclaredTargetIdentityDrift_SucceedsStructurallyButFlagsMismatch()
        {
            byte[] target = TargetEqualSize();
            string projectDir = ExportValidProject(target);

            // Corrupt ONLY the declared target sha256 to a different but canonically-spelled
            // hash: reconstruction still succeeds structurally, but the declared identity drifts.
            string otherSha = Sha256Hex(new byte[] { 1, 2, 3, 4 });
            MutateManifest(projectDir, root => root["target"]["sha256"] = otherSha);

            BuildfileBuildResult result =
                BuildfileBuildCore.Build(MakeRom(SharedClean), projectDir, new BuildfileBuildOptions());

            Assert.True(result.Success);                 // structural reconstruction OK
            Assert.False(result.TargetIdentityMatches);  // declared-target drift
            Assert.True(target.SequenceEqual(result.TargetBytes));
            Assert.NotEmpty(result.TargetIdentityDetail);
        }

        // ------------------------------------------------------------- manifest structure

        [Fact]
        public void Build_UnsupportedSchemaVersion_Fails()
        {
            string projectDir = ExportValidProject(TargetEqualSize());
            MutateManifest(projectDir, root => root["schemaVersion"] = 2);
            AssertBuildFails(projectDir, "schemaVersion");
        }

        [Fact]
        public void Build_WrongDataDirectory_Fails()
        {
            string projectDir = ExportValidProject(TargetEqualSize());
            MutateManifest(projectDir, root => root["dataDirectory"] = "payloads");
            AssertBuildFails(projectDir, "dataDirectory");
        }

        [Fact]
        public void Build_MissingRequiredProperty_Fails()
        {
            string projectDir = ExportValidProject(TargetEqualSize());
            MutateManifest(projectDir, root => root.Remove("version"));
            AssertBuildFails(projectDir, "version");
        }

        [Fact]
        public void Build_MalformedJson_Fails()
        {
            string projectDir = ExportValidProject(TargetEqualSize());
            File.WriteAllText(Path.Combine(projectDir, "buildfile.json"), "{ this is not json ");
            AssertBuildFails(projectDir, "JSON");
        }

        [Fact]
        public void Build_DuplicateProperty_Fails()
        {
            string projectDir = ExportValidProject(TargetEqualSize());
            string path = Path.Combine(projectDir, "buildfile.json");
            string json = File.ReadAllText(path);
            int brace = json.IndexOf('{');
            // Inject a second top-level property with the same name.
            string dup = json.Substring(0, brace + 1) + "\n  \"schemaVersion\": 1," + json.Substring(brace + 1);
            File.WriteAllText(path, dup);
            AssertBuildFails(projectDir, "duplicate");
        }

        [Theory]
        [InlineData("root")]
        [InlineData("clean")]
        [InlineData("target")]
        [InlineData("extension")]
        [InlineData("range")]
        public void Build_UnknownPropertyInSchemaObject_Fails(string objectName)
        {
            byte[] target = objectName == "extension" ? TargetExtended() : TargetEqualSize();
            string projectDir = ExportValidProject(target);
            MutateManifest(projectDir, root =>
            {
                JsonObject targetObject;
                switch (objectName)
                {
                    case "root":
                        targetObject = root;
                        break;
                    case "clean":
                        targetObject = root["clean"].AsObject();
                        break;
                    case "target":
                        targetObject = root["target"].AsObject();
                        break;
                    case "extension":
                        targetObject = root["extension"].AsObject();
                        break;
                    default:
                        targetObject = root["ranges"].AsArray()[0].AsObject();
                        break;
                }
                targetObject["unexpectedMember"] = true;
            });
            AssertBuildFails(projectDir, "unknown property");
        }

        [Fact]
        public void Build_RangeOptionalStringWrongType_Fails()
        {
            string projectDir = ExportValidProject(TargetEqualSize());
            MutateManifest(projectDir, root => root["ranges"][0]["category"] = 7);
            AssertBuildFails(projectDir, "category");
        }

        [Fact]
        public void Build_TargetOptionalCanonicalFlagWrongType_Fails()
        {
            string projectDir = ExportValidProject(TargetEqualSize());
            MutateManifest(projectDir, root =>
                root["target"]["isCanonicalOriginal"] = "false");
            AssertBuildFails(projectDir, "isCanonicalOriginal");
        }

        [Theory]
        [InlineData("patches")]
        [InlineData("projection")]
        public void Build_UnknownPropertyInAdvisoryObject_Fails(string objectName)
        {
            string projectDir = ExportValidProject(TargetEqualSize());
            MutateManifest(projectDir, root =>
                root[objectName].AsObject()["unexpectedMember"] = true);
            AssertBuildFails(projectDir, "unknown property");
        }

        [Theory]
        [InlineData("record")]
        [InlineData("parameter")]
        public void Build_UnknownPropertyInNestedPatchObject_Fails(string objectName)
        {
            string projectDir = ExportValidProject(TargetEqualSize());
            MutateManifest(projectDir, root =>
            {
                var parameter = new JsonObject
                {
                    ["key"] = "key",
                    ["value"] = "value",
                };
                var parameters = new JsonArray();
                parameters.Add(parameter);
                var record = new JsonObject
                {
                    ["params"] = parameters,
                };
                var installed = new JsonArray();
                installed.Add(record);
                root["patches"].AsObject()["installed"] = installed;

                JsonObject targetObject = objectName == "record" ? record : parameter;
                targetObject["unexpectedMember"] = true;
            });
            AssertBuildFails(projectDir, "unknown property");
        }

        [Fact]
        public void Build_ProjectionNullDirectory_RemainsValid()
        {
            byte[] target = TargetEqualSize();
            string projectDir = ExportValidProject(target);
            MutateManifest(projectDir, root =>
                root["projection"].AsObject()["directory"] = null);

            BuildfileBuildResult result =
                BuildfileBuildCore.Build(MakeRom(SharedClean), projectDir, new BuildfileBuildOptions());

            Assert.True(result.Success, result.Error);
            Assert.True(target.SequenceEqual(result.TargetBytes));
            Assert.Null(result.Manifest.Projection.Directory);
        }

        [Fact]
        public void Build_AdvisoryFieldsWithDistinctiveValues_ArePreservedOnManifest_ReconstructionStillExact()
        {
            byte[] target = TargetEqualSize();
            string projectDir = ExportValidProject(target);
            MutateManifest(projectDir, root =>
            {
                root["tool"] = "distinctive-tool-9f3a";
                root["game"] = "distinctive-game-2b71";
                root["entryEvent"] = "distinctive-entry-c04d.event";
                root["target"].AsObject()["isCanonicalOriginal"] = true;

                var parameter = new JsonObject
                {
                    ["key"] = "distinctive-param-key",
                    ["value"] = "distinctive-param-value",
                };
                var parameters = new JsonArray { parameter };
                var record = new JsonObject
                {
                    ["name"] = "distinctive-patch-name",
                    ["path"] = "distinctive/patch/path.ups",
                    ["status"] = "distinctive-record-status",
                    ["confidence"] = "distinctive-confidence",
                    ["reason"] = "distinctive-record-reason",
                    ["params"] = parameters,
                };
                var installed = new JsonArray { record };
                root["patches"] = new JsonObject
                {
                    ["status"] = "distinctive-patches-status",
                    ["reason"] = "distinctive-patches-reason",
                    ["baseRelative"] = "distinctive/base/relative",
                    ["installed"] = installed,
                };

                root["projection"] = new JsonObject
                {
                    ["status"] = "distinctive-projection-status",
                    ["reason"] = "distinctive-projection-reason",
                    ["directory"] = "distinctive/projection/dir",
                };

                var warnings = new JsonArray { "distinctive warning one", "distinctive warning two" };
                root["warnings"] = warnings;
            });

            BuildfileBuildResult result =
                BuildfileBuildCore.Build(MakeRom(SharedClean), projectDir, new BuildfileBuildOptions());

            Assert.True(result.Success, result.Error);
            Assert.True(result.TargetIdentityMatches);
            Assert.True(target.SequenceEqual(result.TargetBytes));

            BuildfileManifest m = result.Manifest;
            Assert.Equal("distinctive-tool-9f3a", m.Tool);
            Assert.Equal("distinctive-game-2b71", m.Game);
            Assert.Equal("distinctive-entry-c04d.event", m.EntryEvent);
            Assert.True(m.Target.IsCanonicalOriginal);

            Assert.Equal("distinctive-patches-status", m.Patches.Status);
            Assert.Equal("distinctive-patches-reason", m.Patches.Reason);
            Assert.Equal("distinctive/base/relative", m.Patches.BaseRelative);
            BuildfilePatchRecord patchRecord = Assert.Single(m.Patches.Installed);
            Assert.Equal("distinctive-patch-name", patchRecord.Name);
            Assert.Equal("distinctive/patch/path.ups", patchRecord.Path);
            Assert.Equal("distinctive-record-status", patchRecord.Status);
            Assert.Equal("distinctive-confidence", patchRecord.Confidence);
            Assert.Equal("distinctive-record-reason", patchRecord.Reason);
            BuildfilePatchParam patchParam = Assert.Single(patchRecord.Params);
            Assert.Equal("distinctive-param-key", patchParam.Key);
            Assert.Equal("distinctive-param-value", patchParam.Value);

            Assert.Equal("distinctive-projection-status", m.Projection.Status);
            Assert.Equal("distinctive-projection-reason", m.Projection.Reason);
            Assert.Equal("distinctive/projection/dir", m.Projection.Directory);

            Assert.Equal(
                new[] { "distinctive warning one", "distinctive warning two" },
                m.Warnings);
        }

        [Fact]
        public void Build_WarningWithWrongType_Fails()
        {
            string projectDir = ExportValidProject(TargetEqualSize());
            MutateManifest(projectDir, root =>
            {
                var warnings = new JsonArray();
                warnings.Add(7);
                root["warnings"] = warnings;
            });
            AssertBuildFails(projectDir, "warnings");
        }

        // --------------------------------------------- shared advisory-item budget (16,384)

        static JsonArray StringArray(int count)
        {
            var arr = new JsonArray();
            for (int i = 0; i < count; i++) arr.Add("advisory-item-" + i);
            return arr;
        }

        static JsonArray NullArray(int count)
        {
            var arr = new JsonArray();
            for (int i = 0; i < count; i++) arr.Add((JsonNode)null);
            return arr;
        }

        [Fact]
        public void Build_AdvisoryBudget_ExactlyAtCap_Succeeds()
        {
            byte[] target = TargetEqualSize();
            string projectDir = ExportValidProject(target);
            MutateManifest(projectDir, root =>
            {
                root["warnings"] = StringArray(BuildfileBuildOptions.MaxAdvisoryItems);
            });

            BuildfileBuildResult result =
                BuildfileBuildCore.Build(MakeRom(SharedClean), projectDir, new BuildfileBuildOptions());

            Assert.True(result.Success, result.Error);
            Assert.True(target.SequenceEqual(result.TargetBytes));
            Assert.Equal(BuildfileBuildOptions.MaxAdvisoryItems, result.Manifest.Warnings.Count);
        }

        [Fact]
        public void Build_AdvisoryBudget_WarningsOnlyOverCap_FailsAtBudgetGuardBeforeTypeValidation()
        {
            // Every entry is JSON null (would also fail the "warnings entries must be strings"
            // type check) — the shared-budget guard must reject BEFORE that per-entry check even
            // looks at a single entry.
            string projectDir = ExportValidProject(TargetEqualSize());
            MutateManifest(projectDir, root =>
            {
                root["warnings"] = NullArray(BuildfileBuildOptions.MaxAdvisoryItems + 1);
            });
            AssertBuildFails(projectDir, "advisory item");
        }

        [Fact]
        public void Build_AdvisoryBudget_InstalledOnlyOverCap_FailsAtBudgetGuardBeforeObjectValidation()
        {
            // Every entry is JSON null (would also fail the "entries must be objects" check) —
            // the shared-budget guard must reject BEFORE that per-entry check looks at a single
            // entry.
            string projectDir = ExportValidProject(TargetEqualSize());
            MutateManifest(projectDir, root =>
            {
                root["patches"].AsObject()["installed"] =
                    NullArray(BuildfileBuildOptions.MaxAdvisoryItems + 1);
            });
            AssertBuildFails(projectDir, "advisory item");
        }

        [Fact]
        public void Build_AdvisoryBudget_NestedParamsTripSharedTotalBeforeParamTypeValidation()
        {
            // One VALID installed record (consumes 1 of the shared budget) whose own `params`
            // array alone has exactly MaxAdvisoryItems null entries — 1 + MaxAdvisoryItems is
            // one over the shared cap, so the params array must be rejected by the shared-budget
            // guard from INSIDE the record loop, before a single param entry's object/type is
            // validated.
            string projectDir = ExportValidProject(TargetEqualSize());
            MutateManifest(projectDir, root =>
            {
                var record = new JsonObject
                {
                    ["name"] = "n",
                    ["path"] = "p",
                    ["status"] = "installed",
                    ["confidence"] = "high",
                    ["reason"] = "r",
                    ["params"] = NullArray(BuildfileBuildOptions.MaxAdvisoryItems),
                };
                root["patches"].AsObject()["installed"] = new JsonArray { record };
            });
            AssertBuildFails(projectDir, "advisory item");
        }

        [Fact]
        public void Build_NonUtf8Manifest_Fails()
        {
            string projectDir = ExportValidProject(TargetEqualSize());
            string path = Path.Combine(projectDir, "buildfile.json");
            byte[] valid = File.ReadAllBytes(path);
            var corrupt = new byte[valid.Length + 1];
            Array.Copy(valid, corrupt, valid.Length);
            corrupt[valid.Length] = 0xFF; // invalid stand-alone UTF-8 byte
            File.WriteAllBytes(path, corrupt);
            AssertBuildFails(projectDir, "UTF-8");
        }

        [Fact]
        public void Build_OversizedManifest_Fails()
        {
            string projectDir = ExportValidProject(TargetEqualSize());
            // Prove the byte-cap without a 16 MiB file: shrink the cap below the manifest size
            // via the internal test seam (InternalsVisibleTo — never a production surface).
            var options = new BuildfileBuildOptions { MaxManifestBytesForTest = 8 };

            BuildfileBuildResult result = BuildfileBuildCore.Build(MakeRom(SharedClean), projectDir, options);
            Assert.False(result.Success);
            Assert.Contains("limit", result.Error, StringComparison.OrdinalIgnoreCase);
        }

        // ------------------------------------------------------------ extension geometry

        [Fact]
        public void Build_ExtensionWrongStart_Fails()
        {
            string projectDir = ExportValidProject(TargetExtended());
            MutateManifest(projectDir, root => root["extension"]["start"] = 123u);
            AssertBuildFails(projectDir, "start");
        }

        [Fact]
        public void Build_ExtensionWrongLength_Fails()
        {
            string projectDir = ExportValidProject(TargetExtended());
            MutateManifest(projectDir, root => root["extension"]["length"] = 7u);
            AssertBuildFails(projectDir, "length");
        }

        [Fact]
        public void Build_ExtensionNonCanonicalFillByte_Fails()
        {
            string projectDir = ExportValidProject(TargetExtended());
            MutateManifest(projectDir, root => root["extension"]["fillByte"] = "0xZZ");
            AssertBuildFails(projectDir, "fillByte");
        }

        [Fact]
        public void Build_ExtensionPresentButSizesEqual_Fails()
        {
            string projectDir = ExportValidProject(TargetEqualSize());
            MutateManifest(projectDir, root =>
                root["extension"] = new JsonObject
                {
                    ["start"] = (uint)RomSize,
                    ["length"] = 16u,
                    ["fillByte"] = "0x00",
                });
            AssertBuildFails(projectDir, "extension");
        }

        [Fact]
        public void Build_ExtensionMissingButSizesDiffer_Fails()
        {
            string projectDir = ExportValidProject(TargetExtended());
            MutateManifest(projectDir, root => root.Remove("extension"));
            AssertBuildFails(projectDir, "extension");
        }

        [Fact]
        public void Build_ExtensionNullWhenSizesDiffer_ReportsRequiredObject()
        {
            string projectDir = ExportValidProject(TargetExtended());
            MutateManifest(projectDir, root => root["extension"] = null);
            AssertBuildFails(projectDir, "object (not null)");
        }

        // ------------------------------------------------------------------ range invariants

        [Fact]
        public void Build_NonContiguousIndex_Fails()
        {
            string projectDir = ExportValidProject(TargetEqualSize());
            MutateManifest(projectDir, root => root["ranges"][0]["index"] = 9);
            AssertBuildFails(projectDir, "index");
        }

        [Fact]
        public void Build_RangeOutOfBounds_Fails()
        {
            string projectDir = ExportValidProject(TargetEqualSize());
            MutateManifest(projectDir, root => root["ranges"][0]["offset"] = (uint)RomSize);
            AssertBuildFails(projectDir, "exceeds");
        }

        [Fact]
        public void Build_OverlappingRanges_Fails()
        {
            string projectDir = ExportValidProject(TargetEqualSize());
            // Force range[1] to start at range[0]'s offset → overlap / not strictly ordered.
            MutateManifest(projectDir, root =>
            {
                uint firstOffset = (uint)(int)root["ranges"][0]["offset"];
                root["ranges"][1]["offset"] = firstOffset;
            });
            AssertBuildFails(projectDir, "overlap");
        }

        [Fact]
        public void Build_AdjacentRanges_FailAsNonCanonical()
        {
            byte[] target = TargetEqualSize();
            string projectDir = ExportValidProject(target);
            MutateManifest(projectDir, root =>
            {
                JsonArray ranges = root["ranges"].AsArray();
                JsonObject first = ranges[0].AsObject();
                JsonObject second = ranges[1].AsObject();
                second["offset"] = first["offset"].GetValue<uint>()
                    + first["length"].GetValue<uint>();
            });
            AssertBuildFails(projectDir, "touches");
        }

        [Fact]
        public void Build_ChangedBytesMismatch_Fails()
        {
            string projectDir = ExportValidProject(TargetEqualSize());
            MutateManifest(projectDir, root =>
            {
                uint len = (uint)(int)root["ranges"][0]["length"];
                root["ranges"][0]["changedBytes"] = len + 1;
            });
            AssertBuildFails(projectDir, "changedBytes");
        }

        [Fact]
        public void Build_WrongGbaAddress_Fails()
        {
            string projectDir = ExportValidProject(TargetEqualSize());
            MutateManifest(projectDir, root => root["ranges"][0]["gbaAddress"] = "0x00000000");
            AssertBuildFails(projectDir, "gbaAddress");
        }

        [Fact]
        public void Build_WrongPayloadPath_Fails()
        {
            string projectDir = ExportValidProject(TargetEqualSize());
            MutateManifest(projectDir, root => root["ranges"][0]["payload"] = "data/tampered.bin");
            AssertBuildFails(projectDir, "payload");
        }

        [Fact]
        public void Build_TotalRangesMismatch_Fails()
        {
            string projectDir = ExportValidProject(TargetEqualSize());
            MutateManifest(projectDir, root => root["totalRanges"] = 99);
            AssertBuildFails(projectDir, "totalRanges");
        }

        [Fact]
        public void Build_TotalChangedBytesMismatch_Fails()
        {
            string projectDir = ExportValidProject(TargetEqualSize());
            MutateManifest(projectDir, root => root["totalChangedBytes"] = 999999u);
            AssertBuildFails(projectDir, "totalChangedBytes");
        }

        // Exact resource-guard boundary: exactly MaxPayloadRanges entries must PASS the
        // count guard and advance to the next (per-element) validation error — proving
        // the guard is "> Max", not "== Max" or ">= Max". A JsonArray of `null` entries
        // keeps this fast and avoids materializing 16,384 payload files.
        [Fact]
        public void Build_RangesAtMaxPayloadRanges_PassesCountGuard_FailsOnElementValidation()
        {
            const int count = BuildfileBuildOptions.MaxPayloadRanges;
            string projectDir = ExportValidProject(TargetEqualSize());
            MutateManifest(projectDir, root =>
            {
                var ranges = new JsonArray();
                for (int i = 0; i < count; i++) ranges.Add(null);
                root["ranges"] = ranges;
                root["totalRanges"] = count;
                root["totalChangedBytes"] = 0u;
            });
            AssertBuildFails(projectDir, "Each range must be a JSON object");
        }

        // MaxPayloadRanges + 1 must fail AT the explicit exceeds-limit guard itself —
        // totalRanges is kept consistent with the array length so the guard under test
        // is unambiguously the count-exceeds check, not the totalRanges-mismatch check.
        [Fact]
        public void Build_RangesExceedingMaxPayloadRanges_FailsAtCountGuard()
        {
            const int max = BuildfileBuildOptions.MaxPayloadRanges;
            const int count = max + 1;
            string projectDir = ExportValidProject(TargetEqualSize());
            MutateManifest(projectDir, root =>
            {
                var ranges = new JsonArray();
                for (int i = 0; i < count; i++) ranges.Add(null);
                root["ranges"] = ranges;
                root["totalRanges"] = count;
                root["totalChangedBytes"] = 0u;
            });
            AssertBuildFails(
                projectDir,
                "ranges count " + count + " exceeds the " + max + " limit");
        }

        // ------------------------------------------------------------------ payload faults

        [Fact]
        public void Build_TamperedPayloadBytes_Fails()
        {
            string projectDir = ExportValidProject(TargetEqualSize());
            string payload = FirstPayloadFile(projectDir);
            byte[] bytes = File.ReadAllBytes(payload);
            bytes[0] ^= 0xFF;
            File.WriteAllBytes(payload, bytes);
            AssertBuildFails(projectDir, "sha256");
        }

        [Fact]
        public void Build_PayloadContainingUnchangedByte_Fails()
        {
            byte[] target = TargetEqualSize();
            string projectDir = ExportValidProject(target);
            string payload = FirstPayloadFile(projectDir);
            byte[] bytes = File.ReadAllBytes(payload);
            bytes[0] = SharedClean[0x100];
            File.WriteAllBytes(payload, bytes);
            string sha = Sha256Hex(bytes);
            MutateManifest(projectDir, root =>
                root["ranges"].AsArray()[0].AsObject()["payloadSha256"] = sha);
            AssertBuildFails(projectDir, "unchanged byte");
        }

        [Fact]
        public void Build_WrongDeclaredPayloadHash_Fails()
        {
            string projectDir = ExportValidProject(TargetEqualSize());
            string otherSha = Sha256Hex(new byte[] { 9, 9, 9 });
            MutateManifest(projectDir, root => root["ranges"][0]["payloadSha256"] = otherSha);
            AssertBuildFails(projectDir, "sha256");
        }

        [Fact]
        public void Build_MissingPayloadFile_Fails()
        {
            string projectDir = ExportValidProject(TargetEqualSize());
            File.Delete(FirstPayloadFile(projectDir));
            AssertBuildFails(projectDir, "payload");
        }

        [Fact]
        public void Build_ExtraPayloadOnZeroRangeProject_Fails()
        {
            byte[] target = (byte[])SharedClean.Clone(); // zero ranges, empty data/
            string projectDir = ExportValidProject(target);
            File.WriteAllBytes(Path.Combine(projectDir, "data", "0000_000000_1.bin"), new byte[0]);
            AssertBuildFails(projectDir, "payload");
        }

        [Fact]
        public void Build_ExtraPayloadExceedsCaptureBound_Fails()
        {
            string projectDir = ExportValidProject(TargetEqualSize());
            File.WriteAllBytes(Path.Combine(projectDir, "data", "zzz_extra.bin"), new byte[] { 1, 2, 3 });
            BuildfileBuildResult result =
                BuildfileBuildCore.Build(MakeRom(SharedClean), projectDir, new BuildfileBuildOptions());
            Assert.False(result.Success);
        }

        [Fact]
        public void Build_SubdirectoryInData_Fails()
        {
            // Use a zero-range project so the single subdirectory entry fits the capture bound
            // and reaches the explicit no-subdirectory check (rather than the entry-count limit).
            byte[] target = (byte[])SharedClean.Clone();
            string projectDir = ExportValidProject(target);
            Directory.CreateDirectory(Path.Combine(projectDir, "data", "nested"));
            AssertBuildFails(projectDir, "subdirector");
        }

        [SkippableFact]
        public void Build_CaseCollidingPayloadNames_Fail()
        {
            string projectDir = ExportValidProject(TargetEqualSize());
            string dataDir = Path.Combine(projectDir, "data");
            string[] payloads = Directory.GetFiles(dataDir)
                .OrderBy(path => new FileInfo(path).Length)
                .ThenBy(path => Path.GetFileName(path), StringComparer.Ordinal)
                .ToArray();
            Assert.True(payloads.Length >= 2);

            string source = payloads[0];
            string removed = payloads[payloads.Length - 1];
            string caseVariant = Path.Combine(
                dataDir, Path.GetFileNameWithoutExtension(source) + ".BIN");
            try
            {
                File.Copy(source, caseVariant);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Skip.If(true, "Filesystem cannot create case-colliding payload names: " + ex.Message);
                return;
            }

            int collisionCount = Directory.GetFiles(dataDir).Count(path =>
                string.Equals(
                    Path.GetFileName(path),
                    Path.GetFileName(source),
                    StringComparison.OrdinalIgnoreCase));
            if (collisionCount < 2)
            {
                Skip.If(true, "Filesystem does not preserve distinct case-colliding names.");
                return;
            }

            // Keep the validated entry/byte caps satisfied so the explicit collision check runs.
            File.Delete(removed);
            AssertBuildFails(projectDir, "case-colliding");
        }

        [Fact]
        public void Build_WrongPayloadLength_Fails()
        {
            string projectDir = ExportValidProject(TargetEqualSize());
            // Truncate a payload so total captured bytes stay within the bound and the explicit
            // per-payload length check (not the capture byte limit) rejects it.
            string payload = FirstPayloadFile(projectDir);
            File.WriteAllBytes(payload, Array.Empty<byte>());
            AssertBuildFails(projectDir, "length");
        }

        [SkippableFact]
        public void Build_SymlinkPayload_Fails()
        {
            string projectDir = ExportValidProject(TargetEqualSize());
            string payload = FirstPayloadFile(projectDir);
            string target = Path.Combine(FreshParent(), "elsewhere.bin");
            File.WriteAllBytes(target, new byte[] { 0 });
            File.Delete(payload);
            try { File.CreateSymbolicLink(payload, target); }
            catch (Exception ex) { Skip.If(true, "Cannot create a file symlink here: " + ex.Message); return; }

            BuildfileBuildResult result =
                BuildfileBuildCore.Build(MakeRom(SharedClean), projectDir, new BuildfileBuildOptions());
            Assert.False(result.Success);
        }

        [Fact]
        public void Build_MissingProjectDirectory_Fails()
        {
            string projectDir = Path.Combine(FreshParent(), "does-not-exist");
            AssertBuildFails(projectDir, "not found");
        }

        [Fact]
        public void PathContainment_HandlesRootDescendantAndSiblingPrefix()
        {
            string parent = FreshParent();
            string data = Path.Combine(parent, "data");
            string nested = Path.Combine(data, "nested");
            string sibling = Path.Combine(parent, "data2");
            Directory.CreateDirectory(nested);
            Directory.CreateDirectory(sibling);

            Assert.True(BuildfilePathSafety.IsSameOrDescendantPath(data, data));
            Assert.True(BuildfilePathSafety.IsSameOrDescendantPath(nested, data));
            Assert.False(BuildfilePathSafety.IsSameOrDescendantPath(sibling, data));
            Assert.True(BuildfilePathSafety.IsSameOrDescendantPath(
                nested,
                Path.GetPathRoot(parent)));
        }

        [Fact]
        public void PathContainment_WalksAncestorsByFilesystemIdentity()
        {
            string parent = FreshParent();
            string realData = Path.Combine(parent, "real-data");
            string aliasData = Path.Combine(parent, "alias-data");
            string aliasNested = Path.Combine(aliasData, "nested");
            Directory.CreateDirectory(realData);
            Directory.CreateDirectory(aliasNested);

            bool SameIdentity(string candidate, string root)
                => BuildfilePathSafety.PathsEqual(candidate, aliasData)
                    && BuildfilePathSafety.PathsEqual(root, realData);

            Assert.True(BuildfilePathSafety.IsSameOrDescendantPath(
                aliasNested,
                realData,
                SameIdentity));
        }

        [Fact]
        public void Build_MissingBuildfileJson_Fails()
        {
            string projectDir = Path.Combine(FreshParent(), "empty");
            Directory.CreateDirectory(projectDir);
            AssertBuildFails(projectDir, "buildfile.json");
        }

        // ------------------------------------------------------- atomic no-replace publish

        [Fact]
        public void PublishBytesNoReplace_WritesBrandNewFileExactly()
        {
            string parent = FreshParent();
            string dest = Path.Combine(parent, "rebuilt.gba");
            byte[] data = { 1, 2, 3, 4, 5 };

            bool ok = BuildfileBuildCore.PublishBytesNoReplace(data, dest, out string error);

            Assert.True(ok, error);
            Assert.True(File.Exists(dest));
            Assert.True(data.SequenceEqual(File.ReadAllBytes(dest)));
            Assert.Empty(Directory.GetFiles(parent, ".febuild-*.tmp"));
        }

        [Fact]
        public void PublishBytesNoReplace_ExistingDestination_Fails_NoOverwrite_CleansStaging()
        {
            string parent = FreshParent();
            string dest = Path.Combine(parent, "rebuilt.gba");
            File.WriteAllText(dest, "precious");

            bool ok = BuildfileBuildCore.PublishBytesNoReplace(new byte[] { 9 }, dest, out string error);

            Assert.False(ok);
            Assert.Contains("already exists", error, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("precious", File.ReadAllText(dest));      // untouched
            Assert.Empty(Directory.GetFiles(parent, ".febuild-*.tmp")); // staging cleaned up
        }

        [Fact]
        public void PublishBytesNoReplace_DestinationRace_Fails_NoOverwrite_CleansStaging()
        {
            string parent = FreshParent();
            string dest = Path.Combine(parent, "rebuilt.gba");

            bool ok = BuildfileBuildCore.PublishBytesNoReplace(
                new byte[] { 1, 2, 3 },
                dest,
                (stage, destination) => File.WriteAllText(destination, "racer"),
                null,
                out string error);

            Assert.False(ok);
            Assert.Contains("Publish failed", error);
            Assert.Equal("racer", File.ReadAllText(dest));
            Assert.Empty(Directory.GetFiles(parent, ".febuild-*.tmp"));
        }

        [Fact]
        public void PublishBytesNoReplace_CleanupFailure_ReportsResidualStage()
        {
            string parent = FreshParent();
            string dest = Path.Combine(parent, "rebuilt.gba");
            string residual = null;

            bool ok = BuildfileBuildCore.PublishBytesNoReplace(
                new byte[] { 1, 2, 3 },
                dest,
                (stage, destination) => File.WriteAllText(destination, "racer"),
                (string stage, out string cleanupError) =>
                {
                    residual = stage;
                    cleanupError = "injected cleanup failure";
                    return false;
                },
                out string error);

            Assert.False(ok);
            Assert.Contains("Cleanup incomplete", error);
            Assert.Contains("injected cleanup failure", error);
            Assert.NotNull(residual);
            Assert.True(File.Exists(residual));
            File.Delete(residual);
        }

        [Fact]
        public void PublishBytesNoReplace_LongDestinationName_UsesBoundedStageName()
        {
            string parent = FreshParent();
            string dest = Path.Combine(parent, new string('a', 220) + ".gba");

            bool ok = BuildfileBuildCore.PublishBytesNoReplace(
                new byte[] { 1, 2, 3 },
                dest,
                out string error);

            Assert.True(ok, error);
            Assert.True(File.Exists(dest));
            Assert.Empty(Directory.GetFiles(parent, ".febuild-*.tmp"));
        }

        [Fact]
        public void PublishBytesNoReplace_MissingParent_Fails()
        {
            string dest = Path.Combine(FreshParent(), "no-such-dir", "rebuilt.gba");
            bool ok = BuildfileBuildCore.PublishBytesNoReplace(new byte[] { 1 }, dest, out string error);
            Assert.False(ok);
            Assert.Contains("parent", error, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ReserveScratchDirectory_AndDeleteTreeAndVerifyGone_RoundTrip()
        {
            string parent = FreshParent();
            string scratch = BuildfileBuildCore.ReserveScratchDirectory(parent, ".febuild-test-");
            Assert.True(Directory.Exists(scratch));
            Assert.StartsWith(parent, scratch);
            File.WriteAllText(Path.Combine(scratch, "child.txt"), "x");

            Assert.True(BuildfileBuildCore.DeleteTreeAndVerifyGone(scratch, out string error), error);
            Assert.False(Directory.Exists(scratch));
        }

        [Fact]
        public void DeleteTreeAndVerifyGone_AlreadyAbsent_ReturnsTrue()
        {
            string missing = Path.Combine(FreshParent(), "never-created");
            Assert.True(BuildfileBuildCore.DeleteTreeAndVerifyGone(missing, out string error), error);
        }

        // ---------------------------------------------------------------
        // BuildfileBuildCore.DeleteFileAndVerifyGone: delegate-injected fail-closed cleanup
        // (Copilot review finding: File.Exists can report cleanup success on a non-file
        // replacement or an inspection failure). No File.Exists anywhere in this path; every
        // false detail includes the exact path.
        // ---------------------------------------------------------------

        [Fact]
        public void DeleteFileAndVerifyGone_DeleteThrowsExpectedFault_FailsClosed_WithExactPath()
        {
            bool ok = BuildfileBuildCore.DeleteFileAndVerifyGone(
                "toxic-staging-file",
                _ => throw new IOException("disk full"),
                _ => FileAttributes.Normal,
                out string error);

            Assert.False(ok);
            Assert.Contains("disk full", error);
            Assert.Contains("toxic-staging-file", error);
        }

        [Fact]
        public void DeleteFileAndVerifyGone_DeleteReportsMissingButAttributesShowReplacedDirectory_FailsClosed_WithExactPath()
        {
            // Delete THINKS the path is already gone (FileNotFoundException), but a
            // post-delete attribute probe shows a directory now sits at the same path — the
            // old File.Exists check would have silently reported "gone" for a directory
            // replacement (Copilot review finding).
            bool ok = BuildfileBuildCore.DeleteFileAndVerifyGone(
                "replaced-with-directory",
                _ => throw new FileNotFoundException("missing"),
                _ => FileAttributes.Directory,
                out string error);

            Assert.False(ok);
            Assert.Contains("path still present after delete", error);
            Assert.Contains("replaced-with-directory", error);
        }

        [Fact]
        public void DeleteFileAndVerifyGone_DeleteReportsMissingButAttributesShowReparsePoint_FailsClosed_WithExactPath()
        {
            bool ok = BuildfileBuildCore.DeleteFileAndVerifyGone(
                "replaced-with-reparse-point",
                _ => throw new DirectoryNotFoundException("missing"),
                _ => FileAttributes.ReparsePoint,
                out string error);

            Assert.False(ok);
            Assert.Contains("path still present after delete", error);
            Assert.Contains("replaced-with-reparse-point", error);
        }

        [Fact]
        public void DeleteFileAndVerifyGone_PostDeleteVerificationThrowsExpectedFault_FailsClosed_WithExactPath()
        {
            bool ok = BuildfileBuildCore.DeleteFileAndVerifyGone(
                "unverifiable-staging-file",
                _ => { },
                _ => throw new UnauthorizedAccessException("locked"),
                out string error);

            Assert.False(ok);
            Assert.Contains("could not verify path absence", error);
            Assert.Contains("locked", error);
            Assert.Contains("unverifiable-staging-file", error);
        }

        [Fact]
        public void DeleteFileAndVerifyGone_DeleteSucceeds_AttributesConfirmAbsent_ReturnsTrue()
        {
            bool ok = BuildfileBuildCore.DeleteFileAndVerifyGone(
                "gone-staging-file",
                _ => { },
                _ => throw new FileNotFoundException("gone"),
                out string error);

            Assert.True(ok, error);
            Assert.Equal("", error);
        }

        // ------------------------------------------------------------------------- helpers

        void AssertBuildFails(string projectDir, string expectedFragment)
        {
            BuildfileBuildResult result =
                BuildfileBuildCore.Build(MakeRom(SharedClean), projectDir, new BuildfileBuildOptions());
            Assert.False(result.Success, "expected a structural failure but Build succeeded");
            Assert.Null(result.TargetBytes);
            if (!string.IsNullOrEmpty(expectedFragment))
                Assert.Contains(expectedFragment, result.Error, StringComparison.OrdinalIgnoreCase);
        }
    }
}
