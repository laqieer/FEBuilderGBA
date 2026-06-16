using System;
using System.Collections.Generic;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// View-model for the "Ending (FE6)" editor — port of WinForms <c>EDFE6Form</c>.
    /// The table lives at <c>p32(RomInfo.ed_3a_pointer)</c>, 8 bytes per entry,
    /// at most 0x42 entries. Each entry holds four u16 text-IDs at offsets +0/+2/+4/+6
    /// (the post-game "after story" ending text slots).
    ///
    /// FE6-ONLY: on FE7/FE8 the same <c>ed_3a_pointer</c> addresses an ED/epilogue table
    /// with a DIFFERENT schema (flag/uid/uid/flag + u32 text-id — see the sibling
    /// <c>EDFE7ViewModel</c> Epilogue tab and Core's <c>{4}</c> ED text-id scan). Reading,
    /// and especially writing, this table as 4×u16 on a non-FE6 ROM would corrupt unrelated
    /// data, so every list/read/write path is gated to <c>RomInfo.version == 6</c>.
    /// </summary>
    public class EDFE6ViewModel : ViewModelBase
    {
        public const uint EntrySize = 8;
        public const uint MaxCount = 0x42;
        const int FE6Version = 6;

        uint _currentAddr;
        bool _isLoaded;
        uint _text0Id, _text2Id, _text4Id, _text6Id;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        public uint Text0Id { get => _text0Id; set { if (SetField(ref _text0Id, value)) OnPropertyChanged(nameof(Text0Preview)); } }
        public uint Text2Id { get => _text2Id; set { if (SetField(ref _text2Id, value)) OnPropertyChanged(nameof(Text2Preview)); } }
        public uint Text4Id { get => _text4Id; set { if (SetField(ref _text4Id, value)) OnPropertyChanged(nameof(Text4Preview)); } }
        public uint Text6Id { get => _text6Id; set { if (SetField(ref _text6Id, value)) OnPropertyChanged(nameof(Text6Preview)); } }

        public string Text0Preview => ResolveText(_text0Id);
        public string Text2Preview => ResolveText(_text2Id);
        public string Text4Preview => ResolveText(_text4Id);
        public string Text6Preview => ResolveText(_text6Id);

        static string ResolveText(uint id)
        {
            if (id == 0) return "(none)";
            try { return NameResolver.GetTextById(id); }
            catch { return "???"; }
        }

        /// <summary>True only for an FE6 ROM — the only version whose ed_3a uses the 4×u16 schema.</summary>
        static bool IsFe6(ROM rom) => rom?.RomInfo != null && rom.RomInfo.version == FE6Version;

        /// <summary>List every ending entry. Label mirrors WinForms: hex index + unit name.</summary>
        public List<AddrResult> LoadList()
        {
            var result = new List<AddrResult>();
            ROM rom = CoreState.ROM;
            if (!IsFe6(rom)) return result;

            uint ptr = rom.RomInfo.ed_3a_pointer;
            if (ptr == 0) return result;

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr, rom)) return result;

            for (uint i = 0; i < MaxCount; i++)
            {
                uint addr = baseAddr + i * EntrySize;
                if (!U.isSafetyOffset(addr + EntrySize - 1, rom)) break;
                string label = U.ToHexString(i) + " " + NameResolver.GetUnitName(i);
                result.Add(new AddrResult(addr, label, i));
            }
            return result;
        }

        /// <summary>
        /// Read the four u16 text-IDs for the selected entry into the editable fields.
        /// On an invalid selection (no ROM / non-FE6 / addr 0 / unsafe) the fields are
        /// cleared and <see cref="IsLoaded"/> stays false so the Write button is disabled
        /// and no stale IDs/previews linger.
        /// </summary>
        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            CurrentAddr = addr;
            if (addr != 0 && IsFe6(rom) && U.isSafetyOffset(addr + EntrySize - 1, rom))
            {
                Text0Id = rom.u16(addr + 0);
                Text2Id = rom.u16(addr + 2);
                Text4Id = rom.u16(addr + 4);
                Text6Id = rom.u16(addr + 6);
                IsLoaded = true;
            }
            else
            {
                Text0Id = 0;
                Text2Id = 0;
                Text4Id = 0;
                Text6Id = 0;
                IsLoaded = false;
            }
        }

        /// <summary>
        /// Write the four u16 text-IDs back to ROM via the direct <c>write_u16</c> overload —
        /// the caller wraps this in <c>UndoService.Begin/Commit</c> for undo support.
        /// Returns false (no mutation) when there is no FE6 ROM or the address is unsafe.
        /// </summary>
        public bool WriteEntry()
        {
            ROM rom = CoreState.ROM;
            if (!IsFe6(rom) || CurrentAddr == 0) return false;
            if (!U.isSafetyOffset(CurrentAddr + EntrySize - 1, rom)) return false;

            rom.write_u16(CurrentAddr + 0, Text0Id);
            rom.write_u16(CurrentAddr + 2, Text2Id);
            rom.write_u16(CurrentAddr + 4, Text4Id);
            rom.write_u16(CurrentAddr + 6, Text6Id);
            return true;
        }
    }
}
