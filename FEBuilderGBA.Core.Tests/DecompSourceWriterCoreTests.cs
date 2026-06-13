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

        [Fact]
        public void Write_JsonFormat_Manual()
        {
            string dir = NewTempDir();
            try
            {
                var proj = ProjectWith(dir, ItemsOwner("item.json", format: "json"));
                CoreState.DecompProject = proj;

                var res = DecompSourceWriterCore.WriteTableEntry(proj, "items", 0,
                    new Dictionary<string, uint> { { "might", 10 } });
                Assert.Equal(DecompSourceWriteStatus.Manual, res.Status);
                Assert.False(proj.NeedsRebuild);
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

        // ---- small helpers ----

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
