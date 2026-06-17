using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// Avalonia counterpart of WinForms <c>MantAnimationForm</c> (the Cape /
    /// Mant flutter-animation editor, #1178). The editor is a list over the
    /// <c>mant_command</c> pointer table: each 4-byte entry (field <c>D0</c> at
    /// +0) is a pointer to a flutter animation, labelled with the battle-anime
    /// id of that slot.
    ///
    /// This table is <b>count-driven</b>, not terminator-driven: a separate u8
    /// at <c>mant_command_count_address</c> stores <c>count - 1</c> and is the
    /// source of truth for how many entries the engine reads. When the list is
    /// grown, that u8 must be rewritten (see <see cref="ExpandList"/>) — a
    /// straight pointer-table expand alone would leave the engine reading the
    /// old count. Mirrors WF <c>AddressListExpandsEvent</c>.
    /// </summary>
    public class MantAnimationViewModel : ViewModelBase, IDataVerifiable
    {
        const uint SIZE = 4;

        static readonly List<EditorFormRef.FieldDef> _fields =
            EditorFormRef.DetectFields(new[] { "D0" });

        uint _currentAddr;
        bool _isLoaded;
        uint _p0;
        // The hex label of the currently-selected entry (e.g. "6B"). Captured
        // on LoadEntry so GetJumpBattleAnimeId() can faithfully replicate the
        // WF Jump (U.atoh(ar.name) - 1) without the View having to re-derive
        // the id from the address.
        string _selectedName = "";

        // Read-config snapshot (mirrors the WF InputFormRef.BaseAddress /
        // DataCount the expand path consumes). Refreshed by LoadList().
        uint _readStartAddress;
        uint _readCount;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        /// <summary>D0 — pointer to the flutter animation for this slot.</summary>
        public uint P0 { get => _p0; set => SetField(ref _p0, value); }

        /// <summary>Base address of the mant_command pointer table.</summary>
        public uint ReadStartAddress { get => _readStartAddress; set => SetField(ref _readStartAddress, value); }
        /// <summary>Number of entries currently in the list.</summary>
        public uint ReadCount { get => _readCount; set => SetField(ref _readCount, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint pointer = rom.RomInfo.mant_command_pointer;
            if (pointer == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr, rom)) return new List<AddrResult>();

            uint startAdd = rom.RomInfo.mant_command_startadd;
            var result = new List<AddrResult>();
            for (int i = 0; ; i++)
            {
                uint addr = (uint)(baseAddr + i * SIZE);
                if (addr + SIZE > (uint)rom.Data.Length) break;
                uint a = rom.u32(addr);
                if (!U.isPointer(a)) break;
                uint id = (uint)i + startAdd;
                // WF formula: U.ToHexString(id) + " " + GetBattleAnimeName(id).
                // No cross-platform battle-anime name resolver exists, so we
                // label by the bare hex id alone (the brief's accepted
                // fallback). The bare-hex form (NOT "0x..") is required so
                // U.atoh(name) round-trips for both the icon thumbnail loader
                // and the Jump quirk in GetJumpBattleAnimeId().
                string name = U.ToHexString(id);
                result.Add(new AddrResult(addr, name, id));
            }

            ReadStartAddress = baseAddr;
            ReadCount = (uint)result.Count;
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + SIZE > (uint)rom.Data.Length) return;
            CurrentAddr = addr;
            var v = EditorFormRef.ReadFields(rom, addr, _fields);
            P0 = v["D0"];
            _selectedName = ResolveLabelForAddr(rom, addr);
            IsLoaded = true;
        }

        /// <summary>
        /// Recompute the WF-style hex label for an address so the Jump quirk
        /// has a faithful <c>ar.name</c> even if the View calls Jump straight
        /// after a programmatic navigation. The label is index-derived
        /// (<c>index + startadd</c>), so we recover the index from the address.
        /// </summary>
        static string ResolveLabelForAddr(ROM rom, uint addr)
        {
            if (rom?.RomInfo == null) return "";
            uint pointer = rom.RomInfo.mant_command_pointer;
            if (pointer == 0) return "";
            uint baseAddr = rom.p32(pointer);
            if (addr < baseAddr) return "";
            uint index = (addr - baseAddr) / SIZE;
            uint id = index + rom.RomInfo.mant_command_startadd;
            return U.ToHexString(id);
        }

        /// <summary>
        /// Write the edited D0 pointer back to the current entry under the
        /// supplied ambient undo scope. No-op (no undo entry) when nothing is
        /// loaded.
        /// </summary>
        public void Write(Undo.UndoData undoData)
        {
            ROM rom = CoreState.ROM;
            if (rom == null || !IsLoaded || CurrentAddr == 0) return;
            var values = new Dictionary<string, uint> { ["D0"] = P0 };
            EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields, undoData);
        }

        /// <summary>
        /// Battle-anime id to jump to for the current selection. Replicates the
        /// WF Jump EXACTLY: <c>U.atoh(ar.name) - 1</c>. The <c>-1</c> quirk is
        /// ported faithfully (NOT "fixed") — in WF the list id is
        /// <c>index + startadd</c> and the BattleAnime jump subtracts one.
        /// Returns 0 when nothing is selected or the id would underflow.
        /// </summary>
        public uint GetJumpBattleAnimeId()
        {
            uint id = U.atoh(_selectedName);
            if (id == 0) return 0;
            return id - 1;
        }

        public int GetListCount() => LoadList().Count;

        /// <summary>
        /// Grow the mant_command pointer table to <paramref name="newCount"/>
        /// rows and rewrite the count u8. Mirrors WF
        /// <c>InputFormRef.ExpandsArea(NO, ...)</c> + the
        /// <c>AddressListExpandsEvent</c> count fix-up:
        /// <list type="number">
        ///   <item>Relocate + grow the pointer table via
        ///         <see cref="DataExpansionCore.ExpandTableTo"/>.</item>
        ///   <item>Repoint every raw 32-bit + ARM-Thumb LDR literal-pool
        ///         reference to the old base (#1025 pattern).</item>
        ///   <item>Write <c>(newCount - 1)</c> as a u8 to
        ///         <c>mant_command_count_address</c> — the count-driven engine's
        ///         source of truth.</item>
        /// </list>
        /// Caller wraps this in an <c>UndoService.Begin/Commit/Rollback</c>
        /// scope and passes the active <see cref="Undo.UndoData"/> so every
        /// write is recorded.
        /// </summary>
        /// <returns>Empty on success, error string otherwise.</returns>
        public string ExpandList(uint newCount, Undo.UndoData? undo)
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return R._("ROM not loaded.");

            uint pointer = rom.RomInfo.mant_command_pointer;
            if (pointer == 0) return R._("Mant animation table not found in this ROM.");

            if (newCount < ReadCount)
                return R._("New count ({0}) must be greater than or equal to current count ({1}).",
                    newCount, ReadCount);
            if (newCount == ReadCount)
                return ""; // no-op success

            // The count is stored as (newCount - 1) in a single u8, so the max
            // representable newCount is 256 (stores 255). Reject BEFORE any
            // mutation — a larger count would wrap the u8 and desync the stored
            // count from the actual table size. (newCount < 1 can't happen here:
            // newCount > ReadCount >= 0, so newCount >= 1.)
            if (newCount > 256)
                return R._("New count ({0}) exceeds the maximum of 256 entries.", newCount);

            uint oldBase = rom.p32(pointer);

            var result = DataExpansionCore.ExpandTableTo(rom, pointer, SIZE, ReadCount, newCount);
            if (!result.Success)
                return result.Error ?? R._("Table expansion failed.");

            // Repoint every raw 32-bit + ARM-Thumb LDR literal-pool reference to
            // the old base (not just the canonical pointer ExpandTableTo moved).
            // RepointAllReferences returning 0 is SUCCESS (clean ROM, no
            // secondary refs). Pass null so it uses the caller's ambient
            // UndoService scope rather than opening a second overwrite scope.
            DataExpansionCore.RepointAllReferences(rom, oldBase, result.NewBaseAddress, null);

            // Mant is count-driven: rewrite the separate u8 count (stores
            // count - 1). Mirrors WF AddressListExpandsEvent. The caller's
            // UndoService.Begin already opened the ambient ROM undo scope, so
            // this rom.write_u8 is captured automatically — we deliberately do
            // NOT open a nested ROM.BeginUndoScope (its Dispose nulls the global
            // ambient scope rather than restoring it, which would silently drop
            // undo tracking for any later write in the same group). The `undo`
            // parameter is the caller's active scope and is kept for signature
            // parity with the other ExpandList VMs / for non-View callers that
            // open the scope themselves before invoking ExpandList.
            _ = undo;
            uint countAddr = rom.RomInfo.mant_command_count_address;
            if (countAddr != 0 && countAddr < (uint)rom.Data.Length)
            {
                rom.write_u8(countAddr, newCount - 1);
            }

            ReadStartAddress = result.NewBaseAddress;
            ReadCount = result.NewCount;
            return "";
        }

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["P0"] = $"0x{P0:X08}",
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
                ["u32@0x00"] = $"0x{rom.u32(a + 0):X08}",
            };
        }

        public Dictionary<string, string> GetFieldOffsetMap() => new()
        {
            ["P0"] = "u32@0x00",
        };
    }
}
