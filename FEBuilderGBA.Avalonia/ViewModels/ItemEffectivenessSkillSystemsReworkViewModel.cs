using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ItemEffectivenessSkillSystemsReworkViewModel : ViewModelBase
    {
        uint _currentAddr;          // effectiveness-array offset (the outer-list key)
        uint _currentItemAddr;      // item struct that owns the selected array (for expand / independence)
        uint _currentEntryAddr;     // 4-byte entry currently selected in the inner list
        uint _coefficient;          // entry +1 (coefficient_times*2)
        uint _classType;            // entry +2 (u16 class-type bitmask)
        string _classTypeNames = "";
        bool _hasSharedOwners;
        bool _isLoaded;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public uint CurrentItemAddr { get => _currentItemAddr; set => SetField(ref _currentItemAddr, value); }
        public uint CurrentEntryAddr { get => _currentEntryAddr; set => SetField(ref _currentEntryAddr, value); }
        public uint Coefficient { get => _coefficient; set => SetField(ref _coefficient, value); }
        public uint ClassType { get => _classType; set => SetField(ref _classType, value); }
        public string ClassTypeNames { get => _classTypeNames; set => SetField(ref _classTypeNames, value); }
        public bool HasSharedOwners { get => _hasSharedOwners; set => SetField(ref _hasSharedOwners, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        /// <summary>
        /// Enumerate items that have a valid P16 (item effectiveness) pointer.
        /// Each emitted <see cref="AddrResult"/> uses the P16 ROM offset as its
        /// <c>addr</c>, so the address passed by
        /// <c>ItemEditorView.JumpToEffectiveness_Click</c> (which is
        /// <c>ptr - 0x08000000</c>) matches a list row directly and the
        /// receiving editor's selection lands on the correct item — not the
        /// previous stub at index 0 (issue #362).
        ///
        /// Mirrors the WinForms <c>ItemEffectivenessSkillSystemsReworkForm</c>
        /// (and the existing Avalonia <c>ItemStatBonusesViewerViewModel</c>)
        /// iteration semantics: walk the item table by
        /// <c>itemBase + i * item_datasize</c> using the dereferenced
        /// <c>item_pointer</c>. The loop <c>break</c>s on the first row whose
        /// P12 or P16 is not pointer-or-null (mirroring
        /// <c>InputFormRef.DataCount</c> termination), and <c>continue</c>s
        /// past rows whose P16 is null / zero / out-of-range (the item is
        /// valid but carries no effectiveness data — nothing to navigate to).
        /// </summary>
        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint itemPtr = rom.RomInfo.item_pointer;
            if (itemPtr == 0) return new List<AddrResult>();

            uint itemBase = rom.p32(itemPtr);
            if (!U.isSafetyOffset(itemBase)) return new List<AddrResult>();

            uint dataSize = rom.RomInfo.item_datasize;
            if (dataSize == 0) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x200; i++)
            {
                uint itemAddr = (uint)(itemBase + i * dataSize);
                if (itemAddr + dataSize > (uint)rom.Data.Length) break;

                // Mirror WinForms InputFormRef.DataCount termination: data
                // table ends on the first row whose P12 (offset +12) or P16
                // (offset +16) is not pointer-or-null. Both must be valid for
                // the row to count as an item.
                if (!U.isPointerOrNULL(rom.u32(itemAddr + 12))) break;
                if (!U.isPointerOrNULL(rom.u32(itemAddr + 16))) break;

                // Skip rows where P16 is null/zero or not a real pointer —
                // the item is valid but carries no effectiveness data, so
                // there is nothing to navigate to.
                uint criticalPtr = rom.u32(itemAddr + 16);
                if (!U.isPointer(criticalPtr)) continue;

                uint criticalAddr = U.toOffset(criticalPtr);
                if (!U.isSafetyOffset(criticalAddr)) continue;

                string itemName = NameResolver.GetItemName(i);
                string name = $"{U.ToHexString(i)} {itemName}";
                result.Add(new AddrResult(criticalAddr, name, i));
            }
            return result;
        }

        /// <summary>
        /// Select an outer-list row by its effectiveness-array offset. Resolves
        /// the owning item via <see cref="ItemClassListCore.FindItemsSharingPointer"/>
        /// (used for the Expand / Make-Independent pointer writes) and records
        /// the array offset as the editor's current address.
        /// </summary>
        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            CurrentAddr = addr;
            CurrentItemAddr = ResolveOwningItemAddr(rom, addr);
            CurrentEntryAddr = 0;
            Coefficient = 0;
            ClassType = 0;
            ClassTypeNames = "";
            IsLoaded = true;
        }

        /// <summary>
        /// Pin the current owning item explicitly by item ID. When several items
        /// share one effectiveness array, <see cref="LoadEntry"/>'s address-based
        /// resolution can only return the first owner, so Expand / Make-Independent
        /// would act on the wrong item when the user picked a different shared row.
        /// The view calls this with the actually-selected row's
        /// <see cref="AddrResult.tag"/> to correct <see cref="CurrentItemAddr"/>.
        /// No-op (leaves the address-resolved fallback) if the ID is out of range.
        /// </summary>
        public void SetCurrentItemById(uint itemId)
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return;
            uint itemBase = rom.p32(rom.RomInfo.item_pointer);
            uint dataSize = rom.RomInfo.item_datasize;
            if (!U.isSafetyOffset(itemBase) || dataSize == 0) return;
            uint itemAddr = itemBase + itemId * dataSize;
            if (itemAddr + dataSize > (uint)rom.Data.Length) return;
            CurrentItemAddr = itemAddr;
        }

        /// <summary>
        /// The "effective against" list — the 4-byte Rework entries at the
        /// current effectiveness array. Each <see cref="AddrResult.addr"/> is
        /// the entry's ROM offset; the name is the class-type bitmask decoded
        /// to its set bit names (localized) prefixed by the raw hex value, so
        /// it mirrors the WinForms <c>N_AddressList</c> rows.
        /// </summary>
        public List<AddrResult> LoadInnerEntries()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return new List<AddrResult>();

            var entries = ItemClassListCore.ScanReworkEntries(rom, CurrentAddr);
            var result = new List<AddrResult>();
            foreach (var e in entries)
            {
                string names = ClassTypeDisplay(e.ClassType);
                string label = string.IsNullOrEmpty(names)
                    ? U.ToHexString(e.ClassType)
                    : $"{U.ToHexString(e.ClassType)} {names}";
                result.Add(new AddrResult(e.Addr, label, e.ClassType));
            }
            return result;
        }

        /// <summary>Items that point at the current effectiveness array (WF ItemListBox).</summary>
        public List<AddrResult> LoadSharedOwners()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null || CurrentAddr == 0) return new List<AddrResult>();

            var owners = ItemClassListCore.FindItemsSharingPointer(rom, CurrentAddr);
            var result = new List<AddrResult>();
            uint itemBase = rom.p32(rom.RomInfo.item_pointer);
            uint dataSize = rom.RomInfo.item_datasize;
            if (!U.isSafetyOffset(itemBase) || dataSize == 0)
            {
                HasSharedOwners = false;
                return result;
            }
            foreach (uint id in owners)
            {
                uint itemAddr = itemBase + id * dataSize;
                string name = $"{U.ToHexString(id)} {NameResolver.GetItemName(id)}";
                result.Add(new AddrResult(itemAddr, name, id));
            }
            HasSharedOwners = result.Count > 1;
            return result;
        }

        /// <summary>Read the selected 4-byte entry (inner-list selection changed).</summary>
        public void LoadEntryFields(uint entryAddr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (entryAddr + ItemClassListCore.ReworkEntrySize > (uint)rom.Data.Length) return;
            CurrentEntryAddr = entryAddr;
            Coefficient = rom.u8(entryAddr + 1);
            ClassType = rom.u16(entryAddr + 2);
            ClassTypeNames = ClassTypeDisplay(ClassType);
        }

        /// <summary>
        /// Write the coefficient + class-type of the currently-selected entry.
        /// The caller owns the undo group (an explicit <see cref="Undo.UndoData"/>)
        /// so the ROM writes stay inside the editor's undo scope rather than the
        /// thread-local ambient one.
        /// </summary>
        public void WriteCurrentEntry(Undo.UndoData undo)
        {
            ROM rom = CoreState.ROM
                ?? throw new InvalidOperationException("No ROM loaded.");
            if (undo == null) throw new ArgumentNullException(nameof(undo));
            if (CurrentEntryAddr == 0)
                throw new InvalidOperationException("No effectiveness entry selected.");
            ItemClassListCore.WriteReworkEntry(rom, CurrentEntryAddr, Coefficient, ClassType, undo);
            ClassTypeNames = ClassTypeDisplay(ClassType);
        }

        /// <summary>Append a new editable Rework entry to the current array.</summary>
        public uint ExpandCurrentList(Undo.UndoData undo)
        {
            ROM rom = CoreState.ROM
                ?? throw new InvalidOperationException("No ROM loaded.");
            if (undo == null) throw new ArgumentNullException(nameof(undo));
            if (CurrentItemAddr == 0)
                throw new InvalidOperationException("No item owns this effectiveness list.");
            uint ptrAddr = CurrentItemAddr + 16;
            uint newAddr = ItemClassListCore.ExpandReworkList(rom, ptrAddr, undo);
            CurrentAddr = newAddr;
            return newAddr;
        }

        /// <summary>Fork the shared effectiveness array into an independent copy.</summary>
        public uint MakeCurrentItemIndependent(Undo.UndoData undo)
        {
            ROM rom = CoreState.ROM
                ?? throw new InvalidOperationException("No ROM loaded.");
            if (undo == null) throw new ArgumentNullException(nameof(undo));
            if (CurrentItemAddr == 0 || CurrentAddr == 0)
                throw new InvalidOperationException("No item owns this effectiveness list.");
            uint ptrAddr = CurrentItemAddr + 16;
            uint newAddr = ItemClassListCore.MakeIndependentReworkCopy(rom, CurrentAddr, ptrAddr, undo);
            CurrentAddr = newAddr;
            return newAddr;
        }

        /// <summary>
        /// Decode a class-type bitmask to its display text. The Core helper
        /// returns canonical English keys (Armor/Cavalry/...); run each through
        /// <c>R._()</c> so the row text is localized.
        /// </summary>
        static string ClassTypeDisplay(uint classType)
        {
            string raw = ItemClassListCore.GetClassTypeNames(classType);
            if (string.IsNullOrEmpty(raw)) return "";
            string[] keys = raw.Split(',');
            for (int i = 0; i < keys.Length; i++)
                keys[i] = R._(keys[i]);
            return string.Join(",", keys);
        }

        /// <summary>
        /// Find the item struct that owns <paramref name="effAddr"/> so the
        /// pointer-write operations (Expand / Make-Independent) know which +16
        /// slot to repoint. Returns 0 when no item owns the array.
        /// </summary>
        static uint ResolveOwningItemAddr(ROM rom, uint effAddr)
        {
            if (rom?.RomInfo == null || effAddr == 0) return 0;
            var owners = ItemClassListCore.FindItemsSharingPointer(rom, effAddr);
            if (owners.Count == 0) return 0;
            uint itemBase = rom.p32(rom.RomInfo.item_pointer);
            uint dataSize = rom.RomInfo.item_datasize;
            if (!U.isSafetyOffset(itemBase) || dataSize == 0) return 0;
            return itemBase + owners[0] * dataSize;
        }
    }
}
