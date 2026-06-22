using System;
using System.Collections.Generic;
using System.IO;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Tests for the decomp-mode shop-save source-routing helper (#1347 Slice 5a):
    /// <see cref="DecompShopSourceWriteCore.TryRouteShopSaveToSource"/>. The helper is
    /// ROM-bound, NEVER mutates the ROM, and NEVER throws: it resolves the shop's address
    /// to a manifest u16-list owner and delegates the pure rewrite to
    /// <see cref="DecompSourceWriterCore.WriteListEntries"/>.
    ///
    /// The class mutates <see cref="CoreState.ROM"/> + <see cref="CoreState.DecompProject"/>,
    /// so it joins the shared-state collection and saves/restores both in the ctor/Dispose.
    /// </summary>
    [Collection("SharedState")]
    public class DecompShopSourceWriteCoreTests : IDisposable
    {
        readonly ROM _savedRom;
        readonly DecompProject _savedProject;

        public DecompShopSourceWriteCoreTests()
        {
            _savedRom = CoreState.ROM;
            _savedProject = CoreState.DecompProject;
            CoreState.ROM = null;
            CoreState.DecompProject = null;
        }

        public void Dispose()
        {
            CoreState.ROM = _savedRom;
            CoreState.DecompProject = _savedProject;
        }

        // ----------------------------------------------------------------- helpers

        // A small ROM whose byte content is irrelevant (the helper never reads it for
        // the routing decision) but which must be the active CoreState.ROM.
        static ROM MakeRom()
        {
            var rom = new ROM();
            rom.SwapNewROMDataDirect(new byte[0x2000]);
            return rom;
        }

        static DecompManifest ManifestFromJson(string tablesJson)
        {
            string manifest =
                "{ \"schemaVersion\": 1, \"builtRom\": \"rom.gba\", \"tables\": " + tablesJson + " }";
            return System.Text.Json.JsonSerializer.Deserialize<DecompManifest>(
                manifest, new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                });
        }

        sealed class Fixture : IDisposable
        {
            public string Dir;
            public DecompProject Project;
            public IAsmMapFile Map;
            public string SourceAbs;
            public void Dispose() { try { Directory.Delete(Dir, true); } catch { } }
        }

        /// <summary>
        /// Build a temp project that holds BOTH a <c>rom.sym.json</c> (so the shop address
        /// resolves to a symbol) AND a literal <c>src/shop.c</c> list. The shop is at ROM
        /// offset 0x1000 → GBA pointer 0x08001000.
        /// </summary>
        static Fixture MakeOwnedFixture(string symbolName, string srcBody, string tablesJson)
        {
            string dir = Path.Combine(Path.GetTempPath(), "shoproute_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            // Symbol JSON: the shop list symbol spans [0x08001000, +6).
            File.WriteAllText(Path.Combine(dir, "rom.sym.json"),
                "[ { \"name\": \"" + symbolName + "\", \"addr\": \"0x08001000\", \"size\": 6 } ]");
            // Source file under src/.
            string srcAbs = Path.Combine(dir, "src", "shop.c");
            Directory.CreateDirectory(Path.GetDirectoryName(srcAbs));
            File.WriteAllText(srcAbs, srcBody);

            var project = new DecompProject
            {
                ProjectRoot = dir,
                BuiltRomPath = Path.Combine(dir, "rom.gba"),
                Manifest = ManifestFromJson(tablesJson),
            };
            var resolver = DecompSymbolResolver.Load(project);
            var map = new MergedAsmMapFile(null, resolver);
            return new Fixture { Dir = dir, Project = project, Map = map, SourceAbs = srcAbs };
        }

        const string LiteralList =
            "const u16 ItemList_Foo[] = {\n" +
            "    0x0501,\n" +
            "    0x0203,\n" +
            "    0x0000,\n" +
            "};\n";

        const string LiteralTables =
            "[ { \"table\": \"shop_foo\", \"format\": \"u16-list\", \"writePolicy\": \"source\"," +
            "    \"arrayName\": \"ItemList_Foo\", \"sourceFile\": \"src/shop.c\" } ]";

        // ----------------------------------------------------------------- Routed

        [Fact]
        public void TryRoute_OwnedLiteralList_Routed_WritesSource_RomUntouched()
        {
            using var fx = MakeOwnedFixture("ItemList_Foo", LiteralList, LiteralTables);
            var rom = MakeRom();
            byte[] romBefore = (byte[])rom.Data.Clone();
            CoreState.ROM = rom;
            CoreState.DecompProject = fx.Project;

            var desired = new ushort[] { 0x0102, 0x0304 };
            var r = DecompShopSourceWriteCore.TryRouteShopSaveToSource(rom, fx.Project, fx.Map, 0x1000u, desired);

            Assert.True(r.Routed, r.Message);
            Assert.Equal(DecompShopRouteOutcome.Routed, r.Outcome);
            Assert.Equal(fx.SourceAbs, r.SourceFile);
            // The source file now contains the new vector.
            string after = File.ReadAllText(fx.SourceAbs);
            Assert.Contains("0x0102,", after);
            Assert.Contains("0x0304,", after);
            Assert.Contains("0x0000,  // ITEM_NONE (terminator)", after);
            // The ROM was NOT mutated.
            Assert.Equal(romBefore, rom.Data);
            Assert.True(fx.Project.NeedsRebuild);
        }

        // ------------------------------------------------- symbolic ITEM_* routing (#1354)

        const string Fe8uHeader =
            "enum {\n" +
            "    ITEM_NONE = 0x00,\n" +
            "    ITEM_SWORD_IRON = 0x01,\n" +
            "    ITEM_LANCE_IRON = 0x14,\n" +
            "    ITEM_AXE_IRON = 0x1F,\n" +
            "};\n";

        const string SymbolicSourceList =
            "CONST_DATA u16 ItemList_Foo[] = {\n" +
            "    ITEM_SWORD_IRON,\n" +
            "    ITEM_NONE,\n" +
            "};\n";

        // Write the FE8U constants header at the conventional default path inside fx.Dir.
        static void WriteDefaultHeader(Fixture fx, string headerText)
        {
            string headerAbs = Path.Combine(fx.Dir, "include", "constants", "items.h");
            Directory.CreateDirectory(Path.GetDirectoryName(headerAbs));
            File.WriteAllText(headerAbs, headerText);
        }

        [Fact]
        public void TryRoute_SymbolicList_Routed_PreservesMacroNames_RomUntouched()
        {
            using var fx = MakeOwnedFixture("ItemList_Foo", SymbolicSourceList, LiteralTables);
            WriteDefaultHeader(fx, Fe8uHeader);
            var rom = MakeRom();
            byte[] romBefore = (byte[])rom.Data.Clone();
            CoreState.ROM = rom;
            CoreState.DecompProject = fx.Project;

            // item-id-only ⇒ quantity must be 0. Add ITEM_LANCE_IRON + ITEM_AXE_IRON.
            var desired = new ushort[] { 0x0001, 0x0014, 0x001F };
            var r = DecompShopSourceWriteCore.TryRouteShopSaveToSource(rom, fx.Project, fx.Map, 0x1000u, desired);

            Assert.True(r.Routed, r.Message);
            string after = File.ReadAllText(fx.SourceAbs);
            Assert.Contains("ITEM_SWORD_IRON,", after);
            Assert.Contains("ITEM_LANCE_IRON,", after);
            Assert.Contains("ITEM_AXE_IRON,", after);
            Assert.Contains("ITEM_NONE,", after);
            Assert.DoesNotContain("0x00", after);
            // ROM untouched (source-only writer).
            Assert.Equal(romBefore, rom.Data);
            Assert.True(fx.Project.NeedsRebuild);
        }

        [Fact]
        public void TryRoute_SymbolicNonzeroQuantity_NotRouted_SourceAndRomUntouched()
        {
            using var fx = MakeOwnedFixture("ItemList_Foo", SymbolicSourceList, LiteralTables);
            WriteDefaultHeader(fx, Fe8uHeader);
            var rom = MakeRom();
            byte[] romBefore = (byte[])rom.Data.Clone();
            CoreState.ROM = rom;
            CoreState.DecompProject = fx.Project;

            // 0x0501 = qty 5, id 1 — symbolic item-id-only can't encode the quantity → refuse.
            var r = DecompShopSourceWriteCore.TryRouteShopSaveToSource(
                rom, fx.Project, fx.Map, 0x1000u, new ushort[] { 0x0501 });

            Assert.False(r.Routed);
            Assert.Equal(DecompShopRouteOutcome.NotRouted, r.Outcome);
            Assert.Contains("item-id-only", r.Message);
            // Source + ROM byte-identical.
            Assert.Equal(SymbolicSourceList, File.ReadAllText(fx.SourceAbs));
            Assert.Equal(romBefore, rom.Data);
        }

        [Fact]
        public void TryRoute_LiteralList_WithHeaderPresent_StaysLiteral()
        {
            using var fx = MakeOwnedFixture("ItemList_Foo", LiteralList, LiteralTables);
            WriteDefaultHeader(fx, Fe8uHeader);   // header present but list is all-hex
            var rom = MakeRom();
            CoreState.ROM = rom;
            CoreState.DecompProject = fx.Project;

            var r = DecompShopSourceWriteCore.TryRouteShopSaveToSource(
                rom, fx.Project, fx.Map, 0x1000u, new ushort[] { 0x0102 });

            Assert.True(r.Routed, r.Message);
            string after = File.ReadAllText(fx.SourceAbs);
            Assert.Contains("0x0102,", after);
            Assert.Contains("0x0000,  // ITEM_NONE (terminator)", after);
            // A literal list stays literal even with a header present.
            Assert.DoesNotContain("ITEM_SWORD_IRON", after);
        }

        // ----------------------------------------------------------------- NotRouted

        [Fact]
        public void TryRoute_SymbolUnresolvable_NotRouted_NoRomMutation()
        {
            using var fx = MakeOwnedFixture("ItemList_Foo", LiteralList, LiteralTables);
            var rom = MakeRom();
            byte[] romBefore = (byte[])rom.Data.Clone();
            CoreState.ROM = rom;
            CoreState.DecompProject = fx.Project;

            // An uncovered address (offset 0x9000) resolves to no symbol.
            var r = DecompShopSourceWriteCore.TryRouteShopSaveToSource(
                rom, fx.Project, fx.Map, 0x9000u, new ushort[] { 0x0102 });

            Assert.False(r.Routed);
            Assert.Equal(DecompShopRouteOutcome.NotRouted, r.Outcome);
            Assert.Contains("no list-owner", r.Message);
            Assert.Equal(romBefore, rom.Data);
            // Source file untouched.
            Assert.Equal(LiteralList, File.ReadAllText(fx.SourceAbs));
        }

        [Fact]
        public void TryRoute_NoManifestOwner_NotRouted()
        {
            // Symbol resolves, but the manifest declares NO list-owner for it.
            using var fx = MakeOwnedFixture("ItemList_Foo", LiteralList, "[ ]");
            var rom = MakeRom();
            CoreState.ROM = rom;
            CoreState.DecompProject = fx.Project;

            var r = DecompShopSourceWriteCore.TryRouteShopSaveToSource(
                rom, fx.Project, fx.Map, 0x1000u, new ushort[] { 0x0102 });

            Assert.False(r.Routed);
            Assert.Equal(DecompShopRouteOutcome.NotRouted, r.Outcome);
            Assert.Contains("no list-owner", r.Message);
        }

        [Fact]
        public void TryRoute_MacroList_NotRouted_NoClobber()
        {
            // Owned, but the source list contains a non-literal macro element → UnsupportedField
            // → NotRouted, source left untouched (no clobber, #1159).
            string macroList = "const u16 ItemList_Foo[] = { ITEM_IRONSWORD, ITEM_NONE };\n";
            using var fx = MakeOwnedFixture("ItemList_Foo", macroList, LiteralTables);
            var rom = MakeRom();
            CoreState.ROM = rom;
            CoreState.DecompProject = fx.Project;

            var r = DecompShopSourceWriteCore.TryRouteShopSaveToSource(
                rom, fx.Project, fx.Map, 0x1000u, new ushort[] { 0x0102 });

            Assert.False(r.Routed);
            Assert.Equal(DecompShopRouteOutcome.NotRouted, r.Outcome);
            Assert.Contains("non-literal element", r.Message);
            Assert.Equal(macroList, File.ReadAllText(fx.SourceAbs));   // unchanged
        }

        [Fact]
        public void TryRoute_RomOnlyPolicy_NotRouted()
        {
            string tables =
                "[ { \"table\": \"shop_foo\", \"format\": \"u16-list\", \"writePolicy\": \"romOnly\"," +
                "    \"arrayName\": \"ItemList_Foo\", \"sourceFile\": \"src/shop.c\" } ]";
            using var fx = MakeOwnedFixture("ItemList_Foo", LiteralList, tables);
            var rom = MakeRom();
            CoreState.ROM = rom;
            CoreState.DecompProject = fx.Project;

            var r = DecompShopSourceWriteCore.TryRouteShopSaveToSource(
                rom, fx.Project, fx.Map, 0x1000u, new ushort[] { 0x0102 });

            Assert.False(r.Routed);
            Assert.Equal(DecompShopRouteOutcome.NotRouted, r.Outcome);
            Assert.Equal(LiteralList, File.ReadAllText(fx.SourceAbs));   // unchanged
        }

        [Fact]
        public void TryRoute_NotDecompMode_NotRouted_NoRomMutation()
        {
            // CoreState.IsDecompMode is false (no active project). The classic ROM path
            // must NOT be routed and the ROM must not be mutated.
            using var fx = MakeOwnedFixture("ItemList_Foo", LiteralList, LiteralTables);
            var rom = MakeRom();
            byte[] romBefore = (byte[])rom.Data.Clone();
            CoreState.ROM = rom;
            CoreState.DecompProject = null;   // classic mode

            var r = DecompShopSourceWriteCore.TryRouteShopSaveToSource(
                rom, fx.Project, fx.Map, 0x1000u, new ushort[] { 0x0102 });

            Assert.False(r.Routed);
            Assert.Equal(DecompShopRouteOutcome.NotRouted, r.Outcome);
            Assert.Equal(romBefore, rom.Data);
            Assert.Equal(LiteralList, File.ReadAllText(fx.SourceAbs));
        }

        [Fact]
        public void TryRoute_ProjectNotActive_NotRouted()
        {
            // Decomp mode is active but with a DIFFERENT project than the one passed.
            using var fx = MakeOwnedFixture("ItemList_Foo", LiteralList, LiteralTables);
            var rom = MakeRom();
            CoreState.ROM = rom;
            CoreState.DecompProject = new DecompProject { ProjectRoot = "other" };  // not fx.Project

            var r = DecompShopSourceWriteCore.TryRouteShopSaveToSource(
                rom, fx.Project, fx.Map, 0x1000u, new ushort[] { 0x0102 });

            Assert.False(r.Routed);
            Assert.Equal(DecompShopRouteOutcome.NotRouted, r.Outcome);
        }

        [Fact]
        public void TryRoute_NullAsmMap_NotRouted()
        {
            using var fx = MakeOwnedFixture("ItemList_Foo", LiteralList, LiteralTables);
            var rom = MakeRom();
            CoreState.ROM = rom;
            CoreState.DecompProject = fx.Project;

            var r = DecompShopSourceWriteCore.TryRouteShopSaveToSource(
                rom, fx.Project, null, 0x1000u, new ushort[] { 0x0102 });

            Assert.False(r.Routed);
            Assert.Equal(DecompShopRouteOutcome.NotRouted, r.Outcome);
        }

        // ----------------------------------------------------------------- ROM-bound guard

        [Fact]
        public void TryRoute_NullRom_NotRouted()
        {
            using var fx = MakeOwnedFixture("ItemList_Foo", LiteralList, LiteralTables);
            CoreState.ROM = MakeRom();
            CoreState.DecompProject = fx.Project;

            var r = DecompShopSourceWriteCore.TryRouteShopSaveToSource(
                null, fx.Project, fx.Map, 0x1000u, new ushort[] { 0x0102 });

            Assert.False(r.Routed);
            Assert.Equal(DecompShopRouteOutcome.NotRouted, r.Outcome);
        }

        [Fact]
        public void TryRoute_RomNotActive_NotRouted()
        {
            // The passed ROM is NOT the active CoreState.ROM → the ROM-bound guard fires.
            using var fx = MakeOwnedFixture("ItemList_Foo", LiteralList, LiteralTables);
            var activeRom = MakeRom();
            var otherRom = MakeRom();
            CoreState.ROM = activeRom;
            CoreState.DecompProject = fx.Project;

            var r = DecompShopSourceWriteCore.TryRouteShopSaveToSource(
                otherRom, fx.Project, fx.Map, 0x1000u, new ushort[] { 0x0102 });

            Assert.False(r.Routed);
            Assert.Equal(DecompShopRouteOutcome.NotRouted, r.Outcome);
            Assert.Equal(LiteralList, File.ReadAllText(fx.SourceAbs));   // unchanged
        }

        // ----------------------------------------------------------------- Error path

        [Fact]
        public void TryRoute_SourceFileMissingOnDisk_NotRouted_SourceNotFound()
        {
            // Owner resolves, but its sourceFile does not exist → WriteListEntries returns
            // SourceNotFound, which the router maps to NotRouted (not Error).
            using var fx = MakeOwnedFixture("ItemList_Foo", LiteralList, LiteralTables);
            File.Delete(fx.SourceAbs);
            var rom = MakeRom();
            CoreState.ROM = rom;
            CoreState.DecompProject = fx.Project;

            var r = DecompShopSourceWriteCore.TryRouteShopSaveToSource(
                rom, fx.Project, fx.Map, 0x1000u, new ushort[] { 0x0102 });

            Assert.False(r.Routed);
            Assert.Equal(DecompShopRouteOutcome.NotRouted, r.Outcome);
        }

        [Fact]
        public void TryRoute_InvalidUtf8Source_Error_SourceUntouched()
        {
            // A non-UTF-8 source file makes WriteListEntries return Error (a fault), which the
            // router surfaces as Error — and the file is left byte-identical.
            using var fx = MakeOwnedFixture("ItemList_Foo", LiteralList, LiteralTables);
            byte[] bad = new byte[] { (byte)'/', (byte)'/', 0xFF, (byte)'\n' };
            File.WriteAllBytes(fx.SourceAbs, bad);
            var rom = MakeRom();
            CoreState.ROM = rom;
            CoreState.DecompProject = fx.Project;

            var r = DecompShopSourceWriteCore.TryRouteShopSaveToSource(
                rom, fx.Project, fx.Map, 0x1000u, new ushort[] { 0x0102 });

            Assert.False(r.Routed);
            Assert.Equal(DecompShopRouteOutcome.Error, r.Outcome);
            Assert.Equal(bad, File.ReadAllBytes(fx.SourceAbs));   // untouched
        }
    }
}
