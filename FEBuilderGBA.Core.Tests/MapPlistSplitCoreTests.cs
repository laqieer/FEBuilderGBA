// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for MapPlistSplitCore — the cross-platform port of the WinForms
// "PLIST Split/Expand" operation (MapPointerForm.PListSplitsExpands), #1432.
//
// The split breaks the single shared PLIST table into one independent
// 256-entry table per PLIST PURPOSE. These tests use a synthetic FE8U ROM
// with a real map table + a shared PLIST table (the vanilla layout) and
// assert the WF invariants:
//   * CanSplit is true before / false after;
//   * IsPlistSplit flips false→true;
//   * the 6 purpose groups become pairwise distinct, BUT OBJECT==PAL and
//     ANIME1==ANIME2 stay shared (Copilot plan-review point 3);
//   * a map's per-purpose pointer survives the split (deref preserved);
//   * the old shared region is wiped 0x00;
//   * a forced free-space fault leaves the ROM byte-identical (point 4).
using System.Collections.Generic;
using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class MapPlistSplitCoreTests
    {
        // ----------------------------------------------------------------
        // CanSplit gating.
        // ----------------------------------------------------------------

        [Fact]
        public void CanSplit_VanillaSharedTable_True()
        {
            var rom = MakeVanillaRom();
            Assert.True(MapPlistSplitCore.CanSplit(rom));
        }

        [Fact]
        public void CanSplit_AlreadySplit_False()
        {
            var rom = MakeVanillaRom();
            Assert.True(MapPlistSplitCore.Split(rom, out _));
            // After a successful split the operation must no longer be offered.
            Assert.False(MapPlistSplitCore.CanSplit(rom));
        }

        [Fact]
        public void CanSplit_NullRom_False()
        {
            Assert.False(MapPlistSplitCore.CanSplit(null));
        }

        // ----------------------------------------------------------------
        // Split — happy path + WF invariants.
        // ----------------------------------------------------------------

        [Fact]
        public void Split_FlipsIsPlistSplit_True()
        {
            var rom = MakeVanillaRom();
            Assert.False(MapChangeCore.IsPlistSplit(rom));

            bool ok = MapPlistSplitCore.Split(rom, out string error);

            Assert.True(ok);
            Assert.Equal("", error);
            Assert.True(MapChangeCore.IsPlistSplit(rom));
        }

        [Fact]
        public void Split_PurposeGroupsDistinct_SharedPairsPreserved()
        {
            var rom = MakeVanillaRom();
            Assert.True(MapPlistSplitCore.Split(rom, out _));

            ROMFEINFO info = rom.RomInfo;
            uint cfg    = rom.p32(info.map_config_pointer);
            uint obj    = rom.p32(info.map_obj_pointer);
            uint pal    = rom.p32(info.map_pal_pointer);
            uint map    = rom.p32(info.map_map_pointer_pointer);
            uint change = rom.p32(info.map_mapchange_pointer);
            uint anime1 = rom.p32(info.map_tileanime1_pointer);
            uint anime2 = rom.p32(info.map_tileanime2_pointer);
            uint evt    = rom.p32(info.map_event_pointer);

            // SHARED PAIRS preserved (WF folds OBJ/PAL and ANIME1/ANIME2 into
            // one split table each).
            Assert.Equal(obj, pal);
            Assert.Equal(anime1, anime2);

            // The 6 distinct PURPOSE GROUPS are pairwise distinct.
            var groups = new List<uint> { cfg, obj, map, change, anime1, evt };
            for (int i = 0; i < groups.Count; i++)
                for (int j = i + 1; j < groups.Count; j++)
                    Assert.NotEqual(groups[i], groups[j]);
        }

        [Fact]
        public void Split_PreservesPerMapPointer_ConfigDerefUnchanged()
        {
            var rom = MakeVanillaRom();

            // Map 0 uses config_plist=4 → that table slot derefs to a known
            // CONFIG data block. Capture the pre-split deref.
            uint sharedBase = rom.p32(rom.RomInfo.map_config_pointer);
            uint preConfigData = rom.p32(sharedBase + 4u * 4u);
            Assert.NotEqual(0u, preConfigData);

            Assert.True(MapPlistSplitCore.Split(rom, out _));

            // After the split the NEW config table's slot 4 must still deref to
            // the same CONFIG data block (the map keeps pointing at its data).
            uint newConfigBase = rom.p32(rom.RomInfo.map_config_pointer);
            uint postConfigData = rom.p32(newConfigBase + 4u * 4u);
            Assert.Equal(preConfigData, postConfigData);
        }

        [Fact]
        public void Split_PreservesPerMapPointer_ObjAndChangeDeref()
        {
            var rom = MakeVanillaRom();
            uint sharedBase = rom.p32(rom.RomInfo.map_obj_pointer);
            // obj_plist low byte = 2, mapchange_plist = 6 (see MakeVanillaRom).
            uint preObjData    = rom.p32(sharedBase + 2u * 4u);
            uint preChangeData = rom.p32(sharedBase + 6u * 4u);

            Assert.True(MapPlistSplitCore.Split(rom, out _));

            uint newObjBase    = rom.p32(rom.RomInfo.map_obj_pointer);
            uint newChangeBase = rom.p32(rom.RomInfo.map_mapchange_pointer);
            Assert.Equal(preObjData,    rom.p32(newObjBase + 2u * 4u));
            Assert.Equal(preChangeData, rom.p32(newChangeBase + 6u * 4u));
        }

        [Fact]
        public void Split_WipesOldSharedRegion_ToZero()
        {
            var rom = MakeVanillaRom();
            uint oldShared = rom.p32(rom.RomInfo.map_config_pointer);

            Assert.True(MapPlistSplitCore.Split(rom, out _));

            // The old shared table region was filled with 0x00. Sample the
            // first default-size span (the WF wipe extent).
            uint wipeBytes = rom.RomInfo.map_map_pointer_list_default_size * 4u;
            for (uint i = 0; i < wipeBytes; i++)
                Assert.Equal(0u, rom.u8(oldShared + i));
        }

        // ----------------------------------------------------------------
        // Atomicity — a forced free-space fault leaves the ROM byte-identical.
        // ----------------------------------------------------------------

        [Fact]
        public void Split_ForcedFault_LeavesRomByteIdentical()
        {
            var rom = MakeVanillaRom();
            byte[] before = (byte[])rom.Data.Clone();
            int beforeLen = rom.Data.Length;

            // Fault-inject the 3rd purpose (MAP) so the first two appends
            // (OBJECT, CONFIG) already wrote + repointed — proving the rollback
            // unwinds partial appends, repoints AND any ROM growth.
            bool ok = MapPlistSplitCore.Split(rom, out string error,
                purpose => purpose == MapChangeCore.PlistType.MAP);

            Assert.False(ok);
            Assert.NotEqual("", error);

            // ROM restored byte-for-byte (length + content).
            Assert.Equal(beforeLen, rom.Data.Length);
            Assert.Equal(before, rom.Data);
            // And it is still NOT split (the operation is a clean no-op on fault).
            Assert.False(MapChangeCore.IsPlistSplit(rom));
        }

        [Fact]
        public void Split_ForcedFaultOnFirstPurpose_ByteIdentical()
        {
            var rom = MakeVanillaRom();
            byte[] before = (byte[])rom.Data.Clone();

            bool ok = MapPlistSplitCore.Split(rom, out _,
                purpose => purpose == MapChangeCore.PlistType.OBJECT);

            Assert.False(ok);
            Assert.Equal(before, rom.Data);
        }

        [Fact]
        public void Split_InsideActiveUndoScope_IsRefused_WithoutMutation()
        {
            // ROM.BeginUndoScope is thread-static / non-reentrant. Split must
            // refuse when an OUTER ambient scope is already open so it does not
            // clobber the caller's scope (#1432 Copilot review).
            var rom = MakeVanillaRom();
            byte[] before = (byte[])rom.Data.Clone();

            var outer = new Undo.UndoData
            {
                time = System.DateTime.Now,
                name = "outer",
                list = new System.Collections.Generic.List<Undo.UndoPostion>(),
                filesize = (uint)rom.Data.Length,
                is_f5test = false,
            };
            using (ROM.BeginUndoScope(outer))
            {
                bool ok = MapPlistSplitCore.Split(rom, out string error);
                Assert.False(ok);
                Assert.NotEqual("", error);
            }
            Assert.Equal(before, rom.Data); // no mutation
        }

        [Fact]
        public void Split_AlreadySplit_RefusesWithoutMutation()
        {
            var rom = MakeVanillaRom();
            Assert.True(MapPlistSplitCore.Split(rom, out _));
            byte[] afterFirst = (byte[])rom.Data.Clone();

            bool ok = MapPlistSplitCore.Split(rom, out string error);
            Assert.False(ok);
            Assert.NotEqual("", error);
            Assert.Equal(afterFirst, rom.Data); // second call mutates nothing
        }

        // ----------------------------------------------------------------
        // Synthetic ROM builder.
        //
        // FE8U layout: map_setting_datasize=148, event_plist_pos=116.
        // Two maps, each with a pointer in dword 0 (→ IsMapSettingValid true),
        // distinct per-purpose PLIST bytes. ONE shared PLIST table (all
        // map_*_pointer slots point at it) whose entries deref to distinct
        // data blocks. This is the vanilla "not split" layout.
        // ----------------------------------------------------------------
        static ROM MakeVanillaRom()
        {
            var rom = new ROM();
            rom.LoadLow("test-fe8u.gba", new byte[0x1100000], "BE8E01");
            ROMFEINFO info = rom.RomInfo;

            uint mapTableBase = 0x00200000u;
            uint dsize = info.map_setting_datasize;
            WriteU32(rom.Data, (int)info.map_setting_pointer, mapTableBase | 0x08000000u);

            // --- Map 0: obj=2, pal=3, config=4, mappointer=5, anime1=8, anime2=9,
            //            mapchange=6, event=7. ---
            PlantMap(rom, mapTableBase + 0 * dsize,
                obj: 2, pal: 3, config: 4, mappointer: 5, anime1: 8, anime2: 9,
                mapchange: 6, evt: 7, info: info);
            // --- Map 1: a different set (config=10, mapchange=11, event=12). ---
            PlantMap(rom, mapTableBase + 1 * dsize,
                obj: 13, pal: 14, config: 10, mappointer: 15, anime1: 16, anime2: 17,
                mapchange: 11, evt: 12, info: info);
            // Terminator row 2 (dword 0 not a pointer, weather invalid).
            uint termBase = mapTableBase + 2 * dsize;
            WriteU32(rom.Data, (int)termBase, 0x00000000u);
            rom.Data[(int)(termBase + 12)] = 0xFF; // weather >= 0xE → invalid

            // --- ONE shared PLIST table at 0x00280000 (all purposes point here). ---
            uint sharedTable = 0x00280000u;
            uint sharedPtr = sharedTable | 0x08000000u;
            WriteU32(rom.Data, (int)info.map_config_pointer,        sharedPtr);
            WriteU32(rom.Data, (int)info.map_obj_pointer,           sharedPtr);
            WriteU32(rom.Data, (int)info.map_pal_pointer,           sharedPtr);
            WriteU32(rom.Data, (int)info.map_map_pointer_pointer,   sharedPtr);
            WriteU32(rom.Data, (int)info.map_mapchange_pointer,     sharedPtr);
            WriteU32(rom.Data, (int)info.map_tileanime1_pointer,    sharedPtr);
            WriteU32(rom.Data, (int)info.map_tileanime2_pointer,    sharedPtr);
            WriteU32(rom.Data, (int)info.map_event_pointer,         sharedPtr);

            // Each PLIST slot (1..20) derefs to a distinct data block so the
            // per-map-pointer-preservation asserts can verify the new tables
            // keep the same targets. Slot 0 stays NULL.
            for (uint slot = 1; slot <= 24; slot++)
            {
                uint dataBlock = 0x00300000u + slot * 0x100u;
                WriteU32(rom.Data, (int)(sharedTable + slot * 4u), dataBlock | 0x08000000u);
                rom.Data[(int)dataBlock] = (byte)slot; // mark the block
            }

            return rom;
        }

        static void PlantMap(ROM rom, uint addr,
            byte obj, byte pal, byte config, byte mappointer, byte anime1, byte anime2,
            byte mapchange, byte evt, ROMFEINFO info)
        {
            // dword 0 = pointer → IsMapSettingValid returns true (skips text checks).
            WriteU32(rom.Data, (int)addr, 0x08123456u);
            rom.Data[(int)(addr + 4)] = obj;   // obj_plist low byte (u16 @ +4)
            rom.Data[(int)(addr + 5)] = 0;     // obj_plist high byte (FE7 dual tileset)
            rom.Data[(int)(addr + 6)] = pal;
            rom.Data[(int)(addr + 7)] = config;
            rom.Data[(int)(addr + 8)] = mappointer;
            rom.Data[(int)(addr + 9)] = anime1;
            rom.Data[(int)(addr + 10)] = anime2;
            rom.Data[(int)(addr + 11)] = mapchange;
            rom.Data[(int)(addr + info.map_setting_event_plist_pos)] = evt;
        }

        static void WriteU32(byte[] data, int offset, uint value)
        {
            data[offset + 0] = (byte)(value & 0xFF);
            data[offset + 1] = (byte)((value >> 8) & 0xFF);
            data[offset + 2] = (byte)((value >> 16) & 0xFF);
            data[offset + 3] = (byte)((value >> 24) & 0xFF);
        }
    }
}
