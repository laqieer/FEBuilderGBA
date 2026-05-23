// SPDX-License-Identifier: GPL-3.0-or-later
// FE8-only world map event pointer editor view-model. (#432)
//
// Pre-#432 this VM read `map_worldmapevent_pointer` — an FE6-only RomInfo
// slot that is 0 on FE8 — which made the editor functionally dead on its
// target platform. The rewrite uses the correct FE8 RomInfo pointers:
//
//   - worldmap_event_on_stageclear_pointer  -> Before list (stage clear)
//   - worldmap_event_on_stageselect_pointer -> After list  (stage select)
//   - oping_event_pointer                   -> Opening cinematic event
//   - ending1_event_pointer                 -> Eirika ending event
//   - ending2_event_pointer                 -> Ephraim ending event
//
// Mirrors the WinForms `WorldMapEventPointerForm` (Designer.cs declares
// 39 controls in panels 1..9 + AddressPanel). The Avalonia view stays
// list-driven on both axes (Before list + After list) so the AV control
// density stays inside the 25% MEDIUM threshold per the gap-sweep
// methodology (#374).
using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public partial class WorldMapEventPointerViewModel : ViewModelBase, IDataVerifiable
    {
        const uint BlockSize = 4;

        uint _currentBeforeAddr;
        uint _currentAfterAddr;
        uint _beforeEventPointer;
        uint _afterEventPointer;
        uint _openingEvent;
        uint _ending1Event;
        uint _ending2Event;
        uint _beforeBaseAddr;
        uint _afterBaseAddr;
        uint _beforeReadCount;
        uint _afterReadCount;
        bool _canWrite;

        // Current row addresses (selection cursors for each list).
        public uint CurrentBeforeAddr { get => _currentBeforeAddr; set => SetField(ref _currentBeforeAddr, value); }
        public uint CurrentAfterAddr { get => _currentAfterAddr; set => SetField(ref _currentAfterAddr, value); }

        // Current row event pointers (the u32 at each selection cursor).
        public uint BeforeEventPointer { get => _beforeEventPointer; set => SetField(ref _beforeEventPointer, value); }
        public uint AfterEventPointer { get => _afterEventPointer; set => SetField(ref _afterEventPointer, value); }

        // Global event pointers — fixed RomInfo slots, not part of either list.
        public uint OpeningEvent { get => _openingEvent; set => SetField(ref _openingEvent, value); }
        public uint Ending1Event { get => _ending1Event; set => SetField(ref _ending1Event, value); }
        public uint Ending2Event { get => _ending2Event; set => SetField(ref _ending2Event, value); }

        // List read-config indicators (mirror the WF top read-config bars).
        public uint BeforeBaseAddr { get => _beforeBaseAddr; set => SetField(ref _beforeBaseAddr, value); }
        public uint AfterBaseAddr { get => _afterBaseAddr; set => SetField(ref _afterBaseAddr, value); }
        public uint BeforeReadCount { get => _beforeReadCount; set => SetField(ref _beforeReadCount, value); }
        public uint AfterReadCount { get => _afterReadCount; set => SetField(ref _afterReadCount, value); }

        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }

        /// <summary>
        /// Auto-loads the three global event pointers from ROM (if available)
        /// so a freshly-constructed VM reports values that match the ROM
        /// without requiring an explicit `LoadGlobalEvents` call. The
        /// DataVerifiableSweepTests FieldLevelCrossCheck_AllFieldsMatch test
        /// constructs the VM fresh and expects GetDataReport to mirror
        /// GetRawRomReport — without this hook, the global event entries
        /// would be 0 in DataReport but non-zero in RawRomReport.
        /// </summary>
        public WorldMapEventPointerViewModel()
        {
            LoadGlobalEvents();
        }

        // ----------------------------------------------------------------
        // Initial load — resolve the three global event pointers and
        // populate both lists. Caller should call this when the view opens.
        // ----------------------------------------------------------------
        public void LoadGlobalEvents()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null)
            {
                OpeningEvent = Ending1Event = Ending2Event = 0;
                return;
            }
            OpeningEvent = rom.p32(rom.RomInfo.oping_event_pointer);
            Ending1Event = rom.p32(rom.RomInfo.ending1_event_pointer);
            Ending2Event = rom.p32(rom.RomInfo.ending2_event_pointer);
        }

        // ----------------------------------------------------------------
        // Build the Before (stage-clear) list. Mirrors WF Init() exactly:
        //
        //   - InputFormRef.IsDataExistsCallback returns `true` unconditionally
        //     for `i == 0`, then `U.isPointer(rom.u32(addr))` for i > 0.
        //   - Termination stops at the first non-pointer entry past row 0.
        //
        // Without the unconditional row-0 rule the WF behaviour would drop
        // the first slot when it's NULL — which is the common case for fresh
        // FE8 ROMs that haven't allocated their stage-clear events yet.
        // (Copilot CLI plan review point 3.)
        // ----------------------------------------------------------------
        public List<AddrResult> LoadBeforeList()
            => LoadListFromPointer(WhichList.BeforeStageClear);

        public List<AddrResult> LoadAfterList()
            => LoadListFromPointer(WhichList.AfterStageSelect);

        enum WhichList { BeforeStageClear, AfterStageSelect }

        /// <summary>
        /// Clear the read-config indicators for the given list. Called on
        /// every early-return path in LoadListFromPointer so a failed
        /// reload (no ROM, null pointer, unsafe address) does not leave
        /// stale values from a prior successful load visible to the user.
        /// (Copilot CLI re-review inline thread on PR #511.)
        /// </summary>
        void ResetReadConfig(WhichList which)
        {
            if (which == WhichList.BeforeStageClear)
            {
                BeforeBaseAddr = 0;
                BeforeReadCount = 0;
            }
            else
            {
                AfterBaseAddr = 0;
                AfterReadCount = 0;
            }
        }

        List<AddrResult> LoadListFromPointer(WhichList which)
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null)
            {
                ResetReadConfig(which);
                return new List<AddrResult>();
            }

            uint ptr = which == WhichList.BeforeStageClear
                ? rom.RomInfo.worldmap_event_on_stageclear_pointer
                : rom.RomInfo.worldmap_event_on_stageselect_pointer;
            if (ptr == 0)
            {
                ResetReadConfig(which);
                return new List<AddrResult>();
            }

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr, rom))
            {
                ResetReadConfig(which);
                return new List<AddrResult>();
            }

            // Update read-config indicators so the top bar matches the
            // resolved address. (WF surfaces these as read-only labels.)
            if (which == WhichList.BeforeStageClear)
                BeforeBaseAddr = baseAddr;
            else
                AfterBaseAddr = baseAddr;

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x200; i++)
            {
                uint addr = baseAddr + i * BlockSize;
                if (addr + BlockSize > (uint)rom.Data.Length) break;

                uint eventPtr = rom.u32(addr);

                // WF predicate: row 0 always passes; row >0 needs U.isPointer.
                bool include = i == 0 || U.isPointer(eventPtr);
                if (!include) break;

                string prefix = which == WhichList.BeforeStageClear
                    ? "Before"
                    : "After";
                string name = $"{U.ToHexString(i)} {prefix} 0x{eventPtr:X08}";
                result.Add(new AddrResult(addr, name, i));
            }

            uint count = (uint)result.Count;
            if (which == WhichList.BeforeStageClear)
                BeforeReadCount = count;
            else
                AfterReadCount = count;
            return result;
        }

        // ----------------------------------------------------------------
        // Per-row load handlers. Read the u32 at the selected address into
        // the corresponding BeforeEventPointer / AfterEventPointer field.
        // ----------------------------------------------------------------
        public void LoadBeforeEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + BlockSize > (uint)rom.Data.Length) return;

            CurrentBeforeAddr = addr;
            // WF stores per-row event pointers as `P0` fields — `rom.p32`
            // (offset form, mask stripped). The Write path inverts via
            // `write_p32` (mask re-applied). Without this convention the
            // round-trip would drift by 0x08000000 (Copilot CLI re-review
            // blocking issue 1 — issue #432).
            BeforeEventPointer = rom.p32(addr);
            CanWrite = true;
        }

        public void LoadAfterEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + BlockSize > (uint)rom.Data.Length) return;

            CurrentAfterAddr = addr;
            AfterEventPointer = rom.p32(addr);
            CanWrite = true;
        }

        // ----------------------------------------------------------------
        // Write handlers — used by the View's Write_Click handlers inside
        // an `_undoService.Begin/Commit` scope. Each returns `true` on a
        // successful write or `false` when a precondition was not met
        // (no ROM, no row selected, address OOB). The View uses the bool
        // to decide whether to Commit + show success or skip the undo
        // entry + show a warning — avoids empty undo entries and
        // misleading "written" messages (Copilot bot inline review).
        // ----------------------------------------------------------------
        public bool WriteBefore()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentBeforeAddr == 0) return false;
            if (CurrentBeforeAddr + BlockSize > (uint)rom.Data.Length) return false;
            // P0 convention: `write_p32` re-applies the 0x08000000 mask
            // when the input is an offset; pointer-form values pass
            // through unchanged. Mirrors WF `Program.ROM.write_p32(addr, val)`.
            rom.write_p32(CurrentBeforeAddr, BeforeEventPointer);
            return true;
        }

        public bool WriteAfter()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAfterAddr == 0) return false;
            if (CurrentAfterAddr + BlockSize > (uint)rom.Data.Length) return false;
            rom.write_p32(CurrentAfterAddr, AfterEventPointer);
            return true;
        }

        public bool WriteGlobalEvents()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return false;
            rom.write_p32(rom.RomInfo.oping_event_pointer, OpeningEvent);
            rom.write_p32(rom.RomInfo.ending1_event_pointer, Ending1Event);
            rom.write_p32(rom.RomInfo.ending2_event_pointer, Ending2Event);
            return true;
        }

        // ----------------------------------------------------------------
        // IDataVerifiable surface — keep the same shape as the prior VM so
        // the data-verify dialog continues to work post-rewrite. Reports
        // both row addresses, both row pointers, and the three globals.
        // ----------------------------------------------------------------
        public int GetListCount() => LoadBeforeList().Count + LoadAfterList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["beforeAddr"] = $"0x{CurrentBeforeAddr:X08}",
                ["afterAddr"] = $"0x{CurrentAfterAddr:X08}",
                ["BeforeEventPointer"] = $"0x{BeforeEventPointer:X08}",
                ["AfterEventPointer"] = $"0x{AfterEventPointer:X08}",
                ["OpeningEvent"] = $"0x{OpeningEvent:X08}",
                ["Ending1Event"] = $"0x{Ending1Event:X08}",
                ["Ending2Event"] = $"0x{Ending2Event:X08}",
            };
        }

        public Dictionary<string, string> GetRawRomReport()
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return new Dictionary<string, string>();
            // Keys follow the FormCompleteness regex shape
            // `Name@0x00` so the AvaloniaFieldCompletenessTests can pair
            // raw-report entries to the ROM-read patterns in this VM.
            // Always emit all five keys so DataVerifiableSweepTests
            // GetFieldOffsetMap_KeysConsistentWithReports finds every value
            // declared in GetFieldOffsetMap regardless of whether a row has
            // been selected yet.
            var dict = new Dictionary<string, string>
            {
                // Use p32 (offset form) so values match the VM's
                // BeforeEventPointer / AfterEventPointer properties which
                // are also loaded via p32 (per the WF P0 convention).
                // FieldLevelCrossCheck compares the two reports directly —
                // they must use the same form. (Copilot CLI re-review.)
                ["BeforePtr@0x00"] = CurrentBeforeAddr != 0
                    ? $"0x{rom.p32(CurrentBeforeAddr):X08}" : "0x00000000",
                ["AfterPtr@0x00"] = CurrentAfterAddr != 0
                    ? $"0x{rom.p32(CurrentAfterAddr):X08}" : "0x00000000",
            };
            if (rom.RomInfo != null)
            {
                // Use p32 (offset form) so values match the VM's
                // OpeningEvent/Ending1Event/Ending2Event properties (also
                // loaded via p32). The DataVerifiableSweepTests
                // FieldLevelCrossCheck_AllFieldsMatch test compares the
                // two reports directly — they must use the same convention.
                dict["OpeningPtr@0x00"] = $"0x{rom.p32(rom.RomInfo.oping_event_pointer):X08}";
                dict["Ending1Ptr@0x00"] = $"0x{rom.p32(rom.RomInfo.ending1_event_pointer):X08}";
                dict["Ending2Ptr@0x00"] = $"0x{rom.p32(rom.RomInfo.ending2_event_pointer):X08}";
            }
            else
            {
                dict["OpeningPtr@0x00"] = "0x00000000";
                dict["Ending1Ptr@0x00"] = "0x00000000";
                dict["Ending2Ptr@0x00"] = "0x00000000";
            }
            return dict;
        }

        public Dictionary<string, string> GetFieldOffsetMap() => new()
        {
            // Values mirror the GetRawRomReport() keys exactly so the
            // DataVerifiableSweepTests cross-check passes (each FieldOffsetMap
            // value must be present in GetRawRomReport()).
            ["BeforeEventPointer"] = "BeforePtr@0x00",
            ["AfterEventPointer"] = "AfterPtr@0x00",
            ["OpeningEvent"] = "OpeningPtr@0x00",
            ["Ending1Event"] = "Ending1Ptr@0x00",
            ["Ending2Event"] = "Ending2Ptr@0x00",
        };
    }
}
