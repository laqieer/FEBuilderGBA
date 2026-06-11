// SPDX-License-Identifier: GPL-3.0-or-later
// #1027 — Definitive Text Editor free-area (unreferenced text) scan.
//
// Faithful port of WinForms TextForm.SearcFreeArea_Click (TextForm.cs:3608):
//   1. union = AsmMapFileAsmCache.GetVarsIDArray()  (cached U.MakeVarsIDArray)
//   2. UseTextIDCache.AppendList(union)
//   3. textmap = UseValsID.ConvertMaps(union)        (raw-id mask, TargetType-blind)
//   4. for each text slot id: textmap.ContainsKey(id) ? referenced : FREE (if decoded
//      text non-empty).
//
// This Core seam builds the SAME definitive used set via
// MakeVarsIDArrayCore.BuildFreeAreaUsedSet(rom, cache) and returns the complement —
// text ids in the ROM text table whose decoded text is non-empty and NOT in the used
// set. UNLIKE the old TextViewerViewModel heuristic, the union now folds in EventCond
// scripts, menu / status-rmenu chains, worldmap events, installed-patch refs, asmmap
// symbol refs, and the cache ids — so the result is DEFINITIVE when the event-scan
// prerequisites are met.
//
// SCANNER-PREREQUISITES GUARD: the event-script collectors dereference the static
// CoreState.EventScript / CommentCache and require the passed ROM to be the active
// CoreState.ROM. If those aren't wired, the union would be INCOMPLETE — which would
// turn referenced texts into FALSE-POSITIVE "free" results. To prevent that, the
// definitive scan returns Status = PrerequisitesMissing (and an EMPTY list) rather
// than a misleading partial list. The caller surfaces the status instead of a list.
//
// READ-ONLY: never mutates the ROM.

using System.Collections.Generic;

namespace FEBuilderGBA
{
    /// <summary>
    /// Definitive Text Editor free-area (unreferenced text) scan. See file header.
    /// </summary>
    public static class TextFreeAreaCore
    {
        public enum ScanStatus
        {
            /// <summary>Prerequisites met — the result list is definitive.</summary>
            Definitive,
            /// <summary>The event-scan prerequisites (active ROM + EventScript +
            /// CommentCache wired) are NOT met. The union would be incomplete, so
            /// no list is produced (it would contain false positives).</summary>
            PrerequisitesMissing,
        }

        public sealed class FreeAreaResult
        {
            public ScanStatus Status { get; init; }
            /// <summary>Unreferenced text ids (only populated when
            /// <see cref="Status"/> == <see cref="ScanStatus.Definitive"/>).</summary>
            public List<uint> FreeTextIds { get; init; } = new List<uint>();
        }

        /// <summary>
        /// True when the definitive scan's prerequisites are satisfied for
        /// <paramref name="rom"/> (it is the active CoreState.ROM and the
        /// EventScript + CommentCache are wired). Mirrors the gating in
        /// <see cref="EventScriptReferenceScanner.FindAllArgReferences"/>.
        /// </summary>
        public static bool PrerequisitesMet(ROM rom)
        {
            return rom != null
                && CoreState.EventScript != null
                && CoreState.ROM != null
                && ReferenceEquals(CoreState.ROM, rom)
                && CoreState.CommentCache != null;
        }

        /// <summary>
        /// Find every unreferenced text id (the free area). Returns a result whose
        /// <see cref="FreeAreaResult.Status"/> is <see cref="ScanStatus.Definitive"/>
        /// with the populated id list, OR <see cref="ScanStatus.PrerequisitesMissing"/>
        /// with an empty list when the scan cannot be definitive (so callers never
        /// surface a false-positive list). The <paramref name="cache"/> supplies the
        /// user/system/FE8-reserved ids (WF <c>UseTextIDCache.AppendList</c>); pass
        /// <see cref="CoreState.UseTextIDCache"/>.
        /// </summary>
        public static FreeAreaResult FindUnreferencedTextIds(ROM rom, ITextIDCache cache)
        {
            if (rom?.RomInfo == null || rom.Data == null)
                return new FreeAreaResult { Status = ScanStatus.PrerequisitesMissing };

            if (!PrerequisitesMet(rom))
                return new FreeAreaResult { Status = ScanStatus.PrerequisitesMissing };

            var used = MakeVarsIDArrayCore.BuildFreeAreaUsedSet(rom, cache);

            uint textBase = ResolveTextTableBase(rom);
            if (textBase == 0)
                return new FreeAreaResult { Status = ScanStatus.Definitive };

            var free = new List<uint>();
            for (uint id = 0; id < 0x2000u; id++)
            {
                uint entryAddr = textBase + id * 4u;
                if (entryAddr + 4 > (uint)rom.Data.Length) break;
                uint textPtr = rom.u32(entryAddr);
                if (!IsValidTextPointer(textPtr)) break;

                if (id == 0) continue;            // system write-protect slot
                if (used.Contains(id)) continue;  // referenced (ConvertMaps hit)

                string decoded;
                try { decoded = FETextDecode.Direct(id) ?? ""; }
                catch { continue; }
                if (string.IsNullOrWhiteSpace(decoded)) continue;

                free.Add(id);
            }

            return new FreeAreaResult { Status = ScanStatus.Definitive, FreeTextIds = free };
        }

        /// <summary>
        /// Resolve the text-pointer table base (mirrors WinForms
        /// <c>TextForm.Init()</c> + recovery fallback).
        /// </summary>
        public static uint ResolveTextTableBase(ROM rom)
        {
            if (rom?.RomInfo == null || rom.Data == null) return 0;
            uint ptr = rom.RomInfo.text_pointer;
            if (ptr == 0) return 0;
            if (ptr + 4 > (uint)rom.Data.Length) return 0;
            uint baseAddr = rom.p32(ptr);
            if (U.isSafetyOffset(baseAddr, rom)) return baseAddr;
            uint recover = rom.RomInfo.text_recover_address;
            if (recover != 0 && U.isSafetyOffset(recover, rom)) return recover;
            return 0;
        }

        // Mirror of the Avalonia VM IsValidTextPointer (the text-table terminator
        // predicate — WF InputFormRef.MakeList stops at the first non-pointer slot).
        static bool IsValidTextPointer(uint p)
        {
            if (U.isPointerOrNULL(p)) return true;
            if (FETextEncode.IsUnHuffmanPatchPointer(p)) return true;
            if (U.is_03RAMPointer(p) || FETextEncode.IsUnHuffmanPatch_IW_RAMPointer(p)) return true;
            if (U.is_02RAMPointer(p) || FETextEncode.IsUnHuffmanPatch_EW_RAMPointer(p)) return true;
            return false;
        }
    }
}
