using System;
using System.IO;
using System.Reflection;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Tests for <see cref="WorldMapEventResolverCore"/> — the cross-platform
    /// world-map-event resolver that branches by ROM version (issue #1420).
    ///
    /// The Avalonia "Export World Map Events" button previously read only the
    /// FE8 <c>worldmap_event_on_stageclear_pointer</c> for every version, which
    /// is <c>0x0</c> for FE6/FE7 — so those versions always reported "not
    /// available". These tests prove the FE6/FE7/FE8 branches all resolve.
    /// </summary>
    [Collection("SharedState")]
    public class WorldMapEventResolverCoreTests
    {
        // ============================================================
        // Synthetic-ROM tests — deterministic, run everywhere.
        // ============================================================

        // FE8: stageclear table indexed by mapid directly.
        [Fact]
        public void FE8_StageClear_ResolvesIndexedTable()
        {
            var rom = MakeFe8uRom();
            // stageclear table base at 0x900000; slot[mapid=2] -> event 0x08123456.
            uint tableBase = 0x00900000u;
            WriteU32(rom.Data, (int)rom.RomInfo.worldmap_event_on_stageclear_pointer, tableBase | 0x08000000u);
            uint eventOffset = 0x00123456u;
            WriteU32(rom.Data, (int)(tableBase + 2u * 4u), eventOffset | 0x08000000u);

            uint addr = WorldMapEventResolverCore.GetEventByMapID(rom, 2, isSelect: false);
            Assert.Equal(eventOffset, addr);
        }

        // FE8: mapid 0 -> wmapid 0 -> NOT_FOUND (WF "does not exist" guard).
        [Fact]
        public void FE8_StageClear_Map0_NotFound()
        {
            var rom = MakeFe8uRom();
            uint tableBase = 0x00900000u;
            WriteU32(rom.Data, (int)rom.RomInfo.worldmap_event_on_stageclear_pointer, tableBase | 0x08000000u);
            WriteU32(rom.Data, (int)(tableBase + 0u), 0x08123456u);

            uint addr = WorldMapEventResolverCore.GetEventByMapID(rom, 0, isSelect: false);
            Assert.Equal(U.NOT_FOUND, addr);
        }

        // FE8: stageselect (selected) export indexes by the per-map WMAP plist byte.
        [Fact]
        public void FE8_StageSelect_ResolvesByWmapId()
        {
            var rom = MakeFe8uRom();
            // Plant map 0 carrying worldmap plist byte = 5.
            PlantMapWithWorldmapPlist(rom, mapId: 0, worldmapPlist: 5);
            uint tableBase = 0x00910000u;
            WriteU32(rom.Data, (int)rom.RomInfo.worldmap_event_on_stageselect_pointer, tableBase | 0x08000000u);
            uint eventOffset = 0x00222222u;
            WriteU32(rom.Data, (int)(tableBase + 5u * 4u), eventOffset | 0x08000000u);

            uint addr = WorldMapEventResolverCore.GetEventByMapID(rom, 0, isSelect: true);
            Assert.Equal(eventOffset, addr);
        }

        // FE7: clear button resolves the stageselect table by WMAP plist byte
        // (stageclear pointer is 0x0 on FE7 — the bug being fixed).
        [Fact]
        public void FE7_Clear_ResolvesStageSelectTableByWmapId()
        {
            var rom = MakeFe7uRom();
            Assert.Equal(0u, rom.RomInfo.worldmap_event_on_stageclear_pointer); // the bug premise
            PlantMapWithWorldmapPlist(rom, mapId: 0, worldmapPlist: 3);
            uint tableBase = 0x00B70000u;
            WriteU32(rom.Data, (int)rom.RomInfo.worldmap_event_on_stageselect_pointer, tableBase | 0x08000000u);
            uint eventOffset = 0x00333333u;
            WriteU32(rom.Data, (int)(tableBase + 3u * 4u), eventOffset | 0x08000000u);

            uint addr = WorldMapEventResolverCore.GetEventByMapID(rom, 0, isSelect: false);
            Assert.Equal(eventOffset, addr);
        }

        // FE7: no worldmap plist byte (0) -> NOT_FOUND.
        [Fact]
        public void FE7_NoWorldMapPlist_NotFound()
        {
            var rom = MakeFe7uRom();
            PlantMapWithWorldmapPlist(rom, mapId: 0, worldmapPlist: 0);
            uint tableBase = 0x00B70000u;
            WriteU32(rom.Data, (int)rom.RomInfo.worldmap_event_on_stageselect_pointer, tableBase | 0x08000000u);

            uint addr = WorldMapEventResolverCore.GetEventByMapID(rom, 0, isSelect: false);
            Assert.Equal(U.NOT_FOUND, addr);
        }

        // FE7: select export has no second table -> always NOT_FOUND (WF early return).
        [Fact]
        public void FE7_Select_AlwaysNotFound()
        {
            var rom = MakeFe7uRom();
            PlantMapWithWorldmapPlist(rom, mapId: 0, worldmapPlist: 3);
            uint tableBase = 0x00B70000u;
            WriteU32(rom.Data, (int)rom.RomInfo.worldmap_event_on_stageselect_pointer, tableBase | 0x08000000u);
            WriteU32(rom.Data, (int)(tableBase + 3u * 4u), 0x08333333u);

            uint addr = WorldMapEventResolverCore.GetEventByMapID(rom, 0, isSelect: true);
            Assert.Equal(U.NOT_FOUND, addr);
        }

        // FE6: clear button resolves through the FE6-only WORLDMAP PLIST table.
        [Fact]
        public void FE6_Clear_ResolvesThroughWorldmapPlistTable()
        {
            var rom = MakeFe6Rom();
            Assert.Equal(0u, rom.RomInfo.worldmap_event_on_stageclear_pointer);  // bug premise
            Assert.Equal(0u, rom.RomInfo.worldmap_event_on_stageselect_pointer); // FE6 has neither
            PlantMapWithWorldmapPlist(rom, mapId: 0, worldmapPlist: 4);

            // FE6 WORLDMAP PLIST table base; slot[4] -> event offset.
            uint plistTableBase = 0x00806000u;
            WriteU32(rom.Data, (int)rom.RomInfo.map_worldmapevent_pointer, plistTableBase | 0x08000000u);
            uint eventOffset = 0x00444444u;
            WriteU32(rom.Data, (int)(plistTableBase + 4u * 4u), eventOffset | 0x08000000u);

            uint addr = WorldMapEventResolverCore.GetEventByMapID(rom, 0, isSelect: false);
            Assert.Equal(eventOffset, addr);
        }

        // FE6: select export -> NOT_FOUND (WF early return).
        [Fact]
        public void FE6_Select_AlwaysNotFound()
        {
            var rom = MakeFe6Rom();
            PlantMapWithWorldmapPlist(rom, mapId: 0, worldmapPlist: 4);
            uint plistTableBase = 0x00806000u;
            WriteU32(rom.Data, (int)rom.RomInfo.map_worldmapevent_pointer, plistTableBase | 0x08000000u);
            WriteU32(rom.Data, (int)(plistTableBase + 4u * 4u), 0x08444444u);

            uint addr = WorldMapEventResolverCore.GetEventByMapID(rom, 0, isSelect: true);
            Assert.Equal(U.NOT_FOUND, addr);
        }

        // FE6: worldmap plist byte 0 -> NOT_FOUND.
        [Fact]
        public void FE6_NoWorldMapPlist_NotFound()
        {
            var rom = MakeFe6Rom();
            PlantMapWithWorldmapPlist(rom, mapId: 0, worldmapPlist: 0);
            uint plistTableBase = 0x00806000u;
            WriteU32(rom.Data, (int)rom.RomInfo.map_worldmapevent_pointer, plistTableBase | 0x08000000u);

            uint addr = WorldMapEventResolverCore.GetEventByMapID(rom, 0, isSelect: false);
            Assert.Equal(U.NOT_FOUND, addr);
        }

        // Null ROM -> NOT_FOUND, never throws.
        [Fact]
        public void NullRom_NotFound()
        {
            Assert.Equal(U.NOT_FOUND, WorldMapEventResolverCore.GetEventByMapID(null, 0));
        }

        // A non-FE6/7/8 ROM (e.g. the ROMFE0 testing build, version 0) has no
        // defined world-map-event layout and must fail cleanly — NOT fall through
        // to the FE6 PLIST path and read unrelated pointers. (Review thread #1481.)
        [Fact]
        public void UnknownVersion_NotFound()
        {
            var rom = MakeFe0Rom();
            Assert.NotEqual(6, rom.RomInfo.version);
            Assert.NotEqual(7, rom.RomInfo.version);
            Assert.NotEqual(8, rom.RomInfo.version);

            // Even if the FE6 worldmap pointer slot happened to hold a valid value,
            // an unknown version must never resolve through it.
            Assert.Equal(U.NOT_FOUND, WorldMapEventResolverCore.GetEventByMapID(rom, 1, isSelect: false));
            Assert.Equal(U.NOT_FOUND, WorldMapEventResolverCore.GetEventByMapID(rom, 1, isSelect: true));
        }

        // Overflow guard: a huge wmapid (corrupted PLIST byte / caller mapid) must
        // not wrap the uint slot past the bounds check. (Review thread #1481.)
        [Fact]
        public void FE8_HugeMapId_NoOverflowWrap_NotFound()
        {
            var rom = MakeFe8uRom();
            uint tableBase = 0x00900000u;
            WriteU32(rom.Data, (int)rom.RomInfo.worldmap_event_on_stageclear_pointer, tableBase | 0x08000000u);
            // mapid chosen so baseAddr + mapid*4 would wrap a uint if not widened.
            uint hugeMapId = (0xFFFFFFFFu - tableBase) / 4u + 4u;
            uint addr = WorldMapEventResolverCore.GetEventByMapID(rom, hugeMapId, isSelect: false);
            Assert.Equal(U.NOT_FOUND, addr);
        }

        // Out-of-range slot -> NOT_FOUND (full-slot guard).
        [Fact]
        public void FE8_TableBaseUnsafe_NotFound()
        {
            var rom = MakeFe8uRom();
            // Point the stageclear pointer at garbage (non-pointer).
            WriteU32(rom.Data, (int)rom.RomInfo.worldmap_event_on_stageclear_pointer, 0x00000001u);
            uint addr = WorldMapEventResolverCore.GetEventByMapID(rom, 1, isSelect: false);
            Assert.Equal(U.NOT_FOUND, addr);
        }

        // ============================================================
        // Real-ROM oracle tests — skip when roms/ is absent.
        // ============================================================

        [Fact]
        public void RealRom_FE8U_FirstMapWithWorldMapEvent_Resolves()
        {
            RealRomResolvesSomeWorldMapEvent("FE8U.gba", expectVersion: 8);
        }

        [Fact]
        public void RealRom_FE7U_FirstMapWithWorldMapEvent_Resolves()
        {
            RealRomResolvesSomeWorldMapEvent("FE7U.gba", expectVersion: 7);
        }

        [Fact]
        public void RealRom_FE6_FirstMapWithWorldMapEvent_Resolves()
        {
            RealRomResolvesSomeWorldMapEvent("FE6.gba", expectVersion: 6);
        }

        /// <summary>
        /// On a real ROM, find the first map carrying a non-zero world-map-event
        /// plist byte and assert the resolver returns a valid (non-NOT_FOUND,
        /// safety-offset) event address — proving the version branch is live for
        /// FE6/FE7 (the issue #1420 regression) as well as FE8.
        /// </summary>
        static void RealRomResolvesSomeWorldMapEvent(string romName, int expectVersion)
        {
            string romPath = FindRom(romName);
            if (romPath == null) return; // skip when roms/ absent (worktree env)

            var saved = CoreState.ROM;
            var savedEnc = CoreState.SystemTextEncoder;
            try
            {
                var rom = new ROM();
                if (!rom.Load(romPath, out string _)) return;
                CoreState.ROM = rom;
                CoreState.SystemTextEncoder = new HeadlessSystemTextEncoder(rom);

                Assert.Equal(expectVersion, rom.RomInfo.version);

                var maps = MapSettingCore.MakeMapIDList(rom);
                bool foundAny = false;
                for (uint mapId = 0; mapId < (uint)maps.Count; mapId++)
                {
                    uint mapAddr = MapSettingCore.GetMapAddr(rom, mapId);
                    if (!U.isSafetyOffset(mapAddr, rom)) continue;
                    uint plistPos = rom.RomInfo.map_setting_worldmap_plist_pos;
                    if (!U.isSafetyOffset(mapAddr + plistPos, rom)) continue;
                    uint wmap = rom.u8(mapAddr + plistPos);
                    if (wmap == 0) continue;

                    uint addr = WorldMapEventResolverCore.GetEventByMapID(rom, mapId, isSelect: false);
                    if (addr != U.NOT_FOUND && U.isSafetyOffset(addr, rom))
                    {
                        foundAny = true;
                        break;
                    }
                }

                // Every vanilla FE6/FE7/FE8 ROM has at least one world-map event.
                Assert.True(foundAny, $"{romName}: no map resolved a world-map event (regression?)");
            }
            finally
            {
                CoreState.ROM = saved;
                CoreState.SystemTextEncoder = savedEnc;
            }
        }

        // ============================================================
        // Synthetic-ROM builders.
        // ============================================================

        static ROM MakeFe8uRom()
        {
            var rom = new ROM();
            rom.LoadLow("test-fe8u.gba", new byte[0x1100000], "BE8E01");
            return rom;
        }

        static ROM MakeFe7uRom()
        {
            var rom = new ROM();
            rom.LoadLow("test-fe7u.gba", new byte[0x1100000], "AE7E01");
            return rom;
        }

        static ROM MakeFe6Rom()
        {
            var rom = new ROM();
            rom.LoadLow("test-fe6jp.gba", new byte[0x1000000], "AFEJ01");
            return rom;
        }

        // ROMFE0 testing build — signature "NAZO", version defaults to 0
        // (no FE6/7/8 world-map-event layout).
        static ROM MakeFe0Rom()
        {
            var rom = new ROM();
            rom.LoadLow("test-fe0.gba", new byte[0x1000000], "NAZO");
            return rom;
        }

        /// <summary>
        /// Plant a single valid map setting (mapId) whose first dword is a
        /// pointer (so the record reads as valid), write the world-map-event
        /// plist byte, and add a terminator row so MakeMapIDList stops.
        /// </summary>
        static void PlantMapWithWorldmapPlist(ROM rom, uint mapId, byte worldmapPlist)
        {
            uint mapTableBase = 0x00700000u;
            uint dataSize = rom.RomInfo.map_setting_datasize;
            WriteU32(rom.Data, (int)rom.RomInfo.map_setting_pointer, mapTableBase | 0x08000000u);

            int rec = (int)(mapTableBase + mapId * dataSize);
            // First dword = a pointer → WF treats the record as valid.
            WriteU32(rom.Data, rec + 0, 0x08123456u);
            rom.Data[(uint)rec + rom.RomInfo.map_setting_worldmap_plist_pos] = worldmapPlist;

            // Terminator row: next record's first dword non-pointer.
            int term = (int)(mapTableBase + (mapId + 1) * dataSize);
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
