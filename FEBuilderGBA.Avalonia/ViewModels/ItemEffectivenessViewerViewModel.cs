using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// Issue #368 — item-driven view-model for the Avalonia Item Effectiveness
    /// editor. Mirrors WinForms <c>ItemEffectivenessForm.Init</c>:
    ///   * Outer list iterates the item table by <c>item_pointer</c> +
    ///     <c>item_datasize</c>; terminates on the first row whose +12 or
    ///     +16 is not pointer-or-null; emits only rows whose +16 dereferences
    ///     to a safe ROM offset.
    ///   * Inner list scans the null-terminated byte array at that
    ///     effectiveness pointer.
    ///   * Per-class edit reads/writes a single byte through
    ///     <see cref="ItemClassListCore.WriteClassByte"/>.
    /// </summary>
    public class ItemEffectivenessViewerViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentItemAddr;       // address of the item struct
        uint _currentEffAddr;        // address of the effectiveness array (item +16 dereferenced)
        uint _currentClassAddr;      // address of the currently-selected class byte inside the inner array
        uint _classId;
        string _className = "";
        bool _canWrite;
        bool _hasSharedOwners;       // true when ItemListBox would have >1 owner
        int _itemIndex;              // index of the current outer-list item in the item table

        public uint CurrentItemAddr { get => _currentItemAddr; set => SetField(ref _currentItemAddr, value); }
        public uint CurrentEffAddr { get => _currentEffAddr; set => SetField(ref _currentEffAddr, value); }
        public uint CurrentClassAddr { get => _currentClassAddr; set => SetField(ref _currentClassAddr, value); }
        public uint ClassId { get => _classId; set => SetField(ref _classId, value); }
        public string ClassName { get => _className; set => SetField(ref _className, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }
        public bool HasSharedOwners { get => _hasSharedOwners; set => SetField(ref _hasSharedOwners, value); }
        public int ItemIndex { get => _itemIndex; set => SetField(ref _itemIndex, value); }

        /// <summary>
        /// Outer list — items whose +16 effectiveness pointer is a real ROM
        /// address. The <see cref="AddrResult.addr"/> field stores the
        /// EFFECTIVENESS ARRAY offset (the dereferenced +16 pointer) so
        /// <c>ItemEditorView.JumpToEffectiveness_Click</c> (which passes
        /// <c>ptr - 0x08000000</c>) and the
        /// <c>ItemEffectivenessViewerJumpTests</c> regression suite both
        /// resolve to the correct list row directly. Matches the post-#363
        /// data model.
        /// </summary>
        public List<AddrResult> LoadItemList()
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
                uint itemAddr = itemBase + i * dataSize;
                if (itemAddr + dataSize > (uint)rom.Data.Length) break;

                // WinForms item-table termination: first row whose +12 OR +16
                // is not pointer-or-null ends the table.
                if (!U.isPointerOrNULL(rom.u32(itemAddr + 12))) break;
                if (!U.isPointerOrNULL(rom.u32(itemAddr + 16))) break;

                uint critPtr = rom.u32(itemAddr + 16);
                if (!U.isPointer(critPtr)) continue;
                uint critOff = U.toOffset(critPtr);
                if (!U.isSafetyOffset(critOff)) continue;

                string itemName = NameResolver.GetItemName(i);
                string name = $"{U.ToHexString(i)} {itemName}";
                // addr = EFFECTIVENESS ARRAY offset (matches the source-side
                // address from ItemEditorView.JumpToEffectiveness_Click).
                result.Add(new AddrResult(critOff, name, i));
            }
            return result;
        }

        /// <summary>
        /// Legacy alias for backward compatibility with the existing #363
        /// regression suite (<c>ItemEffectivenessViewerJumpTests</c>) which
        /// calls <c>LoadItemEffectivenessList()</c>.
        /// </summary>
        public List<AddrResult> LoadItemEffectivenessList() => LoadItemList();

        /// <summary>
        /// Legacy alias used by the #363 regression suite. Equivalent to
        /// <see cref="CurrentEffAddr"/>.
        /// </summary>
        public uint CurrentAddr
        {
            get => _currentEffAddr;
            set => CurrentEffAddr = value;
        }

        /// <summary>
        /// Load the inner class list for a selected outer row. Accepts EITHER
        /// the effectiveness ARRAY offset (preferred — matches the outer list
        /// stored key and the #363 jump source) OR an item struct address
        /// (back-compat for callers that still pass the item). Sets
        /// <see cref="CurrentEffAddr"/> and resolves the owning item via
        /// <see cref="ItemClassListCore.FindItemsSharingPointer"/>.
        /// </summary>
        public List<AddrResult> LoadInnerClassList(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return new List<AddrResult>();
            if (addr + 4 > (uint)rom.Data.Length) return new List<AddrResult>();

            uint critOff;
            uint itemAddr;

            // Heuristic: if addr+16 is in range AND rom.u32(addr+16) is a real
            // pointer, treat addr as an ITEM struct address (legacy path).
            // Otherwise treat addr as an ARRAY offset directly.
            bool looksLikeItem = false;
            if (addr + 20 <= (uint)rom.Data.Length)
            {
                uint maybeCrit = rom.u32(addr + 16);
                if (U.isPointer(maybeCrit) && U.isSafetyOffset(U.toOffset(maybeCrit)))
                {
                    // Could be item; but also could be array byte that happens
                    // to point somewhere. Disambiguate by checking the item
                    // table membership.
                    uint itemBase = rom.p32(rom.RomInfo.item_pointer);
                    uint dataSize = rom.RomInfo.item_datasize;
                    if (dataSize > 0 && addr >= itemBase
                        && (addr - itemBase) % dataSize == 0)
                    {
                        looksLikeItem = true;
                    }
                }
            }

            if (looksLikeItem)
            {
                itemAddr = addr;
                uint critPtr = rom.u32(addr + 16);
                critOff = U.toOffset(critPtr);
            }
            else
            {
                critOff = addr;
                // Find the first item that owns this array.
                var owners = ItemClassListCore.FindItemsSharingPointer(rom, critOff);
                uint itemBase = rom.p32(rom.RomInfo.item_pointer);
                uint dataSize = rom.RomInfo.item_datasize;
                itemAddr = owners.Count > 0
                    ? itemBase + owners[0] * dataSize
                    : 0;
            }

            if (!U.isSafetyOffset(critOff)) return new List<AddrResult>();

            CurrentItemAddr = itemAddr;
            CurrentEffAddr = critOff;
            if (itemAddr != 0)
            {
                uint itemBase = rom.p32(rom.RomInfo.item_pointer);
                uint dataSize = rom.RomInfo.item_datasize;
                if (dataSize > 0 && itemAddr >= itemBase)
                {
                    ItemIndex = (int)((itemAddr - itemBase) / dataSize);
                }
            }

            var classes = ItemClassListCore.ScanClassList(rom, critOff);

            var result = new List<AddrResult>();
            for (int i = 0; i < classes.Count; i++)
            {
                uint classId = classes[i];
                uint byteAddr = critOff + (uint)i;
                string className = NameResolver.GetClassName(classId);
                string name = $"{U.ToHexString(classId)} {className}";
                result.Add(new AddrResult(byteAddr, name, classId));
            }
            CanWrite = true;
            return result;
        }

        /// <summary>Load the list of items that point at the current effectiveness pointer.</summary>
        public List<AddrResult> LoadSharedOwners()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null || CurrentEffAddr == 0) return new List<AddrResult>();

            var owners = ItemClassListCore.FindItemsSharingPointer(rom, CurrentEffAddr);
            var result = new List<AddrResult>();
            uint itemBase = rom.p32(rom.RomInfo.item_pointer);
            uint dataSize = rom.RomInfo.item_datasize;
            foreach (uint id in owners)
            {
                uint itemAddr = itemBase + id * dataSize;
                string name = $"{U.ToHexString(id)} {NameResolver.GetItemName(id)}";
                result.Add(new AddrResult(itemAddr, name, id));
            }
            HasSharedOwners = result.Count > 1;
            return result;
        }

        /// <summary>Read a single class byte (used when the inner list selection changes).</summary>
        public void LoadClassByte(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr >= (uint)rom.Data.Length) return;
            CurrentClassAddr = addr;
            ClassId = rom.u8(addr);
            ClassName = NameResolver.GetClassName(ClassId);
        }

        /// <summary>
        /// Write the single class byte at <see cref="CurrentClassAddr"/>.
        /// Throws when the undo manager is unavailable (rather than silently
        /// succeeding — Copilot bot review on PR #463 caught the silent-fail).
        /// </summary>
        public void WriteCurrentClassByte()
        {
            ROM rom = CoreState.ROM
                ?? throw new InvalidOperationException("No ROM loaded.");
            if (CurrentClassAddr == 0)
                throw new InvalidOperationException("No class slot selected.");
            var undoMgr = CoreState.Undo
                ?? throw new InvalidOperationException("Undo manager unavailable.");
            var undo = undoMgr.NewUndoData(this, "Edit Item Effectiveness");
            ItemClassListCore.WriteClassByte(rom, CurrentClassAddr, ClassId, undo);
            undoMgr.Push(undo);
        }

        /// <summary>Append a new editable 0-slot to the current effectiveness array.</summary>
        public uint ExpandCurrentList()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentItemAddr == 0)
                throw new InvalidOperationException("No item selected.");
            var undoMgr = CoreState.Undo;
            if (undoMgr == null) throw new InvalidOperationException("Undo manager unavailable.");
            uint ptrAddr = CurrentItemAddr + 16;
            var undo = undoMgr.NewUndoData(this, "Expand Item Effectiveness");
            uint newAddr = ItemClassListCore.ExpandClassList(rom, ptrAddr, undo);
            undoMgr.Push(undo);
            CurrentEffAddr = newAddr;
            return newAddr;
        }

        /// <summary>Fork the shared effectiveness array into an independent copy for the current item.</summary>
        public uint MakeCurrentItemIndependent()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentItemAddr == 0 || CurrentEffAddr == 0)
                throw new InvalidOperationException("No item selected.");
            var undoMgr = CoreState.Undo;
            if (undoMgr == null) throw new InvalidOperationException("Undo manager unavailable.");
            uint ptrAddr = CurrentItemAddr + 16;
            var undo = undoMgr.NewUndoData(this, "Independence Item Effectiveness");
            uint newAddr = ItemClassListCore.MakeIndependentCopy(rom, CurrentEffAddr, ptrAddr, undo);
            undoMgr.Push(undo);
            CurrentEffAddr = newAddr;
            return newAddr;
        }

        public int GetListCount() => LoadItemList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["item_addr"] = $"0x{CurrentItemAddr:X08}",
                ["effectiveness_addr"] = $"0x{CurrentEffAddr:X08}",
                ["class_byte_addr"] = $"0x{CurrentClassAddr:X08}",
                ["ClassId"] = $"0x{ClassId:X02}",
                ["ClassName"] = ClassName,
                ["HasSharedOwners"] = HasSharedOwners.ToString(),
            };
        }

        public Dictionary<string, string> GetRawRomReport()
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return new Dictionary<string, string>();
            var report = new Dictionary<string, string>
            {
                ["item_addr"] = $"0x{CurrentItemAddr:X08}",
                ["effectiveness_addr"] = $"0x{CurrentEffAddr:X08}",
                ["class_byte_addr"] = $"0x{CurrentClassAddr:X08}",
            };
            // Always include u8@0x00 — DataVerifiableSweepTests requires the
            // key listed by GetFieldOffsetMap to be present even when no slot
            // has been selected yet (the value defaults to 0 in that case).
            report["u8@0x00"] = CurrentClassAddr != 0
                ? $"0x{rom.u8(CurrentClassAddr):X02}"
                : "0x00";
            if (CurrentItemAddr != 0)
            {
                // Item-table +12 correction and +16 effectiveness pointer.
                report["u32@0x0C"] = $"0x{rom.u32(CurrentItemAddr + 12):X08}";
                report["u32@0x10"] = $"0x{rom.u32(CurrentItemAddr + 16):X08}";
            }
            return report;
        }

        public Dictionary<string, string> GetFieldOffsetMap()
        {
            return new Dictionary<string, string>
            {
                ["ClassId"] = "u8@0x00",
            };
        }
    }
}
