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

        // CSA spell-table entry address (mirrors WF AddrResult.addr). Holds
        // P0..P16 + comment data. P0 = u32 at CurrentAddr+0, P4 = +4, etc.
        uint _currentAddr;

        // Pointer-table slot address (mirrors WF AddrResult.tag). Holds the
        // dim/no-dim/empty pointer (4 bytes). Separate from CurrentAddr
        // because the WF AddressList row carries BOTH addresses (the
        // pointer slot points at the CSA entry, but CommentCache + the
        // user-facing Address spinner key off the CSA entry, while
        // WriteDim writes to the slot itself).
        uint _pointerSlotAddr;

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
        uint _magicEffectTableBase;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        /// <summary>Pointer-table slot for the selected row (mirrors WF
        /// `AddrResult.tag`). dim/no-dim/empty pointer lives here; setter
        /// is internal because the view drives it via OnSelected.</summary>
        public uint PointerSlotAddr { get => _pointerSlotAddr; set => SetField(ref _pointerSlotAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint P0 { get => _p0; set => SetField(ref _p0, value); }
        public uint P4 { get => _p4; set => SetField(ref _p4, value); }
        public uint P8 { get => _p8; set => SetField(ref _p8, value); }
        public uint P12 { get => _p12; set => SetField(ref _p12, value); }
        public uint P16 { get => _p16; set => SetField(ref _p16, value); }
        public uint Frame { get => _frame; set => SetField(ref _frame, value); }
        public DimPointerKind DimPointer { get => _dimPointer; set => SetField(ref _dimPointer, value); }
        public string Comment { get => _comment; set => SetField(ref _comment, value ?? string.Empty); }

        /// <summary>Pointer-table base address used by the top read-config
        /// bar (mirrors WF panel3 ReadStartAddress).</summary>
        public uint ReadStartAddress => _magicEffectTableBase;

        /// <summary>How many magic-effect entries the editor surfaces (mirrors
        /// WF panel3 ReadCount).</summary>
        public uint ReadCount => _spellDataCount > 0 ? _spellDataCount
            : (CoreState.ROM?.RomInfo?.magic_effect_original_data_count ?? 0u);

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
        ///
        /// Each `AddrResult` exposed here uses the WF convention:
        /// `addr = csaAddr` (CSA spell-table entry address — base of the
        /// 20-byte struct that holds P0/P4/P8/P12/P16 + the comment key)
        /// and `tag = slot` (pointer-table slot — the 4-byte location
        /// where the dim/no-dim/empty pointer lives).
        /// </summary>
        public List<AddrResult> LoadList()
        {
            RefreshPatchState();

            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint magicPointer = rom.RomInfo.magic_effect_pointer;
            uint baseAddr = rom.p32(magicPointer);
            if (!U.isSafetyOffset(baseAddr, rom)) return new List<AddrResult>();
            _magicEffectTableBase = baseAddr;

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

                // Bounds-check the CSA entry address — a malformed ROM
                // or unexpected table size could push csaAddr+20 past
                // EOF (Copilot bot review on PR #554). Skipping such
                // rows prevents LoadEntry from bailing out and leaving
                // stale VM state.
                if (csaAddr + 20 > (uint)rom.Data.Length) break;

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
        /// Read one editor row. The CSA entry address (mirrors WF
        /// `AddrResult.addr`) carries the 20-byte struct holding
        /// P0/P4/P8/P12/P16 + the comment key. The pointer-table
        /// slot (mirrors WF `AddrResult.tag`) carries the
        /// dim/no-dim/empty pointer.
        /// </summary>
        public void LoadEntry(uint csaEntryAddr, uint pointerSlotAddr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null)
            {
                IsLoaded = false;
                return;
            }
            if (csaEntryAddr + 20 > (uint)rom.Data.Length ||
                pointerSlotAddr + ENTRY_STRIDE > (uint)rom.Data.Length)
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

            CurrentAddr = csaEntryAddr;
            PointerSlotAddr = pointerSlotAddr;

            // Read dim pointer from the pointer-table slot (WF
            // AddressList_SelectedIndexChanged: `uint dim = Program.ROM.p32(ar.tag)`).
            // When pointerSlotAddr == 0 (LoadEntry(uint) convenience
            // overload), treat it as "unknown slot" and force Empty so
            // we don't read ROM offset 0 (header data) and misinterpret
            // it as a dim pointer (Copilot bot review on PR #554).
            if (pointerSlotAddr == 0u)
            {
                DimPointer = DimPointerKind.Empty;
            }
            else
            {
                uint dataPtr = rom.p32(pointerSlotAddr);
                if (_magicSystemDetected && dataPtr == _dimAddr)
                {
                    // WF semantics: the FEditor signature's `dim` field
                    // surfaces as `dim_pc` (player-cast); `no_dim`
                    // surfaces as `dim`.
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
            }

            // Read the P0..P16 fields from the CSA spell-table entry
            // (20 bytes = 5x u32).
            P0 = rom.u32(csaEntryAddr + 0);
            P4 = rom.u32(csaEntryAddr + 4);
            P8 = rom.u32(csaEntryAddr + 8);
            P12 = rom.u32(csaEntryAddr + 12);
            P16 = rom.u32(csaEntryAddr + 16);

            // Comment is keyed on the CSA entry address (matches WF
            // `MagicComment.Text = Program.CommentCache.At(ar.addr)`).
            Comment = CoreState.CommentCache?.At(csaEntryAddr, "") ?? string.Empty;

            IsLoaded = true;
        }

        /// <summary>
        /// Convenience overload — the view code-behind may only have the
        /// CSA entry address (e.g. NavigateTo path). The pointer-slot
        /// address is explicitly cleared to 0 so a stale
        /// `_pointerSlotAddr` from a previous selection cannot leak
        /// into dim-pointer reads/writes against the wrong row (Copilot
        /// bot review #2 on PR #554). `Write()` skips the pointer-slot
        /// branch when `PointerSlotAddr == 0`.
        /// </summary>
        public void LoadEntry(uint addr) => LoadEntry(addr, 0u);

        /// <summary>
        /// Persist the editor row. Mirrors WF `N_WriteButton_Click`:
        /// - writes the dim pointer to the pointer-table slot
        ///   (`PointerSlotAddr`, WF `ar.tag`) via `rom.write_p32`.
        /// - updates `CoreState.CommentCache` keyed by the CSA entry
        ///   address (`CurrentAddr`, WF `Address.Value`).
        /// - writes the P0..P16 fields to the CSA spell-table entry.
        /// The view wraps this in `_undoService.Begin/Commit/Rollback`.
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

            // VM-level patch-absence guard (Copilot CLI re-review on
            // PR #554). The view's Write_Click already short-circuits,
            // but Write() is public/test-callable and could otherwise
            // wipe vanilla magic-effect pointer slots via the
            // DimPointerKind.Empty branch. Mirrors WF WriteDim()'s
            // assertion that ar.tag != 0 + the patch being present.
            if (!_magicSystemDetected) return;

            // Persist dim/no-dim/empty pointer to the pointer-table slot.
            // WF: `Program.ROM.write_p32(ar.tag, this.DimAddr)` etc.
            if (PointerSlotAddr != 0u)
            {
                switch (DimPointer)
                {
                    case DimPointerKind.DimPc:
                        if (_dimAddr != U.NOT_FOUND)
                            rom.write_p32(PointerSlotAddr, _dimAddr);
                        break;
                    case DimPointerKind.Dim:
                        if (_noDimAddr != U.NOT_FOUND)
                            rom.write_p32(PointerSlotAddr, _noDimAddr);
                        break;
                    case DimPointerKind.Empty:
                        rom.write_u32(PointerSlotAddr, 0u);
                        break;
                }
            }

            // Persist P0..P16 to the CSA spell-table entry (20 bytes).
            // The view's editable spinners feed these properties.
            if (CurrentAddr + 20 <= (uint)rom.Data.Length)
            {
                rom.write_u32(CurrentAddr + 0, P0);
                rom.write_u32(CurrentAddr + 4, P4);
                rom.write_u32(CurrentAddr + 8, P8);
                rom.write_u32(CurrentAddr + 12, P12);
                rom.write_u32(CurrentAddr + 16, P16);
            }

            // Persist comment keyed by the CSA entry address (WF:
            // Program.CommentCache.Update(Address.Value, MagicComment.Text)).
            if (CoreState.CommentCache != null)
            {
                CoreState.CommentCache.Update(CurrentAddr, Comment ?? string.Empty);
            }
        }

        /// <summary>
        /// Expand the magic-effect pointer table (table-1) AND the CSA spell
        /// table (table-2) to the fixed <see cref="MagicListExpandCore.NewCount"/>
        /// (254) rows via the all-reference path
        /// (<see cref="MagicListExpandCore.ExpandMagicLists"/> →
        /// <see cref="DataExpansionCore.ExpandTableTo"/> +
        /// <see cref="DataExpansionCore.RepointAllReferences"/>). Mirrors WF
        /// <c>ImageMagicFEditorForm.MagicListExpandsButton_Click</c> (#837).
        ///
        /// <para>The CSA spell-table-pointer discovery + NOT_FOUND clean-abort
        /// runs FIRST inside the Core helper (before the table-1 expand), so a
        /// ROM without the CSA table is rejected with ZERO mutation.</para>
        ///
        /// <para>Table-1 current count = <see cref="ImageUtilMagicCore.GetSpellDataCount"/>
        /// (WF <c>ImageUtilMagicFEditor.SpellDataCount()</c>); table-2 current
        /// count = the displayed list row count (WF <c>InputFormRef.DataCount</c>).
        /// Refreshes patch state first so both counts reflect the current ROM.</para>
        /// </summary>
        /// <param name="undo">Active undo buffer (from the caller's
        /// <c>UndoService.GetActiveUndoData()</c>) so the all-reference repoint
        /// records into the same transaction.</param>
        /// <returns>Empty string on success, an error message otherwise (no
        /// mutation past the failure point; none at all on CSA NOT_FOUND).</returns>
        public string ExpandMagicLists(Undo.UndoData undo)
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return R._("ROM not loaded.");

            // Ensure the spell-data count + CSA-table state reflect the ROM.
            RefreshPatchState();

            // table-1 current count = magic-effect spell-data count.
            uint magicEffectCurrentCount = ImageUtilMagicCore.GetSpellDataCount(rom);
            // table-2 current count = the displayed list row count (WF
            // InputFormRef.DataCount). LoadList walks the CSA spell table with
            // the same predicate the editor displays.
            uint csaCurrentCount = (uint)LoadList().Count;

            var result = MagicListExpandCore.ExpandMagicLists(
                rom, magicEffectCurrentCount, csaCurrentCount, undo);
            if (!result.Success)
                return result.Error ?? R._("Table expansion failed.");

            // NOTE B: refresh the cached spell-data count from the grown table.
            // GetSpellDataCount stops at the 0xFFFFFFFF terminator ExpandTableTo
            // wrote (counting the zero-filled NULL rows via isPointerOrNULL), so
            // it reports the grown count — no re-scan undercount.
            _spellDataCount = ImageUtilMagicCore.GetSpellDataCount(rom);
            return "";
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

        // ------------------------------------------------------------------
        // #852 — Magic-effect frame preview + read-only Export PNG.
        // ------------------------------------------------------------------

        /// <summary>
        /// Render the currently-selected magic-effect frame using the live
        /// P0/Frame/P4/P12 values. Mirrors WF
        /// <c>ImageMagicFEditorForm.DrawSelectedAnime()</c>:
        /// <c>ImageUtilMagicFEditor.Draw((uint)ShowFrameUpDown.Value, (uint)P0.Value,
        /// (uint)P4.Value, (uint)P12.Value, out log)</c>.
        ///
        /// <para>Returns <c>null</c> when no magic system is detected, when the
        /// ROM is not loaded, or on any bad pointer / decompression failure.</para>
        /// </summary>
        /// <param name="log">Diagnostic text from the renderer
        ///   (<c>MagicEffectRendererCore.RenderMagicFrame</c>).</param>
        /// <returns>A 240×128 <see cref="IImage"/>, or <c>null</c>.</returns>
        public IImage RenderMagicFramePreview(out string log)
        {
            log = string.Empty;
            ROM rom = CoreState.ROM;
            if (rom == null)
            {
                log = "ROM not loaded.";
                return null;
            }
            if (!_magicSystemDetected)
            {
                log = "No magic system patch detected.";
                return null;
            }
            return MagicEffectRendererCore.RenderMagicFrame(
                rom, _p0, _frame, _p4, _p12, out log);
        }

        /// <summary>
        /// True when <see cref="RenderMagicFramePreview"/> can produce a
        /// non-null image (i.e. the magic system is detected and the entry
        /// is loaded). Used to gate the Export PNG button.
        /// </summary>
        public bool CanExportMagicFrame => _magicSystemDetected && _isLoaded;

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
            // CurrentAddr is the CSA spell-table entry address (mirrors
            // WF AddrResult.addr) — the 20-byte struct holds P0..P16
            // as u32 fields at offsets 0/4/8/12/16. The completeness
            // cross-check uses these offsets directly. (PointerSlotAddr
            // separately holds the WF AddrResult.tag value.)
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
            // DimPointer lives in the pointer-table slot (PointerSlotAddr),
            // not at CurrentAddr+0. Omitting it from the offset map prevents
            // the field-completeness scanner from cross-checking it against
            // u32@0 of CurrentAddr (which is the CSA entry's P0 field).
            ["P0"] = "u32@0",
            ["P4"] = "u32@4",
            ["P8"] = "u32@8",
            ["P12"] = "u32@12",
            ["P16"] = "u32@16",
        };
    }
}
