using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Tests for the in-place source-backed shop-list writer (#1347):
    ///   - <see cref="DecompSourceWriterCore.RewriteListBody"/> (PURE; add/remove/reorder,
    ///     empty list, byte-identical no-op idempotency, low-byte==0 rejection, non-literal
    ///     macro no-clobber refusal, array-not-found, BOM+CRLF preservation, parse-stability).
    ///   - <see cref="DecompProject.TryGetListOwner"/> (tolerant, case-insensitive, format-gated).
    ///   - <see cref="DecompShopSourceResolver.TryResolveShopOwner"/> glue (exact + span-covering
    ///     symbol match → manifest list-owner; graceful false when symbol/owner absent).
    /// Pure (no CoreState mutation), so no SharedState collection is required.
    /// </summary>
    public class DecompShopListWriterTests
    {
        // -------------------------------------------------------- RewriteListBody (PURE)

        const string SrcTwoItems =
            "const u16 ItemList_Foo[] = {\n" +
            "    0x0501,\n" +
            "    0x0203,\n" +
            "    0x0000,\n" +
            "};\n";

        [Fact]
        public void RewriteListBody_AddReorder_RewritesAllElements()
        {
            var desired = new ushort[] { 0x0102, 0x0304, 0x0506 };
            var res = DecompSourceWriterCore.RewriteListBody(SrcTwoItems, "ItemList_Foo", desired, out string outText);

            Assert.True(res.Ok, res.Message);
            Assert.Contains("0x0102,", outText);
            Assert.Contains("0x0304,", outText);
            Assert.Contains("0x0506,", outText);
            Assert.Contains("0x0000,  // ITEM_NONE (terminator)", outText);
            // The old elements are gone (full-vector replace).
            Assert.DoesNotContain("0x0501", outText);
            Assert.DoesNotContain("0x0203", outText);
        }

        [Fact]
        public void RewriteListBody_Remove_ShortensList()
        {
            var desired = new ushort[] { 0x0501 };
            var res = DecompSourceWriterCore.RewriteListBody(SrcTwoItems, "ItemList_Foo", desired, out string outText);

            Assert.True(res.Ok, res.Message);
            Assert.Contains("0x0501,", outText);
            Assert.DoesNotContain("0x0203", outText);
            Assert.Contains("0x0000,  // ITEM_NONE (terminator)", outText);
        }

        [Fact]
        public void RewriteListBody_EmptyDesired_EmptiesShop_JustTerminator()
        {
            var res = DecompSourceWriterCore.RewriteListBody(SrcTwoItems, "ItemList_Foo", Array.Empty<ushort>(), out string outText);

            Assert.True(res.Ok, res.Message);
            Assert.DoesNotContain("0x0501", outText);
            Assert.DoesNotContain("0x0203", outText);
            Assert.Contains("0x0000,  // ITEM_NONE (terminator)", outText);
            // Prefix/suffix preserved.
            Assert.StartsWith("const u16 ItemList_Foo[] = {", outText);
            Assert.EndsWith("};\n", outText);
        }

        [Fact]
        public void RewriteListBody_NullDesired_TreatedAsEmpty()
        {
            var res = DecompSourceWriterCore.RewriteListBody(SrcTwoItems, "ItemList_Foo", null, out string outText);
            Assert.True(res.Ok, res.Message);
            Assert.Contains("0x0000,  // ITEM_NONE (terminator)", outText);
        }

        [Fact]
        public void RewriteListBody_Idempotent_ReRunningSameItems_IsByteIdenticalNoOp()
        {
            var desired = new ushort[] { 0x0102, 0x0304 };
            var first = DecompSourceWriterCore.RewriteListBody(SrcTwoItems, "ItemList_Foo", desired, out string firstText);
            Assert.True(first.Ok, first.Message);
            Assert.NotEqual(SrcTwoItems, firstText);

            // Re-running with the SAME items on the rewritten text is a byte-identical no-op.
            var second = DecompSourceWriterCore.RewriteListBody(firstText, "ItemList_Foo", desired, out string secondText);
            Assert.True(second.Ok, second.Message);
            Assert.Empty(second.ChangedFields);
            Assert.Equal("No change needed.", second.Message);
            Assert.Equal(firstText, secondText);   // byte-identical
        }

        [Fact]
        public void RewriteListBody_ItemLowByteZero_Rejected_UnsupportedField()
        {
            // 0x0100 has low byte 0 -> indistinguishable from the ITEM_NONE terminator.
            var desired = new ushort[] { 0x0100 };
            var res = DecompSourceWriterCore.RewriteListBody(SrcTwoItems, "ItemList_Foo", desired, out string outText);

            Assert.Equal(DecompSourceWriteStatus.UnsupportedField, res.Status);
            Assert.Contains("low byte is 0", res.Message);
            Assert.Equal(SrcTwoItems, outText);   // unchanged
        }

        [Fact]
        public void RewriteListBody_NonLiteralMacroElement_Refused_NoClobber()
        {
            string src =
                "const u16 ItemList_Foo[] = { ITEM_IRONSWORD, ITEM_NONE };\n";
            var res = DecompSourceWriterCore.RewriteListBody(src, "ItemList_Foo", new ushort[] { 0x0102 }, out string outText);

            Assert.Equal(DecompSourceWriteStatus.UnsupportedField, res.Status);
            Assert.Contains("non-literal element", res.Message);
            Assert.Equal(src, outText);   // unchanged (no clobber)
        }

        [Fact]
        public void RewriteListBody_ArrayNotFound_ParseFailed()
        {
            var res = DecompSourceWriterCore.RewriteListBody(SrcTwoItems, "ItemList_Missing", new ushort[] { 0x0102 }, out string outText);
            Assert.Equal(DecompSourceWriteStatus.ParseFailed, res.Status);
            Assert.Equal(SrcTwoItems, outText);
        }

        [Fact]
        public void RewriteListBody_NullSource_ParseFailed()
        {
            var res = DecompSourceWriterCore.RewriteListBody(null, "ItemList_Foo", new ushort[] { 0x0102 }, out string outText);
            Assert.Equal(DecompSourceWriteStatus.ParseFailed, res.Status);
            Assert.Null(outText);
        }

        [Fact]
        public void RewriteListBody_EmptySymbol_MalformedManifest()
        {
            var res = DecompSourceWriterCore.RewriteListBody(SrcTwoItems, "", new ushort[] { 0x0102 }, out string outText);
            Assert.Equal(DecompSourceWriteStatus.MalformedManifest, res.Status);
            Assert.Equal(SrcTwoItems, outText);
        }

        [Fact]
        public void RewriteListBody_CrlfPreserved()
        {
            string src =
                "const u16 ItemList_Foo[] = {\r\n" +
                "    0x0501,\r\n" +
                "    0x0000,\r\n" +
                "};\r\n";
            var res = DecompSourceWriterCore.RewriteListBody(src, "ItemList_Foo", new ushort[] { 0x0102, 0x0304 }, out string outText);

            Assert.True(res.Ok, res.Message);
            // CRLF newline style preserved in the rewritten body.
            Assert.Contains("0x0102,\r\n", outText);
            Assert.Contains("0x0304,\r\n", outText);
            Assert.Contains("0x0000,  // ITEM_NONE (terminator)\r\n", outText);
            Assert.DoesNotContain("0x0102,\n0", outText.Replace("\r\n", "")); // sanity: no bare-LF in the new body
        }

        [Fact]
        public void RewriteListBody_IndentationPreserved()
        {
            string src =
                "const u16 ItemList_Foo[] = {\n" +
                "        0x0501,\n" +   // 8-space indent
                "        0x0000,\n" +
                "};\n";
            var res = DecompSourceWriterCore.RewriteListBody(src, "ItemList_Foo", new ushort[] { 0x0102 }, out string outText);
            Assert.True(res.Ok, res.Message);
            Assert.Contains("\n        0x0102,\n", outText);   // 8-space indent preserved
        }

        [Fact]
        public void RewriteListBody_ParseSerializeParse_Stable()
        {
            var desired = new ushort[] { 0x0102, 0x0304, 0x0506 };
            var r1 = DecompSourceWriterCore.RewriteListBody(SrcTwoItems, "ItemList_Foo", desired, out string t1);
            Assert.True(r1.Ok, r1.Message);
            // Round-trip the produced text through the writer again with the same vector:
            // it must be a stable byte-identical no-op.
            var r2 = DecompSourceWriterCore.RewriteListBody(t1, "ItemList_Foo", desired, out string t2);
            Assert.True(r2.Ok, r2.Message);
            Assert.Equal(t1, t2);
        }

        // -------------------------------------------------- Part A: StripComments no-clobber
        // The final top-level element token spans from the previous comma through '}'. A
        // span that BEGINS with an inline // comment must NOT swallow the code (macro) that
        // follows the newline — otherwise the macro is mis-detected as comment-only/empty,
        // the list looks all-literal, and the writer would CLOBBER the macro (#1159).

        [Fact]
        public void RewriteListBody_InlineLineComment_BeforeFinalMacro_Refused_NoClobber()
        {
            // The final element span = "// comment\n    ITEM_NONE". The over-strip bug
            // dropped ITEM_NONE; the fix keeps it so the macro is detected and the rewrite
            // is REFUSED (UnsupportedField), leaving the source untouched.
            string src =
                "const u16 ItemList_Foo[] = {\n" +
                "    0x0501, // a comment\n" +
                "    ITEM_NONE\n" +
                "};\n";
            var res = DecompSourceWriterCore.RewriteListBody(src, "ItemList_Foo", new ushort[] { 0x0102 }, out string outText);

            Assert.Equal(DecompSourceWriteStatus.UnsupportedField, res.Status);
            Assert.Contains("non-literal element", res.Message);
            Assert.Equal(src, outText);   // unchanged (no clobber)
        }

        [Fact]
        public void RewriteListBody_BlockComment_MidList_AroundHexElement_StillRewrites()
        {
            // A /* */ block comment around a hex element must be stripped (not swallow the
            // element), so an all-literal list still parses + rewrites.
            string src =
                "const u16 ItemList_Foo[] = {\n" +
                "    0x0501, /* iron sword */\n" +
                "    0x0203,\n" +
                "    0x0000,\n" +
                "};\n";
            var res = DecompSourceWriterCore.RewriteListBody(src, "ItemList_Foo", new ushort[] { 0x0102, 0x0304 }, out string outText);

            Assert.True(res.Ok, res.Message);
            Assert.Contains("0x0102,", outText);
            Assert.Contains("0x0304,", outText);
            Assert.Contains("0x0000,  // ITEM_NONE (terminator)", outText);
            Assert.DoesNotContain("0x0501", outText);
        }

        [Fact]
        public void RewriteListBody_LastElement_TrailingLineComment_StillRewritesIdempotently()
        {
            // A pure raw-hex list whose LAST element carries an inline // annotation must
            // still rewrite, and re-running is a byte-identical no-op (idempotent). This is
            // the exact shape the writer itself emits ("0x0000,  // ITEM_NONE (terminator)").
            string src =
                "const u16 ItemList_Foo[] = {\n" +
                "    0x0501,\n" +
                "    0x0000,  // ITEM_NONE (terminator)\n" +
                "};\n";
            var r1 = DecompSourceWriterCore.RewriteListBody(src, "ItemList_Foo", new ushort[] { 0x0102, 0x0304 }, out string t1);
            Assert.True(r1.Ok, r1.Message);
            Assert.Contains("0x0102,", t1);
            Assert.Contains("0x0304,", t1);

            // Idempotent re-run: byte-identical no-op.
            var r2 = DecompSourceWriterCore.RewriteListBody(t1, "ItemList_Foo", new ushort[] { 0x0102, 0x0304 }, out string t2);
            Assert.True(r2.Ok, r2.Message);
            Assert.Empty(r2.ChangedFields);
            Assert.Equal(t1, t2);
        }

        // -------------------------------------------------------- TryGetListOwner

        static DecompProject ProjectFromManifestJson(string tablesJson)
        {
            string manifest =
                "{ \"schemaVersion\": 1, \"builtRom\": \"rom.gba\", \"tables\": " + tablesJson + " }";
            var parsed = System.Text.Json.JsonSerializer.Deserialize<DecompManifest>(
                manifest, new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                });
            return new DecompProject { ProjectRoot = "X", Manifest = parsed };
        }

        [Fact]
        public void TryGetListOwner_MatchesByArrayName_CaseInsensitive()
        {
            var project = ProjectFromManifestJson(
                "[ { \"table\": \"shop_fluorn\", \"format\": \"u16-list\", \"writePolicy\": \"source\"," +
                "    \"arrayName\": \"ItemList_Foo\", \"sourceFile\": \"src/shop.c\" } ]");

            // by arrayName, case-insensitive
            Assert.NotNull(project.TryGetListOwner("itemlist_foo"));
            // by table key
            Assert.NotNull(project.TryGetListOwner("SHOP_FLUORN"));
            // miss
            Assert.Null(project.TryGetListOwner("nope"));
        }

        [Fact]
        public void TryGetListOwner_MatchesBySymbol()
        {
            var project = ProjectFromManifestJson(
                "[ { \"table\": \"s\", \"format\": \"u16-list\", \"symbol\": \"ItemList_Bar\"," +
                "    \"sourceFile\": \"src/shop.c\" } ]");
            Assert.NotNull(project.TryGetListOwner("ItemList_Bar"));
        }

        [Fact]
        public void TryGetListOwner_FormatGate_NonU16List_NotReturned()
        {
            // Same symbol, but format=cstruct → NOT a list owner.
            var project = ProjectFromManifestJson(
                "[ { \"table\": \"items\", \"format\": \"cstruct\", \"arrayName\": \"ItemList_Foo\"," +
                "    \"sourceFile\": \"src/items.c\" } ]");
            Assert.Null(project.TryGetListOwner("ItemList_Foo"));
        }

        [Fact]
        public void TryGetListOwner_NoManifest_OrEmptyName_ReturnsNull()
        {
            var noManifest = new DecompProject { ProjectRoot = "X", Manifest = null };
            Assert.Null(noManifest.TryGetListOwner("ItemList_Foo"));

            var project = ProjectFromManifestJson(
                "[ { \"table\": \"s\", \"format\": \"u16-list\", \"arrayName\": \"ItemList_Foo\" } ]");
            Assert.Null(project.TryGetListOwner(""));
            Assert.Null(project.TryGetListOwner(null));
        }

        // -------------------------------------------------------- resolver glue

        // Build a project symbol resolver from a JSON artifact (auto-discovered as
        // <stem>.sym.json) so symbols carry EXPLICIT sizes for span-coverage.
        static DecompSymbolResolver ResolverFromJson(string jsonText)
        {
            string dir = Path.Combine(Path.GetTempPath(), "shopres_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "rom.sym.json"), jsonText);
            var project = new DecompProject { ProjectRoot = dir, BuiltRomPath = Path.Combine(dir, "rom.gba") };
            try { return DecompSymbolResolver.Load(project); }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        [Fact]
        public void TryResolveShopOwner_ExactSymbol_MapsToManifestOwner()
        {
            // Shop list at ROM offset 0x1000 → GBA pointer 0x08001000.
            string json = @"[ { ""name"": ""ItemList_Foo"", ""addr"": ""0x08001000"", ""size"": 6 } ]";
            var resolver = ResolverFromJson(json);
            var map = new MergedAsmMapFile(null, resolver);
            Assert.Equal("ItemList_Foo", map.GetName(0x08001000u));   // exact symbol resolves

            var project = ProjectFromManifestJson(
                "[ { \"table\": \"shop_foo\", \"format\": \"u16-list\", \"writePolicy\": \"source\"," +
                "    \"arrayName\": \"ItemList_Foo\", \"sourceFile\": \"src/shop.c\" } ]");

            bool ok = DecompShopSourceResolver.TryResolveShopOwner(project, map, 0x1000u, out var owner, out var name);
            Assert.True(ok);
            Assert.NotNull(owner);
            Assert.Equal("ItemList_Foo", name);
            Assert.Equal("src/shop.c", owner.SourceFile);
        }

        [Fact]
        public void TryResolveShopOwner_SpanCovering_ResolvesInteriorAddress()
        {
            // Symbol spans [0x08001000, 0x08001000+0x100). An interior shop address
            // 0x08001040 (offset 0x1040) has no EXACT symbol but is span-covered.
            string json = @"[ { ""name"": ""ItemList_Big"", ""addr"": ""0x08001000"", ""size"": 256 } ]";
            var resolver = ResolverFromJson(json);
            var map = new MergedAsmMapFile(null, resolver);
            var project = ProjectFromManifestJson(
                "[ { \"table\": \"s\", \"format\": \"u16-list\", \"arrayName\": \"ItemList_Big\"," +
                "    \"sourceFile\": \"src/shop.c\" } ]");

            bool ok = DecompShopSourceResolver.TryResolveShopOwner(project, map, 0x1040u, out var owner, out var name);
            Assert.True(ok);
            Assert.Equal("ItemList_Big", name);
            Assert.NotNull(owner);
        }

        [Fact]
        public void TryResolveShopOwner_SymbolAbsent_ReturnsFalse()
        {
            string json = @"[ { ""name"": ""ItemList_Foo"", ""addr"": ""0x08001000"", ""size"": 6 } ]";
            var resolver = ResolverFromJson(json);
            var map = new MergedAsmMapFile(null, resolver);
            var project = ProjectFromManifestJson(
                "[ { \"table\": \"s\", \"format\": \"u16-list\", \"arrayName\": \"ItemList_Foo\"," +
                "    \"sourceFile\": \"src/shop.c\" } ]");

            // A different (uncovered) address resolves to no symbol → false.
            bool ok = DecompShopSourceResolver.TryResolveShopOwner(project, map, 0x9999u, out var owner, out var name);
            Assert.False(ok);
            Assert.Null(owner);
            Assert.Equal("", name);
        }

        [Fact]
        public void TryResolveShopOwner_OwnerAbsent_ReturnsFalse()
        {
            // Symbol resolves, but the manifest declares NO list-owner for it.
            string json = @"[ { ""name"": ""ItemList_Foo"", ""addr"": ""0x08001000"", ""size"": 6 } ]";
            var resolver = ResolverFromJson(json);
            var map = new MergedAsmMapFile(null, resolver);
            var project = ProjectFromManifestJson("[ ]");

            bool ok = DecompShopSourceResolver.TryResolveShopOwner(project, map, 0x1000u, out var owner, out var name);
            Assert.False(ok);
            Assert.Null(owner);
        }

        [Fact]
        public void TryResolveShopOwner_NullArgs_ReturnsFalse()
        {
            Assert.False(DecompShopSourceResolver.TryResolveShopOwner(null, null, 0x1000u, out _, out _));
        }
    }
}
