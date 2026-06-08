// SPDX-License-Identifier: GPL-3.0-or-later
// #1016 — shared helper to build a fully-planted FE8U MagicSplit (FE8UMAGIC)
// test ROM for the Class/Unit CSV MagicSplit round-trip tests.
//
// Plants BOTH:
//   1. the FE8UMAGIC detection signature at 0x2BB44 (so
//      MagicSplitUtil.SearchMagicSplit() returns FE8UMAGIC), AND
//   2. the 88-byte FE8UInit code signature (copied verbatim from
//      MagicSplitUtil.FE8UInit) past compress_image_borderline_address,
//      followed by two valid in-ROM pointers so UnitTag / ClassTag resolve to
//      scratch table regions inside rom.Data.
//
// Callers must set CoreState.ROM = rom and call MagicSplitUtil.ClearCache()
// before use, and restore CoreState.ROM + ClearCache() in a finally block.
using System;
using FEBuilderGBA.Core;

namespace FEBuilderGBA.Avalonia.Tests
{
    internal static class FE8UMagicSplitTestRom
    {
        // The 88-byte FE8UInit code signature (pre-2022 variant), verbatim from
        // FEBuilderGBA.Core/MagicSplitUtil.cs FE8UInit().
        static readonly byte[] FE8UInitSignature =
        {
            0x00, 0xB5, 0x13, 0x48, 0x86, 0x46, 0x38, 0x68, 0x00, 0x79, 0x40, 0x00, 0x12, 0x49, 0x40, 0x18,
            0x40, 0x78, 0x50, 0x44, 0x00, 0xF8, 0x01, 0xB4, 0x0E, 0x48, 0x86, 0x46, 0xF8, 0x7A, 0x00, 0xF8,
            0x41, 0x68, 0x3A, 0x30, 0x00, 0x78, 0x09, 0x79, 0x89, 0x00, 0x0C, 0x4A, 0x52, 0x18, 0x92, 0x78,
            0x02, 0xBC, 0x76, 0x18, 0x43, 0x18, 0x93, 0x42, 0x00, 0xDD, 0x11, 0x1A, 0x38, 0x1C, 0x7A, 0x30,
            0x01, 0x70, 0x01, 0xBC, 0x00, 0x99, 0x03, 0x91, 0x01, 0x9B, 0x02, 0x93, 0xC2, 0x46, 0x00, 0x47,
            0xA0, 0xB9, 0x02, 0x08, 0x30, 0x94, 0x01, 0x08,
        };

        const uint SigOffset = 0x100000;    // FE8UInit signature (4-aligned, > 0xDB000).
        public const uint UnitTagOffset = 0x200000;
        public const uint ClassTagOffset = 0x300000;

        /// <summary>Build a 16 MB FE8U ROM with both MagicSplit signatures planted.</summary>
        public static ROM Make()
        {
            byte[] data = new byte[0x1000000]; // 16 MB
            byte[] versionBytes = System.Text.Encoding.ASCII.GetBytes("BE8E01");
            Array.Copy(versionBytes, 0, data, 0xAC, versionBytes.Length);

            // FE8UMAGIC detection signature @ 0x2BB44.
            byte[] det = { 0x01, 0x4B, 0xA5, 0xF0, 0xC1, 0xFE };
            Array.Copy(det, 0, data, 0x2BB44, det.Length);

            // FE8UInit code signature @ SigOffset, then the two tag pointers.
            Array.Copy(FE8UInitSignature, 0, data, (int)SigOffset, FE8UInitSignature.Length);
            uint p = SigOffset + (uint)FE8UInitSignature.Length;
            WritePtr(data, p, UnitTagOffset);       // UnitTag = p32(p)
            WritePtr(data, p + 4, ClassTagOffset);  // ClassTag = p32(p+4)

            var rom = new ROM();
            rom.LoadLow("test.gba", data, "BE8E01");
            return rom;
        }

        static void WritePtr(byte[] data, uint at, uint offset)
        {
            uint ptr = offset + 0x08000000;
            data[at + 0] = (byte)(ptr & 0xFF);
            data[at + 1] = (byte)((ptr >> 8) & 0xFF);
            data[at + 2] = (byte)((ptr >> 16) & 0xFF);
            data[at + 3] = (byte)((ptr >> 24) & 0xFF);
        }

        /// <summary>
        /// Open a public ambient-undo scope (matches what UndoService.Begin does
        /// in the live UI). The CSV importer's MagicSplit write path requires a
        /// non-null ambient undo.
        /// </summary>
        public static IDisposable BeginUndoScope(ROM rom) => ROM.BeginUndoScope(new Undo.UndoData
        {
            time = DateTime.Now,
            name = "test",
            list = new System.Collections.Generic.List<Undo.UndoPostion>(),
            filesize = (uint)rom.Data.Length,
        });
    }
}
