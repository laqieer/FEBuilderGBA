using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// Issue #368 — CC-item-driven view-model for the Avalonia Item Promotion
    /// editor. Mirrors WinForms <c>ItemPromotionForm</c>:
    ///   * Outer list is the fixed set of CC items (Hero Crest, Knight Crest,
    ///     Orion Bolt, Elysian Whip, Guiding Ring, plus FE7+ Fallen Contract,
    ///     Master Seal, Ocean Seal, Moon Bracelet, Sun Bracelet).
    ///   * Inner list scans the null-terminated byte array at the selected CC
    ///     item's <c>cc_*_pointer</c> -> dereferenced offset.
    ///   * Per-class edit reads/writes a single byte through
    ///     <see cref="ItemClassListCore.WriteClassByte"/>.
    /// </summary>
    public class ItemPromotionViewerViewModel : ViewModelBase, IDataVerifiable
    {
        /// <summary>
        /// Describes one CC item entry in the outer list. <c>ItemId</c> is the
        /// game-data item ID (e.g. Hero Crest = 0x47). <c>PointerAddr</c> is
        /// the ROM offset of the GBA pointer that holds the address of the
        /// promotion class array — i.e. <c>rom.p32(PointerAddr)</c> gives the
        /// array base offset.
        /// </summary>
        public class CCItem
        {
            public uint ItemId { get; set; }
            public uint PointerAddr { get; set; }
            public string Name { get; set; } = "";
        }

        uint _currentPointerAddr;       // ROM offset of the pointer slot (cc_*_pointer)
        uint _currentArrayAddr;         // dereferenced array base
        uint _currentClassAddr;         // address of currently-selected class byte
        uint _classId;
        string _className = "";
        bool _canWrite;

        public uint CurrentPointerAddr { get => _currentPointerAddr; set => SetField(ref _currentPointerAddr, value); }
        public uint CurrentArrayAddr { get => _currentArrayAddr; set => SetField(ref _currentArrayAddr, value); }
        public uint CurrentClassAddr { get => _currentClassAddr; set => SetField(ref _currentClassAddr, value); }
        public uint ClassId { get => _classId; set => SetField(ref _classId, value); }
        public string ClassName { get => _className; set => SetField(ref _className, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }

        /// <summary>Returns the CC item definitions present in this ROM version.</summary>
        public List<CCItem> EnumerateCCItems()
        {
            ROM rom = CoreState.ROM;
            var list = new List<CCItem>();
            if (rom?.RomInfo == null) return list;

            void Add(uint itemId, uint pointer)
            {
                if (pointer == 0) return; // version skips this item
                list.Add(new CCItem
                {
                    ItemId = itemId,
                    PointerAddr = pointer,
                    Name = NameResolver.GetItemName(itemId),
                });
            }

            Add(rom.RomInfo.cc_item_hero_crest_itemid, rom.RomInfo.cc_item_hero_crest_pointer);
            Add(rom.RomInfo.cc_item_knight_crest_itemid, rom.RomInfo.cc_item_knight_crest_pointer);
            Add(rom.RomInfo.cc_item_orion_bolt_itemid, rom.RomInfo.cc_item_orion_bolt_pointer);
            Add(rom.RomInfo.cc_elysian_whip_itemid, rom.RomInfo.cc_elysian_whip_pointer);
            Add(rom.RomInfo.cc_guiding_ring_itemid, rom.RomInfo.cc_guiding_ring_pointer);

            // FE7+ extras (FE6 has no Master Seal / Ocean Seal / etc.)
            if (rom.RomInfo.version >= 7)
            {
                Add(rom.RomInfo.cc_fallen_contract_itemid, rom.RomInfo.cc_fallen_contract_pointer);
                Add(rom.RomInfo.cc_master_seal_itemid, rom.RomInfo.cc_master_seal_pointer);
                Add(rom.RomInfo.cc_ocean_seal_itemid, rom.RomInfo.cc_ocean_seal_pointer);
                Add(rom.RomInfo.cc_moon_bracelet_itemid, rom.RomInfo.cc_moon_bracelet_pointer);
                Add(rom.RomInfo.cc_sun_bracelet_itemid, rom.RomInfo.cc_sun_bracelet_pointer);
            }
            return list;
        }

        /// <summary>
        /// Outer list — the fixed set of CC items. <see cref="AddrResult.addr"/>
        /// stores the ROM offset of the <c>cc_*_pointer</c> slot so the caller
        /// can dereference it via <c>rom.p32</c> to get the class array.
        /// </summary>
        public List<AddrResult> LoadItemList()
        {
            var ccItems = EnumerateCCItems();
            var result = new List<AddrResult>();
            foreach (var cc in ccItems)
            {
                string name = $"{U.ToHexString(cc.ItemId)} {cc.Name}";
                result.Add(new AddrResult(cc.PointerAddr, name, cc.ItemId));
            }
            return result;
        }

        /// <summary>Load the inner class list for the selected CC item.</summary>
        public List<AddrResult> LoadInnerClassList(uint pointerAddr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return new List<AddrResult>();
            if (pointerAddr + 4 > (uint)rom.Data.Length) return new List<AddrResult>();

            CurrentPointerAddr = pointerAddr;
            uint arrPtr = rom.u32(pointerAddr);
            if (!U.isPointer(arrPtr)) return new List<AddrResult>();
            uint arrAddr = U.toOffset(arrPtr);
            if (!U.isSafetyOffset(arrAddr)) return new List<AddrResult>();

            CurrentArrayAddr = arrAddr;
            var classes = ItemClassListCore.ScanClassList(rom, arrAddr);

            var result = new List<AddrResult>();
            for (int i = 0; i < classes.Count; i++)
            {
                uint classId = classes[i];
                uint byteAddr = arrAddr + (uint)i;
                string className = NameResolver.GetClassName(classId);
                string name = $"{U.ToHexString(classId)} {className}";
                result.Add(new AddrResult(byteAddr, name, classId));
            }
            CanWrite = true;
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
            if (undoMgr == null) return;
            var undo = undoMgr.NewUndoData(this, "Edit Item Promotion");
            ItemClassListCore.WriteClassByte(rom, CurrentClassAddr, ClassId, undo);
            undoMgr.Push(undo);
        }

        /// <summary>Append a new editable 0-slot to the current promotion array.</summary>
        public uint ExpandCurrentList()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentPointerAddr == 0)
                throw new InvalidOperationException("No CC item selected.");
            var undoMgr = CoreState.Undo;
            if (undoMgr == null) throw new InvalidOperationException("Undo manager unavailable.");
            var undo = undoMgr.NewUndoData(this, "Expand Item Promotion");
            uint newAddr = ItemClassListCore.ExpandClassList(rom, CurrentPointerAddr, undo);
            undoMgr.Push(undo);
            CurrentArrayAddr = newAddr;
            return newAddr;
        }

        public int GetListCount() => LoadItemList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["pointer_addr"] = $"0x{CurrentPointerAddr:X08}",
                ["array_addr"] = $"0x{CurrentArrayAddr:X08}",
                ["class_byte_addr"] = $"0x{CurrentClassAddr:X08}",
                ["ClassId"] = $"0x{ClassId:X02}",
                ["ClassName"] = ClassName,
            };
        }

        public Dictionary<string, string> GetRawRomReport()
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return new Dictionary<string, string>();
            var report = new Dictionary<string, string>
            {
                ["pointer_addr"] = $"0x{CurrentPointerAddr:X08}",
                ["array_addr"] = $"0x{CurrentArrayAddr:X08}",
                ["class_byte_addr"] = $"0x{CurrentClassAddr:X08}",
            };
            if (CurrentPointerAddr != 0 && CurrentPointerAddr + 4 <= (uint)rom.Data.Length)
            {
                // The cc_*_pointer slot itself.
                report["p32@0x00"] = $"0x{rom.u32(CurrentPointerAddr):X08}";
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
