// SPDX-License-Identifier: GPL-3.0-or-later
// #1148: decomp source-backed CHAPTER/MAP editing — Core writer coverage.
//
// Chapter settings (the map_settings struct table) are a plain 0-indexed C-struct array,
// so the existing DecompSourceWriterCore handles them with zero new Core code. These tests
// prove the chapter/map source-write path end-to-end against synthetic fixtures:
//   - a single chapter field rewrite is churn-free (only that token changes) + NeedsRebuild;
//   - JSON map_settings owner (flat object) rewrites the same way;
//   - a pointer/undeclared field is reported UnsupportedField with NO mutation;
//   - a romOnly owner is reported RomOnly with NO mutation;
//   - format=json with a NESTED chapter-settings shape is honestly Manual/UnsupportedField
//     (the writer does NOT silently corrupt the real nested chapter_settings.json) — the
//     documented residual (Copilot plan-review finding 1);
//   - malformed manifest/source never throws.
using System;
using System.Collections.Generic;
using System.IO;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class DecompChapterMapWriterCoreTests : IDisposable
    {
        readonly DecompProject _savedProject;

        public DecompChapterMapWriterCoreTests()
        {
            _savedProject = CoreState.DecompProject;
            CoreState.DecompProject = null;
        }

        public void Dispose()
        {
            CoreState.DecompProject = null;
            CoreState.DecompProject = _savedProject;
        }

        // ---- helpers ----

        static string NewTempDir()
        {
            string d = Path.Combine(Path.GetTempPath(), "decomp_chap_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(d);
            return d;
        }

        static void TryDelete(string dir)
        {
            try { Directory.Delete(dir, true); } catch { }
        }

        /// <summary>map_settings C-struct owner with the canonical chapter-setting fields.</summary>
        static DecompTableEntry MapSettingsOwner(
            string sourceFile, string policy = "source", string format = "cstruct",
            string arrayName = "gMapChapterData")
        {
            return new DecompTableEntry
            {
                Table = "map_settings",
                Format = format,
                WritePolicy = policy,
                ArrayName = arrayName,
                SourceFile = sourceFile,
                IndexBase = 0,
                Fields = new List<DecompTableField>
                {
                    new DecompTableField { Name = "Weather",       Width = 1 },
                    new DecompTableField { Name = "FogLevel",      Width = 1 },
                    new DecompTableField { Name = "PLISTObj",      Width = 2 },
                    new DecompTableField { Name = "BGM1",          Width = 2 },
                    new DecompTableField { Name = "ChapterNumber", Width = 1 },
                    new DecompTableField { Name = "TextGoal",      Width = 2 },
                    new DecompTableField { Name = "EscapeMarkerX", Width = 1 },
                    // pointer fields are intentionally NOT declared (ROM-only/manual).
                },
            };
        }

        static DecompProject ProjectWith(string root, params DecompTableEntry[] owners)
        {
            var man = new DecompManifest();
            var arr = System.Text.Json.JsonSerializer.SerializeToElement(owners);
            man.Tables = arr;
            return new DecompProject { ProjectRoot = root, Manifest = man };
        }

        static DecompProject ActiveProject(string root, params DecompTableEntry[] owners)
        {
            var p = ProjectWith(root, owners);
            CoreState.DecompProject = p;
            return p;
        }

        // =====================================================================
        //  C-struct chapter-setting rewrite — churn-free, NeedsRebuild
        // =====================================================================

        [Fact]
        public void Chapter_CStruct_SingleField_RewritesChurnFree_SetsNeedsRebuild()
        {
            string dir = NewTempDir();
            try
            {
                string srcRel = "src/data/chapters.c";
                string srcAbs = Path.Combine(dir, "src", "data", "chapters.c");
                Directory.CreateDirectory(Path.GetDirectoryName(srcAbs));
                string content =
                    "struct ChapterData gMapChapterData[] = {\n" +
                    "    [0] = { .Weather = 0, .FogLevel = 0, .ChapterNumber = 0 },\n" +
                    "    [1] = { .Weather = 1, .FogLevel = 3, .ChapterNumber = 5 }, // ch1\n" +
                    "    [2] = { .Weather = 2, .FogLevel = 0, .ChapterNumber = 6 },\n" +
                    "};\n";
                File.WriteAllText(srcAbs, content);

                var proj = ActiveProject(dir, MapSettingsOwner(srcRel));
                Assert.False(proj.NeedsRebuild);

                var changes = new Dictionary<string, uint> { { "Weather", 4 } };
                var res = DecompSourceWriterCore.WriteTableEntry(proj, "map_settings", 1, changes);

                Assert.True(res.Ok, res.Message);
                Assert.True(proj.NeedsRebuild);
                Assert.Contains("Weather", res.ChangedFields);

                // Only entry 1's Weather token changed — every other byte is identical.
                string after = File.ReadAllText(srcAbs);
                string expected = content.Replace(
                    "[1] = { .Weather = 1, .FogLevel = 3, .ChapterNumber = 5 }, // ch1",
                    "[1] = { .Weather = 4, .FogLevel = 3, .ChapterNumber = 5 }, // ch1");
                Assert.Equal(expected, after);
            }
            finally { TryDelete(dir); }
        }

        [Fact]
        public void Chapter_CStruct_NoOp_ValueAlreadyMatches_NoWrite_NoRebuild()
        {
            string dir = NewTempDir();
            try
            {
                string srcRel = "src/chapters.c";
                string srcAbs = Path.Combine(dir, "src", "chapters.c");
                Directory.CreateDirectory(Path.GetDirectoryName(srcAbs));
                string content =
                    "struct ChapterData gMapChapterData[] = {\n" +
                    "    [0] = { .Weather = 2, .FogLevel = 0 },\n" +
                    "    [1] = { .Weather = 1, .FogLevel = 3 },\n" +
                    "};\n";
                File.WriteAllText(srcAbs, content);
                var mtimeBefore = File.GetLastWriteTimeUtc(srcAbs);

                var proj = ActiveProject(dir, MapSettingsOwner(srcRel));
                var changes = new Dictionary<string, uint> { { "Weather", 1 } }; // already 1
                var res = DecompSourceWriterCore.WriteTableEntry(proj, "map_settings", 1, changes);

                Assert.True(res.Ok, res.Message);
                Assert.False(proj.NeedsRebuild);            // no rebuild for a no-op
                Assert.Empty(res.ChangedFields);
                Assert.Equal(content, File.ReadAllText(srcAbs)); // byte-identical, untouched
                Assert.Equal(mtimeBefore, File.GetLastWriteTimeUtc(srcAbs));
            }
            finally { TryDelete(dir); }
        }

        // =====================================================================
        //  JSON (flat) chapter-setting rewrite
        // =====================================================================

        [Fact]
        public void Chapter_FlatJson_SingleField_RewritesChurnFree()
        {
            string dir = NewTempDir();
            try
            {
                string srcRel = "src/data/map_settings.json";
                string srcAbs = Path.Combine(dir, "src", "data", "map_settings.json");
                Directory.CreateDirectory(Path.GetDirectoryName(srcAbs));
                string content =
                    "[\n" +
                    "  { \"Weather\": 0, \"FogLevel\": 0, \"ChapterNumber\": 0 },\n" +
                    "  { \"Weather\": 1, \"FogLevel\": 3, \"ChapterNumber\": 5 },\n" +
                    "]\n";
                File.WriteAllText(srcAbs, content);

                var proj = ActiveProject(dir, MapSettingsOwner(srcRel, format: "json", arrayName: ""));
                var changes = new Dictionary<string, uint> { { "FogLevel", 7 } };
                var res = DecompSourceWriterCore.WriteTableEntry(proj, "map_settings", 1, changes);

                Assert.True(res.Ok, res.Message);
                Assert.True(proj.NeedsRebuild);
                string after = File.ReadAllText(srcAbs);
                Assert.Equal(content.Replace("\"FogLevel\": 3", "\"FogLevel\": 7"), after);
            }
            finally { TryDelete(dir); }
        }

        // =====================================================================
        //  Pointer / undeclared field — UnsupportedField, NO mutation
        // =====================================================================

        [Fact]
        public void Chapter_UndeclaredPointerField_Reports_UnsupportedField_NoMutation()
        {
            string dir = NewTempDir();
            try
            {
                string srcRel = "src/chapters.c";
                string srcAbs = Path.Combine(dir, "src", "chapters.c");
                Directory.CreateDirectory(Path.GetDirectoryName(srcAbs));
                string content =
                    "struct ChapterData gMapChapterData[] = {\n" +
                    "    [0] = { .Weather = 0 },\n" +
                    "    [1] = { .Weather = 1 },\n" +
                    "};\n";
                File.WriteAllText(srcAbs, content);

                var proj = ActiveProject(dir, MapSettingsOwner(srcRel));
                // EventDataPtr is NOT declared by the owner → UnsupportedField, no write.
                var changes = new Dictionary<string, uint> { { "EventDataPtr", 0x08123456 } };
                var res = DecompSourceWriterCore.WriteTableEntry(proj, "map_settings", 1, changes);

                Assert.False(res.Ok);
                Assert.Equal(DecompSourceWriteStatus.UnsupportedField, res.Status);
                Assert.False(proj.NeedsRebuild);
                Assert.Equal(content, File.ReadAllText(srcAbs)); // untouched
            }
            finally { TryDelete(dir); }
        }

        // =====================================================================
        //  Declared subset OMITS the edited scalar — UnsupportedField, NO mutation
        //  (Copilot PR #1158 re-review: the Avalonia save-gate intercepts this BEFORE
        //   calling the writer and shows an honest unsupported/manual error rather than
        //   a false no-op; this test pins the writer's underlying contract.)
        // =====================================================================

        [Fact]
        public void Chapter_DeclaredSubsetOmitsEditedScalar_Reports_UnsupportedField_NoMutation()
        {
            string dir = NewTempDir();
            try
            {
                string srcRel = "src/chapters.c";
                string srcAbs = Path.Combine(dir, "src", "chapters.c");
                Directory.CreateDirectory(Path.GetDirectoryName(srcAbs));
                string content =
                    "struct ChapterData gMapChapterData[] = {\n" +
                    "    [0] = { .Weather = 0, .FogLevel = 0 },\n" +
                    "    [1] = { .Weather = 1, .FogLevel = 3 },\n" +
                    "};\n";
                File.WriteAllText(srcAbs, content);

                // The owner declares ONLY FogLevel — a Weather edit is not declared.
                var owner = new DecompTableEntry
                {
                    Table = "map_settings", Format = "cstruct", WritePolicy = "source",
                    ArrayName = "gMapChapterData", SourceFile = srcRel, IndexBase = 0,
                    Fields = new List<DecompTableField> { new DecompTableField { Name = "FogLevel" } },
                };
                var proj = ActiveProject(dir, owner);

                var res = DecompSourceWriterCore.WriteTableEntry(
                    proj, "map_settings", 1, new Dictionary<string, uint> { { "Weather", 4 } });

                Assert.False(res.Ok);
                Assert.Equal(DecompSourceWriteStatus.UnsupportedField, res.Status);
                Assert.False(proj.NeedsRebuild);
                Assert.Equal(content, File.ReadAllText(srcAbs)); // untouched
            }
            finally { TryDelete(dir); }
        }

        // =====================================================================
        //  romOnly owner — RomOnly, NO mutation
        // =====================================================================

        [Fact]
        public void Chapter_RomOnlyOwner_Reports_RomOnly_NoMutation()
        {
            string dir = NewTempDir();
            try
            {
                string srcRel = "src/chapters.c";
                string srcAbs = Path.Combine(dir, "src", "chapters.c");
                Directory.CreateDirectory(Path.GetDirectoryName(srcAbs));
                string content = "struct ChapterData gMapChapterData[] = { [0] = { .Weather = 1 } };\n";
                File.WriteAllText(srcAbs, content);

                var proj = ActiveProject(dir, MapSettingsOwner(srcRel, policy: "romOnly"));
                var changes = new Dictionary<string, uint> { { "Weather", 5 } };
                var res = DecompSourceWriterCore.WriteTableEntry(proj, "map_settings", 0, changes);

                Assert.Equal(DecompSourceWriteStatus.RomOnly, res.Status);
                Assert.False(proj.NeedsRebuild);
                Assert.Equal(content, File.ReadAllText(srcAbs));
            }
            finally { TryDelete(dir); }
        }

        // =====================================================================
        //  Nested chapter_settings.json — honest Manual / no silent corruption
        //  (Copilot plan-review finding 1 — documented residual of the JSON writer)
        // =====================================================================

        [Fact]
        public void Chapter_NestedJson_DeepField_NotSilentlyCorrupted()
        {
            string dir = NewTempDir();
            try
            {
                // The REAL chapter_settings.json shape: nested objects, bools, enum strings.
                string srcRel = "src/data/chapter_settings.json";
                string srcAbs = Path.Combine(dir, "src", "data", "chapter_settings.json");
                Directory.CreateDirectory(Path.GetDirectoryName(srcAbs));
                string content =
                    "[\n" +
                    "  {\n" +
                    "    \"map\": { \"obj1Id\": 5, \"obj2Id\": 0 },\n" +
                    "    \"initialWeather\": \"WEATHER_CLEAR\",\n" +
                    "    \"hasPrepScreen\": true,\n" +
                    "    \"Weather\": 1\n" +
                    "  }\n" +
                    "]\n";
                File.WriteAllText(srcAbs, content);

                // A FLAT top-level number ("Weather") IS rewritable even in this file...
                var ownerFlat = MapSettingsOwner(srcRel, format: "json", arrayName: "");
                var proj = ActiveProject(dir, ownerFlat);
                var resFlat = DecompSourceWriterCore.WriteTableEntry(
                    proj, "map_settings", 0, new Dictionary<string, uint> { { "Weather", 2 } });
                Assert.True(resFlat.Ok, resFlat.Message);
                string afterFlat = File.ReadAllText(srcAbs);
                // The nested objects/bools/enums are byte-identical — only the flat number moved.
                Assert.Contains("\"map\": { \"obj1Id\": 5, \"obj2Id\": 0 }", afterFlat);
                Assert.Contains("\"initialWeather\": \"WEATHER_CLEAR\"", afterFlat);
                Assert.Contains("\"hasPrepScreen\": true", afterFlat);
                Assert.Contains("\"Weather\": 2", afterFlat);

                // ...but a NESTED field key the writer cannot reach is honestly reported
                // (NOT a silent corruption): obj1Id lives inside the "map" object, so a
                // top-level lookup returns UnsupportedField with NO mutation.
                string before = File.ReadAllText(srcAbs);
                var ownerNested = new DecompTableEntry
                {
                    Table = "map_settings", Format = "json", WritePolicy = "source",
                    SourceFile = srcRel, IndexBase = 0,
                    Fields = new List<DecompTableField> { new DecompTableField { Name = "obj1Id" } },
                };
                var projNested = ActiveProject(dir, ownerNested);
                var resNested = DecompSourceWriterCore.WriteTableEntry(
                    projNested, "map_settings", 0, new Dictionary<string, uint> { { "obj1Id", 9 } });
                Assert.False(resNested.Ok);
                Assert.Equal(DecompSourceWriteStatus.UnsupportedField, resNested.Status);
                Assert.Equal(before, File.ReadAllText(srcAbs)); // untouched — no corruption
            }
            finally { TryDelete(dir); }
        }

        // =====================================================================
        //  Classic ROM mode (no active project) — NotDecompMode, never touches ROM
        // =====================================================================

        [Fact]
        public void Chapter_NoActiveProject_Reports_NotDecompMode()
        {
            string dir = NewTempDir();
            try
            {
                string srcRel = "src/chapters.c";
                string srcAbs = Path.Combine(dir, "src", "chapters.c");
                Directory.CreateDirectory(Path.GetDirectoryName(srcAbs));
                File.WriteAllText(srcAbs, "struct ChapterData gMapChapterData[] = { [0] = { .Weather = 1 } };\n");

                // Build a project but do NOT make it the active CoreState.DecompProject.
                var detached = ProjectWith(dir, MapSettingsOwner(srcRel));
                var res = DecompSourceWriterCore.WriteTableEntry(
                    detached, "map_settings", 0, new Dictionary<string, uint> { { "Weather", 9 } });
                Assert.Equal(DecompSourceWriteStatus.NotDecompMode, res.Status);
                Assert.False(detached.NeedsRebuild);
            }
            finally { TryDelete(dir); }
        }

        // =====================================================================
        //  Malformed inputs never throw
        // =====================================================================

        [Fact]
        public void Chapter_MalformedSource_NeverThrows()
        {
            string dir = NewTempDir();
            try
            {
                string srcRel = "src/chapters.c";
                string srcAbs = Path.Combine(dir, "src", "chapters.c");
                Directory.CreateDirectory(Path.GetDirectoryName(srcAbs));
                // No array body the symbol can match → ParseFailed, no throw.
                File.WriteAllText(srcAbs, "this is not a c array at all {{{ \n");

                var proj = ActiveProject(dir, MapSettingsOwner(srcRel));
                var res = DecompSourceWriterCore.WriteTableEntry(
                    proj, "map_settings", 0, new Dictionary<string, uint> { { "Weather", 1 } });
                Assert.False(res.Ok);
                Assert.False(proj.NeedsRebuild);
            }
            finally { TryDelete(dir); }
        }

        [Fact]
        public void Chapter_PureRewrite_NullSource_NeverThrows()
        {
            // RewriteEntryText is the PURE preview path — a null source is a clean fail.
            var owner = MapSettingsOwner("src/chapters.c");
            var res = DecompSourceWriterCore.RewriteEntryText(
                null, owner, 0, new Dictionary<string, uint> { { "Weather", 1 } }, out string outText);
            Assert.False(res.Ok);
            Assert.Null(outText);
        }
    }
}
