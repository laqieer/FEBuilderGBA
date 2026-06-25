using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    // #1442 — Port of WinForms EventTalkGroupFE7Form (FE7 only).
    // WinForms is an InputFormRef list: base 0, stride 4, IsDataExists "i <= 0xD"
    // (14 entries, i=0..0xD). Each entry's editable cell is the named control "D0"
    // (u32 at +0; EventTalkGroupFE7Form.Designer.cs); the list label reads the
    // text id via u16 (EventTalkGroupFE7Form.MakeList). JumpToAddr/ReInit repoints
    // the list onto an arbitrary block base (driven from POINTER_TALKGROUP).
    // NewAlloc allocates byte[4*0xE] (= 56 bytes) via AppendBinaryData with undo.
    public class EventTalkGroupFE7ViewModel : ViewModelBase, IDataVerifiable
    {
        static readonly List<EditorFormRef.FieldDef> _fields =
            EditorFormRef.DetectFields(new[] { "D0" });

        // Fixed list geometry (parity with WinForms InputFormRef base/stride/IsDataExists).
        public const int EntryCount = 0xE;   // 14 entries (i=0..0xD)
        public const uint EntryStride = 4;   // bytes per entry
        public const int NewAllocSize = (int)(EntryStride * EntryCount); // 56 bytes

        uint _baseAddr;
        uint _currentAddr;
        bool _isLoaded;
        uint _textId;

        /// <summary>Base address (ROM offset) of the active talk-group block (repoint target).</summary>
        public uint BaseAddr { get => _baseAddr; set => SetField(ref _baseAddr, value); }
        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        // D0: Text ID / dialogue pointer (u32 at offset 0)
        public uint TextId { get => _textId; set => SetField(ref _textId, value); }

        /// <summary>
        /// Repoint the editor onto a specific block base (parity with WinForms
        /// JumpToAddr → InputFormRef.ReInit). Normalizes via <see cref="U.toOffset"/>
        /// so both raw ROM offsets and 0x08...... GBA pointers work. Pass 0 to clear
        /// and re-auto-discover on the next LoadList.
        /// </summary>
        public void SetBaseAddr(uint addr)
        {
            BaseAddr = addr == 0 ? 0 : U.toOffset(addr);
        }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            // Use the explicitly repointed base if set, otherwise auto-discover the
            // first valid talk-group block from event scripts.
            uint baseAddr = BaseAddr;
            if (baseAddr == 0)
            {
                baseAddr = EventSubEditorHelper.FindFirstTalkGroupAddr(rom);
                if (baseAddr == 0) return new List<AddrResult>();
                BaseAddr = baseAddr;
            }

            uint romLen = (uint)rom.Data.Length;
            var result = new List<AddrResult>();
            // 14 stride-4 entries (i=0..0xD), each labeled with its text id (u16,
            // like WinForms MakeList).
            for (int i = 0; i < EntryCount; i++)
            {
                uint addr = baseAddr + (uint)(i * EntryStride);
                if (addr + EntryStride > romLen) break;

                uint textid = rom.u16(addr);
                string name = textid != 0 ? NameResolver.GetTextById(textid) : "";
                string label = string.IsNullOrEmpty(name)
                    ? $"0x{i:X} : 0x{textid:X04}"
                    : $"0x{i:X} : 0x{textid:X04} {name}";
                result.Add(new AddrResult(addr, label, (uint)i));
            }
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 4 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            var v = EditorFormRef.ReadFields(rom, addr, _fields);
            TextId = v["D0"];
            IsLoaded = true;
        }

        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            if (CurrentAddr + 4 > (uint)rom.Data.Length) return;
            var values = new Dictionary<string, uint> { ["D0"] = TextId };
            EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields);
        }

        /// <summary>
        /// Allocate a fresh, zero-filled talk-group block (14 × 4 = 56 bytes) in ROM
        /// free space and repoint the editor onto it. Parity with WinForms NewAlloc
        /// (byte[4*0xE] via AppendBinaryData). The append is captured by the ambient
        /// undo scope opened by the caller (UndoService.Begin). Returns the new block
        /// base (ROM offset), or U.NOT_FOUND on failure (no mutation).
        ///
        /// NOTE: like WinForms <c>NewAlloc</c>, this only appends the block; it does
        /// NOT write the new block's pointer back into any event script. The standalone
        /// "New Block" button therefore creates an as-yet-unreferenced block — wire it
        /// into an event (the POINTER_TALKGROUP arg) so it is reachable. WinForms reuses
        /// the same append in <c>AllocIfNeed</c>, which is the only path that does the
        /// <c>U.toPointer(newAddr)</c> writeback (when launched from a 0/NOT_FOUND
        /// pointer source); the Avalonia editor opens standalone, so no writeback applies.
        /// </summary>
        public uint NewAlloc()
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return U.NOT_FOUND;

            byte[] alloc = new byte[NewAllocSize];
            // The caller (View "New Block" button) opens a UndoService.Begin scope
            // before calling, so the ambient UndoData is non-null and captures the
            // append (mirrors EventCondViewModel.AppendBinaryDataHeadless).
            Undo.UndoData? ambient = ROM.GetAmbientUndoData();
            uint addr = MapEventUnitCore.AppendBinaryDataHeadless(rom, alloc, ambient);
            if (addr == U.NOT_FOUND) return U.NOT_FOUND;

            SetBaseAddr(addr);
            return addr;
        }

        public int GetListCount() => LoadList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["base"] = $"0x{BaseAddr:X08}",
                ["TextId"] = $"0x{TextId:X08}",
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
                ["u32@0x00_TextId"] = $"0x{rom.u32(a + 0):X08}",
                // The list label reads the text id via u16 (parity with WinForms
                // MakeList); surface it so data-verify cross-checks that read too.
                ["u16@0x00_TextIdLow"] = $"0x{rom.u16(a + 0):X04}",
            };
        }

        public Dictionary<string, string> GetFieldOffsetMap() => new()
        {
            ["TextId"] = "u32@0x00_TextId",
        };
    }
}
