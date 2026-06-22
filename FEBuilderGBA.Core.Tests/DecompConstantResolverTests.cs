using System;
using System.IO;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Tests for the constants-header id&lt;-&gt;macro resolver (#1354):
    /// <see cref="DecompConstantResolver"/>. The resolver is READ-ONLY and NEVER throws:
    /// enum auto-increment + explicit-literal anchors, ambiguous-expression poisoning,
    /// #define parsing, collision/redefinition rules, ushort-range gating, header
    /// discovery precedence (explicit absolute/escape ⇒ Unavailable, default NOT used),
    /// ITEM_NONE injection vs nonzero, and malformed-header tolerance.
    ///
    /// Pure (no CoreState mutation): a temp project dir holds an in-memory header.
    /// </summary>
    public class DecompConstantResolverTests : IDisposable
    {
        readonly string _dir;

        public DecompConstantResolverTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "constres_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch { }
        }

        // Write a header at the conventional default path and return a project rooted at _dir.
        DecompProject ProjectWithDefaultHeader(string headerText)
        {
            string headerAbs = Path.Combine(_dir, "include", "constants", "items.h");
            Directory.CreateDirectory(Path.GetDirectoryName(headerAbs));
            File.WriteAllText(headerAbs, headerText);
            return new DecompProject { ProjectRoot = _dir };
        }

        // Write a header at a custom project-relative path; project root = _dir.
        DecompProject ProjectWithHeaderAt(string relPath, string headerText)
        {
            string headerAbs = Path.Combine(_dir, relPath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(headerAbs));
            File.WriteAllText(headerAbs, headerText);
            return new DecompProject { ProjectRoot = _dir };
        }

        static DecompTableEntry OwnerWithHeader(string constantsHeader)
            => new DecompTableEntry { ConstantsHeader = constantsHeader };

        // ------------------------------------------------------------ enum parse (FE8U)

        [Fact]
        public void EnumParse_Fe8uSyntax_MapsCorrectly_ItemNoneZero()
        {
            var project = ProjectWithDefaultHeader(
                "enum { ITEM_NONE = 0x00, ITEM_SWORD_IRON = 0x01, ITEM_SWORD_SLIM = 0x02 };");
            var r = DecompConstantResolver.BuildForProject(project, null);

            Assert.False(r.IsUnavailable, r.Reason);
            Assert.True(r.ItemNoneIsZero);

            Assert.True(r.TryResolveMacroToId("ITEM_SWORD_IRON", out ushort iron));
            Assert.Equal(0x01, iron);
            Assert.True(r.TryResolveMacroToId("ITEM_SWORD_SLIM", out ushort slim));
            Assert.Equal(0x02, slim);
            Assert.True(r.TryResolveMacroToId("ITEM_NONE", out ushort none));
            Assert.Equal(0x00, none);

            Assert.True(r.TryResolveIdToMacro(0x01, out string m1));
            Assert.Equal("ITEM_SWORD_IRON", m1);
            Assert.True(r.TryResolveIdToMacro(0x00, out string m0));
            Assert.Equal("ITEM_NONE", m0);
            Assert.Equal("ITEM_NONE", r.ItemNoneMacro);
        }

        // ------------------------------------------------------------ auto-increment

        [Fact]
        public void EnumParse_AutoIncrement_AssignsRunningCounter()
        {
            var project = ProjectWithDefaultHeader("enum { ITEM_A = 0x10, ITEM_B, ITEM_C };");
            var r = DecompConstantResolver.BuildForProject(project, null);

            Assert.True(r.TryResolveMacroToId("ITEM_A", out ushort a));
            Assert.Equal(0x10, a);
            Assert.True(r.TryResolveMacroToId("ITEM_B", out ushort b));
            Assert.Equal(0x11, b);
            Assert.True(r.TryResolveMacroToId("ITEM_C", out ushort c));
            Assert.Equal(0x12, c);
        }

        // ------------------------------------------------------------ ambiguous + re-anchor

        [Fact]
        public void EnumParse_AmbiguousExpression_PoisonsFollowingImplicit_UntilNextLiteral()
        {
            // ITEM_A=1, ITEM_B=(ITEM_A|0x80) ambiguous, ITEM_C bare (unknown base) ambiguous,
            // ITEM_D=5 re-anchors.
            var project = ProjectWithDefaultHeader(
                "enum { ITEM_A = 0x01, ITEM_B = (ITEM_A | 0x80), ITEM_C, ITEM_D = 0x05 };");
            var r = DecompConstantResolver.BuildForProject(project, null);

            Assert.True(r.TryResolveMacroToId("ITEM_A", out ushort a));
            Assert.Equal(0x01, a);
            // ITEM_B is ambiguous (non-literal expression) — no macro->id mapping.
            Assert.False(r.TryResolveMacroToId("ITEM_B", out _));
            // ITEM_C is implicit on an UNKNOWN base — stays ambiguous.
            Assert.False(r.TryResolveMacroToId("ITEM_C", out _));
            // ITEM_D re-establishes a known base via an explicit literal.
            Assert.True(r.TryResolveMacroToId("ITEM_D", out ushort d));
            Assert.Equal(0x05, d);
        }

        [Fact]
        public void EnumParse_ImplicitAfterReanchor_Resolves()
        {
            // After ITEM_D=5 re-anchors, ITEM_E (bare) should be 6.
            var project = ProjectWithDefaultHeader(
                "enum { ITEM_A = 0x01, ITEM_B = (ITEM_A | 0x80), ITEM_C, ITEM_D = 0x05, ITEM_E };");
            var r = DecompConstantResolver.BuildForProject(project, null);
            Assert.True(r.TryResolveMacroToId("ITEM_E", out ushort e));
            Assert.Equal(0x06, e);
        }

        // ----------------------------------------- ITEM_-prefix scoping (PR #1356 review)

        [Fact]
        public void NonItemMacro_DoesNotClaimId_AdvancesCounterOnly()
        {
            // FALSE/NULL/MAX_CARRY are siblings in the same enum/header but must NOT be
            // mapped — only ITEM_* names are. A bare sibling member still advances the C
            // auto-increment counter so the following ITEM_* member gets the correct id.
            var project = ProjectWithDefaultHeader(
                "enum {\n" +
                "    ITEM_NONE = 0x00,\n" +
                "    FALSE,\n" +              // sibling at 1 — NOT mapped, counter -> 2
                "    ITEM_SWORD_IRON,\n" +    // bare ITEM_* on a KNOWN base -> 0x02
                "};\n" +
                "#define NULL 0\n" +
                "#define MAX_CARRY 5\n");
            var r = DecompConstantResolver.BuildForProject(project, null);

            // No sibling constant claimed an id.
            Assert.False(r.TryResolveMacroToId("FALSE", out _));
            Assert.False(r.TryResolveMacroToId("NULL", out _));
            Assert.False(r.TryResolveMacroToId("MAX_CARRY", out _));

            // id 0 maps to ITEM_NONE (NOT to a sibling such as FALSE/NULL), so the
            // terminator is correct.
            Assert.True(r.ItemNoneIsZero);
            Assert.Equal("ITEM_NONE", r.ItemNoneMacro);
            Assert.True(r.TryResolveIdToMacro(0x00, out string m0));
            Assert.Equal("ITEM_NONE", m0);

            // The counter still advanced across FALSE, so ITEM_SWORD_IRON resolves to 0x02.
            Assert.True(r.TryResolveMacroToId("ITEM_SWORD_IRON", out ushort iron));
            Assert.Equal(0x02, iron);
            Assert.True(r.TryResolveIdToMacro(0x02, out string m2));
            Assert.Equal("ITEM_SWORD_IRON", m2);
        }

        [Theory]
        [InlineData("enum { FALSE = 0x00, ITEM_SWORD_IRON = 0x01 };")]
        [InlineData("#define FALSE 0\n#define ITEM_SWORD_IRON 0x01\n")]
        public void NonItemMacro_AtIdZero_DoesNotBecomeTerminator(string headerText)
        {
            // A sibling that claims id 0 (FALSE = 0, in either enum OR #define form) must
            // not be picked as the ITEM_NONE terminator; ITEM_NONE is injected because no
            // ITEM_* maps to 0. Serializing id 0 always yields ITEM_NONE, never FALSE.
            var project = ProjectWithDefaultHeader(headerText);
            var r = DecompConstantResolver.BuildForProject(project, null);

            Assert.True(r.ItemNoneIsZero);
            Assert.Equal("ITEM_NONE", r.ItemNoneMacro);
            Assert.True(r.TryResolveIdToMacro(0x00, out string m0));
            Assert.Equal("ITEM_NONE", m0);
            Assert.False(r.TryResolveMacroToId("FALSE", out _));
        }

        // ------------------------------------------------------------ #define form

        [Fact]
        public void DefineForm_ParsesIntLiterals()
        {
            var project = ProjectWithDefaultHeader(
                "#define ITEM_NONE 0\n#define ITEM_SWORD_IRON 0x01\n#define ITEM_LANCE_IRON 5\n");
            var r = DecompConstantResolver.BuildForProject(project, null);

            Assert.True(r.TryResolveMacroToId("ITEM_SWORD_IRON", out ushort iron));
            Assert.Equal(0x01, iron);
            Assert.True(r.TryResolveMacroToId("ITEM_LANCE_IRON", out ushort lance));
            Assert.Equal(5, lance);
            Assert.True(r.ItemNoneIsZero);
        }

        // ------------------------------------------------------------ collisions / redefinition

        [Fact]
        public void DuplicateId_KeepsFirstForIdToMacro_BothMacroToId()
        {
            var project = ProjectWithDefaultHeader(
                "enum { ITEM_NONE = 0, ITEM_A = 0x05, ITEM_A_ALIAS = 0x05 };");
            var r = DecompConstantResolver.BuildForProject(project, null);

            // Both map to id.
            Assert.True(r.TryResolveMacroToId("ITEM_A", out ushort a));
            Assert.Equal(0x05, a);
            Assert.True(r.TryResolveMacroToId("ITEM_A_ALIAS", out ushort alias));
            Assert.Equal(0x05, alias);
            // FIRST wins for id->macro (display).
            Assert.True(r.TryResolveIdToMacro(0x05, out string macro));
            Assert.Equal("ITEM_A", macro);
        }

        [Fact]
        public void ConflictingRedefinition_MakesMacroAmbiguous()
        {
            // ITEM_X defined to 1 (enum) then redefined to 2 (#define) ⇒ ambiguous.
            var project = ProjectWithDefaultHeader(
                "enum { ITEM_NONE = 0, ITEM_X = 0x01 };\n#define ITEM_X 0x02\n");
            var r = DecompConstantResolver.BuildForProject(project, null);

            Assert.False(r.TryResolveMacroToId("ITEM_X", out _));
        }

        // ------------------------------------------------------------ ushort-range gate

        [Fact]
        public void OutOfUshortRange_ValueSkipped()
        {
            var project = ProjectWithDefaultHeader(
                "enum { ITEM_NONE = 0, ITEM_BIG = 0x10000, ITEM_OK = 0x07 };");
            var r = DecompConstantResolver.BuildForProject(project, null);

            // 0x10000 > 0xFFFF ⇒ ambiguous (skipped); the auto-increment base is now unknown
            // for any implicit member, but ITEM_OK has an explicit literal so it resolves.
            Assert.False(r.TryResolveMacroToId("ITEM_BIG", out _));
            Assert.True(r.TryResolveMacroToId("ITEM_OK", out ushort ok));
            Assert.Equal(0x07, ok);
        }

        // ------------------------------------------------------------ never-throws

        [Fact]
        public void MissingHeader_ReturnsUnavailable_NoThrow()
        {
            var project = new DecompProject { ProjectRoot = _dir };   // no header written
            var r = DecompConstantResolver.BuildForProject(project, null);
            Assert.True(r.IsUnavailable);
            Assert.NotNull(r.Reason);
        }

        [Fact]
        public void MalformedHeader_NeverThrows()
        {
            var project = ProjectWithDefaultHeader(
                "enum { ITEM_NONE = 0, ITEM_A = , broken /* unterminated");
            // Should not throw; returns a (possibly partially-populated) resolver.
            var r = DecompConstantResolver.BuildForProject(project, null);
            Assert.NotNull(r);
        }

        [Fact]
        public void NullProject_ReturnsUnavailable_NoThrow()
        {
            var r = DecompConstantResolver.BuildForProject(null, null);
            Assert.True(r.IsUnavailable);
        }

        // ------------------------------------------------------------ discovery precedence

        [Fact]
        public void Discovery_OwnerConstantsHeader_TakesPrecedence()
        {
            // Default header maps ITEM_A=1; the owner header maps ITEM_A=9. Owner wins.
            ProjectWithDefaultHeader("enum { ITEM_NONE = 0, ITEM_A = 0x01 };");
            var project = ProjectWithHeaderAt("src/custom_items.h",
                "enum { ITEM_NONE = 0, ITEM_A = 0x09 };");
            var owner = OwnerWithHeader("src/custom_items.h");

            var r = DecompConstantResolver.BuildForProject(project, owner);
            Assert.False(r.IsUnavailable, r.Reason);
            Assert.True(r.ExplicitPathDeclared);
            Assert.True(r.TryResolveMacroToId("ITEM_A", out ushort a));
            Assert.Equal(0x09, a);
        }

        [Fact]
        public void Discovery_ArtifactsItemConstants_UsedWhenNoOwnerHeader()
        {
            // Manifest artifacts.itemConstants points at src/art_items.h.
            ProjectWithHeaderAt("src/art_items.h", "enum { ITEM_NONE = 0, ITEM_A = 0x07 };");
            var manifest = System.Text.Json.JsonSerializer.Deserialize<DecompManifest>(
                "{ \"schemaVersion\": 1, \"artifacts\": { \"itemConstants\": \"src/art_items.h\" } }",
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            var project = new DecompProject { ProjectRoot = _dir, Manifest = manifest };

            var r = DecompConstantResolver.BuildForProject(project, null);
            Assert.False(r.IsUnavailable, r.Reason);
            Assert.True(r.ExplicitPathDeclared);
            Assert.True(r.TryResolveMacroToId("ITEM_A", out ushort a));
            Assert.Equal(0x07, a);
        }

        [Fact]
        public void Discovery_ExplicitAbsolutePath_IsUnavailable_DefaultNotUsed()
        {
            // A default header EXISTS (maps ITEM_A=1) but the owner declares an ABSOLUTE
            // explicit path — the resolver must refuse, NOT fall back to the default.
            ProjectWithDefaultHeader("enum { ITEM_NONE = 0, ITEM_A = 0x01 };");
            string abs = Path.Combine(Path.GetTempPath(), "elsewhere_items.h");
            var owner = OwnerWithHeader(abs);   // absolute
            var project = new DecompProject { ProjectRoot = _dir };

            var r = DecompConstantResolver.BuildForProject(project, owner);
            Assert.True(r.IsUnavailable);
            Assert.True(r.ExplicitPathDeclared);
            // Default's ITEM_A must NOT have leaked in.
            Assert.False(r.TryResolveMacroToId("ITEM_A", out _));
        }

        [Fact]
        public void Discovery_ExplicitEscapingPath_IsUnavailable_DefaultNotUsed()
        {
            ProjectWithDefaultHeader("enum { ITEM_NONE = 0, ITEM_A = 0x01 };");
            var owner = OwnerWithHeader("../escape_items.h");   // escapes root
            var project = new DecompProject { ProjectRoot = _dir };

            var r = DecompConstantResolver.BuildForProject(project, owner);
            Assert.True(r.IsUnavailable);
            Assert.False(r.TryResolveMacroToId("ITEM_A", out _));
        }

        [Fact]
        public void Discovery_ExplicitMissingPath_IsUnavailable_DefaultNotUsed()
        {
            ProjectWithDefaultHeader("enum { ITEM_NONE = 0, ITEM_A = 0x01 };");
            var owner = OwnerWithHeader("src/does_not_exist.h");   // project-relative but missing
            var project = new DecompProject { ProjectRoot = _dir };

            var r = DecompConstantResolver.BuildForProject(project, owner);
            Assert.True(r.IsUnavailable);
            Assert.False(r.TryResolveMacroToId("ITEM_A", out _));
        }

        // ------------------------------------------------------------ ITEM_NONE injection / nonzero

        [Fact]
        public void ItemNone_InjectedWhenAbsent()
        {
            var project = ProjectWithDefaultHeader("enum { ITEM_SWORD_IRON = 0x01 };");  // no ITEM_NONE
            var r = DecompConstantResolver.BuildForProject(project, null);

            Assert.True(r.ItemNoneIsZero);
            Assert.True(r.TryResolveIdToMacro(0x00, out string m0));
            Assert.Equal("ITEM_NONE", m0);
            Assert.True(r.TryResolveMacroToId("ITEM_NONE", out ushort none));
            Assert.Equal(0, none);
        }

        [Fact]
        public void ItemNone_Nonzero_SetsItemNoneIsZeroFalse()
        {
            // ITEM_NONE explicitly nonzero AND nothing else maps to 0.
            var project = ProjectWithDefaultHeader("enum { ITEM_SWORD_IRON = 0x01, ITEM_NONE = 0x02 };");
            var r = DecompConstantResolver.BuildForProject(project, null);

            Assert.False(r.ItemNoneIsZero);
        }
    }
}
