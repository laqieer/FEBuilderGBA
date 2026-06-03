// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// ViewModel backing the reusable <see cref="Views.SkillSubListEditorView"/>
    /// (issue #930, #769 bucket 2 slice 2). It edits ONE per-skill null-terminated
    /// array of 1-byte IDs referenced through a 32-bit ROM pointer SLOT (the FE8N
    /// Ver2/Ver3 per-skill sub-list pointers <c>P4/P8/P12/P16/P20</c>), consuming
    /// the merged Core primitive <see cref="NullTerminatedByteListCore"/>.
    ///
    /// <para><b>Single-path design (issue #930 v2):</b> every mutation
    /// (Add/Remove/Set-ID) builds the desired id list from <see cref="Entries"/>
    /// and persists it via <see cref="NullTerminatedByteListCore.WriteByteList"/> —
    /// there is NO <c>ExpandByteList</c> and NO in-place <c>WriteByte</c>.
    /// <c>WriteByteList</c> forks shared arrays (leaves the original bytes intact),
    /// so a co-owning skill is never corrupted, and it fresh-allocates when the
    /// slot is NULL (0). The base is never cached across ops: every mutation is
    /// slot-based and the View re-runs <see cref="Load"/> (re-derefs the slot)
    /// afterward.</para>
    ///
    /// <para>The name decoration per entry is supplied by an INJECTED
    /// <c>Func&lt;uint,string&gt;</c> resolver (B1): Ver2/Ver3 Unit/Class/Item tabs
    /// pass <c>NameResolver.GetUnitName/GetClassName/GetItemName</c> (0-based raw
    /// byte ids); the Ver3 Composite tab passes the Ver3 VM's own
    /// <c>ResolveSkillName</c> (the FE8N main-list 『...』 skill text) — never the
    /// global <c>NameResolver.GetSkillName</c>.</para>
    /// </summary>
    public partial class SkillSubListEditorViewModel : ViewModelBase
    {
        uint _pointerSlotAddr;
        Func<uint, string>? _nameResolver;
        bool _canEdit;
        int _selectedIndex = -1;
        uint _editId;
        string _baseDisplay = "Sub-list base: -";
        string _countDisplay = "Entry count: -";

        /// <summary>
        /// The 32-bit pointer SLOT address (e.g. <c>skillRowAddr + 4</c>) whose
        /// u32 references the null-terminated id array. 0 until <see cref="Load"/>.
        /// </summary>
        public uint PointerSlotAddr { get => _pointerSlotAddr; set => SetField(ref _pointerSlotAddr, value); }

        /// <summary>
        /// True iff this editor is allowed to mutate the ROM. Set by
        /// <see cref="Load"/> from the caller-supplied <c>canEdit</c> flag AND a
        /// live ROM AND an in-bounds slot. The View early-returns from every
        /// op handler when this is false (e.g. the Item2 tab when stride &lt; 20).
        /// </summary>
        public bool CanEdit { get => _canEdit; set => SetField(ref _canEdit, value); }

        /// <summary>The displayed id entries (id + decorated name).</summary>
        public ObservableCollection<SubListEntryVM> Entries { get; } = new();

        public int SelectedIndex { get => _selectedIndex; set => SetField(ref _selectedIndex, value); }

        /// <summary>The 0..255 id typed into the "Set ID" NumericUpDown.</summary>
        public uint EditId { get => _editId; set => SetField(ref _editId, value); }

        public string BaseDisplay { get => _baseDisplay; set => SetField(ref _baseDisplay, value); }
        public string CountDisplay { get => _countDisplay; set => SetField(ref _countDisplay, value); }

        /// <summary>
        /// Load the entries from the array referenced by
        /// <paramref name="pointerSlotAddr"/>, decorating each id via
        /// <paramref name="nameResolver"/>. <paramref name="canEdit"/> gates all
        /// mutation (e.g. the Ver2 Item2 tab passes <c>HasItem2</c>).
        /// </summary>
        /// <remarks>
        /// Deref guard (issue #930 minor): the raw slot u32 is checked with
        /// <see cref="U.isSafetyPointer(uint)"/> exactly like the host VM's
        /// <c>ReadPointerOffset</c>. A 0 slot is the legit EMPTY case (Add
        /// allocates a fresh list); a NON-zero garbage Px renders an EMPTY list
        /// rather than scanning junk bytes.
        /// </remarks>
        public void Load(uint pointerSlotAddr, Func<uint, string> nameResolver, bool canEdit)
        {
            PointerSlotAddr = pointerSlotAddr;
            _nameResolver = nameResolver;

            ROM rom = CoreState.ROM;
            // CanEdit requires a live ROM and an in-bounds 4-byte slot.
            bool slotInBounds = rom?.Data != null
                && pointerSlotAddr != 0
                && (ulong)pointerSlotAddr + 4 <= (ulong)rom.Data.Length;
            CanEdit = canEdit && slotInBounds;

            Entries.Clear();
            SelectedIndex = -1;

            if (rom?.Data == null || !slotInBounds)
            {
                BaseDisplay = "Sub-list base: -";
                CountDisplay = "Entry count: -";
                return;
            }

            // Read the RAW slot u32 (a GBA pointer) and guard it. p32 would
            // return an offset; we mirror the host VM's ReadPointerOffset which
            // reads u32 + isSafetyPointer + toOffset.
            uint raw = rom.u32(pointerSlotAddr);
            if (raw == 0)
            {
                // Legit empty list: the slot is unset. Add allocates fresh.
                BaseDisplay = "Sub-list base: (none)";
                CountDisplay = "Entry count: 0";
                return;
            }
            if (!U.isSafetyPointer(raw, rom))
            {
                // Non-zero garbage pointer: render empty rather than junk ids.
                BaseDisplay = $"Sub-list base: 0x{raw:X08} (invalid)";
                CountDisplay = "Entry count: 0";
                return;
            }

            uint baseOffset = U.toOffset(raw);
            var ids = NullTerminatedByteListCore.ScanByteList(rom, baseOffset);
            foreach (uint id in ids)
            {
                Entries.Add(new SubListEntryVM(id, Decorate(id)));
            }

            BaseDisplay = $"Sub-list base: 0x{baseOffset:X08}";
            CountDisplay = $"Entry count: {Entries.Count}";
        }

        string Decorate(uint id)
        {
            string name = "";
            if (_nameResolver != null)
            {
                try { name = _nameResolver(id) ?? ""; } catch { name = ""; }
            }
            return name.Length > 0 ? $"0x{id:X2} {name}" : $"0x{id:X2}";
        }

        /// <summary>The current id list as derived from <see cref="Entries"/>.</summary>
        IReadOnlyList<uint> CurrentIds() => Entries.Select(e => e.Id).ToList();

        /// <summary>
        /// Persist <paramref name="ids"/> as the FULL list via the Core single
        /// path. Returns the new array base offset.
        /// </summary>
        public uint ApplyList(IReadOnlyList<uint> ids, Undo.UndoData undo)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) throw new InvalidOperationException("No ROM loaded.");
            return NullTerminatedByteListCore.WriteByteList(rom, PointerSlotAddr, ids, undo);
        }

        /// <summary>Append one placeholder (0x01) entry to the list.</summary>
        public void AddEntry(Undo.UndoData undo)
        {
            var ids = CurrentIds().ToList();
            ids.Add(NullTerminatedByteListCore.NewSlotPlaceholder);
            ApplyList(ids, undo);
        }

        /// <summary>Remove the currently selected entry (no-op if none).</summary>
        public void RemoveSelected(Undo.UndoData undo)
        {
            int i = SelectedIndex;
            if (i < 0 || i >= Entries.Count) return;
            var ids = CurrentIds().ToList();
            ids.RemoveAt(i);
            ApplyList(ids, undo);
        }

        /// <summary>
        /// Set the selected entry's id to <see cref="EditId"/> (masked to a byte)
        /// via a whole-list rewrite (fork-on-write; no in-place WriteByte).
        /// No-op if no entry is selected.
        /// </summary>
        public void SetSelectedId(Undo.UndoData undo)
        {
            int i = SelectedIndex;
            if (i < 0 || i >= Entries.Count) return;
            var ids = CurrentIds().ToList();
            ids[i] = EditId & 0xFFu;
            ApplyList(ids, undo);
        }
    }

    /// <summary>One row in the sub-list editor: an id + its decorated display.</summary>
    public sealed class SubListEntryVM
    {
        public SubListEntryVM(uint id, string display)
        {
            Id = id;
            Display = display;
        }

        public uint Id { get; }
        public string Display { get; }
    }
}
