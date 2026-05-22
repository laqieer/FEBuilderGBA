using System;
using System.Collections.Generic;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ItemEffectivenessSkillSystemsReworkViewModel : ViewModelBase
    {
        uint _currentAddr;
        bool _isLoaded;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
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

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            CurrentAddr = addr;
            IsLoaded = true;
        }
    }
}
