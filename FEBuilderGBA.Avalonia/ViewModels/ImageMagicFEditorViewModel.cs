// SPDX-License-Identifier: GPL-3.0-or-later
// ViewModel for ImageMagicFEditorView — the Avalonia counterpart of
// WinForms ImageMagicFEditorForm. Gap-sweep fix (#418) extends this
// from a 3-property stub into a full editor model:
//
// - LoadList scans the magic-effect pointer table for valid entries
//   (with FEditorAdv / CSA_Creator patch detection via
//   ImageUtilMagicCore).
// - LoadEntry reads the per-row dim pointer (compared to the patch's
//   dim/no-dim base addresses, mapped to DimPointerKind) plus the
//   comment from CoreState.CommentCache.
// - Write persists the dim pointer (rom.write_p32) AND the user
//   comment (CoreState.CommentCache.Update), mirroring WF
//   WriteDim() + WriteMagicName().
// - DimPointerKind enum covers the three dim pointer states WF
//   surfaces (dim_pc / dim / NULL(EMPTY)).
//
// The class is `partial` so the NavigationTargets manifest entry
// (1 row pointing at ToolAnimationCreatorView, tagged with #500 as
// the open follow-up tracker) can live in its own .NavigationTargets.cs
// file.
using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public partial class ImageMagicFEditorViewModel : ViewModelBase, IDataVerifiable
    {
        /// <summary>
        /// dim-pointer state surfaced by the magic-effect editor. The
        /// integer values match the WF DimComboBox.Items ordering:
        /// dim_pc=0, dim=1, NULL(EMPTY)=2.
        /// </summary>
        public enum DimPointerKind
        {
            /// <summary>Player-cast (dim_pc) animation.</summary>
            DimPc = 0,
            /// <summary>Enemy-cast (dim) animation.</summary>
            Dim = 1,
            /// <summary>Empty slot — entry deleted.</summary>
            Empty = 2,
        }

        /// <summary>Pointer-table entry stride (4 bytes per slot).</summary>
        const uint ENTRY_STRIDE = 4;

        /// <summary>Maximum FEditor slot index (vanilla cap is 0xFE).</summary>
        const int MAX_SLOTS = 0xFE;

        uint _currentAddr;
        bool _isLoaded;
        uint _p0, _p4, _p8, _p12, _p16;
        uint _frame;
        DimPointerKind _dimPointer = DimPointerKind.Empty;
        string _comment = string.Empty;
        bool _magicSystemDetected;
        uint _dimAddr = U.NOT_FOUND;
        uint _noDimAddr = U.NOT_FOUND;
        ImageUtilMagicCore.MagicSystem _system = ImageUtilMagicCore.MagicSystem.No;
        uint _csaSpellTable = U.NOT_FOUND;
        uint _spellDataCount;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint P0 { get => _p0; set => SetField(ref _p0, value); }
        public uint P4 { get => _p4; set => SetField(ref _p4, value); }
        public uint P8 { get => _p8; set => SetField(ref _p8, value); }
        public uint P12 { get => _p12; set => SetField(ref _p12, value); }
        public uint P16 { get => _p16; set => SetField(ref _p16, value); }
        public uint Frame { get => _frame; set => SetField(ref _frame, value); }
        public DimPointerKind DimPointer { get => _dimPointer; set => SetField(ref _dimPointer, value); }
        public string Comment { get => _comment; set => SetField(ref _comment, value ?? string.Empty); }

        /// <summary>True when one of the FEditorAdv / CSA_Creator
        /// signatures was found in the loaded ROM. When false the
        /// editor controls render but are disabled and PatchNotice
        /// explains why.</summary>
        public bool MagicSystemDetected => _magicSystemDetected;

        /// <summary>Magic system type detected (None when no patch).</summary>
        public ImageUtilMagicCore.MagicSystem MagicSystem => _system;

        /// <summary>Resolved dim address from the patch signature.</summary>
        public uint DimAddr => _dimAddr;

        /// <summary>Resolved no-dim address from the patch signature.</summary>
        public uint NoDimAddr => _noDimAddr;

        /// <summary>True when the spell-data table is expanded past the
        /// original per-version magic count, i.e. the "List Expansion"
        /// button should be hidden.</summary>
        public bool IsListExpanded
        {
            get
            {
                ROM rom = CoreState.ROM;
                if (rom?.RomInfo == null) return false;
                return _spellDataCount > rom.RomInfo.magic_effect_original_data_count;
            }
        }

        /// <summary>
        /// Re-scan the loaded ROM for the magic-engine patch + CSA
        /// spell table. Sets <see cref="MagicSystemDetected"/>,
        /// <see cref="DimAddr"/>, <see cref="NoDimAddr"/>, and
        /// <see cref="_csaSpellTable"/> as side effects.
        /// </summary>
        public void RefreshPatchState()
        {
            _magicSystemDetected = false;
            _system = ImageUtilMagicCore.MagicSystem.No;
            _dimAddr = U.NOT_FOUND;
            _noDimAddr = U.NOT_FOUND;
            _csaSpellTable = U.NOT_FOUND;
            _spellDataCount = 0u;

            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return;

            uint baseAddr;
            var system = ImageUtilMagicCore.SearchMagicSystem(
                rom, out baseAddr, out _dimAddr, out _noDimAddr);
            _system = system;
            if (system == ImageUtilMagicCore.MagicSystem.No)
            {
                return;
            }

            uint csaPointer;
            _csaSpellTable = ImageUtilMagicCore.FindCSASpellTable(
                rom, system, out csaPointer);
            if (_csaSpellTable == U.NOT_FOUND)
            {
                return;
            }

            _magicSystemDetected = true;
            _spellDataCount = ImageUtilMagicCore.GetSpellDataCount(rom);
        }

        /// <summary>
        /// Build the entry list for the editor. Mirrors WF
        /// `ImageMagicFEditorForm.Init` callback — walks the
        /// magic-effect pointer table looking for valid dim/no_dim
        /// entries (and EMPTY slots beyond the original count).
        /// </summary>
        public List<AddrResult> LoadList()
        {
            RefreshPatchState();

            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint magicPointer = rom.RomInfo.magic_effect_pointer;
            uint baseAddr = rom.p32(magicPointer);
            if (!U.isSafetyOffset(baseAddr, rom)) return new List<AddrResult>();

            int count = (int)Math.Min((uint)MAX_SLOTS,
                _magicSystemDetected ? _spellDataCount : 0u);
            if (count == 0)
            {
                // Pointer-table fallback so the list is at least
                // browsable even without the FEditor patch.
                count = (int)Math.Min((uint)MAX_SLOTS,
                    rom.RomInfo.magic_effect_original_data_count);
            }

            var result = new List<AddrResult>();
            uint dimAddr = _dimAddr;
            uint noDimAddr = _noDimAddr;
            uint origCount = rom.RomInfo.magic_effect_original_data_count;
            for (int i = 0; i < count; i++)
            {
                uint slot = (uint)(baseAddr + i * ENTRY_STRIDE);
                if (slot + ENTRY_STRIDE > (uint)rom.Data.Length) break;

                uint dataPtr = rom.p32(slot);
                uint csaAddr = _csaSpellTable != U.NOT_FOUND
                    ? (uint)(_csaSpellTable + 20 * i)
                    : slot;

                string name = string.Format("0x{0:X02} Magic Effect", i);

                if (dataPtr == 0)
                {
                    if ((uint)i < origCount) continue; // original slot, skipped
                    result.Add(new AddrResult(csaAddr, name + " EMPTY", slot));
                    continue;
                }
                if (_magicSystemDetected)
                {
                    if (dataPtr == dimAddr || dataPtr == noDimAddr)
                    {
                        result.Add(new AddrResult(csaAddr, name, slot));
                    }
                }
                else
                {
                    // Best effort: still expose the row by its pointer.
                    result.Add(new AddrResult(slot, name, slot));
                }
            }
            return result;
        }

        /// <summary>
        /// Read one editor row. <paramref name="addr"/> is the
        /// pointer-table slot (the WF "tag"). Reads the dim pointer
        /// + comment from CoreState.CommentCache.
        /// </summary>
        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null)
            {
                IsLoaded = false;
                return;
            }
            if (addr + ENTRY_STRIDE > (uint)rom.Data.Length)
            {
                IsLoaded = false;
                return;
            }

            // Ensure patch state is current — caller may have set the ROM
            // after constructing the VM.
            if (!_magicSystemDetected && _system == ImageUtilMagicCore.MagicSystem.No)
            {
                RefreshPatchState();
            }

            CurrentAddr = addr;
            uint dataPtr = rom.p32(addr);

            if (_magicSystemDetected && dataPtr == _dimAddr)
            {
                // WF semantics: the FEditor signature's `dim` field is
                // surfaced as `dim_pc` (player-cast); the FEditor
                // signature's `no_dim` field is surfaced as `dim`.
                DimPointer = DimPointerKind.DimPc;
            }
            else if (_magicSystemDetected && dataPtr == _noDimAddr)
            {
                DimPointer = DimPointerKind.Dim;
            }
            else
            {
                DimPointer = DimPointerKind.Empty;
            }

            // Comment is keyed on the entry slot itself (matches WF
            // `MagicComment.Text = Program.CommentCache.At(ar.addr)` where
            // ar.addr is the same slot returned in LoadList).
            Comment = CoreState.CommentCache?.At(addr, "") ?? string.Empty;

            IsLoaded = true;
        }

        /// <summary>
        /// Persist the editor row. Mirrors WF `N_WriteButton_Click`:
        /// writes the dim pointer via rom.write_p32 and updates the
        /// CoreState.CommentCache entry. The view wraps this in
        /// `_undoService.Begin/Commit/Rollback`.
        /// </summary>
        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0u) return;

            // Refresh patch state in case the ROM changed since LoadList.
            if (_dimAddr == U.NOT_FOUND || _noDimAddr == U.NOT_FOUND)
            {
                RefreshPatchState();
            }

            switch (DimPointer)
            {
                case DimPointerKind.DimPc:
                    if (_dimAddr != U.NOT_FOUND)
                        rom.write_p32(CurrentAddr, _dimAddr);
                    break;
                case DimPointerKind.Dim:
                    if (_noDimAddr != U.NOT_FOUND)
                        rom.write_p32(CurrentAddr, _noDimAddr);
                    break;
                case DimPointerKind.Empty:
                    rom.write_u32(CurrentAddr, 0u);
                    break;
            }

            // Persist comment (WF: Program.CommentCache.Update(newaddr, MagicComment.Text)).
            if (CoreState.CommentCache != null)
            {
                CoreState.CommentCache.Update(CurrentAddr, Comment ?? string.Empty);
            }
        }

        /// <summary>
        /// Original per-version magic count (e.g. FE8U=0x48). Used by
        /// the view to show/hide the List Expansion button.
        /// </summary>
        public uint OriginalMagicCount
        {
            get
            {
                ROM rom = CoreState.ROM;
                return rom?.RomInfo?.magic_effect_original_data_count ?? 0u;
            }
        }

        public int GetListCount() => LoadList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = string.Format("0x{0:X08}", CurrentAddr),
                ["DimPointer"] = DimPointer.ToString(),
                ["Comment"] = Comment ?? string.Empty,
                ["P0"] = string.Format("0x{0:X08}", P0),
                ["P4"] = string.Format("0x{0:X08}", P4),
                ["P8"] = string.Format("0x{0:X08}", P8),
                ["P12"] = string.Format("0x{0:X08}", P12),
                ["P16"] = string.Format("0x{0:X08}", P16),
            };
        }

        public Dictionary<string, string> GetRawRomReport()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return new Dictionary<string, string>();

            uint a = CurrentAddr;
            // CurrentAddr is the pointer-table slot; the P0..P16 fields
            // shown in GetDataReport mirror the WF DragTargetPanel
            // spinners. The completeness cross-check expects offsets
            // 0/4/8/12/16 off CurrentAddr — these stay in-range and are
            // valid ROM reads.
            var result = new Dictionary<string, string>
            {
                ["addr"] = string.Format("0x{0:X08}", a),
                ["u32@0"] = string.Format("0x{0:X08}", rom.u32(a)),
            };
            if (a + 4 + 4 <= (uint)rom.Data.Length)
                result["u32@4"] = string.Format("0x{0:X08}", rom.u32(a + 4));
            if (a + 8 + 4 <= (uint)rom.Data.Length)
                result["u32@8"] = string.Format("0x{0:X08}", rom.u32(a + 8));
            if (a + 12 + 4 <= (uint)rom.Data.Length)
                result["u32@12"] = string.Format("0x{0:X08}", rom.u32(a + 12));
            if (a + 16 + 4 <= (uint)rom.Data.Length)
                result["u32@16"] = string.Format("0x{0:X08}", rom.u32(a + 16));
            return result;
        }

        public Dictionary<string, string> GetFieldOffsetMap() => new()
        {
            ["DimPointer"] = "u32@0",
            ["P0"] = "u32@0",
            ["P4"] = "u32@4",
            ["P8"] = "u32@8",
            ["P12"] = "u32@12",
            ["P16"] = "u32@16",
        };
    }
}
