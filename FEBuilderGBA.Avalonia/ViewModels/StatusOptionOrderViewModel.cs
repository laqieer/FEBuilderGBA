using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class StatusOptionOrderViewModel : ViewModelBase, IDataVerifiable
    {
        static readonly List<EditorFormRef.FieldDef> _fields =
            EditorFormRef.DetectFields(new[] { "B0" });

        uint _currentAddr;
        bool _canWrite;
        uint _optionId;
        uint _readCount;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }
        public uint OptionId { get => _optionId; set => SetField(ref _optionId, value); }

        /// <summary>
        /// The number of order slots the list currently honors — the source of
        /// truth is the count byte at <c>status_game_option_order_count_address</c>.
        /// Seeded by <see cref="LoadStatusOptionOrderList"/> and raised by
        /// <see cref="ExpandList"/>. Used by the expand path as the current row
        /// count (the list has no terminator at runtime; the count byte bounds it).
        /// </summary>
        public uint ReadCount { get => _readCount; set => SetField(ref _readCount, value); }

        public List<AddrResult> LoadStatusOptionOrderList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptrAddr = rom.RomInfo.status_game_option_order_pointer;
            if (ptrAddr == 0) return new List<AddrResult>();

            if (!U.isSafetyOffset(ptrAddr)) return new List<AddrResult>();

            // Dereference pointer: RomInfo values are pointer addresses, not data addresses.
            // WinForms InputFormRef constructor always does p32() on the basepointer.
            uint baseAddr = rom.p32(ptrAddr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            // Read count from the count address. WinForms parity
            // (StatusOptionOrderForm.Init): the row-validity predicate is
            // `i < u8(count_address)` — the RAW count byte (0..255) is the source
            // of truth, with NO 0x20 default and NO 0x40 cap (the old clamp would
            // collapse a valid expanded count of 65+ back to 0x20 and silently
            // break this editor's own list-expand feature — Copilot plan-review
            // finding #2 on #1608). We still break on ROM bounds in the loop.
            uint countAddr = rom.RomInfo.status_game_option_order_count_address;
            uint count = 0;
            if (countAddr != 0 && U.isSafetyOffset(countAddr))
                count = rom.u8(countAddr);

            // Surface the resolved count so the View (and --data-verify-full) bound
            // the expand path on the same value the list was built from.
            ReadCount = count;

            var result = new List<AddrResult>();
            for (uint i = 0; i < count; i++)
            {
                uint addr = (uint)(baseAddr + i * 1);
                if (addr >= (uint)rom.Data.Length) break;

                uint optionId = rom.u8(addr);
                string name = $"{U.ToHexString(i)} Option {optionId}";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        /// <summary>
        /// Grow the 1-byte-per-entry game-option-order list to
        /// <paramref name="newCount"/> slots and raise the order-count byte at
        /// <c>status_game_option_order_count_address</c>. Mirrors WinForms
        /// <c>StatusOptionOrderForm.AddressListExpandsEvent</c> (the active
        /// <c>write_u8(NewDataCount)</c>); the FE7 <c>order2</c> repoint that the
        /// WinForms handler keeps COMMENTED OUT is intentionally NOT ported.
        ///
        /// The list relocation + pointer repoint go through
        /// <see cref="DataExpansionCore.ExpandTableTo"/> with <c>entrySize=1</c>
        /// (the count byte — not the terminator dword it appends — is the runtime
        /// source of truth, so the inert terminator is harmless). The count-byte
        /// write rides the SAME ambient undo scope the caller opened, so a fault
        /// (or user undo) restores both the relocated list and the count byte
        /// byte-identically.
        ///
        /// Validate-all-before-mutate: an unset/unsafe pointer or count address is
        /// rejected BEFORE any relocation, so the list is never moved without a
        /// matching count write.
        /// </summary>
        /// <param name="newCount">Target slot count (must be &gt;= current).</param>
        /// <param name="undo">Active undo buffer for the ambient scope (may be
        /// null — the helper then relies on whatever ambient scope the caller
        /// already opened).</param>
        /// <returns>Empty string on success, an error message otherwise.</returns>
        public string ExpandList(uint newCount, Undo.UndoData? undo)
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return R._("ROM not loaded.");

            uint ptrAddr = rom.RomInfo.status_game_option_order_pointer;
            if (ptrAddr == 0 || !U.isSafetyOffset(ptrAddr, rom))
                return R._("Game option order list pointer is unset for this ROM version.");

            uint countAddr = rom.RomInfo.status_game_option_order_count_address;
            if (countAddr == 0 || !U.isSafetyOffset(countAddr, rom))
                return R._("Game option order count address is unset for this ROM version.");

            // Re-resolve the current count from the ROM count byte — the runtime
            // source of truth — rather than trust ReadCount, which can be 0/stale if
            // ExpandList is invoked without a prior LoadStatusOptionOrderList() or
            // after the ROM was mutated elsewhere. A wrong (e.g. 0) current count
            // would make ExpandTableTo copy too few existing bytes and drop entries
            // (Copilot PR-review finding). ReadCount is refreshed to this value too.
            uint current = rom.u8(countAddr);
            ReadCount = current;

            if (newCount < current)
                return R._("New count ({0}) must be greater than or equal to current count ({1}).",
                    newCount, current);
            if (newCount == current)
                return ""; // no-op success
            if (newCount > 0xFF)
                return R._("Game option order count is a single byte; the maximum is 255.");

            // Relocate + grow the 1-byte list and repoint the canonical pointer.
            var result = DataExpansionCore.ExpandTableTo(rom, ptrAddr, entrySize: 1, current, newCount);
            if (!result.Success)
                return result.Error ?? R._("Table expansion failed.");

            // Raise the order-count byte — the runtime source of truth — under the
            // same ambient undo scope (mirrors WinForms write_u8(NewDataCount)).
            rom.write_u8(countAddr, newCount);

            ReadCount = result.NewCount;
            return "";
        }

        public void LoadStatusOptionOrder(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr >= (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            var values = EditorFormRef.ReadFields(rom, addr, _fields);
            OptionId = values["B0"];
            CanWrite = true;
        }

        public void WriteStatusOptionOrder()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            if (CurrentAddr >= (uint)rom.Data.Length) return;

            var values = new Dictionary<string, uint> { ["B0"] = OptionId };
            EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields);
        }

        public int GetListCount() => LoadStatusOptionOrderList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["OptionId"] = $"0x{OptionId:X02}",
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
                ["u8@0x00"] = $"0x{rom.u8(a):X02}",
            };
        }

        public Dictionary<string, string> GetFieldOffsetMap() => new()
        {
            ["OptionId"] = "u8@0x00",
        };
    }
}
