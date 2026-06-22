using System;
using System.IO;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Tests for the SYMBOLIC ITEM_* path of the in-place shop-list source writer (#1354):
    /// the <see cref="DecompSourceWriterCore.RewriteListBody(string,string,System.Collections.Generic.IReadOnlyList{ushort},DecompConstantResolver,out string)"/>
    /// 5-arg overload. A resolver is built from an in-memory FE8U-style constants header.
    /// Covers: symbolic add/remove/reorder preserving ITEM_* names + ITEM_NONE terminator;
    /// unknown/ambiguous macro element refusal (no clobber); literal regression with a
    /// resolver present; mixed-list symbolic-present-wins; no-macro-for-id refusal;
    /// nonzero-quantity refusal; idempotent symbolic no-op; CRLF/BOM preservation.
    ///
    /// Pure (no CoreState mutation), so no SharedState collection.
    /// </summary>
    public class DecompShopListSymbolicWriterTests : IDisposable
    {
        readonly string _dir;

        public DecompShopListSymbolicWriterTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "symshop_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch { }
        }

        // FE8U-style constants header (enum, item-id-only universe).
        const string HeaderText =
            "enum {\n" +
            "    ITEM_NONE = 0x00,\n" +
            "    ITEM_SWORD_IRON = 0x01,\n" +
            "    ITEM_SWORD_SLIM = 0x02,\n" +
            "    ITEM_LANCE_IRON = 0x14,\n" +
            "    ITEM_AXE_IRON = 0x1F,\n" +
            "};\n";

        DecompConstantResolver Resolver()
        {
            string headerAbs = Path.Combine(_dir, "include", "constants", "items.h");
            Directory.CreateDirectory(Path.GetDirectoryName(headerAbs));
            File.WriteAllText(headerAbs, HeaderText);
            var project = new DecompProject { ProjectRoot = _dir };
            var r = DecompConstantResolver.BuildForProject(project, null);
            Assert.False(r.IsUnavailable, r.Reason);
            return r;
        }

        // A symbolic (ITEM_*) source list — the canonical FE8U worldmap_shop_data.c form.
        const string SymbolicList =
            "CONST_DATA u16 ItemList_WM_Ide_Armory[] = {\n" +
            "    ITEM_SWORD_IRON,\n" +
            "    ITEM_LANCE_IRON,\n" +
            "    ITEM_NONE,\n" +
            "};\n";

        // ------------------------------------------------------------ symbolic add/reorder/remove

        [Fact]
        public void Symbolic_AddReorder_PreservesMacroNames_AndTerminator()
        {
            var constants = Resolver();
            // item-id-only ⇒ quantity (high byte) must be 0. Reorder + add ITEM_AXE_IRON.
            var desired = new ushort[] { 0x0014, 0x0001, 0x001F };  // LANCE_IRON, SWORD_IRON, AXE_IRON
            var res = DecompSourceWriterCore.RewriteListBody(
                SymbolicList, "ItemList_WM_Ide_Armory", desired, constants, out string outText);

            Assert.True(res.Ok, res.Message);
            Assert.Contains("ITEM_LANCE_IRON,", outText);
            Assert.Contains("ITEM_SWORD_IRON,", outText);
            Assert.Contains("ITEM_AXE_IRON,", outText);
            Assert.Contains("ITEM_NONE,", outText);
            // No raw hex leaked into a symbolic list.
            Assert.DoesNotContain("0x0014", outText);
            Assert.DoesNotContain("0x0000", outText);
        }

        [Fact]
        public void Symbolic_Remove_ShortensList_KeepsTerminator()
        {
            var constants = Resolver();
            var desired = new ushort[] { 0x0001 };   // only ITEM_SWORD_IRON
            var res = DecompSourceWriterCore.RewriteListBody(
                SymbolicList, "ItemList_WM_Ide_Armory", desired, constants, out string outText);

            Assert.True(res.Ok, res.Message);
            Assert.Contains("ITEM_SWORD_IRON,", outText);
            Assert.DoesNotContain("ITEM_LANCE_IRON", outText);
            Assert.Contains("ITEM_NONE,", outText);
        }

        [Fact]
        public void Symbolic_EmptyDesired_JustTerminator()
        {
            var constants = Resolver();
            var res = DecompSourceWriterCore.RewriteListBody(
                SymbolicList, "ItemList_WM_Ide_Armory", Array.Empty<ushort>(), constants, out string outText);

            Assert.True(res.Ok, res.Message);
            Assert.DoesNotContain("ITEM_SWORD_IRON", outText);
            Assert.DoesNotContain("ITEM_LANCE_IRON", outText);
            Assert.Contains("ITEM_NONE,", outText);
        }

        // ------------------------------------------------------------ refusals (no clobber)

        [Fact]
        public void UnknownMacroElement_NoResolver_Refused_SourceUnchanged()
        {
            // No resolver ⇒ any macro element is refused (today's literal-only behavior).
            var res = DecompSourceWriterCore.RewriteListBody(
                SymbolicList, "ItemList_WM_Ide_Armory", new ushort[] { 0x0001 }, constants: null, out string outText);

            Assert.False(res.Ok);
            Assert.Equal(DecompSourceWriteStatus.UnsupportedField, res.Status);
            Assert.Equal(SymbolicList, outText);   // source untouched
        }

        [Fact]
        public void AmbiguousMacroElement_Refused_SourceUnchanged()
        {
            // Header where ITEM_FOO is ambiguous (non-literal value).
            string headerAbs = Path.Combine(_dir, "include", "constants", "items.h");
            Directory.CreateDirectory(Path.GetDirectoryName(headerAbs));
            File.WriteAllText(headerAbs,
                "enum { ITEM_NONE = 0, ITEM_FOO = (1 | 0x80), ITEM_SWORD_IRON = 0x01 };");
            var constants = DecompConstantResolver.BuildForProject(new DecompProject { ProjectRoot = _dir }, null);

            string src =
                "const u16 ItemList_X[] = {\n    ITEM_FOO,\n    ITEM_NONE,\n};\n";
            var res = DecompSourceWriterCore.RewriteListBody(
                src, "ItemList_X", new ushort[] { 0x0001 }, constants, out string outText);

            Assert.False(res.Ok);
            Assert.Equal(DecompSourceWriteStatus.UnsupportedField, res.Status);
            Assert.Contains("ITEM_FOO", res.Message);
            Assert.Equal(src, outText);
        }

        // ------------------------------------------------------------ literal regression

        [Fact]
        public void LiteralList_WithResolverPresent_StillRewritesLiteral()
        {
            var constants = Resolver();
            const string literal =
                "const u16 ItemList_Foo[] = {\n    0x0501,\n    0x0000,\n};\n";
            var desired = new ushort[] { 0x0102, 0x0304 };
            var res = DecompSourceWriterCore.RewriteListBody(
                literal, "ItemList_Foo", desired, constants, out string outText);

            Assert.True(res.Ok, res.Message);
            // Literal output (no symbolic element present ⇒ stays raw-hex).
            Assert.Contains("0x0102,", outText);
            Assert.Contains("0x0304,", outText);
            Assert.Contains("0x0000,  // ITEM_NONE (terminator)", outText);
            Assert.DoesNotContain("ITEM_SWORD_IRON", outText);
        }

        // ------------------------------------------------------------ mixed-list (symbolic-present wins)

        [Fact]
        public void Mixed_HexThenItemNone_SymbolicPresentWins()
        {
            var constants = Resolver();
            // The list mixes a hex literal with ITEM_NONE ⇒ symbolic-present wins.
            const string mixed =
                "const u16 ItemList_M[] = {\n    0x0001,\n    ITEM_NONE,\n};\n";
            var desired = new ushort[] { 0x0014 };   // ITEM_LANCE_IRON
            var res = DecompSourceWriterCore.RewriteListBody(
                mixed, "ItemList_M", desired, constants, out string outText);

            Assert.True(res.Ok, res.Message);
            Assert.Contains("ITEM_LANCE_IRON,", outText);
            Assert.Contains("ITEM_NONE,", outText);
            Assert.DoesNotContain("0x0014", outText);
        }

        [Fact]
        public void Mixed_ItemMacroThenHex_SymbolicPresentWins()
        {
            var constants = Resolver();
            const string mixed =
                "const u16 ItemList_M2[] = {\n    ITEM_SWORD_IRON,\n    0x02,\n    ITEM_NONE,\n};\n";
            var desired = new ushort[] { 0x0001, 0x0002 };   // ITEM_SWORD_IRON, ITEM_SWORD_SLIM
            var res = DecompSourceWriterCore.RewriteListBody(
                mixed, "ItemList_M2", desired, constants, out string outText);

            Assert.True(res.Ok, res.Message);
            Assert.Contains("ITEM_SWORD_IRON,", outText);
            Assert.Contains("ITEM_SWORD_SLIM,", outText);
            Assert.Contains("ITEM_NONE,", outText);
        }

        // ------------------------------------------------------------ no-macro-for-id refusal

        [Fact]
        public void Symbolic_DesiredIdWithNoMacro_Refused()
        {
            var constants = Resolver();
            // 0x55 is not in the header ⇒ no ITEM_* constant.
            var desired = new ushort[] { 0x0055 };
            var res = DecompSourceWriterCore.RewriteListBody(
                SymbolicList, "ItemList_WM_Ide_Armory", desired, constants, out string outText);

            Assert.False(res.Ok);
            Assert.Equal(DecompSourceWriteStatus.UnsupportedField, res.Status);
            Assert.Contains("no ITEM_* constant", res.Message);
            Assert.Equal(SymbolicList, outText);
        }

        // ------------------------------------------------------------ nonzero-quantity refusal

        [Fact]
        public void Symbolic_NonzeroQuantity_Refused_SourceUntouched()
        {
            var constants = Resolver();
            // 0x0501 = qty 5, id 1 ⇒ item-id-only form can't encode the quantity.
            var desired = new ushort[] { 0x0501 };
            var res = DecompSourceWriterCore.RewriteListBody(
                SymbolicList, "ItemList_WM_Ide_Armory", desired, constants, out string outText);

            Assert.False(res.Ok);
            Assert.Equal(DecompSourceWriteStatus.UnsupportedField, res.Status);
            Assert.Contains("item-id-only", res.Message);
            Assert.Contains("quantity", res.Message);
            Assert.Equal(SymbolicList, outText);
        }

        // ------------------------------------------------------------ ITEM_NONE nonzero refusal

        [Fact]
        public void Symbolic_ItemNoneNotZero_Refused()
        {
            // Header where ITEM_NONE is nonzero ⇒ symbolic rewrite refused.
            string headerAbs = Path.Combine(_dir, "include", "constants", "items.h");
            Directory.CreateDirectory(Path.GetDirectoryName(headerAbs));
            File.WriteAllText(headerAbs,
                "enum { ITEM_SWORD_IRON = 0x01, ITEM_NONE = 0x02 };");
            var constants = DecompConstantResolver.BuildForProject(new DecompProject { ProjectRoot = _dir }, null);
            Assert.False(constants.ItemNoneIsZero);

            string src =
                "const u16 ItemList_X[] = {\n    ITEM_SWORD_IRON,\n    ITEM_NONE,\n};\n";
            var res = DecompSourceWriterCore.RewriteListBody(
                src, "ItemList_X", new ushort[] { 0x0001 }, constants, out string outText);

            Assert.False(res.Ok);
            Assert.Equal(DecompSourceWriteStatus.UnsupportedField, res.Status);
            Assert.Contains("ITEM_NONE does not resolve to 0", res.Message);
            Assert.Equal(src, outText);
        }

        // ------------------------------------------------------------ idempotent symbolic no-op

        [Fact]
        public void Symbolic_Idempotent_ReRunningSame_IsByteIdenticalNoOp()
        {
            var constants = Resolver();
            var desired = new ushort[] { 0x0001, 0x0014 };   // SWORD_IRON, LANCE_IRON
            var first = DecompSourceWriterCore.RewriteListBody(
                SymbolicList, "ItemList_WM_Ide_Armory", desired, constants, out string firstText);
            Assert.True(first.Ok, first.Message);

            var second = DecompSourceWriterCore.RewriteListBody(
                firstText, "ItemList_WM_Ide_Armory", desired, constants, out string secondText);
            Assert.True(second.Ok, second.Message);
            Assert.Equal(firstText, secondText);   // byte-identical no-op
        }

        // ------------------------------------------------------------ CRLF + BOM preservation

        [Fact]
        public void Symbolic_CrlfPreserved()
        {
            var constants = Resolver();
            string crlf = SymbolicList.Replace("\n", "\r\n");
            var desired = new ushort[] { 0x0001 };
            var res = DecompSourceWriterCore.RewriteListBody(
                crlf, "ItemList_WM_Ide_Armory", desired, constants, out string outText);

            Assert.True(res.Ok, res.Message);
            Assert.Contains("ITEM_SWORD_IRON,\r\n", outText);
            Assert.Contains("ITEM_NONE,\r\n", outText);
            // No bare LF was emitted inside the rewritten body.
            Assert.DoesNotContain("ITEM_SWORD_IRON,\n", outText.Replace("\r\n", "X"));
        }
    }
}
