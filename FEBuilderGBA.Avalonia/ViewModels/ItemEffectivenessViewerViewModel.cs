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
        /// address. The <see cref="AddrResult.addr"/> field stores the ITEM
        /// struct's address so the caller can resolve the effectiveness
        /// pointer by reading <c>rom.p32(addr + 16)</c>.
        /// Mirrors the iteration semantics of
        /// <c>ItemEffectivenessSkillSystemsReworkViewModel.LoadList()</c>.
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
                // addr = ITEM struct address (so jumping back to an item works)
                result.Add(new AddrResult(itemAddr, name, i));
            }
            return result;
        }

        /// <summary>Load the inner class list for the selected item.</summary>
        public List<AddrResult> LoadInnerClassList(uint itemAddr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return new List<AddrResult>();
            if (itemAddr + 20 > (uint)rom.Data.Length) return new List<AddrResult>();

            CurrentItemAddr = itemAddr;
            // Identify item index for shared-owner display.
            uint itemBase = rom.p32(rom.RomInfo.item_pointer);
            uint dataSize = rom.RomInfo.item_datasize;
            if (dataSize > 0 && itemAddr >= itemBase)
            {
                ItemIndex = (int)((itemAddr - itemBase) / dataSize);
            }

            uint critPtr = rom.u32(itemAddr + 16);
            if (!U.isPointer(critPtr)) return new List<AddrResult>();
            uint critOff = U.toOffset(critPtr);
            if (!U.isSafetyOffset(critOff)) return new List<AddrResult>();

            CurrentEffAddr = critOff;
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

        /// <summary>Write the single class byte at <see cref="CurrentClassAddr"/>.</summary>
        public void WriteCurrentClassByte()
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (CurrentClassAddr == 0) return;
            var undoMgr = CoreState.Undo;
            if (undoMgr == null) return; // headless / standalone — no undo available
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
            if (CurrentItemAddr != 0)
            {
                // Item-table +12 correction and +16 effectiveness pointer.
                report["u32@0x0C"] = $"0x{rom.u32(CurrentItemAddr + 12):X08}";
                report["u32@0x10"] = $"0x{rom.u32(CurrentItemAddr + 16):X08}";
            }
            if (CurrentClassAddr != 0)
            {
                report["u8@0x00"] = $"0x{rom.u8(CurrentClassAddr):X02}";
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
