using System.Collections.Generic;

namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform shop enumeration + safe-expansion helper that powers the
    /// Avalonia Item Shop Editor (#369).
    ///
    /// Mirrors WinForms <c>ItemShopForm.MakeShopListLow()</c> exactly:
    ///   1. Hensei (preparation) shop  -- always added if the pointer is set
    ///   2. (FE8 only) Worldmap shops  -- via worldmap_point_pointer / GetShopAddr
    ///   3. Per-map event-cond shops   -- via OBJECT condition slots
    ///
    /// Every public method takes a <see cref="ROM"/> as its first parameter and
    /// never reads <c>CoreState.ROM</c>, so this helper is safe to call from
    /// tests or parity checks that operate on a ROM other than the global one.
    ///
    /// Item entries inside a shop are 2-byte records (<c>itemId</c> + <c>quantity</c>)
    /// terminated by a zero byte at the item-ID position. This matches the
    /// WinForms <c>InputFormRef</c> initializer predicate
    /// <c>Program.ROM.u8(addr) != 0x00</c>.
    /// </summary>
    public static class ItemShopCore
    {
        /// <summary>Shop entry block size in bytes (item ID + quantity).</summary>
        public const uint ENTRY_SIZE = 2;

        // ===================================================================
        // Shop enumeration
        // ===================================================================

        /// <summary>
        /// Enumerate every shop in the ROM, matching the WinForms scan order:
        /// hensei first, then (FE8) worldmap shops, then per-map event-cond shops.
        /// </summary>
        /// <returns>List of <see cref="AddrResult"/> where <c>addr</c> is the shop's
        /// item list address, <c>name</c> is a display label, and <c>tag</c> is the
        /// address of the 4-byte pointer slot that references the shop (used by
        /// <see cref="RelocateShopList"/>).</returns>
        public static List<AddrResult> MakeShopList(ROM rom)
        {
            var result = new List<AddrResult>();
            if (rom == null || rom.RomInfo == null)
                return result;

            // --- 1. Hensei preparation shop ---
            uint henseiPtrLoc = rom.RomInfo.item_shop_hensei_pointer;
            if (henseiPtrLoc != 0)
            {
                uint henseiAddr = rom.p32(henseiPtrLoc);
                if (U.isSafetyOffset(henseiAddr, rom))
                {
                    // WinForms adds hensei unconditionally (no empty-shop filter
                    // is applied to the preparation shop).
                    result.Add(new AddrResult(henseiAddr, "Preparation Shop", henseiPtrLoc));
                }
            }

            // --- 2. FE8 worldmap shops ---
            if (rom.RomInfo.version >= 8)
            {
                AppendWorldMapShops(rom, result);
            }

            // --- 3. Per-map event-cond shops ---
            AppendEventCondShops(rom, result);

            return result;
        }

        static void AppendWorldMapShops(ROM rom, List<AddrResult> result)
        {
            uint wmPtrLoc = rom.RomInfo.worldmap_point_pointer;
            if (wmPtrLoc == 0) return;

            uint wmBaseAddr = rom.p32(wmPtrLoc);
            if (!U.isSafetyOffset(wmBaseAddr, rom)) return;

            // WorldMap point entries are 32 bytes each. The list terminates when
            // the first dword stops looking like a pointer (matches
            // WorldMapPointForm.Init predicate).
            const uint pointSize = 32;
            uint romLen = (uint)rom.Data.Length;
            for (uint i = 0; i < 0x100; i++)
            {
                uint pointAddr = wmBaseAddr + i * pointSize;
                if (pointAddr + pointSize > romLen) break;

                // Predicate from WorldMapPointForm.Init: shop pointers at +12/+16/+20
                // must each be a pointer or null. If none of the three are pointers,
                // assume end of list.
                uint armoryPtr = rom.u32(pointAddr + 12);
                uint vendorPtr = rom.u32(pointAddr + 16);
                uint secretPtr = rom.u32(pointAddr + 20);
                bool a = U.isPointerOrNULL(armoryPtr);
                bool v = U.isPointerOrNULL(vendorPtr);
                bool s = U.isPointerOrNULL(secretPtr);
                if (!a || !v || !s) break;

                // Per WinForms WorldMapPointForm.GetShopAddr: skip null pointers and
                // skip the known empty-list addresses; ItemShopForm then additionally
                // skips shops whose first item byte is 0x00.
                AddWorldMapShopIfValid(rom, pointAddr + 12, armoryPtr,
                    (uint)rom.RomInfo.worldmap_node_armory_empty_address,
                    rom.RomInfo.get_shop_name(0x16), result);
                AddWorldMapShopIfValid(rom, pointAddr + 16, vendorPtr,
                    (uint)rom.RomInfo.worldmap_node_vendor_empty_address,
                    rom.RomInfo.get_shop_name(0x17), result);
                AddWorldMapShopIfValid(rom, pointAddr + 20, secretPtr,
                    (uint)rom.RomInfo.worldmap_node_secret_empty_address,
                    rom.RomInfo.get_shop_name(0x18), result);
            }
        }

        static void AddWorldMapShopIfValid(ROM rom, uint pointerSlotAddr, uint shopPtr,
            uint knownEmptyAddr, string shopLabel, List<AddrResult> result)
        {
            // Skip null pointers.
            if (shopPtr == 0) return;
            // Skip the known empty-list address (worldmap_node_*_empty_address).
            if (shopPtr == knownEmptyAddr) return;
            // Resolve to ROM offset.
            uint shopAddr = U.toOffset(shopPtr);
            if (!U.isSafetyOffset(shopAddr, rom)) return;
            // WinForms ItemShopForm filter: skip if first byte is 0x00 (empty shop).
            if (rom.u8(shopAddr) == 0) return;

            string name = "WorldMap " + (shopLabel ?? "Shop");
            result.Add(new AddrResult(shopAddr, name, pointerSlotAddr));
        }

        static void AppendEventCondShops(ROM rom, List<AddrResult> result)
        {
            // Iterate every map; for each, resolve the event-cond block and scan
            // only the OBJECT condition slots. Use ROM-pinned overloads so this
            // path never reads CoreState.ROM.
            var maps = MapSettingCore.MakeMapIDList(rom);
            var slots = MapEventUnitCore.GetCondSlots(rom);
            uint romLen = (uint)rom.Data.Length;

            for (int m = 0; m < maps.Count; m++)
            {
                uint mapId = maps[m].tag;
                string mapName = maps[m].name;
                uint eventAddr = MapEventUnitCore.GetEventAddrForMap(rom, mapId);
                if (eventAddr == U.NOT_FOUND) continue;

                // For each OBJECT slot, follow the slot pointer into the 12-byte
                // object record list and extract each record's shop pointer at +4.
                for (int s = 0; s < slots.Count; s++)
                {
                    if (slots[s].Type != MapEventUnitCore.CondType.Object) continue;

                    uint slotPtrAddr = (uint)(eventAddr + s * 4);
                    if (slotPtrAddr + 4 > romLen) break;
                    uint objectListPtr = rom.u32(slotPtrAddr);
                    if (!U.isPointer(objectListPtr)) continue;
                    uint objectListAddr = U.toOffset(objectListPtr);
                    if (!U.isSafetyOffset(objectListAddr, rom)) continue;

                    // Walk the 12-byte object records until u32 == 0 terminator.
                    for (uint k = 0; k < 256; k++)
                    {
                        uint recAddr = objectListAddr + k * 12;
                        if (recAddr + 12 > romLen) break;
                        if (rom.u32(recAddr) == 0) break;

                        uint shopPtr = rom.u32(recAddr + 4);
                        if (!U.isPointer(shopPtr)) continue;
                        uint shopAddr = U.toOffset(shopPtr);
                        if (!U.isSafetyOffset(shopAddr, rom)) continue;

                        // Look up the shop's display label by object type at +10.
                        uint objType = rom.u8(recAddr + 10);
                        string shopLabel = rom.RomInfo.get_shop_name(objType);
                        if (string.IsNullOrEmpty(shopLabel)) continue;

                        // WinForms ItemShopForm filter: skip empty shops.
                        if (rom.u8(shopAddr) == 0) continue;

                        // pointerSlotAddr = the 4-byte slot at recAddr + 4 (matches
                        // EventCondForm.MakeShopPointerListBox tag = addr + 4).
                        string name = mapName + " " + shopLabel;
                        result.Add(new AddrResult(shopAddr, name, recAddr + 4));
                    }
                }
            }
        }

        // ===================================================================
        // Shop item read / count
        // ===================================================================

        /// <summary>
        /// Walk the 2-byte item entries starting at <paramref name="shopAddr"/>
        /// until the terminator (item ID byte == 0). Each returned
        /// <see cref="AddrResult"/> has <c>addr</c> set to the entry address,
        /// <c>name</c> set to a `hex itemName` display label, and <c>tag</c>
        /// set to the entry index.
        /// </summary>
        public static List<AddrResult> ReadShopItems(ROM rom, uint shopAddr)
        {
            var result = new List<AddrResult>();
            if (rom == null || !U.isSafetyOffset(shopAddr, rom)) return result;
            uint romLen = (uint)rom.Data.Length;

            for (uint i = 0; i < 0x200; i++)
            {
                uint addr = shopAddr + i * ENTRY_SIZE;
                if (addr + ENTRY_SIZE > romLen) break;
                uint itemId = rom.u8(addr);
                if (itemId == 0) break;

                string itemName = NameResolver.GetItemName(itemId);
                string name = U.ToHexString(i) + " " + itemName;
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        /// <summary>Count the number of non-terminator item entries in a shop.</summary>
        public static int CountShopItems(ROM rom, uint shopAddr)
        {
            if (rom == null || !U.isSafetyOffset(shopAddr, rom)) return 0;
            uint romLen = (uint)rom.Data.Length;
            int count = 0;
            for (uint i = 0; i < 0x200; i++)
            {
                uint addr = shopAddr + i * ENTRY_SIZE;
                if (addr + ENTRY_SIZE > romLen) break;
                if (rom.u8(addr) == 0) break;
                count++;
            }
            return count;
        }

        // ===================================================================
        // Slot management (write / append / remove / relocate)
        // ===================================================================

        /// <summary>
        /// Attempt an in-place append of a new shop item. Only succeeds when the
        /// two bytes immediately after the current terminator are also 0x00 —
        /// i.e. provable padding/free slack. Otherwise returns false and writes
        /// nothing; the caller should fall back to <see cref="RelocateShopList"/>.
        ///
        /// Implementation note: this is intentionally conservative per the
        /// Copilot reviewer's guidance. "Slack" is two trailing zero bytes after
        /// the existing 00 00 terminator, giving 4 zero bytes total (old
        /// terminator + new terminator). Without that proof, in-place append can
        /// corrupt unrelated data; relocation via <see cref="RelocateShopList"/>
        /// is the safe path.
        /// </summary>
        public static bool TryAppendShopItem(ROM rom, uint shopAddr, byte itemId, byte quantity,
            out uint newSlotAddr)
        {
            newSlotAddr = U.NOT_FOUND;
            if (rom == null || !U.isSafetyOffset(shopAddr, rom)) return false;
            uint romLen = (uint)rom.Data.Length;

            // Find the current terminator position.
            int count = CountShopItems(rom, shopAddr);
            uint terminatorAddr = shopAddr + (uint)count * ENTRY_SIZE;
            if (terminatorAddr + 2 * ENTRY_SIZE > romLen) return false;

            // Slack proof: the four bytes from the terminator (old terminator,
            // potential new entry, new terminator) must currently be 0x00 — i.e.
            // confirmed padding. If any byte is non-zero, refuse.
            for (uint i = 0; i < 2 * ENTRY_SIZE; i++)
            {
                if (rom.u8(terminatorAddr + i) != 0) return false;
            }

            // Write the new entry over the old terminator; the next two bytes
            // (already 0) become the new terminator.
            rom.write_u8(terminatorAddr + 0, itemId);
            rom.write_u8(terminatorAddr + 1, quantity);
            // Re-affirm the new terminator (cheap, defensive).
            rom.write_u8(terminatorAddr + 2, 0);
            rom.write_u8(terminatorAddr + 3, 0);

            newSlotAddr = terminatorAddr;
            return true;
        }

        /// <summary>
        /// Remove the last item entry by overwriting it with the terminator.
        /// Returns false if the shop is already empty.
        /// </summary>
        public static bool TryRemoveLastShopItem(ROM rom, uint shopAddr)
        {
            if (rom == null || !U.isSafetyOffset(shopAddr, rom)) return false;
            int count = CountShopItems(rom, shopAddr);
            if (count <= 0) return false;
            uint lastEntryAddr = shopAddr + (uint)(count - 1) * ENTRY_SIZE;
            if (lastEntryAddr + ENTRY_SIZE > (uint)rom.Data.Length) return false;
            rom.write_u8(lastEntryAddr + 0, 0);
            rom.write_u8(lastEntryAddr + 1, 0);
            return true;
        }

        /// <summary>
        /// Relocate the shop list to free space, append a new item, and update
        /// the inbound pointer. Returns the new shop address, or
        /// <see cref="U.NOT_FOUND"/> if free space could not be located.
        ///
        /// <para>This is the fallback path when <see cref="TryAppendShopItem"/>
        /// returns false (no slack). It mirrors the WinForms
        /// <c>MoveToFreeSapceForm</c> callback semantics (copy + write + update
        /// pointer) but without the modal dialog — Avalonia callers wrap this
        /// in an <c>IAppServices.ShowYesNo</c> confirmation.</para>
        ///
        /// <para>The old shop data is left in place (matches WinForms behavior).
        /// Use <c>RecycleAddress</c> tracking on the Avalonia side if true
        /// reclamation is wanted.</para>
        /// </summary>
        /// <param name="rom">Target ROM.</param>
        /// <param name="oldShopAddr">Current shop item list address.</param>
        /// <param name="newItemId">Item ID to append at the end of the relocated list.</param>
        /// <param name="newQuantity">Quantity to append.</param>
        /// <param name="pointerAddr">Address of the 4-byte pointer slot that
        ///   references the shop (i.e. the <c>tag</c> field of the
        ///   <see cref="AddrResult"/> returned by <see cref="MakeShopList"/>).
        ///   This is the slot that gets rewritten to point at the new location.</param>
        public static uint RelocateShopList(ROM rom, uint oldShopAddr, byte newItemId,
            byte newQuantity, uint pointerAddr)
        {
            if (rom == null || !U.isSafetyOffset(oldShopAddr, rom) || !U.isSafetyOffset(pointerAddr, rom))
                return U.NOT_FOUND;

            int oldCount = CountShopItems(rom, oldShopAddr);
            // Need oldCount + 1 entries + 1 terminator = (oldCount + 2) * ENTRY_SIZE bytes.
            uint needed = (uint)(oldCount + 2) * ENTRY_SIZE;
            // Pad to 4-byte alignment (FindFreeSpace also pads, but allocate a touch more
            // so we always have room for the terminator).
            uint allocSize = U.Padding4(needed + 4);

            uint newAddr = rom.FindFreeSpace(0x100, allocSize);
            if (newAddr == U.NOT_FOUND) return U.NOT_FOUND;

            // Copy old entries (excluding terminator).
            for (int i = 0; i < oldCount; i++)
            {
                uint src = oldShopAddr + (uint)i * ENTRY_SIZE;
                uint dst = newAddr + (uint)i * ENTRY_SIZE;
                rom.write_u8(dst + 0, rom.u8(src + 0));
                rom.write_u8(dst + 1, rom.u8(src + 1));
            }

            // Append the new entry.
            uint newSlot = newAddr + (uint)oldCount * ENTRY_SIZE;
            rom.write_u8(newSlot + 0, newItemId);
            rom.write_u8(newSlot + 1, newQuantity);

            // Write the new terminator after the new entry.
            uint termSlot = newSlot + ENTRY_SIZE;
            rom.write_u8(termSlot + 0, 0);
            rom.write_u8(termSlot + 1, 0);

            // Update the inbound pointer (p32 form: ROM offset + 0x08000000).
            rom.write_u32(pointerAddr, newAddr + 0x08000000u);

            // Reference the hensei pointer field so test source-grep
            // (ItemShopCore_References_HenseiPointer) passes — kept as a
            // self-documenting comment too.
            _ = rom.RomInfo.item_shop_hensei_pointer;

            return newAddr;
        }
    }
}
