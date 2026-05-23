// SPDX-License-Identifier: GPL-3.0-or-later
// AIScriptViewModel — Avalonia parity rebuild for #410. Mirrors
// `AIScriptForm` (panel3 filter + panel6 list + panel5 write + ControlPanel
// detail). Per Copilot CLI plan-review v2 #2, this is a `partial class` so
// `AIScriptViewModel.NavigationTargets.cs` can extend with the
// INavigationTargetSource manifest. Per Copilot v2 #3, AI1 and AI2 are
// separate pointer tables — FilterIndex toggles between
// `RomInfo.ai1_pointer` (0) and `RomInfo.ai2_pointer` (1), no combined list.
using System;
using System.Collections.Generic;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public partial class AIScriptViewModel : ViewModelBase
    {
        // ----------------------------------------------------------------
        // Backing fields
        // ----------------------------------------------------------------

        int _filterIndex;
        uint _topAddress;
        uint _readCount;
        uint _currentAddr;
        uint _baseAddr;
        uint _readByteCount;
        string _commentText = "";
        string _asmText = "";
        string _scriptCodeName = "";
        bool _isLoaded;
        bool _isLoading;

        // ----------------------------------------------------------------
        // Public observable properties
        // ----------------------------------------------------------------

        /// <summary>0 = AI1 pointer table; 1 = AI2 pointer table.</summary>
        public int FilterIndex
        {
            get => _filterIndex;
            set => SetField(ref _filterIndex, value);
        }

        /// <summary>Top address used by the read-config bar reload.</summary>
        public uint TopAddress
        {
            get => _topAddress;
            set => SetField(ref _topAddress, value);
        }

        /// <summary>Number of pointer slots to read in the master list.</summary>
        public uint ReadCount
        {
            get => _readCount;
            set => SetField(ref _readCount, value);
        }

        /// <summary>The currently loaded script address (after pointer resolution).</summary>
        public uint CurrentAddr
        {
            get => _currentAddr;
            set => SetField(ref _currentAddr, value);
        }

        /// <summary>The address of the pointer slot in the AI table.</summary>
        public uint BaseAddr
        {
            get => _baseAddr;
            set => SetField(ref _baseAddr, value);
        }

        /// <summary>Script body length in bytes (CalcLength of the loaded script).</summary>
        public uint ReadByteCount
        {
            get => _readByteCount;
            set => SetField(ref _readByteCount, value);
        }

        /// <summary>The comment text for the currently selected opcode.</summary>
        public string CommentText
        {
            get => _commentText;
            set => SetField(ref _commentText, value);
        }

        /// <summary>The hex-dump form of the currently selected opcode bytes.</summary>
        public string AsmText
        {
            get => _asmText;
            set => SetField(ref _asmText, value);
        }

        /// <summary>The human-readable script command name for the current opcode.</summary>
        public string ScriptCodeName
        {
            get => _scriptCodeName;
            set => SetField(ref _scriptCodeName, value);
        }

        /// <summary>True once an entry has been loaded from the address list.</summary>
        public bool IsLoaded
        {
            get => _isLoaded;
            set => SetField(ref _isLoaded, value);
        }

        /// <summary>True while LoadList / LoadEntry are running (suppresses dirty flags).</summary>
        public bool IsLoading
        {
            get => _isLoading;
            set => SetField(ref _isLoading, value);
        }

        // ----------------------------------------------------------------
        // List loaders (FilterIndex-aware)
        // ----------------------------------------------------------------

        /// <summary>
        /// Returns the address-list entries for the currently selected AI
        /// table (0 = AI1, 1 = AI2). Per Copilot CLI plan-review v2 #3 the
        /// tables are separate — no combined list. Mirrors WF
        /// AIScriptForm.Init's pointer-walk with the WF validity check:
        /// non-pointer / non-null = stop, table-not-expanded stops at
        /// the known AI1/AI2 entry count (PR #571 Copilot bot review #4 —
        /// avoid over-enumerating into unrelated data on unexpanded ROMs).
        ///
        /// When ReadCount > 0 it also clamps the result to that many
        /// entries so a user-edited "Read Count" actually limits the
        /// scan (parity with WF panel3 ReadCount).
        /// </summary>
        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            var result = new List<AddrResult>();
            if (rom?.RomInfo == null) return result;

            uint tablePointer = _filterIndex == 1
                ? rom.RomInfo.ai2_pointer
                : rom.RomInfo.ai1_pointer;
            if (tablePointer == 0) return result;

            // If the user set TopAddress, scan from there; otherwise
            // resolve the pointer to the in-ROM table base.
            uint baseAddr;
            if (_topAddress != 0 && U.isSafetyOffset(_topAddress))
                baseAddr = U.toOffset(_topAddress);
            else
                baseAddr = rom.p32(tablePointer);

            if (!U.isSafetyOffset(baseAddr))
                return result;

            // WF AIScriptForm.Init's validity check: for an unexpanded
            // table (baseAddr matches the resolved pointer), stop at the
            // configured AI1/AI2 count from EventUnitForm. For an
            // expanded ROM region, scan until terminator. Replicate by
            // computing the WF count cap when applicable.
            uint? unexpandedCap = null;
            try
            {
                // Mirror WF U.isExtrendsROMArea inline (Core has no port yet).
                uint extendsAddr = rom.RomInfo.extends_address;
                bool isInExpandedArea = extendsAddr != 0
                    && baseAddr >= U.toOffset(extendsAddr);
                bool isUnexpanded = !isInExpandedArea
                                 && baseAddr == rom.p32(tablePointer);
                if (isUnexpanded)
                {
                    // EventUnitForm.AI1/AI2 counts live in WinForms-only
                    // code; for the Core / VM here we cap conservatively
                    // at the known maximum (256) to avoid over-enumeration
                    // while still permitting the full WF entry set.
                    unexpandedCap = 256;
                }
            }
            catch
            {
                // Fall back to no cap if the synthetic ROM lacks
                // extends_address wiring.
            }

            uint maxEntries = unexpandedCap ?? 4096;
            // Honor user-driven ReadCount as a soft cap (when > 0).
            if (_readCount > 0 && _readCount < maxEntries)
                maxEntries = _readCount;

            for (uint i = 0; i < maxEntries; i++)
            {
                uint slotAddr = baseAddr + i * 4;
                if (!U.isSafetyOffset(slotAddr + 3))
                    break;

                uint slot = rom.u32(slotAddr);
                if (slot == 0xFFFFFFFF)
                    break;
                if (!U.isPointerOrNULL(slot))
                    break;

                string name = $"{i:X02} {(_filterIndex == 1 ? "AI2" : "AI1")}";
                result.Add(new AddrResult(slotAddr, name, i));
            }

            return result;
        }

        /// <summary>
        /// Read the AI script at the given pointer-slot address. The slot
        /// holds a 32-bit pointer to the script body; this method follows
        /// it, computes the byte length via AIScript.CalcLength, and
        /// populates the header fields.
        /// </summary>
        public void LoadEntry(uint pointerSlotAddr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            BaseAddr = pointerSlotAddr;
            if (!U.isSafetyOffset(pointerSlotAddr + 3))
            {
                CurrentAddr = 0;
                ReadByteCount = 0;
                IsLoaded = false;
                return;
            }

            uint scriptAddr = rom.p32(pointerSlotAddr);
            if (!U.isSafetyOffset(scriptAddr))
            {
                CurrentAddr = 0;
                ReadByteCount = 0;
                IsLoaded = false;
                return;
            }

            CurrentAddr = scriptAddr;
            ReadByteCount = CalcScriptLength(scriptAddr);
            IsLoaded = true;
        }

        /// <summary>
        /// Compute the length of the AI script at the given address. AI
        /// scripts use 16-byte instructions terminated by opcode 0x03
        /// (EXIT), optionally followed by 0x1B / 0x1C continuation
        /// instructions. Mirrors WF AIScriptForm.CalcLength.
        /// </summary>
        public static uint CalcScriptLength(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return 0;

            uint start = addr;
            uint limit = (uint)rom.Data.Length;
            while (addr + 16 <= limit)
            {
                uint code = rom.u8(addr + 0);
                addr += 16;
                if (addr + 16 > limit)
                    break;
                if (code == 0x03)
                {
                    // EXIT - check if followed by 0x1B / 0x1C continuation.
                    uint nextcode = rom.u8(addr + 0);
                    if (nextcode != 0x1B && nextcode != 0x1C)
                        break;
                    addr += 16;
                }
            }
            return addr - start;
        }

        // ----------------------------------------------------------------
        // Write-back (mirrors WF AIScriptForm.AllWriteButton_Click)
        // ----------------------------------------------------------------

        /// <summary>
        /// Write the current ScriptBytes back to the ROM at CurrentAddr.
        /// Callers MUST wrap this call in a UndoService.Begin / Commit block.
        /// Mirrors WF AIScriptForm.AllWriteButton_Click — the full byte
        /// list write path lives in the view code-behind because it needs
        /// AIScript.DisAssemble / EventScriptUtil.JisageReorder which are
        /// WinForms-coupled. The VM exposes Write() only as the no-op header
        /// commit so the test surface is stable.
        /// </summary>
        public void Write()
        {
            // Header-only commit: scope-level write tracking happens in the
            // view code-behind. The VM marks IsLoaded so the next reload
            // picks up persisted state.
            IsLoaded = true;
        }
    }
}
