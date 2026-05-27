// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for MapChangeCore — Phase 7 gap-sweep fix (#423).
using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Tests for <see cref="MapChangeCore"/>. The helper ports the WinForms
    /// <c>MapPointerForm.PlistToOffsetAddrFast</c> + <c>MapSettingForm.GetMapChangeAddrWhereMapID</c>
    /// path to ROM-explicit Core code so the Avalonia EventMapChange editor
    /// can resolve a map's change-data address without depending on
    /// WinForms or <c>CoreState.ROM</c>.
    /// </summary>
    [Collection("SharedState")]
    public class MapChangeCoreTests
    {
        // -------------------------------------------------------------
        // IsPlistSplit — mirrors WF MapPointerForm.IsPlistSplits().
        // The vanilla layout has all 6 PLIST tables (CONFIG / ANIMATION /
        // OBJECT / MAP / CHANGE / EVENT) sharing the same base address;
        // expanded ROMs split each table into its own block.
        // -------------------------------------------------------------

        [Fact]
        public void IsPlistSplit_VanillaSharedBase_ReturnsFalse()
        {
            var rom = MakeFe8uRom();
            // Make every PLIST type-pointer resolve to the same base
            // (= "tables not split").
            uint baseAddr = 0x00800000u;
            WriteU32(rom.Data, (int)rom.RomInfo.map_config_pointer, baseAddr | 0x08000000u);
            WriteU32(rom.Data, (int)rom.RomInfo.map_tileanime1_pointer, baseAddr | 0x08000000u);
            WriteU32(rom.Data, (int)rom.RomInfo.map_obj_pointer, baseAddr | 0x08000000u);
            WriteU32(rom.Data, (int)rom.RomInfo.map_map_pointer_pointer, baseAddr | 0x08000000u);
            WriteU32(rom.Data, (int)rom.RomInfo.map_mapchange_pointer, baseAddr | 0x08000000u);
            WriteU32(rom.Data, (int)rom.RomInfo.map_event_pointer, baseAddr | 0x08000000u);

            Assert.False(MapChangeCore.IsPlistSplit(rom));
        }

        [Fact]
        public void IsPlistSplit_DifferentBases_ReturnsTrue()
        {
            var rom = MakeFe8uRom();
            // Each pointer resolves to a different base — "tables split".
            WriteU32(rom.Data, (int)rom.RomInfo.map_config_pointer, 0x08800000u);
            WriteU32(rom.Data, (int)rom.RomInfo.map_tileanime1_pointer, 0x08801000u);
            WriteU32(rom.Data, (int)rom.RomInfo.map_obj_pointer, 0x08802000u);
            WriteU32(rom.Data, (int)rom.RomInfo.map_map_pointer_pointer, 0x08803000u);
            WriteU32(rom.Data, (int)rom.RomInfo.map_mapchange_pointer, 0x08804000u);
            WriteU32(rom.Data, (int)rom.RomInfo.map_event_pointer, 0x08805000u);

            Assert.True(MapChangeCore.IsPlistSplit(rom));
        }

        /// <summary>
        /// Copilot CLI re-review on issue #423: FE6 split-detection must
        /// also fold the FE6-only <c>map_worldmapevent_pointer</c> table
        /// into the comparison. A FE6 ROM where CONFIG differs from
        /// ANIMATION/OBJECT/MAP/CHANGE/EVENT but matches WORLDMAP must
        /// be classified as "not split" (matching WF semantics) — so
        /// <see cref="MapChangeCore.GetPlistLimit"/> returns the vanilla
        /// default instead of 256.
        /// </summary>
        [Fact]
        public void IsPlistSplit_Fe6WithSharedWorldMapBase_ReturnsFalse()
        {
            var rom = MakeFe6Rom();
            // Each of the 5 non-worldmap pointers gets a unique base —
            // would normally classify as split — but WORLDMAP shares the
            // CONFIG base, so the FE6 branch must catch it.
            uint sharedBase = 0x00800000u;
            WriteU32(rom.Data, (int)rom.RomInfo.map_config_pointer, sharedBase | 0x08000000u);
            WriteU32(rom.Data, (int)rom.RomInfo.map_tileanime1_pointer, 0x08801000u);
            WriteU32(rom.Data, (int)rom.RomInfo.map_obj_pointer, 0x08802000u);
            WriteU32(rom.Data, (int)rom.RomInfo.map_map_pointer_pointer, 0x08803000u);
            WriteU32(rom.Data, (int)rom.RomInfo.map_mapchange_pointer, 0x08804000u);
            WriteU32(rom.Data, (int)rom.RomInfo.map_event_pointer, 0x08805000u);
            WriteU32(rom.Data, (int)rom.RomInfo.map_worldmapevent_pointer, sharedBase | 0x08000000u);

            Assert.False(MapChangeCore.IsPlistSplit(rom));
            // ...and so the limit must be the FE6 default, not 256.
            Assert.Equal(rom.RomInfo.map_map_pointer_list_default_size,
                MapChangeCore.GetPlistLimit(rom));
        }

        /// <summary>
        /// On FE8/FE7 (no world-map pointer set), the new FE6 branch is
        /// inert. Verify that the split classification still flips on
        /// the same input that would trigger it before the fix.
        /// </summary>
        [Fact]
        public void IsPlistSplit_Fe8u_FE6BranchIsInert()
        {
            var rom = MakeFe8uRom();
            // Genuinely split tables across the 5 non-FE6 pointers.
            WriteU32(rom.Data, (int)rom.RomInfo.map_config_pointer, 0x08800000u);
            WriteU32(rom.Data, (int)rom.RomInfo.map_tileanime1_pointer, 0x08801000u);
            WriteU32(rom.Data, (int)rom.RomInfo.map_obj_pointer, 0x08802000u);
            WriteU32(rom.Data, (int)rom.RomInfo.map_map_pointer_pointer, 0x08803000u);
            WriteU32(rom.Data, (int)rom.RomInfo.map_mapchange_pointer, 0x08804000u);
            WriteU32(rom.Data, (int)rom.RomInfo.map_event_pointer, 0x08805000u);
            // FE8U has map_worldmapevent_pointer = 0 → branch is skipped.
            Assert.True(MapChangeCore.IsPlistSplit(rom));
        }

        [Fact]
        public void GetPlistLimit_VanillaRom_ReturnsRomDefaultSize()
        {
            var rom = MakeFe8uRom();
            // Make tables shared so IsPlistSplit returns false.
            uint baseAddr = 0x00800000u;
            WriteU32(rom.Data, (int)rom.RomInfo.map_config_pointer, baseAddr | 0x08000000u);
            WriteU32(rom.Data, (int)rom.RomInfo.map_tileanime1_pointer, baseAddr | 0x08000000u);
            WriteU32(rom.Data, (int)rom.RomInfo.map_obj_pointer, baseAddr | 0x08000000u);
            WriteU32(rom.Data, (int)rom.RomInfo.map_map_pointer_pointer, baseAddr | 0x08000000u);
            WriteU32(rom.Data, (int)rom.RomInfo.map_mapchange_pointer, baseAddr | 0x08000000u);
            WriteU32(rom.Data, (int)rom.RomInfo.map_event_pointer, baseAddr | 0x08000000u);

            uint limit = MapChangeCore.GetPlistLimit(rom);
            Assert.Equal(rom.RomInfo.map_map_pointer_list_default_size, limit);
        }

        [Fact]
        public void GetPlistLimit_SplitRom_Returns256()
        {
            var rom = MakeFe8uRom();
            // Each pointer resolves to a different base — split tables.
            WriteU32(rom.Data, (int)rom.RomInfo.map_config_pointer, 0x08800000u);
            WriteU32(rom.Data, (int)rom.RomInfo.map_tileanime1_pointer, 0x08801000u);
            WriteU32(rom.Data, (int)rom.RomInfo.map_obj_pointer, 0x08802000u);
            WriteU32(rom.Data, (int)rom.RomInfo.map_map_pointer_pointer, 0x08803000u);
            WriteU32(rom.Data, (int)rom.RomInfo.map_mapchange_pointer, 0x08804000u);
            WriteU32(rom.Data, (int)rom.RomInfo.map_event_pointer, 0x08805000u);

            Assert.Equal(256u, MapChangeCore.GetPlistLimit(rom));
        }

        // -------------------------------------------------------------
        // GetMapChangeAddrWhereMapID — happy / sad paths.
        // -------------------------------------------------------------

        /// <summary>
        /// Plant a map setting with mapchange_plist=3 and a CHANGE PLIST
        /// table whose entry 3 points to a synthetic change-data block.
        /// Expect GetMapChangeAddrWhereMapID to walk both jumps.
        /// </summary>
        [Fact]
        public void GetMapChangeAddrWhereMapID_ValidPlist_ResolvesToChangeData()
        {
            var rom = MakeFe8uRomWithMapTable(
                mapId: 0,
                mapChangePlist: 3,
                plistTableEntries: new uint[] { 0u, 0u, 0u, 0x08900000u },
                changeDataAddr: 0x00900000u);

            uint outPointer;
            uint result = MapChangeCore.GetMapChangeAddrWhereMapID(rom, 0u, out outPointer);
            Assert.Equal(0x00900000u, result);
            Assert.NotEqual(U.NOT_FOUND, outPointer);
        }

        [Fact]
        public void GetMapChangeAddrWhereMapID_PlistFF_ReturnsNotFound()
        {
            var rom = MakeFe8uRomWithMapTable(
                mapId: 0,
                mapChangePlist: 0xFF,
                plistTableEntries: new uint[] { 0u, 0u, 0u, 0u },
                changeDataAddr: 0u);

            uint outPointer;
            uint result = MapChangeCore.GetMapChangeAddrWhereMapID(rom, 0u, out outPointer);
            Assert.Equal(U.NOT_FOUND, result);
        }

        [Fact]
        public void GetMapChangeAddrWhereMapID_NullPointerEntry_ReturnsNotFound()
        {
            // PLIST entry contains 0x00000000 (null pointer) — not a valid
            // change-data address.
            var rom = MakeFe8uRomWithMapTable(
                mapId: 0,
                mapChangePlist: 3,
                plistTableEntries: new uint[] { 0u, 0u, 0u, 0u }, // entry 3 = NULL
                changeDataAddr: 0u);

            uint outPointer;
            uint result = MapChangeCore.GetMapChangeAddrWhereMapID(rom, 0u, out outPointer);
            Assert.Equal(U.NOT_FOUND, result);
        }

        [Fact]
        public void GetMapChangeAddrWhereMapID_InvalidMapId_ReturnsNotFound()
        {
            var rom = MakeFe8uRom();
            // mapId beyond a populated map table — MapSettingCore.GetMapAddr returns
            // an addr past EOF, ours should catch it and return NOT_FOUND.
            uint outPointer;
            uint result = MapChangeCore.GetMapChangeAddrWhereMapID(rom, 0xFFFFFFFFu, out outPointer);
            Assert.Equal(U.NOT_FOUND, result);
        }

        /// <summary>
        /// Copilot bot review on issue #423: plist == 0 is reserved by
        /// WF semantics (`MapPointerForm.GetPListNameSplited` returns
        /// "NULL"). Even if entry 0 of the PLIST table contains a
        /// pointer to valid-looking ROM data, the helper must refuse
        /// to resolve it.
        /// </summary>
        [Fact]
        public void GetMapChangeAddrWhereMapID_PlistZero_ReturnsNotFound()
        {
            var rom = MakeFe8uRomWithMapTable(
                mapId: 0,
                mapChangePlist: 0, // explicit "no data" marker
                plistTableEntries: new uint[] { 0x08900000u, 0u, 0u, 0u }, // entry 0 has a pointer
                changeDataAddr: 0x00900000u);

            uint outPointer;
            uint result = MapChangeCore.GetMapChangeAddrWhereMapID(rom, 0u, out outPointer);
            Assert.Equal(U.NOT_FOUND, result);
        }

        /// <summary>
        /// Copilot bot review on issue #423: the helper must validate
        /// <paramref name="mapId"/> against the populated map count
        /// (mirrors WF `InputFormRef.IDToAddr` which returns NOT_FOUND
        /// when id >= DataCount). A mapId that produces an in-bounds
        /// but invalid map-setting record must not accidentally
        /// resolve through whatever bytes happen to live there.
        /// </summary>
        [Fact]
        public void GetMapChangeAddrWhereMapID_MapIdBeyondPopulatedTable_ReturnsNotFound()
        {
            // Seed exactly 1 valid map (id=0). Asking for mapId=1
            // computes an address that's still inside the ROM but is
            // not a valid map record — IsMapSettingValid returns false.
            var rom = MakeFe8uRomWithMapTable(
                mapId: 0,
                mapChangePlist: 3,
                plistTableEntries: new uint[] { 0u, 0u, 0u, 0x08900000u },
                changeDataAddr: 0x00900000u);

            uint outPointer;
            uint result = MapChangeCore.GetMapChangeAddrWhereMapID(rom, 1u, out outPointer);
            Assert.Equal(U.NOT_FOUND, result);
        }

        // -------------------------------------------------------------
        // ResolvePlistSlotAddr / WritePlistData — CONFIG slice for #671.
        // -------------------------------------------------------------

        /// <summary>
        /// ResolvePlistSlotAddr returns the per-entry slot address even
        /// when the dereferenced target is currently null. WF
        /// Write_Plsit needs to be able to overwrite a previously-unset
        /// slot with a freshly-appended pointer.
        /// </summary>
        [Fact]
        public void ResolvePlistSlotAddr_NullTarget_ReturnsSlotAddress()
        {
            var rom = MakeFe8uRom();
            uint cfgTableAddr = 0x00880000u;
            WriteU32(rom.Data, (int)rom.RomInfo.map_config_pointer, cfgTableAddr | 0x08000000u);
            // Slot 3 is null (uint 0). Make sure ResolvePlistSlotAddr
            // still returns the slot's address so a write can populate it.
            WriteU32(rom.Data, (int)(cfgTableAddr + 3 * 4u), 0u);

            uint slot = MapChangeCore.ResolvePlistSlotAddr(rom, MapChangeCore.PlistType.CONFIG, 3);
            Assert.Equal(cfgTableAddr + 3 * 4u, slot);
        }

        /// <summary>
        /// PLIST 0 is reserved by WF's Write_Plsit semantics — the helper
        /// must refuse to resolve a slot for it.
        /// </summary>
        [Fact]
        public void ResolvePlistSlotAddr_PlistZero_ReturnsNotFound()
        {
            var rom = MakeFe8uRom();
            uint cfgTableAddr = 0x00880000u;
            WriteU32(rom.Data, (int)rom.RomInfo.map_config_pointer, cfgTableAddr | 0x08000000u);

            uint slot = MapChangeCore.ResolvePlistSlotAddr(rom, MapChangeCore.PlistType.CONFIG, 0);
            Assert.Equal(U.NOT_FOUND, slot);
        }

        /// <summary>
        /// PLIST values past <see cref="MapChangeCore.GetPlistLimit"/> are
        /// rejected (mirrors WF Init's `i >= limit ⇒ invalid`).
        /// </summary>
        [Fact]
        public void ResolvePlistSlotAddr_PlistPastLimit_ReturnsNotFound()
        {
            var rom = MakeFe8uRom();
            uint cfgTableAddr = 0x00880000u;
            WriteU32(rom.Data, (int)rom.RomInfo.map_config_pointer, cfgTableAddr | 0x08000000u);
            // Force the not-split branch so the per-version vanilla limit applies.
            WriteU32(rom.Data, (int)rom.RomInfo.map_tileanime1_pointer, cfgTableAddr | 0x08000000u);

            uint limit = MapChangeCore.GetPlistLimit(rom);
            // limit must be > 0 and < uint.MaxValue for the test to make sense.
            Assert.True(limit > 0);
            uint slot = MapChangeCore.ResolvePlistSlotAddr(rom, MapChangeCore.PlistType.CONFIG, limit);
            Assert.Equal(U.NOT_FOUND, slot);
        }

        /// <summary>
        /// Round-trip: write a compressed payload through WritePlistData
        /// and assert the PLIST slot now points at the new offset and the
        /// new offset contains the payload bytes.
        /// </summary>
        [Fact]
        public void WritePlistData_RoundTrips_UpdatesSlotAndWritesBytes()
        {
            var rom = MakeFe8uRom();
            uint cfgTableAddr = 0x00880000u;
            WriteU32(rom.Data, (int)rom.RomInfo.map_config_pointer, cfgTableAddr | 0x08000000u);

            byte[] payload = { 0x10, 0x00, 0x00, 0x00, 0xAA, 0xBB, 0xCC, 0xDD };
            uint addr = MapChangeCore.WritePlistData(rom, MapChangeCore.PlistType.CONFIG, 3, payload, out string err);
            Assert.NotEqual(U.NOT_FOUND, addr);
            Assert.Equal("", err);
            // Slot now points at the new offset.
            uint slot = cfgTableAddr + 3 * 4u;
            Assert.Equal(addr, rom.p32(slot));
            // Bytes were written at the new offset.
            for (int i = 0; i < payload.Length; i++)
                Assert.Equal(payload[i], rom.Data[addr + i]);
        }

        /// <summary>
        /// WritePlistData refuses PLIST 0 (matches WF Write_Plsit early
        /// return — the reserved sentinel slot must never be overwritten).
        /// PLIST 0xFF is allowed at the Core level (WF's IDToAddr accepts
        /// any in-range index); the VM-level WriteChipsetConfig adds the
        /// additional 0xFF rejection per plan WU4. See
        /// MapStyleEditorViewModelChipsetTests.WriteChipsetConfig_FailsOn_PlistZero_OrPlistFF.
        /// </summary>
        [Fact]
        public void WritePlistData_FailsOn_PlistZero()
        {
            var rom = MakeFe8uRom();
            uint cfgTableAddr = 0x00100000u;
            WriteU32(rom.Data, (int)rom.RomInfo.map_config_pointer, cfgTableAddr | 0x08000000u);

            byte[] payload = { 0x10, 0x00, 0x00, 0x00 };
            uint addr0 = MapChangeCore.WritePlistData(rom, MapChangeCore.PlistType.CONFIG, 0, payload, out string err0);
            Assert.Equal(U.NOT_FOUND, addr0);
            Assert.NotEqual("", err0);
        }

        /// <summary>
        /// Undo-scope coverage (Copilot v1 plan review item 6): the write
        /// is recorded into ambient undo so calling RunUndo restores both
        /// the PLIST slot value AND any byte writes at the appended offset.
        /// The table sits well before the free-space search midpoint so
        /// FindAndWriteData doesn't accidentally append into the table region.
        /// </summary>
        [Fact]
        public void WritePlistData_UndoableRestoresOriginalSlot()
        {
            var rom = MakeFe8uRom();
            // Place CONFIG PLIST table at 0x100000 — well below mid-ROM
            // (0x880000) so FindAndWriteData's mid-ROM scan doesn't hit it.
            uint cfgTableAddr = 0x00100000u;
            WriteU32(rom.Data, (int)rom.RomInfo.map_config_pointer, cfgTableAddr | 0x08000000u);

            // Pre-seed slot 5 with a known pointer so undo has a value to restore.
            uint preSlotValue = 0x080A0000u;
            WriteU32(rom.Data, (int)(cfgTableAddr + 5 * 4u), preSlotValue);

            // Plant a "wall" of 0x01 bytes at mid-ROM so FindAndWriteData
            // sees the region as occupied and either lands further along
            // or appends past it.
            for (int i = 0; i < 0x10000; i++) rom.Data[0x880000 + i] = 0x01;

            var prevUndo = CoreState.Undo;
            var prevRom = CoreState.ROM;
            try
            {
                // Undo.NewUndoDataLow needs CoreState.ROM.Data.Length for
                // the file-size snapshot.
                CoreState.ROM = rom;
                CoreState.Undo = new Undo();
                byte[] payload = { 0x42, 0x42, 0x42, 0x42 };
                var undoData = CoreState.Undo.NewUndoData("test config write");
                uint newAddr;
                using (ROM.BeginUndoScope(undoData))
                {
                    newAddr = MapChangeCore.WritePlistData(rom, MapChangeCore.PlistType.CONFIG, 5, payload, out _);
                }
                Assert.NotEqual(U.NOT_FOUND, newAddr);
                CoreState.Undo.Push(undoData);

                // Sanity: the write did NOT overlap the table region.
                Assert.True(newAddr < cfgTableAddr || newAddr >= cfgTableAddr + 256 * 4u,
                    $"new addr 0x{newAddr:X} must not overlap the PLIST table");

                // Confirm slot now points at the new offset.
                Assert.Equal(newAddr, rom.p32(cfgTableAddr + 5 * 4u));

                CoreState.Undo.RunUndo();
                // Slot value restored.
                Assert.Equal(preSlotValue, rom.u32(cfgTableAddr + 5 * 4u));
            }
            finally
            {
                CoreState.Undo = prevUndo;
                CoreState.ROM = prevRom;
            }
        }

        // -------------------------------------------------------------
        // Helpers.
        // -------------------------------------------------------------

        /// <summary>
        /// Build a minimal FE8U ROM image without any pointer-table data.
        /// </summary>
        static ROM MakeFe8uRom()
        {
            var rom = new ROM();
            rom.LoadLow("test-fe8u.gba", new byte[0x1100000], "BE8E01");
            return rom;
        }

        /// <summary>
        /// Build a minimal FE6JP ROM (only FE6 carries the FE6-only
        /// <c>map_worldmapevent_pointer</c> != 0). Used by the FE6 split-detection
        /// regression test added per Copilot CLI re-review on issue #423.
        /// </summary>
        static ROM MakeFe6Rom()
        {
            var rom = new ROM();
            rom.LoadLow("test-fe6jp.gba", new byte[0x1000000], "AFEJ01");
            return rom;
        }

        /// <summary>
        /// Build an FE8U ROM with a single map at <paramref name="mapId"/>=0,
        /// whose offset-11 byte = <paramref name="mapChangePlist"/>, and a
        /// CHANGE PLIST pointer table at a known address whose entries are
        /// <paramref name="plistTableEntries"/>. If
        /// <paramref name="changeDataAddr"/> is non-zero, also seeds a valid
        /// change-data block at that ROM offset.
        /// </summary>
        static ROM MakeFe8uRomWithMapTable(
            uint mapId, byte mapChangePlist, uint[] plistTableEntries, uint changeDataAddr)
        {
            var rom = MakeFe8uRom();

            // 1. Plant a map setting block at 0x00800000.
            uint mapTableBase = 0x00800000u;
            uint mapSettingDataSize = rom.RomInfo.map_setting_datasize;

            // map_setting_pointer => mapTableBase
            WriteU32(rom.Data, (int)rom.RomInfo.map_setting_pointer, mapTableBase | 0x08000000u);

            // Lay out one valid map setting record. WF treats a pointer in
            // the first dword (offset 0) as valid; mirror that.
            int mapRecordBase = (int)(mapTableBase + mapId * mapSettingDataSize);
            WriteU32(rom.Data, mapRecordBase + 0, 0x08123456u);  // pointer-valued first dword
            rom.Data[mapRecordBase + 11] = mapChangePlist;       // offset 11 = map change plist

            // Add a terminator at the next slot (offset 0 = 0xFF byte = non-pointer)
            // so MakeMapIDList stops at 1 entry.
            int termBase = (int)(mapTableBase + (mapId + 1) * mapSettingDataSize);
            WriteU32(rom.Data, termBase + 0, 0x00000000u);

            // 2. Plant the CHANGE PLIST pointer table at a known address.
            //    Use a separate region so that IsPlistSplit-related tests
            //    can be tuned independently.
            uint plistTableAddr = 0x00880000u;
            WriteU32(rom.Data, (int)rom.RomInfo.map_mapchange_pointer, plistTableAddr | 0x08000000u);

            for (int i = 0; i < plistTableEntries.Length; i++)
            {
                WriteU32(rom.Data, (int)(plistTableAddr + i * 4u), plistTableEntries[i]);
            }

            // 3. (Optional) seed the change data block so isSafetyOffset
            //    succeeds. Plant a non-FF first byte so the WF "valid"
            //    check would also pass.
            if (changeDataAddr != 0u)
            {
                rom.Data[(int)changeDataAddr] = 0x01;
                rom.Data[(int)changeDataAddr + 1] = 0x00;
                rom.Data[(int)changeDataAddr + 2] = 0x00;
                rom.Data[(int)changeDataAddr + 11] = 0x00;
            }

            return rom;
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
