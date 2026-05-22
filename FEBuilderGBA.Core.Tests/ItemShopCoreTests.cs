using Xunit;
using FEBuilderGBA;
using System.Collections.Generic;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Unit tests for ItemShopCore — the cross-platform shop enumeration and
    /// expansion helper that powers the Avalonia Item Shop Editor (#369).
    ///
    /// These tests build small synthetic ROMs to exercise specific code paths
    /// without requiring real ROM fixtures. They cover:
    /// - shop enumeration (hensei + worldmap + per-map event-cond)
    /// - empty-shop filtering (matches WinForms ItemShopForm.MakeShopListLow)
    /// - ROM-identity safety (delegated map lookups use the passed ROM, not CoreState.ROM)
    /// - safe append / no-slack failure / remove / relocate
    /// </summary>
    [Collection("SharedState")]
    public class ItemShopCoreTests
    {
        // ---------- helpers ----------
        static void WriteU32(byte[] data, int offset, uint value)
        {
            data[offset + 0] = (byte)(value & 0xFF);
            data[offset + 1] = (byte)((value >> 8) & 0xFF);
            data[offset + 2] = (byte)((value >> 16) & 0xFF);
            data[offset + 3] = (byte)((value >> 24) & 0xFF);
        }

        static void WriteU16(byte[] data, int offset, ushort value)
        {
            data[offset + 0] = (byte)(value & 0xFF);
            data[offset + 1] = (byte)((value >> 8) & 0xFF);
        }

        /// <summary>
        /// Create a barebones FE8U ROM with a Hensei shop at offset 0x200 containing
        /// just the terminator (0x00 0x00). This is the simplest valid shop list.
        /// </summary>
        static ROM MakeFE8UWithHenseiShop()
        {
            var rom = new ROM();
            rom.LoadLow("test.gba", new byte[0x1000000], "BE8E01");
            // Point item_shop_hensei_pointer at 0x200, write `00 00` terminator there.
            WriteU32(rom.Data, (int)rom.RomInfo.item_shop_hensei_pointer, 0x08000200);
            rom.Data[0x200] = 0x00; // terminator first byte
            rom.Data[0x201] = 0x00;
            // To meet ReadShopItems' empty-list filter and keep the test focused, write
            // a non-zero item ID before the terminator so the hensei shop has 1 item.
            // Layout: 0x01 0x03 0x00 0x00 -> one item (id=1, qty=3), terminator.
            rom.Data[0x200] = 0x01;
            rom.Data[0x201] = 0x03;
            rom.Data[0x202] = 0x00;
            rom.Data[0x203] = 0x00;
            return rom;
        }

        // ===================================================================
        // ROM identity safety — guards against CoreState.ROM leakage
        // ===================================================================

        [Fact]
        public void MakeShopList_UsesProvidedRom_NotCoreStateRom()
        {
            // Populate one ROM with a hensei shop; leave CoreState.ROM null.
            // MakeShopList(populated) must return the populated ROM's shops, not
            // silently fall back through delegated helpers to CoreState.ROM.
            var origRom = CoreState.ROM;
            try
            {
                CoreState.ROM = null;
                var rom = MakeFE8UWithHenseiShop();

                var shops = ItemShopCore.MakeShopList(rom);

                Assert.NotEmpty(shops); // must succeed without CoreState.ROM
                Assert.Equal((uint)0x200, shops[0].addr);
            }
            finally
            {
                CoreState.ROM = origRom;
            }
        }

        // ===================================================================
        // MakeShopList: hensei
        // ===================================================================

        [Fact]
        public void MakeShopList_ContainsHenseiShop_OnSyntheticRom()
        {
            var rom = MakeFE8UWithHenseiShop();
            var shops = ItemShopCore.MakeShopList(rom);
            Assert.NotEmpty(shops);
            Assert.Equal((uint)0x200, shops[0].addr);
        }

        [Fact]
        public void MakeShopList_WithNoRom_ReturnsEmpty()
        {
            var shops = ItemShopCore.MakeShopList(null);
            Assert.NotNull(shops);
            Assert.Empty(shops);
        }

        // ===================================================================
        // MakeShopList: worldmap (FE8)
        // ===================================================================

        [Fact]
        public void MakeShopList_IncludesWorldMapShops_OnSyntheticRom()
        {
            var rom = MakeFE8UWithHenseiShop();

            // Set up one worldmap point at offset 0x400.
            // Layout per WorldMapPointForm: 32-byte entries; offsets 12/16/20 hold
            // armory/vendor/secret shop pointers; offset 28 is the text ID.
            // The pointer table at worldmap_point_pointer (an absolute ROM offset
            // because the field is the offset of the pointer table itself) must
            // be a pointer to the 32-byte point table.
            uint wmPointTablePtrLoc = rom.RomInfo.worldmap_point_pointer;
            WriteU32(rom.Data, (int)wmPointTablePtrLoc, 0x08000400); // pointer -> 0x400

            // Point[0] @ 0x400, 32 bytes
            // first dword must be a pointer (WinForms InputFormRef predicate)
            WriteU32(rom.Data, 0x400 + 0, 0x08000600); // some pointer
            // armory shop pointer at +12 -> 0x500
            WriteU32(rom.Data, 0x400 + 12, 0x08000500);
            // vendor: known empty list address (forces skip)
            WriteU32(rom.Data, 0x400 + 16, (uint)rom.RomInfo.worldmap_node_vendor_empty_address);
            // secret: null pointer (forces skip)
            WriteU32(rom.Data, 0x400 + 20, 0x00000000);
            // text ID at +28
            WriteU16(rom.Data, 0x400 + 28, 1);

            // Point[1] terminator: invalid (non-pointer) at offset 0 so the loop ends
            WriteU32(rom.Data, 0x420 + 0, 0x00000001);
            // also invalid shop pointers
            WriteU32(rom.Data, 0x420 + 12, 0x00000000);
            WriteU32(rom.Data, 0x420 + 16, 0x00000000);
            WriteU32(rom.Data, 0x420 + 20, 0x00000000);

            // The armory shop at 0x500 must have a non-empty list.
            rom.Data[0x500] = 0x05; // item id
            rom.Data[0x501] = 0x01; // qty
            rom.Data[0x502] = 0x00; // terminator
            rom.Data[0x503] = 0x00;

            var shops = ItemShopCore.MakeShopList(rom);

            // Expect at least 2 entries: hensei (0x200) + worldmap armory (0x500).
            Assert.Contains(shops, s => s.addr == 0x500);
        }

        [Fact]
        public void MakeShopList_FiltersEmptyWorldMapShops()
        {
            // Worldmap point with a non-empty-list-address pointer that nevertheless
            // points to an immediate terminator (u8 == 0). WinForms filters these.
            var rom = MakeFE8UWithHenseiShop();

            uint wmPointTablePtrLoc = rom.RomInfo.worldmap_point_pointer;
            WriteU32(rom.Data, (int)wmPointTablePtrLoc, 0x08000400);

            WriteU32(rom.Data, 0x400 + 0, 0x08000600);
            // armory -> 0x500 (looks valid, but content starts with 0x00)
            WriteU32(rom.Data, 0x400 + 12, 0x08000500);
            WriteU32(rom.Data, 0x400 + 16, 0x00000000);
            WriteU32(rom.Data, 0x400 + 20, 0x00000000);
            WriteU16(rom.Data, 0x400 + 28, 1);

            WriteU32(rom.Data, 0x420 + 0, 0x00000001); // terminator point

            // Shop at 0x500: empty (just 0x00).
            rom.Data[0x500] = 0x00;
            rom.Data[0x501] = 0x00;

            var shops = ItemShopCore.MakeShopList(rom);

            // The empty 0x500 shop must be filtered out.
            Assert.DoesNotContain(shops, s => s.addr == 0x500);
        }

        // ===================================================================
        // MakeShopList: per-map event-cond
        // ===================================================================

        [Fact]
        public void MakeShopList_IncludesEventCondShops_OnSyntheticRom()
        {
            // Build the chain: map_setting_pointer -> map[0] -> PLIST byte -> event-cond block
            //   -> slot[2] (OBJECT) -> object record -> shop address.
            var rom = MakeFE8UWithHenseiShop();

            // ---- map setting table ----
            uint mapSettingPtr = rom.RomInfo.map_setting_pointer;
            uint mapBase = 0x800;
            WriteU32(rom.Data, (int)mapSettingPtr, 0x08000000 + mapBase);

            // map[0]: D0 = a pointer (WinForms shortcut to validate)
            WriteU32(rom.Data, (int)mapBase + 0, 0x08000900);
            // text IDs at 0x70/0x72 set valid
            WriteU16(rom.Data, (int)(mapBase + 0x70), 1);
            WriteU16(rom.Data, (int)(mapBase + 0x72), 1);
            // clear conditions for FE8 (148-byte struct) at 0x88/0x8A
            WriteU16(rom.Data, (int)(mapBase + 0x88), 1);
            WriteU16(rom.Data, (int)(mapBase + 0x8A), 1);
            // text pointer table for the text validation path
            WriteU32(rom.Data, (int)rom.RomInfo.text_pointer, 0x08000A00);
            WriteU32(rom.Data, 0xA00, 0x08000B00);
            WriteU32(rom.Data, 0xA04, 0x08000C00);

            // map[1]: terminator (non-pointer D0 + missing/invalid validation fields)
            uint mapTerm = mapBase + rom.RomInfo.map_setting_datasize;
            WriteU32(rom.Data, (int)mapTerm + 0, 0x00000001);
            rom.Data[(int)mapTerm + 12] = 0xFF; // weather invalid -> ends validation

            // ---- PLIST byte: map_setting_event_plist_pos -> 1 ----
            uint plistPos = rom.RomInfo.map_setting_event_plist_pos;
            rom.Data[mapBase + plistPos] = 1;

            // ---- event pointer table ----
            uint eventPtrTable = rom.RomInfo.map_event_pointer;
            WriteU32(rom.Data, (int)eventPtrTable, 0x08001000);
            // entry[0] = 0 (unused), entry[1] = 0x08001100 (the event-cond block)
            WriteU32(rom.Data, 0x1000, 0x00000000);
            WriteU32(rom.Data, 0x1004, 0x08001100);

            // ---- event-cond block @ 0x1100 ----
            // Slot order (FE8 from MapEventUnitCore.GetCondSlotsFE8):
            //   [0] Turn, [1] Talk, [2] Object, [3] Always, [4] Always, ...
            // -> Object is slot index 2 -> 4-byte pointer at 0x1100 + 8.
            WriteU32(rom.Data, 0x1100 + 0, 0x00000000); // slot 0
            WriteU32(rom.Data, 0x1100 + 4, 0x00000000); // slot 1
            WriteU32(rom.Data, 0x1100 + 8, 0x08001200); // slot 2 (Object) -> object record list

            // ---- object record list @ 0x1200 ----
            // 12-byte records, terminated by u32 == 0.
            // record[0]: byte 0=type, +4=shop_addr ptr, +10=obj_type (0x16=armory in FE8)
            WriteU32(rom.Data, 0x1200 + 0, 0x00000001); // type byte = 0x01 (loop predicate u32 != 0)
            WriteU32(rom.Data, 0x1200 + 4, 0x08001300); // shop_addr -> 0x1300
            rom.Data[0x1200 + 10] = 0x16; // armory
            WriteU32(rom.Data, 0x1200 + 12, 0x00000000); // record terminator

            // ---- shop @ 0x1300: 1 item + terminator ----
            rom.Data[0x1300] = 0x02; // item id 2
            rom.Data[0x1301] = 0x01; // qty 1
            rom.Data[0x1302] = 0x00;
            rom.Data[0x1303] = 0x00;

            var shops = ItemShopCore.MakeShopList(rom);

            // We expect: hensei (0x200) + the event-cond armory shop (0x1300).
            Assert.Contains(shops, s => s.addr == 0x1300);
        }

        [Fact]
        public void MakeShopList_FiltersEmptyEventCondShops()
        {
            // Same scaffolding as above but the OBJECT slot's shop is empty (starts with 0x00).
            var rom = MakeFE8UWithHenseiShop();

            uint mapSettingPtr = rom.RomInfo.map_setting_pointer;
            uint mapBase = 0x800;
            WriteU32(rom.Data, (int)mapSettingPtr, 0x08000000 + mapBase);
            WriteU32(rom.Data, (int)mapBase + 0, 0x08000900);
            WriteU16(rom.Data, (int)(mapBase + 0x70), 1);
            WriteU16(rom.Data, (int)(mapBase + 0x72), 1);
            WriteU16(rom.Data, (int)(mapBase + 0x88), 1);
            WriteU16(rom.Data, (int)(mapBase + 0x8A), 1);
            WriteU32(rom.Data, (int)rom.RomInfo.text_pointer, 0x08000A00);
            WriteU32(rom.Data, 0xA00, 0x08000B00);
            WriteU32(rom.Data, 0xA04, 0x08000C00);

            uint mapTerm = mapBase + rom.RomInfo.map_setting_datasize;
            WriteU32(rom.Data, (int)mapTerm + 0, 0x00000001);
            rom.Data[(int)mapTerm + 12] = 0xFF;

            uint plistPos = rom.RomInfo.map_setting_event_plist_pos;
            rom.Data[mapBase + plistPos] = 1;

            uint eventPtrTable = rom.RomInfo.map_event_pointer;
            WriteU32(rom.Data, (int)eventPtrTable, 0x08001000);
            WriteU32(rom.Data, 0x1004, 0x08001100);

            WriteU32(rom.Data, 0x1100 + 8, 0x08001200);
            WriteU32(rom.Data, 0x1200 + 0, 0x00000001);
            WriteU32(rom.Data, 0x1200 + 4, 0x08001300);
            rom.Data[0x1200 + 10] = 0x16;
            WriteU32(rom.Data, 0x1200 + 12, 0x00000000);

            // Shop at 0x1300 is EMPTY — must be filtered out.
            rom.Data[0x1300] = 0x00;
            rom.Data[0x1301] = 0x00;

            var shops = ItemShopCore.MakeShopList(rom);

            Assert.DoesNotContain(shops, s => s.addr == 0x1300);
        }

        // ===================================================================
        // ReadShopItems
        // ===================================================================

        [Fact]
        public void ReadShopItems_StopsAtTerminator()
        {
            var rom = MakeFE8UWithHenseiShop();
            // Hensei shop @ 0x200: one item (id=1 qty=3) + 0x00 0x00 terminator.
            var items = ItemShopCore.ReadShopItems(rom, 0x200);
            Assert.Single(items);
            Assert.Equal((uint)0x200, items[0].addr);
        }

        // ===================================================================
        // TryAppendShopItem
        // ===================================================================

        [Fact]
        public void TryAppendShopItem_SucceedsWhenSlackAvailable()
        {
            // Build a shop at 0x200 with one item + 4 bytes of zero padding (slack).
            // Layout: 01 03 00 00 00 00 (... rest of ROM is 0x00)
            var rom = MakeFE8UWithHenseiShop();
            // Hensei shop already has 01 03 00 00 at 0x200. Bytes 0x204..0x207 in the
            // freshly-allocated zero-byte array are 0, so slack is plenty.

            bool ok = ItemShopCore.TryAppendShopItem(rom, 0x200, 0x07, 0x05, out uint newSlotAddr);

            Assert.True(ok);
            Assert.Equal((uint)0x202, newSlotAddr); // old terminator becomes the new slot
            Assert.Equal(0x07u, rom.u8(0x202));
            Assert.Equal(0x05u, rom.u8(0x203));
            Assert.Equal(0x00u, rom.u8(0x204));
            Assert.Equal(0x00u, rom.u8(0x205));
        }

        [Fact]
        public void TryAppendShopItem_FailsWhenNoSlack()
        {
            // Same shop, but the bytes after the terminator are non-zero (existing
            // ROM data — appending would corrupt them).
            var rom = MakeFE8UWithHenseiShop();
            rom.Data[0x204] = 0xAB; // non-zero — slack check must fail
            rom.Data[0x205] = 0xCD;

            bool ok = ItemShopCore.TryAppendShopItem(rom, 0x200, 0x07, 0x05, out uint newSlotAddr);

            Assert.False(ok);
            Assert.Equal(U.NOT_FOUND, newSlotAddr);
            // Ensure no write occurred at the old terminator.
            Assert.Equal(0x00u, rom.u8(0x202));
            Assert.Equal(0x00u, rom.u8(0x203));
            // And the non-zero data after is preserved.
            Assert.Equal(0xABu, rom.u8(0x204));
            Assert.Equal(0xCDu, rom.u8(0x205));
        }

        // -------------------- Unterminated-list guard --------------------
        // These tests confirm that Append / Remove / Relocate refuse to operate
        // on a shop whose item list lacks a terminator within MAX_SCAN_ENTRIES.
        // Without the guard, CountShopItems would return MAX_SCAN_ENTRIES and
        // Append would write into unrelated ROM data. (Per Copilot bot review
        // on PR #465.)

        [Fact]
        public void TryAppendShopItem_FailsOnUnterminatedList()
        {
            var rom = MakeFE8UWithHenseiShop();
            for (uint i = 0; i < ItemShopCore.MAX_SCAN_ENTRIES * 2; i++)
            {
                rom.Data[0x200 + i] = 0xAB;
            }
            bool ok = ItemShopCore.TryAppendShopItem(rom, 0x200, 0x07, 0x05, out uint newSlotAddr);
            Assert.False(ok);
            Assert.Equal(U.NOT_FOUND, newSlotAddr);
        }

        [Fact]
        public void TryRemoveLastShopItem_FailsOnUnterminatedList()
        {
            var rom = MakeFE8UWithHenseiShop();
            for (uint i = 0; i < ItemShopCore.MAX_SCAN_ENTRIES * 2; i++)
            {
                rom.Data[0x200 + i] = 0xAB;
            }
            bool ok = ItemShopCore.TryRemoveLastShopItem(rom, 0x200);
            Assert.False(ok);
            Assert.Equal(0xABu, rom.u8(0x200));
        }

        [Fact]
        public void RelocateShopList_FailsOnUnterminatedList()
        {
            var rom = MakeFE8UWithHenseiShop();
            for (uint i = 0; i < ItemShopCore.MAX_SCAN_ENTRIES * 2; i++)
            {
                rom.Data[0x200 + i] = 0xAB;
            }
            uint pointerAddr = rom.RomInfo.item_shop_hensei_pointer;
            uint result = ItemShopCore.RelocateShopList(rom, 0x200, 0x09, 0x02, pointerAddr);
            Assert.Equal(U.NOT_FOUND, result);
        }

        // ===================================================================
        // TryRemoveLastShopItem
        // ===================================================================

        [Fact]
        public void TryRemoveLastShopItem_RewritesPenultimateEntry()
        {
            var rom = MakeFE8UWithHenseiShop();
            // Shop has 1 item (id=1 qty=3) + terminator. Add a second item.
            rom.Data[0x202] = 0x05;
            rom.Data[0x203] = 0x02;
            rom.Data[0x204] = 0x00;
            rom.Data[0x205] = 0x00;

            bool ok = ItemShopCore.TryRemoveLastShopItem(rom, 0x200);

            Assert.True(ok);
            // After removal: 01 03 00 00 [00 00] — second item becomes terminator.
            Assert.Equal(0x01u, rom.u8(0x200));
            Assert.Equal(0x03u, rom.u8(0x201));
            Assert.Equal(0x00u, rom.u8(0x202));
            Assert.Equal(0x00u, rom.u8(0x203));
        }

        [Fact]
        public void TryRemoveLastShopItem_FailsOnEmptyShop()
        {
            var rom = MakeFE8UWithHenseiShop();
            // Make it empty.
            rom.Data[0x200] = 0x00;
            rom.Data[0x201] = 0x00;

            bool ok = ItemShopCore.TryRemoveLastShopItem(rom, 0x200);

            Assert.False(ok);
        }

        // ===================================================================
        // RelocateShopList
        // ===================================================================

        [Fact]
        public void RelocateShopList_UpdatesPointerAndKeepsItems()
        {
            var rom = MakeFE8UWithHenseiShop();
            // We need to call RelocateShopList(rom, oldShopAddr, newItemId, newQty, pointerAddr).
            // pointerAddr is the address of the 4-byte pointer slot that holds p32(shopAddr).
            // For the hensei shop, that's the item_shop_hensei_pointer location.
            uint pointerAddr = rom.RomInfo.item_shop_hensei_pointer;

            // Mark a chunk of free space deep in the ROM so FindFreeSpace can succeed.
            // FE8U ROMs start with the file data already 0x1000000 bytes of zero, so the
            // free space at the end is huge. Just make sure the area near 0x200 doesn't
            // accidentally match.
            // After relocation: pointer at pointerAddr must point somewhere new, and the
            // first 4 bytes of the new shop must be 01 03 09 02 (old item + new item).
            uint newShopAddr = ItemShopCore.RelocateShopList(rom, 0x200, 0x09, 0x02, pointerAddr);

            Assert.NotEqual((uint)0x200, newShopAddr);
            Assert.NotEqual(U.NOT_FOUND, newShopAddr);

            // The pointer slot must now point to newShopAddr.
            uint storedPointer = rom.u32(pointerAddr);
            Assert.Equal(0x08000000u + newShopAddr, storedPointer);

            // New shop must contain old item + new item + terminator.
            Assert.Equal(0x01u, rom.u8(newShopAddr + 0)); // id=1 (old)
            Assert.Equal(0x03u, rom.u8(newShopAddr + 1)); // qty=3 (old)
            Assert.Equal(0x09u, rom.u8(newShopAddr + 2)); // id=9 (new)
            Assert.Equal(0x02u, rom.u8(newShopAddr + 3)); // qty=2 (new)
            Assert.Equal(0x00u, rom.u8(newShopAddr + 4)); // terminator
            Assert.Equal(0x00u, rom.u8(newShopAddr + 5));
        }

        // ===================================================================
        // CountShopItems
        // ===================================================================

        [Fact]
        public void CountShopItems_ReturnsZero_OnEmptyShop()
        {
            var rom = MakeFE8UWithHenseiShop();
            rom.Data[0x200] = 0x00;
            rom.Data[0x201] = 0x00;
            int count = ItemShopCore.CountShopItems(rom, 0x200);
            Assert.Equal(0, count);
        }

        [Fact]
        public void CountShopItems_ReturnsOne_OnSingleItemShop()
        {
            var rom = MakeFE8UWithHenseiShop();
            int count = ItemShopCore.CountShopItems(rom, 0x200);
            Assert.Equal(1, count);
        }

        // ===================================================================
        // Source-grep: hensei pointer reference moved from VM to Core
        // ===================================================================

        [Fact]
        public void ItemShopCore_References_HenseiPointer()
        {
            // The original AvaloniaEditorTests.ItemShopViewModel_UsesShopPointer asserted
            // that `item_shop_hensei_pointer` appeared in the VM source. After the parity
            // refactor, that pointer reference moved into ItemShopCore. This test enforces
            // that the constant is still referenced somewhere in the Core helper.
            var srcPath = System.IO.Path.Combine(
                System.AppDomain.CurrentDomain.BaseDirectory,
                "..", "..", "..", "..",
                "FEBuilderGBA.Core", "ItemShopCore.cs");
            string src = System.IO.File.ReadAllText(srcPath);
            Assert.Contains("item_shop_hensei_pointer", src);
        }
    }
}
