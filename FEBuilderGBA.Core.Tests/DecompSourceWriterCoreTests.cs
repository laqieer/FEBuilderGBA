using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Unit tests for the source-backed table writer (#1132):
    /// <see cref="DecompSourceWriterCore"/> plus the manifest <c>tables</c> schema
    /// parse on <see cref="DecompManifest"/> / <see cref="DecompProject"/>.
    ///
    /// All fixtures are synthetic temp-dir projects. The class mutates
    /// <see cref="CoreState.DecompProject"/> for the full <c>WriteTableEntry</c> path,
    /// so it joins the shared-state collection and saves/restores that field.
    /// </summary>
    [Collection("SharedState")]
    public class DecompSourceWriterCoreTests : IDisposable
    {
        readonly DecompProject _savedProject;

        public DecompSourceWriterCoreTests()
        {
            _savedProject = CoreState.DecompProject;
            CoreState.DecompProject = null;
        }

        public void Dispose()
        {
            CoreState.DecompProject = null; // ensure no leak into the next test
            CoreState.DecompProject = _savedProject;
        }

        // ---- helpers ----

        static string NewTempDir()
        {
            string d = Path.Combine(Path.GetTempPath(), "decomp_src_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(d);
            return d;
        }

        static DecompManifest ManifestFromJson(string json)
        {
            var opts = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            };
            return JsonSerializer.Deserialize<DecompManifest>(json, opts);
        }

        static DecompTableEntry ItemsOwner(string sourceFile, string policy = "source", string format = "cstruct")
        {
            return new DecompTableEntry
            {
                Table = "items",
                Format = format,
                WritePolicy = policy,
                ArrayName = "gItemData",
                SourceFile = sourceFile,
                Fields = new List<DecompTableField>
                {
                    new DecompTableField { Name = "nameId" },
                    new DecompTableField { Name = "might" },
                    new DecompTableField { Name = "hitRate" },
                    new DecompTableField { Name = "maxUses" },
                },
            };
        }

        static DecompProject ProjectWith(string root, params DecompTableEntry[] owners)
        {
            var man = new DecompManifest();
            // Inject the owners via the parsed Tables array so TablesList sees them.
            var arr = JsonSerializer.SerializeToElement(owners);
            man.Tables = arr;
            return new DecompProject { ProjectRoot = root, Manifest = man };
        }

        // =====================================================================
        //  Manifest tables parse
        // =====================================================================

        [Fact]
        public void TablesList_ArrayForm_Parses()
        {
            var man = ManifestFromJson(@"{
              ""schemaVersion"": 1,
              ""tables"": [
                { ""table"": ""items"", ""format"": ""cstruct"", ""writePolicy"": ""source"",
                  ""arrayName"": ""gItemData"", ""sourceFile"": ""src/item.c"",
                  ""fields"": [ { ""name"": ""might"" }, { ""name"": ""hitRate"" } ] }
              ]
            }");
            Assert.NotNull(man);
            Assert.Single(man.TablesList);
            Assert.Equal("items", man.TablesList[0].Table);
            Assert.Equal("gItemData", man.TablesList[0].EffectiveSymbol);
            Assert.Equal(2, man.TablesList[0].Fields.Count);
        }

        [Fact]
        public void TablesList_ObjectMapForm_InjectsKeyAsTable()
        {
            var man = ManifestFromJson(@"{
              ""tables"": {
                ""items"": { ""format"": ""cstruct"", ""writePolicy"": ""source"",
                  ""symbol"": ""gItemData"", ""sourceFile"": ""src/item.c"" },
                ""classes"": { ""writePolicy"": ""romOnly"" }
              }
            }");
            Assert.NotNull(man);
            Assert.Equal(2, man.TablesList.Count);
            Assert.Contains(man.TablesList, e => e.Table == "items" && e.EffectiveSymbol == "gItemData");
            Assert.Contains(man.TablesList, e => e.Table == "classes" && e.WritePolicy == "romOnly");
        }

        [Fact]
        public void TablesList_UnknownKeys_Tolerated()
        {
            var man = ManifestFromJson(@"{
              ""tables"": [
                { ""table"": ""items"", ""futureKey"": 42, ""nested"": { ""x"": 1 },
                  ""fields"": [ { ""name"": ""might"", ""enumMap"": { ""A"": 1 } } ] }
              ]
            }");
            Assert.NotNull(man);
            Assert.Single(man.TablesList);
            Assert.NotNull(man.TablesList[0].Extra);
            Assert.True(man.TablesList[0].Extra.ContainsKey("futureKey"));
            Assert.NotNull(man.TablesList[0].Fields[0].Extra);
        }

        [Fact]
        public void TablesList_Malformed_EmptyNoThrow()
        {
            // tables is a scalar (string) → not array/object → empty list, no throw.
            var man = ManifestFromJson(@"{ ""tables"": ""nonsense"" }");
            Assert.NotNull(man);
            Assert.Empty(man.TablesList);
        }

        [Fact]
        public void TablesList_Absent_Empty()
        {
            var man = ManifestFromJson(@"{ ""schemaVersion"": 1 }");
            Assert.NotNull(man);
            Assert.Empty(man.TablesList);
        }

        [Fact]
        public void TryGetTableOwner_CaseInsensitive_AndMiss()
        {
            var man = ManifestFromJson(@"{ ""tables"": [ { ""table"": ""Items"" } ] }");
            var proj = new DecompProject { Manifest = man };
            Assert.NotNull(proj.TryGetTableOwner("items"));
            Assert.NotNull(proj.TryGetTableOwner("ITEMS"));
            Assert.Null(proj.TryGetTableOwner("classes"));
            Assert.Null(proj.TryGetTableOwner(null));
        }

        [Fact]
        public void TryGetTableOwner_NoManifest_Null()
        {
            var proj = new DecompProject();
            Assert.Null(proj.TryGetTableOwner("items"));
        }

        // =====================================================================
        //  RewriteEntryText (pure)
        // =====================================================================

        [Fact]
        public void Rewrite_Designated_OnlyTokenChanges_RestIdentical()
        {
            string src =
                "const struct Item gItemData[] = {\n" +
                "    [0] = { .nameId = 1, .might = 5, .hitRate = 90 },\n" +
                "    [1] = { .nameId = 2, .might = 8, .hitRate = 75 },\n" +
                "};\n";
            var owner = ItemsOwner("src/item.c");
            var changes = new Dictionary<string, uint> { { "might", 10 } };

            var res = DecompSourceWriterCore.RewriteEntryText(src, owner, 1, changes, out string outText);
            Assert.True(res.Ok, res.Message);
            // Only "8" → "10" in entry 1; entry 0 unchanged.
            Assert.Contains(".might = 10", outText);
            Assert.Contains("[0] = { .nameId = 1, .might = 5, .hitRate = 90 }", outText);
            // Exactly one difference region: entry 0 line byte-identical.
            Assert.Equal(src.Replace(".might = 8", ".might = 10"), outText);
        }

        [Fact]
        public void Rewrite_Positional_ManifestOrderTokenChanges()
        {
            // Element body is purely positional: nameId, might, hitRate, maxUses.
            string src =
                "Item gItemData[] = {\n" +
                "    { 1, 5, 90, 40 },\n" +
                "    { 2, 8, 75, 30 },\n" +
                "};\n";
            var owner = ItemsOwner("src/item.c");
            var changes = new Dictionary<string, uint> { { "hitRate", 99 } }; // index 2

            var res = DecompSourceWriterCore.RewriteEntryText(src, owner, 1, changes, out string outText);
            Assert.True(res.Ok, res.Message);
            Assert.Contains("{ 2, 8, 99, 30 }", outText);
            Assert.Contains("{ 1, 5, 90, 40 }", outText); // entry 0 intact
        }

        [Fact]
        public void Rewrite_CrlfFile_PreservesCrlf()
        {
            string src =
                "Item gItemData[] = {\r\n" +
                "    [0] = { .might = 5 },\r\n" +
                "    [1] = { .might = 8 },\r\n" +
                "};\r\n";
            var owner = ItemsOwner("src/item.c");
            var changes = new Dictionary<string, uint> { { "might", 10 } };

            int crlfBefore = CountSubstr(src, "\r\n");
            var res = DecompSourceWriterCore.RewriteEntryText(src, owner, 1, changes, out string outText);
            Assert.True(res.Ok, res.Message);
            Assert.Equal(crlfBefore, CountSubstr(outText, "\r\n"));
            // No lone LF introduced.
            Assert.Equal(crlfBefore, CountSubstr(outText, "\n"));
            Assert.Contains(".might = 10", outText);
        }

        [Fact]
        public void Rewrite_PreservesTrailingAndLineComments()
        {
            string src =
                "Item gItemData[] = {\n" +
                "    [0] = { .might = 5 /* iron */ }, // first item\n" +
                "    [1] = { .might = 8 /* steel */ }, // second item\n" +
                "};\n";
            var owner = ItemsOwner("src/item.c");
            var changes = new Dictionary<string, uint> { { "might", 12 } };

            var res = DecompSourceWriterCore.RewriteEntryText(src, owner, 1, changes, out string outText);
            Assert.True(res.Ok, res.Message);
            Assert.Contains(".might = 12 /* steel */", outText);
            Assert.Contains("// second item", outText);
            Assert.Contains("// first item", outText);
            Assert.Contains(".might = 5 /* iron */", outText); // entry 0 intact
        }

        [Fact]
        public void Rewrite_MacroValueToken_UnsupportedField_NoChange()
        {
            string src =
                "Item gItemData[] = {\n" +
                "    [0] = { .might = IRON_MIGHT },\n" +
                "};\n";
            var owner = ItemsOwner("src/item.c");
            var changes = new Dictionary<string, uint> { { "might", 10 } };

            var res = DecompSourceWriterCore.RewriteEntryText(src, owner, 0, changes, out string outText);
            Assert.False(res.Ok);
            Assert.True(res.Status == DecompSourceWriteStatus.UnsupportedField
                     || res.Status == DecompSourceWriteStatus.Manual);
            Assert.Equal(src, outText); // unchanged
        }

        [Fact]
        public void Rewrite_HexValuePreservesRadix()
        {
            string src =
                "Item gItemData[] = {\n" +
                "    [0] = { .might = 0x05 },\n" +
                "};\n";
            var owner = ItemsOwner("src/item.c");
            var changes = new Dictionary<string, uint> { { "might", 0x1F } };

            var res = DecompSourceWriterCore.RewriteEntryText(src, owner, 0, changes, out string outText);
            Assert.True(res.Ok, res.Message);
            Assert.Contains(".might = 0x1F", outText);
        }

        [Fact]
        public void Rewrite_BracesInCommentsAndStrings_ElementBoundariesCorrect()
        {
            // Entry 0 contains a string with a brace and a comment with a brace; the
            // parser must not be fooled into ending the element early.
            string src =
                "Item gItemData[] = {\n" +
                "    [0] = { .nameId = 1, .name = \"a}b{c\", /* } weird { */ .might = 5 },\n" +
                "    [1] = { .nameId = 2, .might = 8 },\n" +
                "};\n";
            // Owner must declare 'name' too for the validate step to pass when changing might only.
            var owner = ItemsOwner("src/item.c");
            owner.Fields.Add(new DecompTableField { Name = "name" });
            var changes = new Dictionary<string, uint> { { "might", 9 } };

            var res = DecompSourceWriterCore.RewriteEntryText(src, owner, 1, changes, out string outText);
            Assert.True(res.Ok, res.Message);
            Assert.Contains("[1] = { .nameId = 2, .might = 9 }", outText);
            // Entry 0 (with the tricky braces) untouched.
            Assert.Contains(".name = \"a}b{c\", /* } weird { */ .might = 5", outText);
        }

        [Fact]
        public void Rewrite_MultiEntry_OtherEntriesByteIdentical()
        {
            string src =
                "Item gItemData[] = {\n" +
                "    [0] = { .might = 5 },\n" +
                "    [1] = { .might = 8 },\n" +
                "    [2] = { .might = 11 },\n" +
                "};\n";
            var owner = ItemsOwner("src/item.c");
            var changes = new Dictionary<string, uint> { { "might", 99 } };

            var res = DecompSourceWriterCore.RewriteEntryText(src, owner, 1, changes, out string outText);
            Assert.True(res.Ok, res.Message);
            Assert.Equal(src.Replace(".might = 8", ".might = 99"), outText);
        }

        [Fact]
        public void Rewrite_EntryOutOfRange_ParseFailed()
        {
            string src = "Item gItemData[] = { [0] = { .might = 5 } };\n";
            var owner = ItemsOwner("src/item.c");
            var changes = new Dictionary<string, uint> { { "might", 9 } };

            var res = DecompSourceWriterCore.RewriteEntryText(src, owner, 7, changes, out string outText);
            Assert.False(res.Ok);
            Assert.Equal(DecompSourceWriteStatus.ParseFailed, res.Status);
            Assert.Equal(src, outText);
        }

        [Fact]
        public void Rewrite_UndeclaredField_UnsupportedField()
        {
            string src = "Item gItemData[] = { [0] = { .might = 5 } };\n";
            var owner = ItemsOwner("src/item.c");
            var changes = new Dictionary<string, uint> { { "bogus", 9 } };

            var res = DecompSourceWriterCore.RewriteEntryText(src, owner, 0, changes, out string outText);
            Assert.False(res.Ok);
            Assert.Equal(DecompSourceWriteStatus.UnsupportedField, res.Status);
            Assert.Equal(src, outText);
        }

        // =====================================================================
        //  WriteTableEntry (full — sets CoreState.DecompProject)
        // =====================================================================

        [Fact]
        public void Write_Success_SetsNeedsRebuild_AndChangesFile()
        {
            string dir = NewTempDir();
            try
            {
                string srcRel = "src/item.c";
                string srcAbs = Path.Combine(dir, "src", "item.c");
                Directory.CreateDirectory(Path.GetDirectoryName(srcAbs));
                string content =
                    "Item gItemData[] = {\n" +
                    "    [0] = { .might = 5 },\n" +
                    "    [1] = { .might = 8 },\n" +
                    "};\n";
                File.WriteAllText(srcAbs, content);

                var proj = ProjectWith(dir, ItemsOwner(srcRel));
                CoreState.DecompProject = proj;
                Assert.False(proj.NeedsRebuild);

                var changes = new Dictionary<string, uint> { { "might", 10 } };
                var res = DecompSourceWriterCore.WriteTableEntry(proj, "items", 1, changes);

                Assert.True(res.Ok, res.Message);
                Assert.True(proj.NeedsRebuild);
                string after = File.ReadAllText(srcAbs);
                Assert.Equal(content.Replace(".might = 8", ".might = 10"), after);
            }
            finally { TryDelete(dir); }
        }

        [Fact]
        public void Write_RomOnly_NoWrite()
        {
            string dir = NewTempDir();
            try
            {
                string srcAbs = Path.Combine(dir, "item.c");
                string content = "Item gItemData[] = { [0] = { .might = 5 } };\n";
                File.WriteAllText(srcAbs, content);
                var proj = ProjectWith(dir, ItemsOwner("item.c", policy: "romOnly"));
                CoreState.DecompProject = proj;

                var res = DecompSourceWriterCore.WriteTableEntry(proj, "items", 0,
                    new Dictionary<string, uint> { { "might", 10 } });

                Assert.Equal(DecompSourceWriteStatus.RomOnly, res.Status);
                Assert.False(proj.NeedsRebuild);
                Assert.Equal(content, File.ReadAllText(srcAbs)); // untouched
            }
            finally { TryDelete(dir); }
        }

        [Fact]
        public void Write_Manual_NoWrite()
        {
            string dir = NewTempDir();
            try
            {
                string srcAbs = Path.Combine(dir, "item.c");
                File.WriteAllText(srcAbs, "Item gItemData[] = { [0] = { .might = 5 } };\n");
                var proj = ProjectWith(dir, ItemsOwner("item.c", policy: "manual"));
                CoreState.DecompProject = proj;

                var res = DecompSourceWriterCore.WriteTableEntry(proj, "items", 0,
                    new Dictionary<string, uint> { { "might", 10 } });

                Assert.Equal(DecompSourceWriteStatus.Manual, res.Status);
                Assert.False(proj.NeedsRebuild);
            }
            finally { TryDelete(dir); }
        }

        [Fact]
        public void Write_NotOwned_WhenTableMissing()
        {
            string dir = NewTempDir();
            try
            {
                var proj = ProjectWith(dir, ItemsOwner("item.c"));
                CoreState.DecompProject = proj;

                var res = DecompSourceWriterCore.WriteTableEntry(proj, "classes", 0,
                    new Dictionary<string, uint> { { "hp", 1 } });
                Assert.Equal(DecompSourceWriteStatus.NotOwned, res.Status);
                Assert.False(proj.NeedsRebuild);
            }
            finally { TryDelete(dir); }
        }

        [Fact]
        public void Write_UnsupportedField_NoWrite()
        {
            string dir = NewTempDir();
            try
            {
                string srcAbs = Path.Combine(dir, "item.c");
                string content = "Item gItemData[] = { [0] = { .might = 5 } };\n";
                File.WriteAllText(srcAbs, content);
                var proj = ProjectWith(dir, ItemsOwner("item.c"));
                CoreState.DecompProject = proj;

                var res = DecompSourceWriterCore.WriteTableEntry(proj, "items", 0,
                    new Dictionary<string, uint> { { "undeclaredField", 10 } });

                Assert.Equal(DecompSourceWriteStatus.UnsupportedField, res.Status);
                Assert.False(proj.NeedsRebuild);
                Assert.Equal(content, File.ReadAllText(srcAbs));
            }
            finally { TryDelete(dir); }
        }

        // #1141: JSON format is now implemented (was Manual in #1132). The full path
        // through WriteTableEntry rewrites a JSON-backed owner's Number token.
        [Fact]
        public void Write_JsonFormat_RewritesNumberToken()
        {
            string dir = NewTempDir();
            try
            {
                string srcAbs = Path.Combine(dir, "item.json");
                string content = "[ { \"might\": 5 } ]\n";
                File.WriteAllText(srcAbs, content);

                var proj = ProjectWith(dir, ItemsOwner("item.json", format: "json"));
                CoreState.DecompProject = proj;

                var res = DecompSourceWriterCore.WriteTableEntry(proj, "items", 0,
                    new Dictionary<string, uint> { { "might", 10 } });
                Assert.True(res.Ok, res.Message);
                Assert.True(proj.NeedsRebuild);
                Assert.Equal(content.Replace("\"might\": 5", "\"might\": 10"), File.ReadAllText(srcAbs));
            }
            finally { TryDelete(dir); }
        }

        [Fact]
        public void Write_AbsoluteSourceFile_Rejected_NoWrite()
        {
            string dir = NewTempDir();
            try
            {
                // An absolute sourceFile path must be rejected by the containment check.
                string abs = Path.Combine(Path.GetTempPath(), "outside_" + Guid.NewGuid().ToString("N") + ".c");
                File.WriteAllText(abs, "Item gItemData[] = { [0] = { .might = 5 } };\n");
                var proj = ProjectWith(dir, ItemsOwner(abs));
                CoreState.DecompProject = proj;

                var res = DecompSourceWriterCore.WriteTableEntry(proj, "items", 0,
                    new Dictionary<string, uint> { { "might", 10 } });

                Assert.Equal(DecompSourceWriteStatus.Rejected, res.Status);
                Assert.False(proj.NeedsRebuild);
                // outside file untouched
                Assert.Contains(".might = 5", File.ReadAllText(abs));
                TryDelete(abs);
            }
            finally { TryDelete(dir); }
        }

        [Fact]
        public void Write_DotDotEscapeSourceFile_Rejected()
        {
            string dir = NewTempDir();
            try
            {
                var proj = ProjectWith(dir, ItemsOwner("../escape.c"));
                CoreState.DecompProject = proj;

                var res = DecompSourceWriterCore.WriteTableEntry(proj, "items", 0,
                    new Dictionary<string, uint> { { "might", 10 } });
                Assert.Equal(DecompSourceWriteStatus.Rejected, res.Status);
                Assert.False(proj.NeedsRebuild);
            }
            finally { TryDelete(dir); }
        }

        [Fact]
        public void Write_SourceFileMissing_SourceNotFound()
        {
            string dir = NewTempDir();
            try
            {
                var proj = ProjectWith(dir, ItemsOwner("does-not-exist.c"));
                CoreState.DecompProject = proj;

                var res = DecompSourceWriterCore.WriteTableEntry(proj, "items", 0,
                    new Dictionary<string, uint> { { "might", 10 } });
                Assert.Equal(DecompSourceWriteStatus.SourceNotFound, res.Status);
                Assert.False(proj.NeedsRebuild);
            }
            finally { TryDelete(dir); }
        }

        [Fact]
        public void Write_NotDecompMode_WhenProjectNotActive()
        {
            string dir = NewTempDir();
            try
            {
                string srcAbs = Path.Combine(dir, "item.c");
                string content = "Item gItemData[] = { [0] = { .might = 5 } };\n";
                File.WriteAllText(srcAbs, content);
                var proj = ProjectWith(dir, ItemsOwner("item.c"));
                // CoreState.DecompProject is null (classic mode) — write must be a no-op.
                CoreState.DecompProject = null;

                var res = DecompSourceWriterCore.WriteTableEntry(proj, "items", 0,
                    new Dictionary<string, uint> { { "might", 10 } });
                Assert.Equal(DecompSourceWriteStatus.NotDecompMode, res.Status);
                Assert.False(proj.NeedsRebuild);
                Assert.Equal(content, File.ReadAllText(srcAbs));
            }
            finally { TryDelete(dir); }
        }

        [Fact]
        public void Write_MalformedManifestTable_NoThrow_NoWrite()
        {
            string dir = NewTempDir();
            try
            {
                // tables is a scalar → TablesList empty → owner null → NotOwned (no throw).
                var man = ManifestFromJson(@"{ ""tables"": 12345 }");
                var proj = new DecompProject { ProjectRoot = dir, Manifest = man };
                CoreState.DecompProject = proj;

                var res = DecompSourceWriterCore.WriteTableEntry(proj, "items", 0,
                    new Dictionary<string, uint> { { "might", 10 } });
                Assert.True(res.Status == DecompSourceWriteStatus.NotOwned
                         || res.Status == DecompSourceWriteStatus.MalformedManifest);
                Assert.False(proj.NeedsRebuild);
            }
            finally { TryDelete(dir); }
        }

        [Fact]
        public void Write_NullProject_NotDecompMode_NoThrow()
        {
            var res = DecompSourceWriterCore.WriteTableEntry(null, "items", 0,
                new Dictionary<string, uint> { { "might", 10 } });
            Assert.Equal(DecompSourceWriteStatus.NotDecompMode, res.Status);
        }

        // =====================================================================
        //  #1132 review fixes: indexBase, no-op/no-churn, macro skip-vs-fail
        // =====================================================================

        // Finding 1 — owner.IndexBase is honored (manifest ids are translated to a
        // 0-based element index).
        [Fact]
        public void Rewrite_IndexBase1_EditId1_RewritesFirstElement()
        {
            string src =
                "Item gItemData[] = {\n" +
                "    [0] = { .might = 5 },\n" +
                "    [1] = { .might = 8 },\n" +
                "};\n";
            var owner = ItemsOwner("src/item.c");
            owner.IndexBase = 1;   // ids are 1-based
            var changes = new Dictionary<string, uint> { { "might", 10 } };

            // id 1 with base 1 → element index 0 (the FIRST element).
            var res = DecompSourceWriterCore.RewriteEntryText(src, owner, 1, changes, out string outText);
            Assert.True(res.Ok, res.Message);
            Assert.Equal(src.Replace("[0] = { .might = 5 }", "[0] = { .might = 10 }"), outText);
        }

        // Finding 1 — an id below the declared base is a ParseFailed (no write).
        [Fact]
        public void Rewrite_IndexBase1_EditId0_BelowBase_ParseFailed()
        {
            string src = "Item gItemData[] = { [0] = { .might = 5 } };\n";
            var owner = ItemsOwner("src/item.c");
            owner.IndexBase = 1;
            var res = DecompSourceWriterCore.RewriteEntryText(src, owner, 0,
                new Dictionary<string, uint> { { "might", 10 } }, out string outText);
            Assert.Equal(DecompSourceWriteStatus.ParseFailed, res.Status);
            Assert.Equal(src, outText);   // unchanged
        }

        // Finding 2 — an empty change-set writes NOTHING and never flags rebuild.
        [Fact]
        public void Write_EmptyChangeSet_NoWrite_NoRebuild()
        {
            string dir = NewTempDir();
            try
            {
                string srcAbs = Path.Combine(dir, "item.c");
                string content = "Item gItemData[] = { [0] = { .might = 5 } };\n";
                File.WriteAllText(srcAbs, content);
                var before = File.GetLastWriteTimeUtc(srcAbs);

                var proj = ProjectWith(dir, ItemsOwner("item.c"));
                CoreState.DecompProject = proj;

                var res = DecompSourceWriterCore.WriteTableEntry(proj, "items", 0,
                    new Dictionary<string, uint>());

                Assert.True(res.Ok, res.Message);
                Assert.False(proj.NeedsRebuild);
                Assert.Equal(content, File.ReadAllText(srcAbs));     // byte-identical
                Assert.Equal(before, File.GetLastWriteTimeUtc(srcAbs)); // not even touched
            }
            finally { TryDelete(dir); }
        }

        // Finding 2 — a value already equal to the source token is a no-op: no churn,
        // no rebuild, file untouched.
        [Fact]
        public void Write_ValueAlreadyEqual_NoChurn_NoRebuild()
        {
            string dir = NewTempDir();
            try
            {
                string srcAbs = Path.Combine(dir, "item.c");
                string content = "Item gItemData[] = { [0] = { .might = 5 } };\n";
                File.WriteAllText(srcAbs, content);
                var before = File.GetLastWriteTimeUtc(srcAbs);

                var proj = ProjectWith(dir, ItemsOwner("item.c"));
                CoreState.DecompProject = proj;

                // .might is already 5 → requesting 5 is a no-op.
                var res = DecompSourceWriterCore.WriteTableEntry(proj, "items", 0,
                    new Dictionary<string, uint> { { "might", 5 } });

                Assert.True(res.Ok, res.Message);
                Assert.Empty(res.ChangedFields);
                Assert.False(proj.NeedsRebuild);
                Assert.Equal(content, File.ReadAllText(srcAbs));      // byte-identical
                Assert.Equal(before, File.GetLastWriteTimeUtc(srcAbs)); // not touched
            }
            finally { TryDelete(dir); }
        }

        // Finding 2/3 — hex no-op: 0x05 token requested as 5 is recognized as a no-op
        // and the original radix/formatting is left exactly as written.
        [Fact]
        public void Rewrite_HexNoOp_LeavesTokenUntouched()
        {
            string src = "Item gItemData[] = { [0] = { .might = 0x05 } };\n";
            var owner = ItemsOwner("src/item.c");
            var res = DecompSourceWriterCore.RewriteEntryText(src, owner, 0,
                new Dictionary<string, uint> { { "might", 5 } }, out string outText);
            Assert.True(res.Ok, res.Message);
            Assert.Empty(res.ChangedFields);
            Assert.Equal(src, outText);   // 0x05 preserved verbatim
        }

        // Finding 3 — in a BULK (multi-field) change-set, a macro-valued field is
        // SKIPPED (not rewritten, not a failure) while an integer field is rewritten.
        [Fact]
        public void Rewrite_BulkSet_MacroFieldSkipped_IntFieldChanged()
        {
            string src =
                "Item gItemData[] = {\n" +
                "    [0] = { .might = SOME_MACRO, .hitRate = 90 },\n" +
                "};\n";
            var owner = ItemsOwner("src/item.c");
            // Two fields → bulk intent. might is a macro (skip); hitRate is an int (change).
            var changes = new Dictionary<string, uint> { { "might", 7 }, { "hitRate", 99 } };

            var res = DecompSourceWriterCore.RewriteEntryText(src, owner, 0, changes, out string outText);
            Assert.True(res.Ok, res.Message);
            Assert.Contains(".might = SOME_MACRO", outText);  // macro untouched
            Assert.Contains(".hitRate = 99", outText);        // int rewritten
            Assert.Contains("hitRate", res.ChangedFields);
            Assert.DoesNotContain("might", res.ChangedFields);
        }

        // Finding 3 — a SINGLE-field change-set targeting a macro field is an honest
        // hard failure (UnsupportedField), no write.
        [Fact]
        public void Rewrite_SingleField_MacroTarget_UnsupportedField()
        {
            string src = "Item gItemData[] = { [0] = { .might = SOME_MACRO } };\n";
            var owner = ItemsOwner("src/item.c");
            var res = DecompSourceWriterCore.RewriteEntryText(src, owner, 0,
                new Dictionary<string, uint> { { "might", 7 } }, out string outText);
            Assert.Equal(DecompSourceWriteStatus.UnsupportedField, res.Status);
            Assert.Equal(src, outText);   // untouched
        }

        // =====================================================================
        //  #1141 — JSON-backed writer
        // =====================================================================

        static DecompTableEntry JsonItemsOwner(string sourceFile, int? indexBase = null)
        {
            return new DecompTableEntry
            {
                Table = "items",
                Format = "json",
                WritePolicy = "source",
                SourceFile = sourceFile,
                IndexBase = indexBase,
                Fields = new List<DecompTableField>
                {
                    new DecompTableField { Name = "nameId" },
                    new DecompTableField { Name = "might" },
                    new DecompTableField { Name = "hitRate" },
                    new DecompTableField { Name = "name" },
                },
            };
        }

        [Fact]
        public void Json_Array_HappyPath_OnlyTokenChanges()
        {
            string src =
                "[\n" +
                "  { \"nameId\": 1, \"might\": 5, \"hitRate\": 90 },\n" +
                "  { \"nameId\": 2, \"might\": 8, \"hitRate\": 75 }\n" +
                "]\n";
            var owner = JsonItemsOwner("data/items.json");
            var res = DecompSourceWriterCore.RewriteJsonEntryText(src, owner, 1,
                new Dictionary<string, uint> { { "might", 10 } }, out string outText);
            Assert.True(res.Ok, res.Message);
            // Only entry 1's might 8 → 10. Entry 0 + every other byte identical.
            Assert.Equal(src.Replace("\"might\": 8", "\"might\": 10"), outText);
            Assert.Contains("might", res.ChangedFields);
        }

        [Fact]
        public void Json_ObjectMap_KeyLookup()
        {
            string src =
                "{\n" +
                "  \"0\": { \"might\": 5 },\n" +
                "  \"1\": { \"might\": 8 }\n" +
                "}\n";
            var owner = JsonItemsOwner("data/items.json");
            var res = DecompSourceWriterCore.RewriteJsonEntryText(src, owner, 1,
                new Dictionary<string, uint> { { "might", 99 } }, out string outText);
            Assert.True(res.Ok, res.Message);
            Assert.Equal(src.Replace("\"might\": 8", "\"might\": 99"), outText);
        }

        [Fact]
        public void Json_NoOp_ValueAlreadyEqual_NoChange()
        {
            string src = "[ { \"might\": 5 } ]\n";
            var owner = JsonItemsOwner("data/items.json");
            var res = DecompSourceWriterCore.RewriteJsonEntryText(src, owner, 0,
                new Dictionary<string, uint> { { "might", 5 } }, out string outText);
            Assert.True(res.Ok, res.Message);
            Assert.Empty(res.ChangedFields);
            Assert.Equal(src, outText);   // byte-identical
        }

        [Fact]
        public void Json_NonNumber_SingleField_UnsupportedField()
        {
            string src = "[ { \"name\": \"Iron Sword\", \"might\": 5 } ]\n";
            var owner = JsonItemsOwner("data/items.json");
            var res = DecompSourceWriterCore.RewriteJsonEntryText(src, owner, 0,
                new Dictionary<string, uint> { { "name", 7 } }, out string outText);
            Assert.False(res.Ok);
            Assert.Equal(DecompSourceWriteStatus.UnsupportedField, res.Status);
            Assert.Equal(src, outText);
        }

        [Fact]
        public void Json_NonNumber_Bulk_Skipped_OtherFieldWritten()
        {
            string src = "[ { \"name\": \"Iron Sword\", \"might\": 5 } ]\n";
            var owner = JsonItemsOwner("data/items.json");
            // bulk: name (non-number → skip) + might (number → change)
            var res = DecompSourceWriterCore.RewriteJsonEntryText(src, owner, 0,
                new Dictionary<string, uint> { { "name", 7 }, { "might", 9 } }, out string outText);
            Assert.True(res.Ok, res.Message);
            Assert.Contains("\"name\": \"Iron Sword\"", outText);  // string untouched
            Assert.Contains("\"might\": 9", outText);
            Assert.Contains("might", res.ChangedFields);
            Assert.DoesNotContain("name", res.ChangedFields);
        }

        [Fact]
        public void Json_Malformed_ParseFailed_NoThrow()
        {
            string src = "{ this is not valid json ";
            var owner = JsonItemsOwner("data/items.json");
            var res = DecompSourceWriterCore.RewriteJsonEntryText(src, owner, 0,
                new Dictionary<string, uint> { { "might", 9 } }, out string outText);
            Assert.False(res.Ok);
            Assert.Equal(DecompSourceWriteStatus.ParseFailed, res.Status);
            Assert.Equal(src, outText);   // untouched
        }

        [Fact]
        public void Json_CrlfAndNonAsciiBeforeToken_Preserved()
        {
            // Non-ASCII (multi-byte UTF-8) text before the token must not corrupt offsets.
            string src =
                "[\r\n" +
                "  { \"name\": \"アイテム\", \"might\": 8 }\r\n" +
                "]\r\n";
            var owner = JsonItemsOwner("data/items.json");
            int crlfBefore = CountSubstr(src, "\r\n");
            var res = DecompSourceWriterCore.RewriteJsonEntryText(src, owner, 0,
                new Dictionary<string, uint> { { "might", 12 } }, out string outText);
            Assert.True(res.Ok, res.Message);
            Assert.Equal(crlfBefore, CountSubstr(outText, "\r\n"));
            Assert.Contains("\"might\": 12", outText);
            Assert.Contains("アイテム", outText);   // JP text intact
        }

        [Fact]
        public void Json_CommentsAndTrailingComma_Tolerated()
        {
            string src =
                "[\n" +
                "  // first item\n" +
                "  { \"might\": 5, },\n" +
                "  { \"might\": 8, },\n" +
                "]\n";
            var owner = JsonItemsOwner("data/items.json");
            var res = DecompSourceWriterCore.RewriteJsonEntryText(src, owner, 1,
                new Dictionary<string, uint> { { "might", 10 } }, out string outText);
            Assert.True(res.Ok, res.Message);
            Assert.Equal(src.Replace("{ \"might\": 8, }", "{ \"might\": 10, }"), outText);
            Assert.Contains("// first item", outText);
        }

        [Fact]
        public void Json_IndexBase1_EditId1_RewritesFirstElement()
        {
            string src = "[ { \"might\": 5 }, { \"might\": 8 } ]\n";
            var owner = JsonItemsOwner("data/items.json", indexBase: 1);
            var res = DecompSourceWriterCore.RewriteJsonEntryText(src, owner, 1,
                new Dictionary<string, uint> { { "might", 10 } }, out string outText);
            Assert.True(res.Ok, res.Message);
            Assert.Contains("{ \"might\": 10 }, { \"might\": 8 }", outText);
        }

        [Fact]
        public void Json_MissingIndex_ParseFailed()
        {
            string src = "[ { \"might\": 5 } ]\n";
            var owner = JsonItemsOwner("data/items.json");
            var res = DecompSourceWriterCore.RewriteJsonEntryText(src, owner, 7,
                new Dictionary<string, uint> { { "might", 9 } }, out string outText);
            Assert.False(res.Ok);
            Assert.Equal(DecompSourceWriteStatus.ParseFailed, res.Status);
        }

        [Fact]
        public void Json_UndeclaredField_UnsupportedField()
        {
            string src = "[ { \"might\": 5 } ]\n";
            var owner = JsonItemsOwner("data/items.json");
            var res = DecompSourceWriterCore.RewriteJsonEntryText(src, owner, 0,
                new Dictionary<string, uint> { { "bogus", 9 } }, out string outText);
            Assert.False(res.Ok);
            Assert.Equal(DecompSourceWriteStatus.UnsupportedField, res.Status);
            Assert.Equal(src, outText);
        }

        [Fact]
        public void Json_Signed_NegativeValue_EmitsMinusN()
        {
            // promoHp is a signed int8 field; the caller packs -1 as 0xFF (255).
            string src = "[ { \"promoHp\": 2 } ]\n";
            var owner = new DecompTableEntry
            {
                Table = "classes", Format = "json", WritePolicy = "source",
                SourceFile = "data/classes.json",
                Fields = new List<DecompTableField>
                { new DecompTableField { Name = "promoHp", Signed = true, Width = 1 } },
            };
            var res = DecompSourceWriterCore.RewriteJsonEntryText(src, owner, 0,
                new Dictionary<string, uint> { { "promoHp", Pack(-1) } }, out string outText);
            Assert.True(res.Ok, res.Message);
            Assert.Contains("\"promoHp\": -1", outText);
        }

        [Fact]
        public void Json_Signed_NoOp_NegativeToken_RecognizedEqual()
        {
            string src = "[ { \"promoHp\": -1 } ]\n";
            var owner = new DecompTableEntry
            {
                Table = "classes", Format = "json", WritePolicy = "source",
                SourceFile = "data/classes.json",
                Fields = new List<DecompTableField>
                { new DecompTableField { Name = "promoHp", Signed = true, Width = 1 } },
            };
            // 0xFF byte == -1 (int8) → no-op.
            var res = DecompSourceWriterCore.RewriteJsonEntryText(src, owner, 0,
                new Dictionary<string, uint> { { "promoHp", 0xFF } }, out string outText);
            Assert.True(res.Ok, res.Message);
            Assert.Empty(res.ChangedFields);
            Assert.Equal(src, outText);
        }

        [Fact]
        public void Json_Write_FullPath_SetsNeedsRebuild_NoOpUntouchesFile()
        {
            string dir = NewTempDir();
            try
            {
                string srcRel = "data/items.json";
                string srcAbs = Path.Combine(dir, "data", "items.json");
                Directory.CreateDirectory(Path.GetDirectoryName(srcAbs));
                string content = "[ { \"might\": 5 }, { \"might\": 8 } ]\n";
                File.WriteAllText(srcAbs, content);

                var proj = ProjectWith(dir, JsonItemsOwner(srcRel));
                CoreState.DecompProject = proj;

                // Change → file rewritten, NeedsRebuild set.
                var res = DecompSourceWriterCore.WriteTableEntry(proj, "items", 1,
                    new Dictionary<string, uint> { { "might", 10 } });
                Assert.True(res.Ok, res.Message);
                Assert.True(proj.NeedsRebuild);
                Assert.Equal(content.Replace("\"might\": 8", "\"might\": 10"),
                    File.ReadAllText(srcAbs));

                // No-op on the SAME file → no churn, mtime untouched.
                proj.NeedsRebuild = false;
                File.WriteAllText(srcAbs, content);   // reset to known state
                var before = File.GetLastWriteTimeUtc(srcAbs);
                var res2 = DecompSourceWriterCore.WriteTableEntry(proj, "items", 1,
                    new Dictionary<string, uint> { { "might", 8 } });
                Assert.True(res2.Ok, res2.Message);
                Assert.Empty(res2.ChangedFields);
                Assert.False(proj.NeedsRebuild);
                Assert.Equal(content, File.ReadAllText(srcAbs));
                Assert.Equal(before, File.GetLastWriteTimeUtc(srcAbs));
            }
            finally { TryDelete(dir); }
        }

        // =====================================================================
        //  #1141 — signed C-array fields + multi-field bulk
        // =====================================================================

        static DecompTableEntry SignedClassOwner(string sourceFile)
        {
            return new DecompTableEntry
            {
                Table = "classes", Format = "cstruct", WritePolicy = "source",
                ArrayName = "gClassData", SourceFile = sourceFile,
                Fields = new List<DecompTableField>
                {
                    new DecompTableField { Name = "baseHp" },
                    new DecompTableField { Name = "promoHp", Signed = true, Width = 1 },
                    new DecompTableField { Name = "promoStr", Signed = true, Width = 1 },
                },
            };
        }

        [Fact]
        public void Cstruct_SignedField_NegativeValue_EmitsMinusN()
        {
            string src =
                "struct ClassData gClassData[] = {\n" +
                "    [0] = { .baseHp = 18, .promoHp = 2, .promoStr = 0 },\n" +
                "};\n";
            var owner = SignedClassOwner("src/class.c");
            var res = DecompSourceWriterCore.RewriteEntryText(src, owner, 0,
                new Dictionary<string, uint> { { "promoHp", Pack(-3) } }, out string outText);
            Assert.True(res.Ok, res.Message);
            Assert.Contains(".promoHp = -3", outText);
            Assert.Contains(".baseHp = 18", outText);   // unsigned untouched
        }

        [Fact]
        public void Cstruct_SignedField_NegativeToken_RewriteToPositive()
        {
            string src = "struct ClassData gClassData[] = { [0] = { .promoHp = -2 } };\n";
            var owner = SignedClassOwner("src/class.c");
            var res = DecompSourceWriterCore.RewriteEntryText(src, owner, 0,
                new Dictionary<string, uint> { { "promoHp", 4 } }, out string outText);
            Assert.True(res.Ok, res.Message);
            Assert.Contains(".promoHp = 4", outText);
        }

        [Fact]
        public void Cstruct_SignedField_NoOp_NegativeToken_RecognizedEqual()
        {
            string src = "struct ClassData gClassData[] = { [0] = { .promoHp = -1 } };\n";
            var owner = SignedClassOwner("src/class.c");
            // 0xFF byte (255) == -1 int8 → no-op
            var res = DecompSourceWriterCore.RewriteEntryText(src, owner, 0,
                new Dictionary<string, uint> { { "promoHp", 0xFF } }, out string outText);
            Assert.True(res.Ok, res.Message);
            Assert.Empty(res.ChangedFields);
            Assert.Equal(src, outText);
        }

        [Fact]
        public void Cstruct_UnsignedField_LeadingMinusRejected_NoChange()
        {
            // baseHp is UNSIGNED; an existing token like a macro still fails single-field.
            string src = "struct ClassData gClassData[] = { [0] = { .baseHp = SOME_MACRO } };\n";
            var owner = SignedClassOwner("src/class.c");
            var res = DecompSourceWriterCore.RewriteEntryText(src, owner, 0,
                new Dictionary<string, uint> { { "baseHp", 20 } }, out string outText);
            Assert.Equal(DecompSourceWriteStatus.UnsupportedField, res.Status);
            Assert.Equal(src, outText);
        }

        [Fact]
        public void Cstruct_MultiField_Bulk_BothChanged()
        {
            string src =
                "struct ClassData gClassData[] = {\n" +
                "    [0] = { .baseHp = 18, .promoHp = 2, .promoStr = 0 },\n" +
                "};\n";
            var owner = SignedClassOwner("src/class.c");
            var res = DecompSourceWriterCore.RewriteEntryText(src, owner, 0,
                new Dictionary<string, uint>
                {
                    { "baseHp", 20 },
                    { "promoHp", Pack(-1) },
                }, out string outText);
            Assert.True(res.Ok, res.Message);
            Assert.Contains(".baseHp = 20", outText);
            Assert.Contains(".promoHp = -1", outText);
            Assert.Equal(2, res.ChangedFields.Count);
        }

        // =====================================================================
        //  #1141 — units / classes C-array fixtures + units<->characters alias
        // =====================================================================

        [Fact]
        public void Cstruct_UnitsTable_EditOneField()
        {
            string src =
                "struct CharacterData gCharacterData[] = {\n" +
                "    [0] = { .hp = 16, .pow = 5 },\n" +
                "    [1] = { .hp = 18, .pow = 7 },\n" +
                "};\n";
            var owner = new DecompTableEntry
            {
                Table = "units", Format = "cstruct", WritePolicy = "source",
                ArrayName = "gCharacterData", SourceFile = "src/chardata.c",
                Fields = new List<DecompTableField>
                {
                    new DecompTableField { Name = "hp", Signed = true, Width = 1 },
                    new DecompTableField { Name = "pow", Signed = true, Width = 1 },
                },
            };
            var res = DecompSourceWriterCore.RewriteEntryText(src, owner, 1,
                new Dictionary<string, uint> { { "pow", 9 } }, out string outText);
            Assert.True(res.Ok, res.Message);
            Assert.Contains("[1] = { .hp = 18, .pow = 9 }", outText);
            Assert.Contains("[0] = { .hp = 16, .pow = 5 }", outText);
        }

        [Fact]
        public void TryGetTableOwner_UnitsCharactersAlias_BothDirections()
        {
            // Owner declared as "characters" → "units" lookup resolves it (and reverse).
            var manChars = ManifestFromJson(@"{ ""tables"": [ { ""table"": ""characters"" } ] }");
            var projChars = new DecompProject { Manifest = manChars };
            Assert.NotNull(projChars.TryGetTableOwner("units"));
            Assert.NotNull(projChars.TryGetTableOwner("characters"));

            var manUnits = ManifestFromJson(@"{ ""tables"": [ { ""table"": ""units"" } ] }");
            var projUnits = new DecompProject { Manifest = manUnits };
            Assert.NotNull(projUnits.TryGetTableOwner("characters"));
            Assert.NotNull(projUnits.TryGetTableOwner("units"));

            // Unrelated lookup still misses.
            Assert.Null(projUnits.TryGetTableOwner("classes"));
        }

        [Fact]
        public void Cstruct_OtherOwnerFile_Untouched_WhenWritingDifferentTable()
        {
            string dir = NewTempDir();
            try
            {
                // Two owners: items + classes, in two different files.
                string itemsAbs = Path.Combine(dir, "item.c");
                string classAbs = Path.Combine(dir, "class.c");
                string itemsContent = "Item gItemData[] = { [0] = { .might = 5 } };\n";
                string classContent = "struct ClassData gClassData[] = { [0] = { .baseHp = 18, .promoHp = 2, .promoStr = 0 } };\n";
                File.WriteAllText(itemsAbs, itemsContent);
                File.WriteAllText(classAbs, classContent);

                var proj = ProjectWith(dir, ItemsOwner("item.c"), SignedClassOwner("class.c"));
                CoreState.DecompProject = proj;

                // Write to classes — the items file must be byte-identical afterward.
                var res = DecompSourceWriterCore.WriteTableEntry(proj, "classes", 0,
                    new Dictionary<string, uint> { { "baseHp", 20 } });
                Assert.True(res.Ok, res.Message);
                Assert.Equal(itemsContent, File.ReadAllText(itemsAbs));   // untouched
                Assert.Contains(".baseHp = 20", File.ReadAllText(classAbs));
            }
            finally { TryDelete(dir); }
        }

        // ---- small helpers ----

        /// <summary>Pack a signed int8 into the two's-complement byte the writer reinterprets.</summary>
        static uint Pack(int signed) => (uint)(byte)(sbyte)signed;

        static int CountSubstr(string s, string sub)
        {
            int n = 0, i = 0;
            while ((i = s.IndexOf(sub, i, StringComparison.Ordinal)) >= 0) { n++; i += sub.Length; }
            return n;
        }

        static void TryDelete(string path)
        {
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, true);
                else if (File.Exists(path)) File.Delete(path);
            }
            catch { /* best effort */ }
        }
    }
}
