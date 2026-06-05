// SPDX-License-Identifier: GPL-3.0-or-later
// Independent oracle tests for MapPListResolverCore — the WinForms map-PLIST
// label resolver port (#952, T5 slice A).
//
// Two layers:
//   1. SYNTHETIC ROMs — full control over every PLIST byte so each label
//      branch (NULL / -EMPTY- / UNK / MAP / CONFIG / EVENT / MAPCHANGE /
//      OBJ low+high / PAL / PAL2 / ANIME1 / ANIME2 / FE6 WMEVENT) is exercised
//      with HAND-BUILT expectations. These never compare VM==golden; they
//      assert the literal label the resolver MUST return for a planted byte.
//   2. REAL ROMs (FE6/FE7U/FE8U, skipped when absent) — read a couple of maps'
//      PLISTs DIRECTLY from ROM bytes, then assert the resolver names those
//      exact ids with the expected TYPE prefix + map name. Independence: the
//      WHICH-map / WHICH-type derivation reads raw bytes, not the resolver.

using System.IO;
using System.Reflection;
using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class MapPListResolverCoreTests
    {
        // =================================================================
        // Synthetic-ROM branch coverage (split layout).
        // =================================================================

        /// <summary>plist 0 is the reserved sentinel → "NULL" (split + non-split).</summary>
        [Fact]
        public void Splited_PlistZero_ReturnsNull()
        {
            var rom = MakeSplitFe8uRomWithMap(/*config*/7, /*event*/0, /*mapchange*/0,
                /*mappointer*/0, /*anime1*/0, /*anime2*/0, /*palette*/0, /*palette2*/0,
                /*objLow*/0, /*objHigh*/0);
            var cache = MapPListResolverCore.BuildCache(rom);
            uint cfgBase = rom.p32(rom.RomInfo.map_config_pointer);
            string label = MapPListResolverCore.GetPListNameSplited(rom, 0, cfgBase, cache);
            Assert.Equal("NULL", label);
        }

        /// <summary>
        /// A CONFIG-base row whose id matches the map's config_plist →
        /// "CONFIG {mapname}". Hand-built: config_plist=7, so id 7 under the
        /// CONFIG base must resolve to CONFIG.
        /// </summary>
        [Fact]
        public void Splited_ConfigMatch_ReturnsConfigLabel()
        {
            var rom = MakeSplitFe8uRomWithMap(config: 7, evt: 3, mapchange: 4,
                mappointer: 5, anime1: 8, anime2: 9, palette: 10, palette2: 0,
                objLow: 11, objHigh: 12);
            var cache = MapPListResolverCore.BuildCache(rom);
            uint cfgBase = rom.p32(rom.RomInfo.map_config_pointer);
            string label = MapPListResolverCore.GetPListNameSplited(rom, 7, cfgBase, cache);
            Assert.StartsWith("CONFIG ", label);
        }

        /// <summary>EVENT base + event_plist match → "EVENT {mapname}".</summary>
        [Fact]
        public void Splited_EventMatch_ReturnsEventLabel()
        {
            var rom = MakeSplitFe8uRomWithMap(config: 7, evt: 3, mapchange: 4,
                mappointer: 5, anime1: 8, anime2: 9, palette: 10, palette2: 0,
                objLow: 11, objHigh: 12);
            var cache = MapPListResolverCore.BuildCache(rom);
            uint eventBase = rom.p32(rom.RomInfo.map_event_pointer);
            string label = MapPListResolverCore.GetPListNameSplited(rom, 3, eventBase, cache);
            Assert.StartsWith("EVENT ", label);
        }

        /// <summary>CHANGE base + mapchange_plist match → "MAPCHANGE {mapname}".</summary>
        [Fact]
        public void Splited_MapChangeMatch_ReturnsMapChangeLabel()
        {
            var rom = MakeSplitFe8uRomWithMap(config: 7, evt: 3, mapchange: 4,
                mappointer: 5, anime1: 8, anime2: 9, palette: 10, palette2: 0,
                objLow: 11, objHigh: 12);
            var cache = MapPListResolverCore.BuildCache(rom);
            uint changeBase = rom.p32(rom.RomInfo.map_mapchange_pointer);
            string label = MapPListResolverCore.GetPListNameSplited(rom, 4, changeBase, cache);
            Assert.StartsWith("MAPCHANGE ", label);
        }

        /// <summary>MAP base + mappointer_plist match → "MAP {mapname}".</summary>
        [Fact]
        public void Splited_MapMatch_ReturnsMapLabel()
        {
            var rom = MakeSplitFe8uRomWithMap(config: 7, evt: 3, mapchange: 4,
                mappointer: 5, anime1: 8, anime2: 9, palette: 10, palette2: 0,
                objLow: 11, objHigh: 12);
            var cache = MapPListResolverCore.BuildCache(rom);
            uint mapBase = rom.p32(rom.RomInfo.map_map_pointer_pointer);
            string label = MapPListResolverCore.GetPListNameSplited(rom, 5, mapBase, cache);
            Assert.StartsWith("MAP ", label);
        }

        /// <summary>ANIMATION base + anime1_plist match → "ANIME1 {mapname}".</summary>
        [Fact]
        public void Splited_Anime1Match_ReturnsAnime1Label()
        {
            var rom = MakeSplitFe8uRomWithMap(config: 7, evt: 3, mapchange: 4,
                mappointer: 5, anime1: 8, anime2: 9, palette: 10, palette2: 0,
                objLow: 11, objHigh: 12);
            var cache = MapPListResolverCore.BuildCache(rom);
            uint animBase = rom.p32(rom.RomInfo.map_tileanime1_pointer);
            string label = MapPListResolverCore.GetPListNameSplited(rom, 8, animBase, cache);
            Assert.StartsWith("ANIME1 ", label);
        }

        /// <summary>ANIMATION base + anime2_plist match → "ANIME2 {mapname}".</summary>
        [Fact]
        public void Splited_Anime2Match_ReturnsAnime2Label()
        {
            var rom = MakeSplitFe8uRomWithMap(config: 7, evt: 3, mapchange: 4,
                mappointer: 5, anime1: 8, anime2: 9, palette: 10, palette2: 0,
                objLow: 11, objHigh: 12);
            var cache = MapPListResolverCore.BuildCache(rom);
            uint animBase = rom.p32(rom.RomInfo.map_tileanime1_pointer);
            string label = MapPListResolverCore.GetPListNameSplited(rom, 9, animBase, cache);
            Assert.StartsWith("ANIME2 ", label);
        }

        /// <summary>OBJECT base + palette_plist match → "PAL {mapname}".</summary>
        [Fact]
        public void Splited_PaletteMatch_ReturnsPalLabel()
        {
            var rom = MakeSplitFe8uRomWithMap(config: 7, evt: 3, mapchange: 4,
                mappointer: 5, anime1: 8, anime2: 9, palette: 10, palette2: 0,
                objLow: 11, objHigh: 12);
            var cache = MapPListResolverCore.BuildCache(rom);
            uint objBase = rom.p32(rom.RomInfo.map_obj_pointer);
            string label = MapPListResolverCore.GetPListNameSplited(rom, 10, objBase, cache);
            Assert.StartsWith("PAL ", label);
        }

        /// <summary>
        /// OBJECT base + packed obj_plist LOW byte match → "OBJ {mapname}".
        /// obj_plist is a u16; the low byte (11) is one plist id.
        /// </summary>
        [Fact]
        public void Splited_ObjLowByteMatch_ReturnsObjLabel()
        {
            var rom = MakeSplitFe8uRomWithMap(config: 7, evt: 3, mapchange: 4,
                mappointer: 5, anime1: 8, anime2: 9, palette: 10, palette2: 0,
                objLow: 11, objHigh: 12);
            var cache = MapPListResolverCore.BuildCache(rom);
            uint objBase = rom.p32(rom.RomInfo.map_obj_pointer);
            string label = MapPListResolverCore.GetPListNameSplited(rom, 11, objBase, cache);
            Assert.StartsWith("OBJ ", label);
        }

        /// <summary>
        /// OBJECT base + packed obj_plist HIGH byte match → "OBJ {mapname}".
        /// The high byte (12) is a SECOND plist id (FE7-style dual OBJ).
        /// </summary>
        [Fact]
        public void Splited_ObjHighByteMatch_ReturnsObjLabel()
        {
            var rom = MakeSplitFe8uRomWithMap(config: 7, evt: 3, mapchange: 4,
                mappointer: 5, anime1: 8, anime2: 9, palette: 10, palette2: 0,
                objLow: 11, objHigh: 12);
            var cache = MapPListResolverCore.BuildCache(rom);
            uint objBase = rom.p32(rom.RomInfo.map_obj_pointer);
            string label = MapPListResolverCore.GetPListNameSplited(rom, 12, objBase, cache);
            Assert.StartsWith("OBJ ", label);
        }

        /// <summary>
        /// A split-layout id with NO matching field returns "-EMPTY-" (NOT
        /// "UNK" — that's the non-split sentinel). Id 200 isn't used by any
        /// field in our planted map.
        /// </summary>
        [Fact]
        public void Splited_NoMatch_ReturnsEmpty()
        {
            var rom = MakeSplitFe8uRomWithMap(config: 7, evt: 3, mapchange: 4,
                mappointer: 5, anime1: 8, anime2: 9, palette: 10, palette2: 0,
                objLow: 11, objHigh: 12);
            var cache = MapPListResolverCore.BuildCache(rom);
            uint cfgBase = rom.p32(rom.RomInfo.map_config_pointer);
            string label = MapPListResolverCore.GetPListNameSplited(rom, 200, cfgBase, cache);
            Assert.Equal("-EMPTY-", label);
        }

        /// <summary>
        /// Type disambiguation: id 7 IS config_plist, but querying it under the
        /// MAP base (not CONFIG) must NOT return "CONFIG" — config only matches
        /// when the base type is CONFIG. With no MAP field == 7, returns
        /// "-EMPTY-".
        /// </summary>
        [Fact]
        public void Splited_ConfigIdUnderWrongBase_DoesNotMatchConfig()
        {
            var rom = MakeSplitFe8uRomWithMap(config: 7, evt: 3, mapchange: 4,
                mappointer: 5, anime1: 8, anime2: 9, palette: 10, palette2: 0,
                objLow: 11, objHigh: 12);
            var cache = MapPListResolverCore.BuildCache(rom);
            uint mapBase = rom.p32(rom.RomInfo.map_map_pointer_pointer);
            string label = MapPListResolverCore.GetPListNameSplited(rom, 7, mapBase, cache);
            Assert.Equal("-EMPTY-", label);
        }

        // =================================================================
        // PAL2 — second-palette patch offset (146 vs 45).
        // =================================================================

        /// <summary>
        /// When the Flag0x28_146 patch is installed, palette2_plist is read
        /// from map-record offset 146, and a match under OBJECT → "PAL2".
        /// We plant the patch signature at the FE8U detector address (0x19950
        /// = {0x00,0x4A}) and palette2 id at offset 146.
        /// </summary>
        [Fact]
        public void Splited_Pal2MatchWithPatch146_ReturnsPal2Label()
        {
            PatchDetection.ClearCacheMapSecondPalette();
            var rom = MakeSplitFe8uRomWithMap(config: 7, evt: 3, mapchange: 4,
                mappointer: 5, anime1: 8, anime2: 9, palette: 10, palette2: 0,
                objLow: 11, objHigh: 12);
            // Install the Flag0x28_146 signature for FE8U.
            rom.Data[0x19950] = 0x00;
            rom.Data[0x19951] = 0x4A;
            // Plant palette2 id 20 at map offset 146.
            uint mapAddr = MapSettingCore.GetMapAddr(rom, 0);
            rom.Data[mapAddr + 146] = 20;

            Assert.Equal(PatchDetection.MapSecondPalette_extends.Flag0x28_146,
                PatchDetection.SearchFlag0x28ToMapSecondPalettePatch(rom));

            var cache = MapPListResolverCore.BuildCache(rom);
            uint objBase = rom.p32(rom.RomInfo.map_obj_pointer);
            string label = MapPListResolverCore.GetPListNameSplited(rom, 20, objBase, cache);
            Assert.StartsWith("PAL2 ", label);
            PatchDetection.ClearCacheMapSecondPalette();
        }

        // =================================================================
        // Non-split layout semantics — UNK on no match, first-field-wins.
        // =================================================================

        /// <summary>
        /// Non-split layout: every PLIST table shares one base, so the
        /// resolver scans ALL fields and returns "UNK" (NOT "-EMPTY-") when
        /// nothing matches. plist 0 still → "NULL".
        /// </summary>
        [Fact]
        public void NotSplite_NoMatch_ReturnsUnk()
        {
            var rom = MakeNonSplitFe8uRomWithMap(config: 7, evt: 3, mapchange: 4,
                mappointer: 5, anime1: 8, anime2: 9, palette: 10, palette2: 0,
                objLow: 11, objHigh: 12);
            var cache = MapPListResolverCore.BuildCache(rom);
            string label = MapPListResolverCore.GetPListNameNotSplite(rom, 200, cache);
            Assert.Equal("UNK", label);
        }

        [Fact]
        public void NotSplite_PlistZero_ReturnsNull()
        {
            var rom = MakeNonSplitFe8uRomWithMap(config: 7, evt: 3, mapchange: 4,
                mappointer: 5, anime1: 8, anime2: 9, palette: 10, palette2: 0,
                objLow: 11, objHigh: 12);
            var cache = MapPListResolverCore.BuildCache(rom);
            string label = MapPListResolverCore.GetPListNameNotSplite(rom, 0, cache);
            Assert.Equal("NULL", label);
        }

        /// <summary>
        /// Non-split match WITHOUT base disambiguation: config_plist=7, no
        /// other field == 7, so id 7 resolves to "CONFIG" purely by the
        /// first-field-wins scan (no base type passed).
        /// </summary>
        [Fact]
        public void NotSplite_ConfigOnlyField_ReturnsConfigLabel()
        {
            var rom = MakeNonSplitFe8uRomWithMap(config: 7, evt: 3, mapchange: 4,
                mappointer: 5, anime1: 8, anime2: 9, palette: 10, palette2: 0,
                objLow: 11, objHigh: 12);
            var cache = MapPListResolverCore.BuildCache(rom);
            string label = MapPListResolverCore.GetPListNameNotSplite(rom, 7, cache);
            Assert.StartsWith("CONFIG ", label);
        }

        // =================================================================
        // FE6 WMEVENT branch — the quirk: wmapevent_plist == 0 (after the
        // plist==0 early-return) under WORLDMAP_FE6ONLY → "WMEVENT".
        // =================================================================

        /// <summary>
        /// FE6 split layout: a map whose world-map event PLIST byte is 0,
        /// queried under the WORLDMAP base with a NON-zero plist id, resolves
        /// to "WMEVENT {mapname}". This preserves the WF quirk literally —
        /// the branch fires when wmapevent_plist == 0 (not == plist).
        /// </summary>
        [Fact]
        public void Splited_Fe6WorldMapEventZero_ReturnsWmEventLabel()
        {
            var rom = MakeSplitFe6RomWithMap(config: 7, evt: 3, mapchange: 4,
                mappointer: 5, anime1: 8, anime2: 9, palette: 10,
                objLow: 11, objHigh: 12, worldmapEvent: 0);
            var cache = MapPListResolverCore.BuildCache(rom);
            uint wmapBase = rom.p32(rom.RomInfo.map_worldmapevent_pointer);
            // Query a non-zero id (1) under the WORLDMAP base; the WF quirk
            // fires because the map's wmapevent byte is 0.
            string label = MapPListResolverCore.GetPListNameSplited(rom, 1, wmapBase, cache);
            Assert.StartsWith("WMEVENT ", label);
        }

        /// <summary>
        /// FE6: if the world-map event byte is NON-zero, the WMEVENT branch
        /// does NOT fire for a mismatched id → "-EMPTY-".
        /// </summary>
        [Fact]
        public void Splited_Fe6WorldMapEventNonZero_NoWmEventForMismatch()
        {
            var rom = MakeSplitFe6RomWithMap(config: 7, evt: 3, mapchange: 4,
                mappointer: 5, anime1: 8, anime2: 9, palette: 10,
                objLow: 11, objHigh: 12, worldmapEvent: 50);
            var cache = MapPListResolverCore.BuildCache(rom);
            uint wmapBase = rom.p32(rom.RomInfo.map_worldmapevent_pointer);
            string label = MapPListResolverCore.GetPListNameSplited(rom, 1, wmapBase, cache);
            Assert.Equal("-EMPTY-", label);
        }

        // =================================================================
        // ConvertBaseAddrToType — base→type map incl FE6 WORLDMAP.
        // =================================================================

        [Fact]
        public void ConvertBaseAddrToType_KnownBases_MapToTypes()
        {
            var rom = MakeSplitFe8uRomWithMap(config: 7, evt: 3, mapchange: 4,
                mappointer: 5, anime1: 8, anime2: 9, palette: 10, palette2: 0,
                objLow: 11, objHigh: 12);

            Assert.Equal(MapChangeCore.PlistType.CONFIG,
                MapPListResolverCore.ConvertBaseAddrToType(rom, rom.p32(rom.RomInfo.map_config_pointer)));
            Assert.Equal(MapChangeCore.PlistType.MAP,
                MapPListResolverCore.ConvertBaseAddrToType(rom, rom.p32(rom.RomInfo.map_map_pointer_pointer)));
            Assert.Equal(MapChangeCore.PlistType.CHANGE,
                MapPListResolverCore.ConvertBaseAddrToType(rom, rom.p32(rom.RomInfo.map_mapchange_pointer)));
            Assert.Equal(MapChangeCore.PlistType.EVENT,
                MapPListResolverCore.ConvertBaseAddrToType(rom, rom.p32(rom.RomInfo.map_event_pointer)));
            Assert.Equal(MapChangeCore.PlistType.OBJECT,
                MapPListResolverCore.ConvertBaseAddrToType(rom, rom.p32(rom.RomInfo.map_obj_pointer)));
            // Unknown base → null (WF UNKNOWN).
            Assert.Null(MapPListResolverCore.ConvertBaseAddrToType(rom, 0x08DEAD00u));
        }

        [Fact]
        public void ConvertBaseAddrToType_Fe6WorldMapBase_MapsToWorldmap()
        {
            var rom = MakeSplitFe6RomWithMap(config: 7, evt: 3, mapchange: 4,
                mappointer: 5, anime1: 8, anime2: 9, palette: 10,
                objLow: 11, objHigh: 12, worldmapEvent: 0);
            Assert.Equal(MapChangeCore.PlistType.WORLDMAP_FE6ONLY,
                MapPListResolverCore.ConvertBaseAddrToType(rom,
                    rom.p32(rom.RomInfo.map_worldmapevent_pointer)));
        }

        // =================================================================
        // Real-ROM oracle — hand-derived from actual map bytes.
        // =================================================================

        /// <summary>
        /// FE8U: read map 0's config_plist directly, then assert the resolver
        /// names id config_plist under the appropriate base as
        /// "CONFIG {map0name}" (split) or scans to CONFIG (non-split). The map
        /// name is read independently via GetMapNameWhereAddr. Skips when the
        /// ROM is absent or the plist would collide with an earlier field.
        /// </summary>
        [Fact]
        public void RealRom_FE8U_ResolvesConfigPlistForMap0()
        {
            RealRomConfigOracle("FE8U.gba");
        }

        [Fact]
        public void RealRom_FE7U_ResolvesConfigPlistForMap0()
        {
            RealRomConfigOracle("FE7U.gba");
        }

        /// <summary>
        /// FE6: exercises the resolver on a real FE6 ROM (the only version with
        /// the WMEVENT branch live). Reads map 0's mappointer_plist directly
        /// and asserts the resolver names it "MAP {map0name}" under the MAP
        /// base. The name is read independently.
        /// </summary>
        [Fact]
        public void RealRom_FE6_ResolvesMapPlistForMap0()
        {
            string romPath = FindRom("FE6.gba");
            if (romPath == null) return; // skip

            var saved = CoreState.ROM;
            var savedEnc = CoreState.SystemTextEncoder;
            try
            {
                var rom = new ROM();
                if (!rom.Load(romPath, out string _)) return;
                CoreState.ROM = rom;
                CoreState.SystemTextEncoder = new HeadlessSystemTextEncoder(rom);

                uint mapAddr = MapSettingCore.GetMapAddr(rom, 0);
                if (!U.isSafetyOffset(mapAddr, rom)) return;

                MapPListResolverCore.PLists pl =
                    MapPListResolverCore.GetMapPListsWhereAddr(rom, mapAddr);
                uint mapPlist = pl.mappointer_plist;
                if (mapPlist == 0) return; // can't assert NULL→MAP

                string expectedName = MapSettingCore.GetMapNameWhereAddr(rom, mapAddr);
                string label = MapPListResolverCore.ResolveLabel(
                    rom, MapChangeCore.PlistType.MAP, mapPlist);

                // The label must NOT be a raw hex pointer.
                Assert.DoesNotContain("0x08", label);
                Assert.False(label.StartsWith("0x"), $"label must be resolved, got '{label}'");

                if (MapChangeCore.IsPlistSplit(rom))
                {
                    // Split: MAP base disambiguates → "MAP {name}".
                    Assert.Equal("MAP " + expectedName, label);
                }
                else
                {
                    // Non-split: first-field-wins; the label is some TYPE name,
                    // and if no earlier field shares mapPlist it is MAP.
                    Assert.NotEqual("UNK", label);
                }
            }
            finally
            {
                CoreState.ROM = saved;
                CoreState.SystemTextEncoder = savedEnc;
            }
        }

        // -----------------------------------------------------------------
        // Real-ROM oracle helper: assert config_plist resolves under CONFIG.
        // -----------------------------------------------------------------
        static void RealRomConfigOracle(string romName)
        {
            string romPath = FindRom(romName);
            if (romPath == null) return; // skip

            var saved = CoreState.ROM;
            var savedEnc = CoreState.SystemTextEncoder;
            try
            {
                var rom = new ROM();
                if (!rom.Load(romPath, out string _)) return;
                CoreState.ROM = rom;
                CoreState.SystemTextEncoder = new HeadlessSystemTextEncoder(rom);

                uint mapAddr = MapSettingCore.GetMapAddr(rom, 0);
                if (!U.isSafetyOffset(mapAddr, rom)) return;

                MapPListResolverCore.PLists pl =
                    MapPListResolverCore.GetMapPListsWhereAddr(rom, mapAddr);
                uint cfgPlist = pl.config_plist;
                if (cfgPlist == 0) return;

                string expectedName = MapSettingCore.GetMapNameWhereAddr(rom, mapAddr);
                string label = MapPListResolverCore.ResolveLabel(
                    rom, MapChangeCore.PlistType.CONFIG, cfgPlist);

                // Must be resolved, not a raw pointer.
                Assert.False(label.StartsWith("0x"), $"label must be resolved, got '{label}'");

                if (MapChangeCore.IsPlistSplit(rom))
                {
                    Assert.Equal("CONFIG " + expectedName, label);
                }
                else
                {
                    // Non-split: first matching field. config is scanned 3rd
                    // (after anime1/anime2), so a config-only id → CONFIG, but
                    // if anime shares the id it could differ. Assert non-UNK +
                    // that the suffix is the right map name.
                    Assert.NotEqual("UNK", label);
                    Assert.EndsWith(expectedName, label);
                }
            }
            finally
            {
                CoreState.ROM = saved;
                CoreState.SystemTextEncoder = savedEnc;
            }
        }

        // =================================================================
        // Synthetic-ROM builders.
        // =================================================================

        static ROM MakeFe8uRom()
        {
            var rom = new ROM();
            rom.LoadLow("test-fe8u.gba", new byte[0x1100000], "BE8E01");
            return rom;
        }

        static ROM MakeFe6Rom()
        {
            var rom = new ROM();
            rom.LoadLow("test-fe6jp.gba", new byte[0x1000000], "AFEJ01");
            return rom;
        }

        /// <summary>
        /// Plant one valid map setting (id 0) carrying the given PLIST bytes,
        /// and lay out SIX SEPARATE pointer-table bases (split layout) so each
        /// PLIST type is independently addressable. Each table has 256 dword
        /// entries all pointing to a benign in-ROM offset (so isSafetyOffset
        /// passes when the resolver derefs nothing — it only reads ids).
        /// </summary>
        static ROM MakeSplitFe8uRomWithMap(
            uint config, uint evt, uint mapchange, uint mappointer,
            uint anime1, uint anime2, uint palette, uint palette2,
            uint objLow, uint objHigh)
        {
            var rom = MakeFe8uRom();
            PlantMap(rom, config, evt, mapchange, mappointer, anime1, anime2,
                palette, palette2, objLow, objHigh);
            // Six distinct bases → split layout.
            WriteU32(rom.Data, (int)rom.RomInfo.map_config_pointer,        0x08800000u);
            WriteU32(rom.Data, (int)rom.RomInfo.map_tileanime1_pointer,    0x08801000u);
            WriteU32(rom.Data, (int)rom.RomInfo.map_tileanime2_pointer,    0x08801000u); // ANIME2 shares ANIME1
            WriteU32(rom.Data, (int)rom.RomInfo.map_obj_pointer,           0x08802000u);
            WriteU32(rom.Data, (int)rom.RomInfo.map_pal_pointer,           0x08802000u); // PAL shares OBJ
            WriteU32(rom.Data, (int)rom.RomInfo.map_map_pointer_pointer,   0x08803000u);
            WriteU32(rom.Data, (int)rom.RomInfo.map_mapchange_pointer,     0x08804000u);
            WriteU32(rom.Data, (int)rom.RomInfo.map_event_pointer,         0x08805000u);
            return rom;
        }

        static ROM MakeNonSplitFe8uRomWithMap(
            uint config, uint evt, uint mapchange, uint mappointer,
            uint anime1, uint anime2, uint palette, uint palette2,
            uint objLow, uint objHigh)
        {
            var rom = MakeFe8uRom();
            PlantMap(rom, config, evt, mapchange, mappointer, anime1, anime2,
                palette, palette2, objLow, objHigh);
            // All bases identical → non-split layout.
            uint shared = 0x08800000u;
            WriteU32(rom.Data, (int)rom.RomInfo.map_config_pointer,      shared);
            WriteU32(rom.Data, (int)rom.RomInfo.map_tileanime1_pointer,  shared);
            WriteU32(rom.Data, (int)rom.RomInfo.map_tileanime2_pointer,  shared);
            WriteU32(rom.Data, (int)rom.RomInfo.map_obj_pointer,         shared);
            WriteU32(rom.Data, (int)rom.RomInfo.map_pal_pointer,         shared);
            WriteU32(rom.Data, (int)rom.RomInfo.map_map_pointer_pointer, shared);
            WriteU32(rom.Data, (int)rom.RomInfo.map_mapchange_pointer,   shared);
            WriteU32(rom.Data, (int)rom.RomInfo.map_event_pointer,       shared);
            return rom;
        }

        static ROM MakeSplitFe6RomWithMap(
            uint config, uint evt, uint mapchange, uint mappointer,
            uint anime1, uint anime2, uint palette,
            uint objLow, uint objHigh, uint worldmapEvent)
        {
            var rom = MakeFe6Rom();
            PlantMap(rom, config, evt, mapchange, mappointer, anime1, anime2,
                palette, 0, objLow, objHigh);
            // FE6 world-map event PLIST byte.
            uint mapAddr = MapSettingCore.GetMapAddr(rom, 0);
            rom.Data[mapAddr + rom.RomInfo.map_setting_worldmap_plist_pos] = (byte)worldmapEvent;

            WriteU32(rom.Data, (int)rom.RomInfo.map_config_pointer,        0x08800000u);
            WriteU32(rom.Data, (int)rom.RomInfo.map_tileanime1_pointer,    0x08801000u);
            WriteU32(rom.Data, (int)rom.RomInfo.map_tileanime2_pointer,    0x08801000u);
            WriteU32(rom.Data, (int)rom.RomInfo.map_obj_pointer,           0x08802000u);
            WriteU32(rom.Data, (int)rom.RomInfo.map_pal_pointer,           0x08802000u);
            WriteU32(rom.Data, (int)rom.RomInfo.map_map_pointer_pointer,   0x08803000u);
            WriteU32(rom.Data, (int)rom.RomInfo.map_mapchange_pointer,     0x08804000u);
            WriteU32(rom.Data, (int)rom.RomInfo.map_event_pointer,         0x08805000u);
            WriteU32(rom.Data, (int)rom.RomInfo.map_worldmapevent_pointer, 0x08806000u);
            return rom;
        }

        /// <summary>
        /// Lay out a single valid map setting (id 0) at a fixed base and write
        /// the given PLIST bytes at their real per-record offsets. Adds a
        /// terminator row so MakeMapIDList stops at exactly 1 entry.
        /// </summary>
        static void PlantMap(ROM rom,
            uint config, uint evt, uint mapchange, uint mappointer,
            uint anime1, uint anime2, uint palette, uint palette2,
            uint objLow, uint objHigh)
        {
            uint mapTableBase = 0x00700000u;
            uint dataSize = rom.RomInfo.map_setting_datasize;
            WriteU32(rom.Data, (int)rom.RomInfo.map_setting_pointer, mapTableBase | 0x08000000u);

            int rec = (int)mapTableBase;
            // First dword = a pointer → WF treats the record as valid.
            WriteU32(rom.Data, rec + 0, 0x08123456u);
            // obj_plist is u16 @ +4: low | high<<8.
            ushort obj = (ushort)((objLow & 0xFF) | ((objHigh & 0xFF) << 8));
            rom.Data[rec + 4] = (byte)(obj & 0xFF);
            rom.Data[rec + 5] = (byte)((obj >> 8) & 0xFF);
            rom.Data[rec + 6] = (byte)palette;     // palette_plist
            rom.Data[rec + 7] = (byte)config;      // config_plist
            rom.Data[rec + 8] = (byte)mappointer;  // mappointer_plist
            rom.Data[rec + 9] = (byte)anime1;      // anime1_plist
            rom.Data[rec + 10] = (byte)anime2;     // anime2_plist
            rom.Data[rec + 11] = (byte)mapchange;  // mapchange_plist
            rom.Data[rec + (int)rom.RomInfo.map_setting_event_plist_pos] = (byte)evt;
            if (palette2 != 0)
            {
                // Caller is responsible for the patch signature; this just
                // seeds the byte at +146 (FE8 default Flag0x28_146 offset).
                rom.Data[rec + 146] = (byte)palette2;
            }

            // Terminator: next record's first dword = 0 (non-pointer) so the
            // map enumeration stops after 1 entry.
            int term = (int)(mapTableBase + dataSize);
            WriteU32(rom.Data, term + 0, 0x00000000u);
        }

        static void WriteU32(byte[] data, int offset, uint value)
        {
            data[offset + 0] = (byte)(value & 0xFF);
            data[offset + 1] = (byte)((value >> 8) & 0xFF);
            data[offset + 2] = (byte)((value >> 16) & 0xFF);
            data[offset + 3] = (byte)((value >> 24) & 0xFF);
        }

        // Walk up from the test assembly to find roms/<name>.
        static string FindRom(string romName)
        {
            string thisAssembly = Assembly.GetExecutingAssembly().Location;
            string dir = Path.GetDirectoryName(thisAssembly);
            for (int i = 0; i < 10 && dir != null; i++)
            {
                if (File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
                {
                    string path = Path.Combine(dir, "roms", romName);
                    if (File.Exists(path)) return path;
                    break;
                }
                dir = Path.GetDirectoryName(dir);
            }
            return null;
        }
    }
}
