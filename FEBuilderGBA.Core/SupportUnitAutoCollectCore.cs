// SPDX-License-Identifier: GPL-3.0-or-later
// Reciprocal-support init/growth mirroring for the Support Unit Editor (#1455).
//
// Ports WinForms SupportUnitForm.AutoCollect / AutoCollectByTargetSupport
// (FEBuilderGBA/SupportUnitForm.cs) into a cross-platform Core helper so the
// Avalonia Support Unit Editor can keep both sides of a support pair in sync on
// Write (matching the FELint SUPPORT_UNIT reciprocity check).
//
// WinForms behavior (ground truth):
//   - When the AutoCollect checkbox is on, editing a unit's per-partner initial
//     value / growth rate must also write the SAME init (+SUPPORT_LIMIT) and
//     growth (+2*SUPPORT_LIMIT) into that partner's *reciprocal* support row
//     slot that points back at this unit. Only an EXISTING reciprocal slot is
//     updated (no row is added). B21 (partner count) is recomputed from the
//     non-zero partner slots of the current row.
//
// All writes go through rom.write_u8, so when the caller has an ambient undo
// scope open (ROM.BeginUndoScope, as the Avalonia View does) every byte touched
// here is recorded as part of the same undo record. ROM-MUTATING but UI-free.
using System;

namespace FEBuilderGBA
{
    /// <summary>
    /// Pure-of-UI reciprocal-support mirroring helper that mirrors WinForms
    /// <c>SupportUnitForm.AutoCollect</c>. FE7/FE8 (24-byte support struct);
    /// FE6's support data differs and is out of scope (matching #1455).
    /// </summary>
    public static class SupportUnitAutoCollectCore
    {
        /// <summary>Number of partner slots in a support row (same as WinForms <c>SUPPORT_LIMIT</c>).</summary>
        public const uint SUPPORT_LIMIT = 7;

        /// <summary>FE7/FE8 support struct size in bytes.</summary>
        public const uint BLOCK_SIZE = 24;

        /// <summary>
        /// Count the non-zero entries of the 7 partner-id slots. Mirrors the
        /// <c>support_count</c> tally in WinForms <c>AutoCollect</c> that becomes
        /// B21 (partner count). Returns 0 for a null/short array (defensive).
        /// </summary>
        public static uint RecomputePartnerCount(uint[] partners7)
        {
            if (partners7 == null) return 0;
            uint count = 0;
            int n = Math.Min(partners7.Length, (int)SUPPORT_LIMIT);
            for (int i = 0; i < n; i++)
            {
                if (partners7[i] != 0) count++;
            }
            return count;
        }

        /// <summary>
        /// Mirror this support row's per-partner init/growth edits into each
        /// partner's reciprocal slot. <paramref name="addr"/> is the file offset
        /// of the CURRENT row; <paramref name="partners7"/>/<paramref name="inits7"/>/
        /// <paramref name="growths7"/> are its 7 partner ids, 7 init values, and
        /// 7 growth rates. No-op (never throws) when the row is unowned or any
        /// safety check fails. Writes via <c>rom.write_u8</c> so an active
        /// ambient undo scope records every change.
        /// </summary>
        /// <returns>The number of reciprocal slot pairs written.</returns>
        public static int AutoCollect(ROM rom, uint addr, uint[] partners7, uint[] inits7, uint[] growths7)
        {
            if (rom?.RomInfo == null) return 0;
            if (partners7 == null || inits7 == null || growths7 == null) return 0;

            // Resolve the owning unit id (1-based) of the current row, mirroring
            // WinForms `GetUnitIDWhereSupportAddr(addr) + 1`. Unowned => no-op.
            uint? ownerZeroBased = SupportUnitNavigation.GetUnitIdAtSupportAddr(rom, addr);
            if (ownerZeroBased == null) return 0;
            uint uid = ownerZeroBased.Value + 1;

            int written = 0;
            int n = (int)SUPPORT_LIMIT;
            n = Math.Min(n, Math.Min(partners7.Length, Math.Min(inits7.Length, growths7.Length)));
            for (int i = 0; i < n; i++)
            {
                if (AutoCollectByTargetSupport(rom, uid, partners7[i], inits7[i], growths7[i]))
                {
                    written++;
                }
            }
            return written;
        }

        /// <summary>
        /// Port of WinForms <c>AutoCollectByTargetSupport</c>: locate the partner
        /// (<paramref name="targetUid"/>, 1-based) support row, scan its 7 partner
        /// slots for the one equal to <paramref name="uid"/>, and write
        /// <paramref name="initValue"/> at <c>+SUPPORT_LIMIT</c> and
        /// <paramref name="growthValue"/> at <c>+2*SUPPORT_LIMIT</c>. Full target-row
        /// range is bounds-checked before any scan/write (#1455 review). Returns
        /// true when a reciprocal slot was written.
        /// </summary>
        public static bool AutoCollectByTargetSupport(ROM rom, uint uid, uint targetUid, uint initValue, uint growthValue)
        {
            if (rom == null) return false;
            if (targetUid == 0) return false;

            uint? targetAddrN = SupportUnitNavigation.GetSupportAddrForUnitId(rom, targetUid);
            if (targetAddrN == null) return false;
            uint targetAddr = targetAddrN.Value;

            // Full-row safety. The highest byte we ever touch is the LAST slot's
            // growth byte: targetAddr + (SUPPORT_LIMIT-1) + 2*SUPPORT_LIMIT (the
            // scan reaches slot SUPPORT_LIMIT-1, whose growth is at +2*SUPPORT_LIMIT).
            // Guard that exact max AND the whole FE7/8 row (overflow-safe 64-bit) so
            // a malformed/near-EOF partner pointer can never read or write OOB.
            // (WinForms only guarded the base address — #1455 review finding 2.)
            if (!U.isSafetyOffset(targetAddr + (SUPPORT_LIMIT - 1) + 2 * SUPPORT_LIMIT, rom)) return false;
            if ((ulong)targetAddr + BLOCK_SIZE > (ulong)rom.Data.Length) return false;

            uint limit = targetAddr + SUPPORT_LIMIT;
            for (uint scan = targetAddr; scan < limit; scan++)
            {
                uint slotUid = rom.u8(scan);
                if (slotUid != uid)
                {
                    continue;
                }
                rom.write_u8(scan + SUPPORT_LIMIT, initValue);
                rom.write_u8(scan + SUPPORT_LIMIT + SUPPORT_LIMIT, growthValue);
                return true;
            }
            return false;
        }
    }
}
