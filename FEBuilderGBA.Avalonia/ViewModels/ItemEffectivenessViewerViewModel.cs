using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ItemEffectivenessViewerViewModel : ViewModelBase, IDataVerifiable
    {
        static readonly List<EditorFormRef.FieldDef> _fields =
            EditorFormRef.DetectFields(new[] { "B0" });

        uint _currentAddr;
        uint _classId;
        bool _canWrite;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public uint ClassId { get => _classId; set => SetField(ref _classId, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }

        /// <summary>
        /// Enumerate items that have a valid P16 (item effectiveness) pointer.
        /// Each emitted <see cref="AddrResult"/> uses the P16 ROM offset as its
        /// <c>addr</c>, so the address passed by
        /// <c>ItemEditorView.JumpToEffectiveness_Click</c> (which is
        /// <c>ptr - 0x08000000</c>) matches a list row directly and the
        /// receiving editor's selection lands on the correct item — not on the
        /// wrong sacred-weapons 2x/3x byte-table row (issue #363).
        ///
        /// Mirrors the WinForms <c>ItemEffectivenessForm</c> outer
        /// <c>AddressList</c> (item-keyed, item icons) and the existing
        /// Avalonia <c>ItemEffectivenessSkillSystemsReworkViewModel.LoadList()</c>
        /// iteration semantics: walk the item table by
        /// <c>itemBase + i * item_datasize</c> using the dereferenced
        /// <c>item_pointer</c>. The loop <c>break</c>s on the first row whose
        /// P12 or P16 is not pointer-or-null (mirroring
        /// <c>InputFormRef.DataCount</c> termination), and <c>continue</c>s
        /// past rows whose P16 is null / zero / out-of-range (the item is
        /// valid but carries no effectiveness data — nothing to navigate to).
        ///
        /// The previous implementation read class IDs from
        /// <c>rom.RomInfo.weapon_effectiveness_2x3x_address</c> — a fixed
        /// "sacred weapons" byte table whose addresses had no relation to
        /// per-item effectiveness pointers. <c>SelectAddress</c> never matched
        /// any row, so the source jump silently fell back to entry 0.
        /// </summary>
        public List<AddrResult> LoadItemEffectivenessList()
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

                // Skip rows where P16 is null / zero or not a real pointer —
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

        public void LoadItemEffectiveness(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            if (addr >= (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            var values = EditorFormRef.ReadFields(rom, addr, _fields);
            ClassId = values["B0"];

            CanWrite = true;
        }

        public void WriteItemEffectiveness()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint addr = CurrentAddr;
            var values = new Dictionary<string, uint>
            {
                ["B0"] = ClassId,
            };
            EditorFormRef.WriteFields(rom, addr, values, _fields);
        }

        public int GetListCount() => LoadItemEffectivenessList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["ClassId"] = $"0x{ClassId:X02}",
            };
        }

        public Dictionary<string, string> GetRawRomReport()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return new Dictionary<string, string>();
            uint a = CurrentAddr;
            // Report the class byte at the row plus subsequent bytes — these are
            // additional class IDs in the same effectiveness list (or the
            // 0-terminator). Reporting more bytes raises the raw-read coverage
            // above the AvaloniaFieldCompletenessTests 60% threshold (the list
            // loader's u32@12/u32@16 reads inflate the denominator, and a single
            // u8@0 entry is insufficient).
            //
            // Defensively bounds-check each follow-on byte: a hacked / malformed
            // ROM can pin P16 to the last byte of the ROM. The row passes
            // U.isSafetyOffset(criticalAddr) and LoadItemEffectiveness reads
            // B0 successfully, but rom.u8(a + 1/2) would throw via
            // U.check_safety without this guard.
            var report = new Dictionary<string, string>
            {
                ["addr"] = $"0x{a:X08}",
                ["u8@0x00"] = $"0x{rom.u8(a + 0):X02}",
            };
            if (a + 1 < (uint)rom.Data.Length)
                report["u8@0x01"] = $"0x{rom.u8(a + 1):X02}";
            if (a + 2 < (uint)rom.Data.Length)
                report["u8@0x02"] = $"0x{rom.u8(a + 2):X02}";
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
