// SPDX-License-Identifier: GPL-3.0-or-later
// #837 — shared Core helper for the Magic FEditor + CSA Creator
// "List Expansion" buttons. Both WF forms
// (ImageMagicFEditorForm.MagicListExpandsButton_Click :579 and
// ImageMagicCSACreatorForm.MagicListExpandsButton_Click :568) are
// byte-identical, so the expand mechanism is factored here once and
// consumed by both Avalonia ViewModels
// (ImageMagicFEditorViewModel.ExpandMagicLists /
//  ImageMagicCSACreatorViewModel.ExpandMagicLists).
//
// Mechanism (verified against WF, plan v2 APPROVED on #837): each click
// expands TWO pointer-based tables, BOTH via the all-reference path
// (DataExpansionCore.ExpandTableTo + RepointAllReferences) that mirrors
// WF InputFormRef.ExpandsArea -> MoveToFreeSapceForm.SearchPointer
// (raw 32-bit pointers + ARM-Thumb LDR literal-pool loads). A single-slot
// write here would leave dangling LDR refs from the FEditor/CSA patch ASM
// -> ROM corruption, so RepointAllReferences is mandatory.
//
//   table-1: magic-effect pointer table  (RomInfo.magic_effect_pointer,
//            entrySize 4, expanded UNCONDITIONALLY — WF :597-599)
//   table-2: CSA spell table             (at the CSA pointer slot,
//            entrySize 20, expanded CONDITIONALLY-sized — WF :601-610)
//
// Both grow to a FIXED newCount = 254 (WF passes 254 to ExpandsArea).
//
// Ordering is LOAD-BEARING: the CSA spell-table-pointer discovery + the
// NOT_FOUND clean-abort MUST run BEFORE the first (table-1) expand. WF
// checks GetCSASpellTablePointer at :589 and ShowStopError + return at
// :590-594 BEFORE the magic-effect expand at :599. If a failed CSA
// discovery were allowed to run after table-1 had already been mutated,
// a table-1 write would leak on a ROM where table-2 can't be located.
using System;

namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform "Magic List Expansion" operation shared by the
    /// Avalonia <c>ImageMagicFEditorViewModel</c> and
    /// <c>ImageMagicCSACreatorViewModel</c>. Mirrors the byte-identical WF
    /// <c>MagicListExpandsButton_Click</c> handlers in both
    /// <c>ImageMagicFEditorForm</c> and <c>ImageMagicCSACreatorForm</c>.
    /// </summary>
    public static class MagicListExpandCore
    {
        /// <summary>Fixed target row count both tables grow to (WF passes
        /// <c>254</c> to <c>InputFormRef.ExpandsArea</c>).</summary>
        public const uint NewCount = 254;

        /// <summary>Entry stride of the magic-effect pointer table (table-1).</summary>
        public const uint MagicEffectEntrySize = 4;

        /// <summary>Entry stride of the CSA spell table (table-2): 5 x 4-byte
        /// pointers = 20 bytes.</summary>
        public const uint CsaEntrySize = 5 * 4;

        /// <summary>Outcome of <see cref="ExpandMagicLists"/>.</summary>
        public sealed class Result
        {
            /// <summary>Whether the operation succeeded.</summary>
            public bool Success { get; set; }

            /// <summary>Human-readable error when <see cref="Success"/> is false.
            /// Empty on success.</summary>
            public string Error { get; set; } = "";

            /// <summary>New base offset of the magic-effect table (table-1) on
            /// success.</summary>
            public uint MagicEffectNewBase { get; set; }

            /// <summary>New base offset of the CSA spell table (table-2) on
            /// success.</summary>
            public uint CsaNewBase { get; set; }

            /// <summary>New row count both tables were grown to (= <see cref="NewCount"/>).</summary>
            public uint ResultCount { get; set; }
        }

        /// <summary>
        /// Expand the magic-effect pointer table (table-1) AND the CSA spell
        /// table (table-2) to <see cref="NewCount"/> rows, repointing EVERY
        /// reference (canonical pointer + raw pointers + ARM-Thumb LDR
        /// literal-pool loads) to each moved base.
        ///
        /// <para><b>Ordering (load-bearing):</b> the CSA spell-table-pointer
        /// discovery + NOT_FOUND clean-abort runs FIRST, before the table-1
        /// expand — exactly mirroring WF
        /// <c>ImageMagicFEditorForm.cs:589-594</c> (the
        /// <c>ShowStopError</c> + <c>return</c>) preceding the magic-effect
        /// expand at <c>:599</c>. On a NOT_FOUND CSA pointer this method
        /// returns an error with <b>ZERO ROM mutation</b> — no
        /// <c>ExpandTableTo</c>, no <c>RepointAllReferences</c>, nothing
        /// written through <paramref name="undo"/>.</para>
        ///
        /// <para><b>NOTE A:</b> <see cref="DataExpansionCore.RepointAllReferences"/>
        /// returning <c>0</c> is SUCCESS here — <c>ExpandTableTo</c> already
        /// repointed the canonical pointer, so a clean ROM with no secondary
        /// references legitimately has zero further slots to rewrite. Never
        /// roll back on a <c>0</c> return.</para>
        ///
        /// <para><b>NOTE B (caller):</b> the result reports
        /// <see cref="Result.ResultCount"/> = <see cref="NewCount"/>; callers
        /// must drive the post-expand render from that count (or rescan with a
        /// terminator-honouring predicate), NOT a <c>!isPointer</c> re-scan
        /// that would stop at the first zero-filled new row. The magic
        /// editors' own <c>GetSpellDataCount</c> scan uses
        /// <c>isPointerOrNULL</c> and stops at the <c>0xFFFFFFFF</c>
        /// terminator <c>ExpandTableTo</c> wrote, so re-running their
        /// <c>LoadList</c> reports the grown count correctly.</para>
        ///
        /// <para><b>Inherited KnownGaps:</b> (1) <c>ExpandTableTo</c>'s
        /// comment/lint cache repoint is forward-only — ROM undo restores
        /// bytes but does NOT reverse the cache repoint (accepted WF parity).
        /// (2) <c>RepointAllReferences</c> omits the event-aware
        /// <c>GrepPointerAllOnEvent</c> pass + <c>IsFixedASM</c> guard that WF
        /// <c>SearchPointer</c> includes (#781 limitation) — acceptable here
        /// because these are ASM-referenced graphics/animation tables, not
        /// event-script data.</para>
        /// </summary>
        /// <param name="rom">ROM to modify.</param>
        /// <param name="magicEffectCurrentCount">Current row count of the
        /// magic-effect pointer table (WF <c>ImageUtilMagicFEditor.SpellDataCount()</c>).</param>
        /// <param name="csaCurrentCount">Current row count of the CSA spell
        /// table (WF <c>InputFormRef.DataCount</c> when the CSA pointer is a
        /// safety offset, else 0).</param>
        /// <param name="undo">The active undo transaction. NOTE: this method is
        /// always invoked INSIDE the caller's
        /// <c>ROM.BeginUndoScope(undo)</c> (the View's
        /// <c>UndoService.Begin</c> / the test's <c>using</c>), so BOTH
        /// <c>ExpandTableTo</c> and the two <c>RepointAllReferences</c> passes
        /// record into that single ambient scope. It is deliberately NOT
        /// re-passed to <see cref="DataExpansionCore.RepointAllReferences"/>
        /// (we pass <c>null</c> there) because <c>BeginUndoScope</c> is
        /// non-reentrant — its <c>UndoScope.Dispose()</c> clears the thread's
        /// ambient scope unconditionally, so letting the FIRST
        /// RepointAllReferences open+close its own scope would silently stop
        /// the SECOND table's writes from being recorded. The parameter is
        /// retained for API symmetry with the single-table NV1a callers and so
        /// the caller can assert against the same transaction.</param>
        /// <returns>A <see cref="Result"/>. On failure no further mutation
        /// occurs after the point of failure (and on CSA NOT_FOUND, none at all).</returns>
        public static Result ExpandMagicLists(
            ROM rom,
            uint magicEffectCurrentCount,
            uint csaCurrentCount,
            Undo.UndoData undo)
        {
            if (rom == null || rom.RomInfo == null)
                return Fail(R._("ROM not loaded."));

            // --- Step 1: CSA discovery FIRST (load-bearing ordering) ---------
            // Mirrors WF ImageMagicFEditorForm.cs:589 GetCSASpellTablePointer()
            // + :590-594 ShowStopError + return, BEFORE the :599 table-1 expand.
            // A NOT_FOUND CSA pointer aborts with ZERO mutation.
            uint csaSpellTablePointer = MagicCSACore.GetCSASpellTablePointer(rom);
            if (csaSpellTablePointer == U.NOT_FOUND)
                return Fail(R._("CSASpellTable Not Found."));

            // --- Step 2: guard the fixed newCount against table-1's count ----
            // WF InputFormRef.ExpandsArea asserts/NOT_FOUNDs when newCount is
            // not greater than the current count. Both tables share the fixed
            // 254 target; gate the (larger) magic-effect count here. ExpandTableTo
            // independently rejects newCount < currentCount for table-2.
            if (NewCount <= magicEffectCurrentCount)
                return Fail(R._("New count ({0}) must be greater than the current count ({1}).",
                    NewCount, magicEffectCurrentCount));

            // --- Step 3: table-1 — magic-effect pointer table (entrySize 4) --
            // Expanded UNCONDITIONALLY (WF :597-599). ExpandTableTo moves +
            // copies + writes the 0xFFFFFFFF terminator + wipes the old region
            // + single-slot-repoints the canonical pointer; RepointAllReferences
            // then repoints every OTHER raw/LDR reference to the old base.
            uint magicEffectPtr = rom.RomInfo.magic_effect_pointer;
            uint magicEffectOldBase = rom.p32(magicEffectPtr);
            var r1 = DataExpansionCore.ExpandTableTo(
                rom, magicEffectPtr, MagicEffectEntrySize, magicEffectCurrentCount, NewCount);
            if (!r1.Success)
                return Fail(r1.Error ?? R._("Table expansion failed."));
            // NOTE A: 0 is success — do NOT roll back on 0. Pass null undo so
            // RepointAllReferences records into the caller's already-open ambient
            // scope WITHOUT opening+closing its own (BeginUndoScope is
            // non-reentrant; a nested close would drop table-2's writes).
            DataExpansionCore.RepointAllReferences(rom, magicEffectOldBase, r1.NewBaseAddress, null);

            // --- Step 4: table-2 — CSA spell table (entrySize 20) ------------
            // Expanded CONDITIONALLY-sized (WF :601-610: datasize =
            // InputFormRef.DataCount when csaSpellTablePointer is a safety
            // offset, else 0). The pointer slot is unaffected by the table-1
            // move, so re-reading the CSA base here is valid.
            uint csaSizeCurrent = (U.isSafetyOffset(csaSpellTablePointer, rom))
                ? csaCurrentCount
                : 0u;
            uint csaOldBase = rom.p32(csaSpellTablePointer);
            var r2 = DataExpansionCore.ExpandTableTo(
                rom, csaSpellTablePointer, CsaEntrySize, csaSizeCurrent, NewCount);
            if (!r2.Success)
                return Fail(r2.Error ?? R._("Table expansion failed."));
            // NOTE A: 0 is success. Null undo — same ambient-scope reasoning as
            // table-1 above.
            DataExpansionCore.RepointAllReferences(rom, csaOldBase, r2.NewBaseAddress, null);

            return new Result
            {
                Success = true,
                Error = "",
                MagicEffectNewBase = r1.NewBaseAddress,
                CsaNewBase = r2.NewBaseAddress,
                ResultCount = NewCount,
            };
        }

        static Result Fail(string error) => new Result { Success = false, Error = error };
    }
}
