// SPDX-License-Identifier: GPL-3.0-or-later
// Cross-platform helpers for the CSA Magic Creator editor (CSA-specific
// scan + per-row layout). The signature tables and detection logic live
// in `ImageUtilMagicCore` (used by both `ImageMagicFEditor` #418 and
// this view #417); `MagicCSACore` delegates to that shared helper and
// layers only the CSA-Creator-specific bits on top:
//
//   - MagicSystemKind:   thin alias of ImageUtilMagicCore.MagicSystem
//                        (kept for backwards compat with the Avalonia
//                        view and tests authored before the delegate
//                        refactor).
//   - SearchMagicSystem: wraps ImageUtilMagicCore.SearchMagicSystem +
//                        FindCSASpellTable into a single call that
//                        ALSO surfaces the CSA-table address and
//                        pointer slot (the WF "magic_effect_pointer"
//                        slot the dim/no-dim/empty mode write lands at).
//   - ComputeSpellDataCount: re-exports ImageUtilMagicCore.GetSpellDataCount
//                            under the older name the Avalonia view uses.
//   - CsaEntry + ScanCsaEntries: the CSA-only spell-table walk that
//                                does not exist anywhere else.
//
// Closes Copilot CLI inline review thread on PR #547 (line-316 bounds
// check + line-31 duplication) by routing through the shared helper.
using System;
using System.Collections.Generic;

namespace FEBuilderGBA
{
    /// <summary>
    /// Magic engine kind detected by <see cref="MagicCSACore.SearchMagicSystem"/>.
    /// Thin alias of <see cref="ImageUtilMagicCore.MagicSystem"/> kept for
    /// API compatibility with code that was authored before the helper
    /// was consolidated into <c>ImageUtilMagicCore</c> (the Avalonia
    /// <c>ImageMagicCSACreatorViewModel</c> and the matching tests).
    /// </summary>
    public enum MagicSystemKind
    {
        None = 0,
        FEditor = 1,
        CsaCreator = 2,
    }

    /// <summary>One row of the CSA spell table.</summary>
    public sealed class CsaEntry
    {
        /// <summary>Address of the CSA struct (the row that owns P0..P16).</summary>
        public uint Addr { get; set; }

        /// <summary>Address of the pointer-table slot (where the dim/no-dim/empty pointer lives).</summary>
        public uint TagAddr { get; set; }

        /// <summary>
        /// Display name. The Core helper emits the hex id (e.g.
        /// <c>"0x49"</c>) plus an <c>" EMPTY"</c> suffix for post-original
        /// data=0 slots. The richer WF <c>"0xNN effectName"</c> format
        /// (which resolves the effect name from the <c>item_anime_effect_</c>
        /// dictionary or <c>CommentCache</c>) requires a name resolver that
        /// is callable from Core; that hook is out of scope here and a
        /// follow-up for #500 (tracked as a KnownGap on the Avalonia view).
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>The data pointer stored at <see cref="TagAddr"/> (raw GBA pointer or 0).</summary>
        public uint DataAddr { get; set; }

        /// <summary>True if the slot is an extension EMPTY (after the original magic count).</summary>
        public bool IsEmpty { get; set; }
    }

    /// <summary>
    /// Stateless read-only helpers for the CSA Magic Creator detection
    /// + spell-table walk. The detection tables live in
    /// <see cref="ImageUtilMagicCore"/>; this class is a thin layer that
    /// exposes the extra <c>csaSpellTable</c>/<c>csaSpellTablePointer</c>
    /// outputs the WF CSA editor needs PLUS the CSA-specific entry walk.
    /// </summary>
    public static class MagicCSACore
    {
        // ---- mapping helpers ----

        /// <summary>
        /// Map the shared <see cref="ImageUtilMagicCore.MagicSystem"/>
        /// enum onto the CSA-creator-facing <see cref="MagicSystemKind"/>
        /// alias. <c>FEditorAdv</c> -> <c>FEditor</c>; <c>CsaCreator</c>
        /// passes through; <c>No</c> -> <c>None</c>.
        /// </summary>
        static MagicSystemKind Map(ImageUtilMagicCore.MagicSystem ms)
        {
            switch (ms)
            {
                case ImageUtilMagicCore.MagicSystem.FEditorAdv: return MagicSystemKind.FEditor;
                case ImageUtilMagicCore.MagicSystem.CsaCreator: return MagicSystemKind.CsaCreator;
                default: return MagicSystemKind.None;
            }
        }

        // ---- public API ----

        /// <summary>
        /// Detect the magic engine kind installed in <paramref name="rom"/>.
        /// Returns <see cref="MagicSystemKind.None"/> when no signature
        /// matches; in that case all <c>out</c> parameters are set to
        /// <see cref="U.NOT_FOUND"/>. The returned <c>csaSpellTable</c> and
        /// <c>csaSpellTablePointer</c> reflect the CSA spell-table location
        /// (the table itself, and the pointer slot containing the table addr).
        ///
        /// <para>
        /// Delegates to <see cref="ImageUtilMagicCore.SearchMagicSystem"/>
        /// (engine detection) and <see cref="ImageUtilMagicCore.FindCSASpellTable"/>
        /// (table location + bounds-checked p32 read) to avoid keeping a
        /// duplicate signature table in the Core helper.
        /// </para>
        /// </summary>
        public static MagicSystemKind SearchMagicSystem(
            ROM rom,
            out uint baseAddr,
            out uint dimAddr,
            out uint noDimAddr,
            out uint csaSpellTable,
            out uint csaSpellTablePointer)
        {
            baseAddr = U.NOT_FOUND;
            dimAddr = U.NOT_FOUND;
            noDimAddr = U.NOT_FOUND;
            csaSpellTable = U.NOT_FOUND;
            csaSpellTablePointer = U.NOT_FOUND;

            var detected = ImageUtilMagicCore.SearchMagicSystem(rom,
                out uint b, out uint d, out uint nd);
            if (detected == ImageUtilMagicCore.MagicSystem.No) return MagicSystemKind.None;

            uint table = ImageUtilMagicCore.FindCSASpellTable(rom, detected, out uint pointer);
            if (pointer == U.NOT_FOUND) return MagicSystemKind.None;

            baseAddr = b;
            dimAddr = d;
            noDimAddr = nd;
            csaSpellTable = table;
            csaSpellTablePointer = pointer;
            return Map(detected);
        }

        /// <summary>
        /// Convenience overload that returns just the spell-table address.
        /// Mirrors WF <c>ImageUtilMagic.GetCSASpellTableAddr</c>.
        /// </summary>
        public static uint GetCSASpellTableAddr(ROM rom)
        {
            SearchMagicSystem(rom, out _, out _, out _, out uint csa, out _);
            return csa;
        }

        /// <summary>
        /// Convenience overload that returns just the spell-table pointer slot.
        /// Mirrors WF <c>ImageUtilMagic.GetCSASpellTablePointer</c>.
        /// </summary>
        public static uint GetCSASpellTablePointer(ROM rom)
        {
            SearchMagicSystem(rom, out _, out _, out _, out _, out uint ptr);
            return ptr;
        }

        /// <summary>
        /// Compute the "spell data count" used by the WF CSA editor to bound
        /// the entry list. Delegates to
        /// <see cref="ImageUtilMagicCore.GetSpellDataCount"/> (the shared
        /// implementation already mirrors WF
        /// <c>ImageUtilMagicFEditor.SpellDataCount</c> and is the same
        /// algorithm).
        /// </summary>
        public static uint ComputeSpellDataCount(ROM rom)
        {
            return ImageUtilMagicCore.GetSpellDataCount(rom);
        }

        /// <summary>
        /// Walk the CSA spell table and yield one <see cref="CsaEntry"/> per
        /// slot. Mirrors the WF <c>InputFormRef.MakeList</c> + reader lambda
        /// inside <c>ImageMagicCSACreatorForm.Init</c>. The detection /
        /// pointer-table location is provided by the caller (typically
        /// from <see cref="SearchMagicSystem"/>); this method only does the
        /// CSA-specific per-row walk.
        /// </summary>
        /// <param name="rom">Target ROM (must not be null).</param>
        /// <param name="kind">Detected magic system kind. Must be
        /// <see cref="MagicSystemKind.CsaCreator"/>; any other value returns
        /// an empty list (FEditor uses a different layout).</param>
        /// <param name="dimAddr">Dim address (from SearchMagicSystem).</param>
        /// <param name="noDimAddr">No-dim address (from SearchMagicSystem).</param>
        /// <param name="csaSpellTable">CSA spell-table address (from SearchMagicSystem).</param>
        /// <param name="spellDataCount">Spell-data count (from
        /// <see cref="ComputeSpellDataCount"/>; mirrors WF
        /// <c>ImageUtilMagicFEditor.SpellDataCount</c>). Pass 0 to fall back
        /// to the legacy <c>0xFE</c> cap.</param>
        /// <param name="maxRows">Hard cap on rows (matches WF <c>0xFE</c>).</param>
        public static List<CsaEntry> ScanCsaEntries(
            ROM rom,
            MagicSystemKind kind,
            uint dimAddr,
            uint noDimAddr,
            uint csaSpellTable,
            uint spellDataCount = 0,
            int maxRows = 0xFE)
        {
            var result = new List<CsaEntry>();
            if (rom == null || rom.RomInfo == null) return result;
            if (kind != MagicSystemKind.CsaCreator) return result;
            if (csaSpellTable == U.NOT_FOUND) return result;

            uint pointerTable = rom.RomInfo.magic_effect_pointer;
            if (pointerTable == 0) return result;
            uint pointerTableAddr = rom.p32(pointerTable);
            if (pointerTableAddr == 0) return result;

            uint originalCount = rom.RomInfo.magic_effect_original_data_count;
            uint romLen = (uint)rom.Data.Length;

            // Effective row cap mirrors WF: spellDataCount AND 0xFE.
            int cap = maxRows;
            if (spellDataCount > 0 && spellDataCount < (uint)cap) cap = (int)spellDataCount;

            for (int i = 0; i < cap; i++)
            {
                uint slotPtr = pointerTableAddr + (uint)(4 * i);
                if (slotPtr + 4 > romLen) break;
                uint dataAddr = rom.p32(slotPtr);

                bool isExtensionEmpty = false;
                if (dataAddr == 0)
                {
                    if (i < originalCount)
                    {
                        // Legacy slot - mirrors WF: skip without adding.
                        continue;
                    }
                    isExtensionEmpty = true;
                }
                else if (dataAddr != dimAddr && dataAddr != noDimAddr)
                {
                    // Not a CSA-creator slot (probably the FEditor format).
                    continue;
                }

                uint csaAddr = csaSpellTable + (uint)(20 * i);
                if (csaAddr + 20 > romLen) break;

                string name = $"{U.ToHexString(i)}{(isExtensionEmpty ? " EMPTY" : "")}";

                result.Add(new CsaEntry
                {
                    Addr = csaAddr,
                    TagAddr = slotPtr,
                    Name = name,
                    DataAddr = dataAddr,
                    IsEmpty = isExtensionEmpty,
                });
            }

            return result;
        }
    }
}
