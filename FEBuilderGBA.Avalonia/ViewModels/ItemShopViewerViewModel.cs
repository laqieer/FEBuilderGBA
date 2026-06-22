using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// ViewModel for the Avalonia Item Shop Editor (#369 parity with WinForms
    /// <c>ItemShopForm</c>). Delegates all shop enumeration / read / mutate
    /// operations to <see cref="ItemShopCore"/>.
    /// </summary>
    public class ItemShopViewerViewModel : ViewModelBase, IDataVerifiable
    {
        static readonly List<EditorFormRef.FieldDef> _fields =
            EditorFormRef.DetectFields(new[] { "B0", "B1" });

        // ---- Per-slot edit state ----
        uint _currentAddr;       // address of the selected 2-byte item entry
        uint _itemId;
        uint _quantity;
        bool _canWrite;

        // ---- Shop-level state ----
        uint _currentShopAddr;      // start address of the selected shop's item list
        uint _currentShopPointerAddr; // address of the 4-byte pointer slot that references the shop
        string _currentShopName = string.Empty;
        int _slotCount;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public uint ItemId { get => _itemId; set => SetField(ref _itemId, value); }
        public uint Quantity { get => _quantity; set => SetField(ref _quantity, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }

        public uint CurrentShopAddr { get => _currentShopAddr; set => SetField(ref _currentShopAddr, value); }
        public uint CurrentShopPointerAddr { get => _currentShopPointerAddr; set => SetField(ref _currentShopPointerAddr, value); }
        public string CurrentShopName { get => _currentShopName; set => SetField(ref _currentShopName, value); }
        public int SlotCount { get => _slotCount; set => SetField(ref _slotCount, value); }

        // ===================================================================
        // Shop enumeration / item listing
        // ===================================================================

        /// <summary>
        /// Return every shop in the ROM (hensei + worldmap + per-map event-cond).
        /// Calls <see cref="ItemShopCore.MakeShopList"/>.
        /// </summary>
        public List<AddrResult> LoadShopList()
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return new List<AddrResult>();
            return ItemShopCore.MakeShopList(rom);
        }

        /// <summary>
        /// Load the item list for a specific shop and remember its pointer slot
        /// for later append/relocate operations.
        /// </summary>
        public List<AddrResult> LoadShopItems(uint shopAddr, uint pointerAddr, string shopName)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return new List<AddrResult>();
            CurrentShopAddr = shopAddr;
            CurrentShopPointerAddr = pointerAddr;
            CurrentShopName = shopName ?? string.Empty;
            var items = ItemShopCore.ReadShopItems(rom, shopAddr);
            SlotCount = items.Count;
            return items;
        }

        // ===================================================================
        // Per-slot edit (matches WinForms B0 / B1 fields)
        // ===================================================================

        public void LoadItemShop(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 1 >= (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            var values = EditorFormRef.ReadFields(rom, addr, _fields);
            ItemId = values["B0"];
            Quantity = values["B1"];
            CanWrite = true;
        }

        public void WriteItemShop()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            var values = new Dictionary<string, uint>
            {
                ["B0"] = ItemId,
                ["B1"] = Quantity,
            };
            EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields);
        }

        // ===================================================================
        // Slot management
        // ===================================================================

        /// <summary>Result of an append attempt.</summary>
        public enum AppendOutcome
        {
            /// <summary>No-op (no shop selected or other precondition failure).</summary>
            NoOp,
            /// <summary>The new slot was appended in place over existing free space.</summary>
            AppendedInPlace,
            /// <summary>The shop list was relocated to ROM free space and the new slot appended.</summary>
            Relocated,
            /// <summary>Relocation was needed but free space could not be found.</summary>
            RelocationFailed,
        }

        /// <summary>
        /// Attempt to append a new item slot in place. Returns
        /// <see cref="AppendOutcome.AppendedInPlace"/> on success, or
        /// <see cref="AppendOutcome.NoOp"/> if no slack — caller can then
        /// confirm relocation via <see cref="AppendSlotWithRelocation"/>.
        /// New slot defaults to itemId=1 / qty=1 (itemId=0 would be treated
        /// as a terminator by ReadShopItems).
        /// </summary>
        public AppendOutcome TryAppendSlotInPlace(out uint newSlotAddr)
        {
            newSlotAddr = U.NOT_FOUND;
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentShopAddr == 0) return AppendOutcome.NoOp;

            if (ItemShopCore.TryAppendShopItem(rom, CurrentShopAddr, 1, 1, out newSlotAddr))
            {
                SlotCount = ItemShopCore.CountShopItems(rom, CurrentShopAddr);
                return AppendOutcome.AppendedInPlace;
            }
            return AppendOutcome.NoOp;
        }

        /// <summary>
        /// Relocate the current shop list to ROM free space and append a new item.
        /// Updates the inbound pointer. Returns <see cref="AppendOutcome.Relocated"/>
        /// on success and updates <see cref="CurrentShopAddr"/>.
        /// </summary>
        public AppendOutcome AppendSlotWithRelocation(out uint newShopAddr)
        {
            newShopAddr = U.NOT_FOUND;
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentShopAddr == 0 || CurrentShopPointerAddr == 0)
                return AppendOutcome.NoOp;

            uint result = ItemShopCore.RelocateShopList(rom, CurrentShopAddr, 1, 1, CurrentShopPointerAddr);
            if (result == U.NOT_FOUND)
                return AppendOutcome.RelocationFailed;

            newShopAddr = result;
            CurrentShopAddr = result;
            SlotCount = ItemShopCore.CountShopItems(rom, result);
            return AppendOutcome.Relocated;
        }

        /// <summary>Remove the last item slot from the current shop.</summary>
        public bool RemoveLastSlot()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentShopAddr == 0) return false;
            bool ok = ItemShopCore.TryRemoveLastShopItem(rom, CurrentShopAddr);
            if (ok)
                SlotCount = ItemShopCore.CountShopItems(rom, CurrentShopAddr);
            return ok;
        }

        // ===================================================================
        // Decomp source-routing (#1347 Slice 5a) — build the desired item vector
        // for a save without mutating the ROM, then route it to the owning source.
        // ===================================================================

        /// <summary>
        /// Read the current shop's item entries into a packed <c>(qty&lt;&lt;8)|id</c> u16
        /// vector WITHOUT mutating the ROM. Returns null when no shop is selected or the
        /// ROM is unavailable.
        /// </summary>
        ushort[] ReadCurrentShopVector()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentShopAddr == 0) return null;
            var entries = ItemShopCore.ReadShopItems(rom, CurrentShopAddr);
            var vec = new ushort[entries.Count];
            for (int i = 0; i < entries.Count; i++)
            {
                uint a = entries[i].addr;
                uint id = rom.u8(a);
                uint qty = rom.u8(a + 1);
                vec[i] = (ushort)((qty << 8) | id);
            }
            return vec;
        }

        /// <summary>
        /// Build the desired item vector for a per-slot WRITE: the current shop's items
        /// with the currently-selected slot replaced by the edited (<see cref="ItemId"/>,
        /// <see cref="Quantity"/>) pair. Matches the slot by address (== <see cref="CurrentAddr"/>).
        /// Returns null when no shop/slot is selected (caller: "select a slot first").
        /// Does NOT mutate the ROM.
        /// </summary>
        public ushort[] BuildVectorForWrite()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentShopAddr == 0 || CurrentAddr == 0) return null;

            var entries = ItemShopCore.ReadShopItems(rom, CurrentShopAddr);
            var vec = new ushort[entries.Count];
            int matched = -1;
            for (int i = 0; i < entries.Count; i++)
            {
                uint a = entries[i].addr;
                if (a == CurrentAddr)
                {
                    matched = i;
                    vec[i] = (ushort)((Quantity << 8) | ItemId);
                }
                else
                {
                    uint id = rom.u8(a);
                    uint qty = rom.u8(a + 1);
                    vec[i] = (ushort)((qty << 8) | id);
                }
            }
            if (matched < 0) return null;   // selected slot not part of this shop list
            return vec;
        }

        /// <summary>
        /// Build the desired item vector for an APPEND: the current shop's items plus one
        /// new entry (id=1, qty=1 — matching the ROM-path default). Returns null when no
        /// shop is selected. Does NOT mutate the ROM.
        /// </summary>
        public ushort[] BuildVectorForAppend()
        {
            ushort[] cur = ReadCurrentShopVector();
            if (cur == null) return null;
            var vec = new ushort[cur.Length + 1];
            Array.Copy(cur, vec, cur.Length);
            vec[cur.Length] = (ushort)((1 << 8) | 1);   // id=1, qty=1
            return vec;
        }

        /// <summary>
        /// Build the desired item vector for a REMOVE-LAST: the current shop's items minus
        /// the last entry. Returns null when no shop is selected OR the list is already
        /// empty (nothing to remove). Does NOT mutate the ROM.
        /// </summary>
        public ushort[] BuildVectorForRemoveLast()
        {
            ushort[] cur = ReadCurrentShopVector();
            if (cur == null || cur.Length == 0) return null;
            var vec = new ushort[cur.Length - 1];
            Array.Copy(cur, vec, cur.Length - 1);
            return vec;
        }

        /// <summary>
        /// Route a decomp-mode shop save to the owning decomp source list (#1347 Slice 5a).
        /// Resolves the current shop's address to a manifest u16-list owner and delegates to
        /// <see cref="DecompShopSourceWriteCore.TryRouteShopSaveToSource"/>. NEVER mutates the
        /// ROM — on a non-Routed result the caller keeps the #1159 ROM-only guard.
        /// </summary>
        public DecompShopRouteResult TryRouteCurrentShopToSource(IReadOnlyList<ushort> desired)
        {
            return DecompShopSourceWriteCore.TryRouteShopSaveToSource(
                CoreState.ROM,
                CoreState.DecompProject,
                CoreState.AsmMapFileAsmCache?.GetAsmMapFile(),
                CurrentShopAddr,
                desired);
        }

        // ===================================================================
        // IDataVerifiable
        // ===================================================================

        public int GetListCount() => LoadShopList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["ShopAddr"] = $"0x{CurrentShopAddr:X08}",
                ["ShopPointerAddr"] = $"0x{CurrentShopPointerAddr:X08}",
                ["ShopName"] = CurrentShopName,
                ["SlotCount"] = SlotCount.ToString(),
                ["ItemId"] = $"0x{ItemId:X02}",
                ["Quantity"] = $"0x{Quantity:X02}",
            };
        }

        public Dictionary<string, string> GetRawRomReport()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return new Dictionary<string, string>();
            uint a = CurrentAddr;
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{a:X08}",
                ["u8@0x00"] = $"0x{rom.u8(a + 0):X02}",
                ["u8@0x01"] = $"0x{rom.u8(a + 1):X02}",
            };
        }

        public Dictionary<string, string> GetFieldOffsetMap()
        {
            return new Dictionary<string, string>
            {
                ["ItemId"] = "u8@0x00",
                ["Quantity"] = "u8@0x01",
            };
        }
    }
}
