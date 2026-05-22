// SPDX-License-Identifier: GPL-3.0-or-later
// Gap-sweep #440 rebuild — exposes all 10 array-kind filters and the
// switch2-aware list builder backed by FEBuilderGBA.Core.ItemUsagePointerCore.
using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// View model for the Item Usage Pointer editor (#440). Mirrors the
    /// WinForms <c>ItemUsagePointerForm</c> dispatch over the 10
    /// array kinds (Usability, Effect, Promotion1/2, Staff1/2,
    /// StatBooster1/2, ErrorMessage, NameArticle).
    ///
    /// The ROM read/write path is shared with WinForms via
    /// <c>ItemUsagePointerCore</c> in the Core library; this VM holds
    /// the UI state (selected filter, current address, IER detection).
    /// </summary>
    public partial class ItemUsagePointerViewerViewModel : ViewModelBase, IDataVerifiable
    {
        static readonly List<EditorFormRef.FieldDef> _fields =
            EditorFormRef.DetectFields(new[] { "D0" });

        int _filterIndex;
        uint _currentArrayAddr;
        uint _currentSelectedAddr;
        uint _usabilityPointer;
        uint _readStartAddress;
        uint _readCount;
        uint _blockSize;
        uint _selectedItemId;
        bool _canWrite;
        bool _isEnabledForCurrentFilter;
        bool _isIERPatchInstalled;
        string _asmSwitchText = "";

        // ------------ Public state ----------------

        /// <summary>Selected FilterComboBox index (0..9).</summary>
        public int FilterIndex { get => _filterIndex; set => SetField(ref _filterIndex, value); }

        /// <summary>Base address of the currently-loaded array (head pointer).</summary>
        public uint CurrentArrayAddr { get => _currentArrayAddr; set => SetField(ref _currentArrayAddr, value); }

        /// <summary>Address of the currently-selected list row.</summary>
        public uint CurrentSelectedAddr { get => _currentSelectedAddr; set => SetField(ref _currentSelectedAddr, value); }

        /// <summary>Backwards-compat alias used by the pre-#440 view.</summary>
        public uint CurrentAddr { get => _currentSelectedAddr; set => SetField(ref _currentSelectedAddr, value); }

        /// <summary>The function pointer at the selected row.</summary>
        public uint UsabilityPointer { get => _usabilityPointer; set => SetField(ref _usabilityPointer, value); }

        /// <summary>Read-only Hex Address indicator that mirrors WF panel3.</summary>
        public uint ReadStartAddress { get => _readStartAddress; set => SetField(ref _readStartAddress, value); }

        /// <summary>Read-only Count indicator (total list rows).</summary>
        public uint ReadCount { get => _readCount; set => SetField(ref _readCount, value); }

        /// <summary>Block size — 4 bytes per row (pointer-table convention).</summary>
        public uint BlockSize { get => _blockSize; set => SetField(ref _blockSize, value); }

        /// <summary>Item ID derived from the selected row + switch2 start.</summary>
        public uint SelectedItemId { get => _selectedItemId; set => SetField(ref _selectedItemId, value); }

        /// <summary>True after a row was loaded — gates the Write button.</summary>
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }

        /// <summary>True when the current filter has a valid Switch2 ASM signature.</summary>
        public bool IsEnabledForCurrentFilter
        {
            get => _isEnabledForCurrentFilter;
            set => SetField(ref _isEnabledForCurrentFilter, value);
        }

        /// <summary>True when the Item Effect Range (IER) patch is detected.</summary>
        public bool IsIERPatchInstalled
        {
            get => _isIERPatchInstalled;
            set => SetField(ref _isIERPatchInstalled, value);
        }

        /// <summary>Hex representation of the switch2 ASM address (display only).</summary>
        public string AsmSwitchText { get => _asmSwitchText; set => SetField(ref _asmSwitchText, value); }

        /// <summary>
        /// Display labels for the 10 array filters, in the order shown
        /// in the FilterComboBox. Provided as a defensive copy of the
        /// Core metadata so binding consumers cannot mutate it.
        /// </summary>
        public IReadOnlyList<string> FilterEntries
        {
            get
            {
                var list = new List<string>(10);
                foreach (var fm in ItemUsagePointerCore.GetAllFilters())
                    list.Add(fm.Label);
                return list;
            }
        }

        // ------------ Public methods ----------------

        /// <summary>
        /// Refresh patch-detection state — should be called after ROM
        /// load + at any patch install/uninstall. Hooks
        /// PatchDetectionService.Instance.ItemEffectRange.
        /// </summary>
        public void RefreshPatchState()
        {
            try
            {
                IsIERPatchInstalled = PatchDetectionService.Instance.ItemEffectRange;
            }
            catch
            {
                IsIERPatchInstalled = false;
            }
        }

        /// <summary>
        /// Convenience: load the rows for filterIndex 0 (Usability) — the
        /// initial state of the view. Returns the row list for binding.
        /// </summary>
        public List<AddrResult> LoadItemUsagePointerList()
            => LoadList(0);

        /// <summary>
        /// Build the row list for the given filter using
        /// <see cref="ItemUsagePointerCore.MakeRows"/>. Updates
        /// the read-only indicators in this VM as a side effect.
        /// </summary>
        public List<AddrResult> LoadList(int filterIndex)
        {
            FilterIndex = filterIndex;
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null)
            {
                IsEnabledForCurrentFilter = false;
                ReadCount = 0;
                ReadStartAddress = 0;
                CurrentArrayAddr = 0;
                AsmSwitchText = "";
                return new List<AddrResult>();
            }

            var kind = (ItemUsagePointerCore.FilterKind)filterIndex;
            uint switchAddr = ItemUsagePointerCore.GetSwitchSlot(rom, kind);
            uint pointerSlot = ItemUsagePointerCore.GetPointerSlot(rom, kind);

            // Update read-only indicators regardless of enable-state so the
            // user can see the static metadata even when this filter is not
            // available on the current ROM version.
            AsmSwitchText = switchAddr == 0 ? "" : $"0x{switchAddr:X08}";
            BlockSize = 4;

            if (!ItemUsagePointerCore.IsSwitch2Enable(rom, switchAddr))
            {
                IsEnabledForCurrentFilter = false;
                ReadCount = 0;
                ReadStartAddress = 0;
                CurrentArrayAddr = 0;
                return new List<AddrResult>();
            }

            uint baseAddr = pointerSlot != 0 ? rom.p32(pointerSlot) : 0u;
            CurrentArrayAddr = baseAddr;
            ReadStartAddress = baseAddr;

            var rows = ItemUsagePointerCore.MakeRows(rom, kind);
            ReadCount = (uint)rows.Count;
            IsEnabledForCurrentFilter = rows.Count > 0;
            return rows;
        }

        /// <summary>
        /// Compute the item ID for a selected row address. Mirrors the
        /// WinForms callback's `itemID = switch2_address[0] + i` derivation.
        /// </summary>
        public uint ItemIdForRow(uint rowAddr)
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return 0;
            var kind = (ItemUsagePointerCore.FilterKind)FilterIndex;
            uint pointerSlot = ItemUsagePointerCore.GetPointerSlot(rom, kind);
            uint switchAddr = ItemUsagePointerCore.GetSwitchSlot(rom, kind);
            if (pointerSlot == 0 || switchAddr == 0) return 0;
            uint baseAddr = rom.p32(pointerSlot);
            if (baseAddr == 0) return 0;
            uint i = (rowAddr - baseAddr) / 4;
            var s2 = ItemUsagePointerCore.ReadSwitch2(rom, switchAddr);
            uint start = s2.HasValue ? s2.Value.Start : 0u;
            return start + i;
        }

        public void LoadItemUsagePointer(uint addr)
            => LoadEntry(addr);

        /// <summary>
        /// Load the currently-selected row into the editor fields.
        /// </summary>
        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            if (addr + 3 >= (uint)rom.Data.Length) return;

            CurrentSelectedAddr = addr;
            var values = EditorFormRef.ReadFields(rom, addr, _fields);
            UsabilityPointer = values["D0"];
            SelectedItemId = ItemIdForRow(addr);
            CanWrite = true;
        }

        /// <summary>
        /// Write the currently-edited UsabilityPointer back to ROM.
        /// The View wraps this in an UndoService scope.
        /// </summary>
        public void WriteItemUsagePointer()
            => Write();

        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentSelectedAddr == 0) return;
            var values = new Dictionary<string, uint> { ["D0"] = UsabilityPointer };
            EditorFormRef.WriteFields(rom, CurrentSelectedAddr, values, _fields);
        }

        /// <summary>
        /// Expand the current filter's array. Delegates to
        /// <see cref="ItemUsagePointerCore.Switch2Expands"/> using the
        /// undo data supplied by the caller (the View owns the scope).
        /// Returns the new table address, or U.NOT_FOUND on failure.
        /// </summary>
        public uint ExpandList(uint newCount, uint defaultJumpAddr, Undo.UndoData undo)
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return U.NOT_FOUND;

            var kind = (ItemUsagePointerCore.FilterKind)FilterIndex;
            uint pointerSlot = ItemUsagePointerCore.GetPointerSlot(rom, kind);
            uint switchAddr = ItemUsagePointerCore.GetSwitchSlot(rom, kind);
            if (pointerSlot == 0 || switchAddr == 0) return U.NOT_FOUND;

            return ItemUsagePointerCore.Switch2Expands(
                rom,
                pointerSlot,
                switchAddr,
                newCount,
                defaultJumpAddr,
                undo);
        }

        public int GetListCount() => LoadList(FilterIndex).Count;

        // ------------ IDataVerifiable ----------------

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["filterIndex"] = FilterIndex.ToString(),
                ["arrayAddr"] = $"0x{CurrentArrayAddr:X08}",
                ["selectedAddr"] = $"0x{CurrentSelectedAddr:X08}",
                ["UsabilityPointer"] = $"0x{UsabilityPointer:X08}",
                ["ierPatch"] = IsIERPatchInstalled.ToString(),
                ["enabled"] = IsEnabledForCurrentFilter.ToString(),
            };
        }

        public Dictionary<string, string> GetRawRomReport()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentSelectedAddr == 0) return new Dictionary<string, string>();
            uint a = CurrentSelectedAddr;
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{a:X08}",
                ["u32@0x00"] = $"0x{rom.u32(a):X08}",
            };
        }

        public Dictionary<string, string> GetFieldOffsetMap()
        {
            return new Dictionary<string, string>
            {
                ["UsabilityPointer"] = "u32@0x00",
            };
        }
    }
}
