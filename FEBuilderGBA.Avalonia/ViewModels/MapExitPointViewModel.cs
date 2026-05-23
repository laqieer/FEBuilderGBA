using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// ViewModel for `MapExitPointView` — the three-pane master-detail
    /// editor for Map Exit Points. Mirrors WinForms `MapExitPointForm`
    /// but routes all ROM access through <see cref="MapExitPointCore"/>
    /// so the same logic works in the headless test suite.
    ///
    /// The Core layer reads <c>RomInfo.map_exit_point_pointer</c> as the
    /// top-level slot table base — the VM never accesses it directly so
    /// every ROM lookup goes through the shared <see cref="MapExitPointCore"/>
    /// API (Copilot v1 review #4: no WinForms-only helpers).
    ///
    /// Layout shape (matches WF panels):
    /// <list type="bullet">
    ///   <item>Map list (filter-aware) — left pane.</item>
    ///   <item>Per-map exit-point sub-list — middle pane.</item>
    ///   <item>Detail panel (X / Y / Escape Method / Flag) — right pane.</item>
    /// </list>
    ///
    /// FilterIndex 0 = Enemy escape points, 1 = NPC escape points; the
    /// shift is `4 * RomInfo.map_exit_point_npc_blockadd` bytes into the
    /// shared pointer table (Copilot v1 review #1 correction).
    /// </summary>
    public class MapExitPointViewModel : ViewModelBase, IDataVerifiable
    {
        // The per-row struct shape (X, Y, EscapeMethod, Flag) auto-bound
        // via EditorFormRef. Names follow the WF Designer convention:
        // B{offset} = byte at that offset.
        static readonly List<EditorFormRef.FieldDef> _fields =
            EditorFormRef.DetectFields(new[] { "B0", "B1", "B2", "B3" });

        // ----- Filter / list state -----
        uint _filterIndex;
        uint _readStartAddress;
        uint _readCount;
        bool _isAllocated;
        string _notice = string.Empty;

        // ----- Map selection -----
        uint _selectedMapSlotAddr;
        uint _currentExitPointAddr; // dereferenced map → exit list base, or U.NOT_FOUND
        bool _isBlank;

        // ----- Per-row detail state -----
        uint _currentAddr;
        uint _exitX;
        uint _exitY;
        uint _escapeMethod;
        uint _flagId;
        uint _selectedAddressDisplay;
        uint _blockSize;
        bool _canWrite;

        // ----- Property surface -----
        public uint FilterIndex { get => _filterIndex; set => SetField(ref _filterIndex, value); }
        public uint ReadStartAddress { get => _readStartAddress; set => SetField(ref _readStartAddress, value); }
        public uint ReadCount { get => _readCount; set => SetField(ref _readCount, value); }
        public bool IsAllocated { get => _isAllocated; set => SetField(ref _isAllocated, value); }
        public string Notice { get => _notice; set => SetField(ref _notice, value); }

        public uint SelectedMapSlotAddr { get => _selectedMapSlotAddr; set => SetField(ref _selectedMapSlotAddr, value); }
        public uint CurrentExitPointAddr { get => _currentExitPointAddr; set => SetField(ref _currentExitPointAddr, value); }
        public bool IsBlank { get => _isBlank; set => SetField(ref _isBlank, value); }

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        /// <summary>Exit X coordinate (B0).</summary>
        public uint ExitX { get => _exitX; set => SetField(ref _exitX, value); }
        /// <summary>Exit Y coordinate (B1).</summary>
        public uint ExitY { get => _exitY; set => SetField(ref _exitY, value); }
        /// <summary>Disappearance / escape method (B2). 00=Left 2 steps, 01=Right 2 steps, 02=Down 2 steps, 03=Up 2 steps, 05=Stay in place.</summary>
        public uint EscapeMethod { get => _escapeMethod; set => SetField(ref _escapeMethod, value); }
        /// <summary>Event flag ID written when the unit reaches the exit (B3).</summary>
        public uint FlagId { get => _flagId; set => SetField(ref _flagId, value); }
        /// <summary>Display copy of the currently-selected row address.</summary>
        public uint SelectedAddressDisplay { get => _selectedAddressDisplay; set => SetField(ref _selectedAddressDisplay, value); }
        /// <summary>Width in bytes of one exit-point row (always 4 for this editor).</summary>
        public uint BlockSize { get => _blockSize; set => SetField(ref _blockSize, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }

        // -----------------------------------------------------------------
        // Loading — Filter / Map / Exit-point sub-list
        // -----------------------------------------------------------------

        /// <summary>
        /// Load the per-filter map list. Each entry's address points at the
        /// 4-byte pointer slot in the per-map pointer table; the view's
        /// sub-list step dereferences that slot to find the exit-point block.
        /// </summary>
        public List<AddrResult> LoadMapList(int filterIndex)
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            FilterIndex = (uint)filterIndex;
            var entries = MapExitPointCore.ListMapEntries(rom, (uint)filterIndex);
            uint baseAddr = MapExitPointCore.ResolveBaseAddress(rom, (uint)filterIndex);
            if (baseAddr != U.NOT_FOUND)
            {
                ReadStartAddress = baseAddr;
            }
            ReadCount = (uint)entries.Count;
            // Update the filter notice text (mirrors WF X_Filter_Note_Message).
            // Use R._() so the JA-keyed translation chain runs at runtime —
            // same JP source strings as WF, with en/zh translations already
            // in config/translate/*.txt (Copilot PR #531 review thread on
            // ViewModel line 105 — fix for ja/zh locale).
            Notice = filterIndex == 0
                ? R._("これは敵のエスケープポイントです。\r\nNPC用は、左上のコンボボックスを切り替えてください。")
                : R._("これはNPCのエスケープポイントです。\r\n敵用は、左上のコンボボックスを切り替えてください。");
            return entries;
        }

        /// <summary>
        /// Load the per-map exit-point sub-list. The
        /// <paramref name="mapPointerSlotAddr"/> is the 4-byte slot returned
        /// by <see cref="LoadMapList(int)"/>; this method dereferences it
        /// and walks the rows. When the slot points to the blank marker,
        /// returns an empty list and sets <see cref="IsBlank"/> to true.
        /// </summary>
        public List<AddrResult> LoadExitListForMap(uint mapPointerSlotAddr)
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();
            SelectedMapSlotAddr = mapPointerSlotAddr;
            if (!U.isSafetyOffset(mapPointerSlotAddr, rom))
            {
                IsBlank = true;
                IsAllocated = false;
                CurrentExitPointAddr = U.NOT_FOUND;
                return new List<AddrResult>();
            }

            uint exitPointer = rom.u32(mapPointerSlotAddr);
            if (!U.isPointer(exitPointer))
            {
                IsBlank = true;
                IsAllocated = false;
                CurrentExitPointAddr = U.NOT_FOUND;
                return new List<AddrResult>();
            }
            uint exitOffset = U.toOffset(exitPointer);
            if (MapExitPointCore.IsBlankPointer(rom, exitOffset))
            {
                IsBlank = true;
                IsAllocated = false;
                CurrentExitPointAddr = exitOffset;
                return new List<AddrResult>();
            }

            IsBlank = false;
            IsAllocated = true;
            CurrentExitPointAddr = exitOffset;
            return MapExitPointCore.ListExitPointsForMap(rom, exitOffset);
        }

        /// <summary>
        /// Load a single 4-byte exit-point row into the detail fields.
        /// </summary>
        public void LoadExitPointEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (!U.isSafetyOffset(addr, rom)) return;
            if (addr + 4 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            SelectedAddressDisplay = addr;
            BlockSize = 4;
            var values = EditorFormRef.ReadFields(rom, addr, _fields);
            ExitX = values["B0"];
            ExitY = values["B1"];
            EscapeMethod = values["B2"];
            FlagId = values["B3"];
            CanWrite = true;
        }

        /// <summary>
        /// Write the detail fields back to the current row. Caller MUST have
        /// opened an undo scope (the View wraps this in
        /// `_undoService.Begin/Commit`).
        /// </summary>
        public void WriteExitPoint()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            var values = new Dictionary<string, uint>
            {
                ["B0"] = ExitX,
                ["B1"] = ExitY,
                ["B2"] = EscapeMethod,
                ["B3"] = FlagId,
            };
            EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields);
        }

        /// <summary>
        /// Allocate a new 8-byte exit-point block in free space and repoint
        /// the current map's pointer slot to it. Returns the new address
        /// (or <see cref="U.NOT_FOUND"/> on failure). Caller must have an
        /// active undo scope.
        /// </summary>
        public uint NewAlloc(Undo.UndoData? undodata)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return U.NOT_FOUND;
            if (SelectedMapSlotAddr == 0 || !U.isSafetyOffset(SelectedMapSlotAddr, rom))
                return U.NOT_FOUND;
            return MapExitPointCore.NewAlloc(rom, SelectedMapSlotAddr, undodata);
        }

        // -----------------------------------------------------------------
        // IDataVerifiable — diagnostic reports
        // -----------------------------------------------------------------

        public int GetListCount() => LoadMapList((int)FilterIndex).Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["FilterIndex"] = $"0x{FilterIndex:X02}",
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["ExitX"] = $"0x{ExitX:X02}",
                ["ExitY"] = $"0x{ExitY:X02}",
                ["EscapeMethod"] = $"0x{EscapeMethod:X02}",
                ["FlagId"] = $"0x{FlagId:X02}",
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
                // Packed dword at offset 0: (Flag << 24) | (Escape << 16) | (Y << 8) | X
                // (useful for diff'ing entire 4-byte row entries against WF dumps).
                // Also mirrors WF MapExitPointForm.Designer.cs P0 NumericUpDown
                // (the "Leave Pointer" hex field WF exposes at offset 0 — the
                // AvaloniaFieldCompletenessTests regex pairs WF designer fields
                // with the equivalent VM read type, so the u32 read here keeps
                // the cross-check green even though the semantic meaning is the
                // per-byte X/Y/Escape/Flag below).
                ["u32@0x00"] = $"0x{rom.u32(a + 0):X08}",
                ["u8@0x00"] = $"0x{rom.u8(a + 0):X02}",
                ["u8@0x01"] = $"0x{rom.u8(a + 1):X02}",
                ["u8@0x02"] = $"0x{rom.u8(a + 2):X02}",
                ["u8@0x03"] = $"0x{rom.u8(a + 3):X02}",
            };
        }

        public Dictionary<string, string> GetFieldOffsetMap() => new()
        {
            ["ExitX"] = "B0@0x00",
            ["ExitY"] = "B1@0x01",
            ["EscapeMethod"] = "B2@0x02",
            ["FlagId"] = "B3@0x03",
        };
    }
}
