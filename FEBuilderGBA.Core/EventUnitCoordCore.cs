// SPDX-License-Identifier: GPL-3.0-or-later
// Cross-platform FE8 Event-Unit after-coord (move-path) list seam (#1017).
//
// Ports the read/write halves of WinForms EventUnitForm.MakeUnitCoordList +
// PreWriteHandler so the Avalonia Event Unit editor can edit the FE8
// after-coord move-path list (the WF FE8CoordListBox).
//
// FE8 20-byte unit block field map used here:
//   W4 = +4 (u16)  — the packed START (before) UnitPos (X/Y/Ext)
//   B7 = +7 (u8)   — after-coord record COUNT (u8 cap)
//   D8 = +8 (u32)  — after-coord blob GBA pointer
//
// List shape (the unified Fe8Coord list this seam reads/writes):
//   index 0   = the synthetic START row sourced from W4 (X/Y/Ext only;
//               Unk1=Unk2=0xFF, Speed=UnitId=Wait=0). Persisted back to W4.
//   indices 1+ = the after-coord records, each an 8-byte blob entry at D8:
//                 u16 unitpos @+0 (X/Y/Ext), u8 Speed @+2, u8 UnitId @+3,
//                 u8 Unk1 @+4, u8 Unk2 @+5, u16 Wait @+6.
//
// WRITE atomicity: runs under the CALLER's ambient undo scope. A defensive
// length-aware rom.Data snapshot is restored byte-identical on ANY fault
// (exception OR a free-space NOT_FOUND), mirroring the #885/#923 pattern.
using System;
using System.Collections.Generic;
using FEBuilderGBA; // ROM, U, R live here.

namespace FEBuilderGBA.Core
{
    /// <summary>
    /// One unified FE8 after-coord row. Index 0 is the synthetic START row
    /// (sourced from / persisted to W4); indices 1+ are the 8-byte blob records.
    /// </summary>
    public sealed class Fe8Coord
    {
        public uint X, Y, Ext, Speed, UnitId, Unk1, Unk2, Wait;
    }

    /// <summary>
    /// Cross-platform read/write seam for the FE8 Event-Unit after-coord
    /// (move-path) list (#1017). FE8-only — callers gate on
    /// <c>eventunit_data_size == 20</c>.
    /// </summary>
    public static class EventUnitCoordCore
    {
        // B7 is a u8, so the after-coord record count cannot exceed 255.
        public const int MaxAfterCoordRecords = 255;

        /// <summary>
        /// READ-ONLY. Build the unified after-coord list for a FE8 unit block.
        /// Always returns at least one row (the START row from <paramref name="w4"/>);
        /// indices 1+ come from the <paramref name="b7"/> records of 8 bytes at
        /// <c>toOffset(<paramref name="p8"/>)</c>. A record is dropped (and the
        /// scan stops) when its <c>addr+7</c> would read out of range.
        /// </summary>
        public static List<Fe8Coord> ReadAfterCoords(ROM rom, uint w4, uint b7, uint p8)
        {
            var list = new List<Fe8Coord>();

            // index 0 — synthetic START row from W4. This is a PURE parse of w4
            // (no ROM read), so the start row is present even with a null ROM —
            // honoring the "always returns >=1 row" contract (Copilot bot review
            // on PR #1073). Only the blob records below need the ROM.
            var start = new Fe8Coord
            {
                X = U.ParseFe8UnitPosX(w4),
                Y = U.ParseFe8UnitPosY(w4),
                Ext = U.ParseFe8UnitPosExt(w4),
                Speed = 0,
                UnitId = 0,
                Unk1 = 0xFF,
                Unk2 = 0xFF,
                Wait = 0,
            };
            list.Add(start);

            if (rom == null) return list;

            // indices 1+ — the after-coord blob records (8 bytes each).
            uint count = b7;
            uint addr = U.toOffset(p8);
            for (uint i = 0; i < count; i++, addr += 8)
            {
                // Guard the full 8-byte record (the WF code guards addr+7).
                if (!U.isSafetyOffset(addr + 7, rom))
                    break;

                uint unitpos = rom.u16(addr + 0);
                var rec = new Fe8Coord
                {
                    X = U.ParseFe8UnitPosX(unitpos),
                    Y = U.ParseFe8UnitPosY(unitpos),
                    Ext = U.ParseFe8UnitPosExt(unitpos),
                    Speed = rom.u8(addr + 2),
                    UnitId = rom.u8(addr + 3),
                    Unk1 = rom.u8(addr + 4),
                    Unk2 = rom.u8(addr + 5),
                    Wait = rom.u16(addr + 6),
                };
                list.Add(rec);
            }

            return list;
        }

        /// <summary>
        /// ROM-MUTATING. Persist the unified after-coord list back to the FE8
        /// unit block: <c>list[0]</c> -> W4 (via <see cref="U.MakeFe8UnitPos"/>),
        /// indices 1+ -> the 8-byte blob (in-place when it fits the old blob and
        /// the old pointer is safe, otherwise append to free space + repoint +
        /// recycle the old region). count==0 (only the START row) clears the blob
        /// (P8=0, B7=0, old region 0xFF-filled).
        ///
        /// Runs under the CALLER's ambient undo scope (all writes go through the
        /// rom write methods). Never throws; returns "" on success or a localized
        /// error string. On ANY fault (exception OR a free-space NOT_FOUND) the
        /// ROM is restored byte-identical (length-aware snapshot) so the failure
        /// mutates ZERO bytes.
        /// </summary>
        public static string WriteAfterCoords(ROM rom, uint w4FieldAddr, uint b7FieldAddr, uint p8FieldAddr,
            List<Fe8Coord> list)
        {
            if (rom == null) return R._("ROM is not loaded.");
            if (list == null || list.Count == 0)
                return R._("The after-coord list must contain at least the start row.");

            int count = list.Count - 1; // after-coord records (excludes the START row)
            if (count > MaxAfterCoordRecords)
                return R._("Too many after-coords: {0} (max {1}).", count, MaxAfterCoordRecords);

            // Field-slot bounds — these must be in range for the W4/B7/P8 writes.
            if (w4FieldAddr + 2 > (uint)rom.Data.Length
                || b7FieldAddr + 1 > (uint)rom.Data.Length
                || p8FieldAddr + 4 > (uint)rom.Data.Length)
            {
                return R._("The event-unit block address is out of range.");
            }

            // Capture the OLD blob pointer + count BEFORE any write so the
            // in-place / recycle decisions use the pre-write state.
            uint oldP8 = rom.p32(p8FieldAddr);
            uint oldB7 = rom.u8(b7FieldAddr);
            uint oldBlobOffset = U.toOffset(oldP8);
            uint oldBlobLen = 8 * oldB7;

            // Defensive snapshot for the byte-identical restore on fault. The
            // caller's ambient undo scope captures the writes for UNDO; this
            // snapshot guarantees a FAILED write mutates ZERO bytes.
            byte[] snap = (byte[])rom.Data.Clone();
            try
            {
                // 1. list[0] -> W4 (the synthetic START row).
                Fe8Coord startRow = list[0];
                rom.write_u16(w4FieldAddr, (ushort)U.MakeFe8UnitPos(startRow.X, startRow.Y, startRow.Ext));

                // 2a. count==0 — no after-coords. Clear the old blob and zero P8/B7.
                if (count == 0)
                {
                    if (oldB7 > 0 && U.isSafetyOffset(oldBlobOffset, rom)
                        && oldBlobOffset + oldBlobLen <= (uint)rom.Data.Length)
                    {
                        rom.write_fill(oldBlobOffset, oldBlobLen, 0xFF);
                    }
                    rom.write_p32(p8FieldAddr, 0);
                    rom.write_u8(b7FieldAddr, 0);
                    return "";
                }

                // 2b. Serialize indices 1+ into the 8-byte blob layout.
                byte[] bin = new byte[8 * count];
                uint binOff = 0;
                for (int i = 1; i < list.Count; i++, binOff += 8)
                {
                    Fe8Coord rec = list[i];
                    uint unitpos = U.MakeFe8UnitPos(rec.X, rec.Y, rec.Ext);
                    U.write_u16(bin, binOff + 0, unitpos);
                    U.write_u8(bin, binOff + 2, rec.Speed);
                    U.write_u8(bin, binOff + 3, rec.UnitId);
                    U.write_u8(bin, binOff + 4, rec.Unk1);
                    U.write_u8(bin, binOff + 5, rec.Unk2);
                    U.write_u16(bin, binOff + 6, rec.Wait);
                }

                bool oldBlobSafe = oldB7 > 0 && U.isSafetyOffset(oldBlobOffset, rom)
                    && oldBlobOffset + oldBlobLen <= (uint)rom.Data.Length;

                if ((uint)bin.Length <= oldBlobLen && oldBlobSafe)
                {
                    // IN-PLACE: the new blob fits the old region. Overwrite + zero
                    // the unused tail; the pointer stays put (no repoint).
                    rom.write_range(oldBlobOffset, bin);
                    uint tail = oldBlobLen - (uint)bin.Length;
                    if (tail > 0)
                        rom.write_fill(oldBlobOffset + (uint)bin.Length, tail, 0xFF);

                    rom.write_u8(b7FieldAddr, (uint)count);
                    // P8 unchanged on the in-place path.
                    return "";
                }

                // APPEND: find free space, write the new blob, repoint P8, then
                // recycle the old region (only when it was a real, safe blob).
                uint searchStart = (uint)(rom.Data.Length / 2);
                uint newAddr = rom.FindFreeSpace(searchStart, (uint)bin.Length);
                if (newAddr == U.NOT_FOUND)
                    newAddr = rom.FindFreeSpace(0x100, (uint)bin.Length);
                if (newAddr == U.NOT_FOUND)
                {
                    RestoreSnapshot(rom, snap);
                    return R._("Failed to write after-coords. Check ROM free space.");
                }

                rom.write_range(newAddr, bin);
                rom.write_p32(p8FieldAddr, U.toPointer(newAddr));
                rom.write_u8(b7FieldAddr, (uint)count);

                // Recycle the OLD region (0xFF) — but never if it overlaps the
                // freshly-written new blob (defensive; FindFreeSpace returns a
                // non-overlapping region so this only guards a degenerate case).
                if (oldBlobSafe && !RegionsOverlap(oldBlobOffset, oldBlobLen, newAddr, (uint)bin.Length))
                {
                    rom.write_fill(oldBlobOffset, oldBlobLen, 0xFF);
                }

                return "";
            }
            catch (Exception ex)
            {
                RestoreSnapshot(rom, snap);
                return R._("After-coord write failed: {0}", ex.Message);
            }
        }

        static bool RegionsOverlap(uint aStart, uint aLen, uint bStart, uint bLen)
        {
            if (aLen == 0 || bLen == 0) return false;
            uint aEnd = aStart + aLen;
            uint bEnd = bStart + bLen;
            return aStart < bEnd && bStart < aEnd;
        }

        /// <summary>
        /// Length-aware byte-identical restore: a free-space resize-append can
        /// GROW rom.Data, so down-resize back to the snapshot length BEFORE the
        /// in-place copy (a naive Array.Copy would leave the grown tail alive).
        /// </summary>
        static void RestoreSnapshot(ROM rom, byte[] snap)
        {
            if (rom.Data.Length != snap.Length)
                rom.write_resize_data((uint)snap.Length);
            Array.Copy(snap, rom.Data, snap.Length);
        }
    }
}
