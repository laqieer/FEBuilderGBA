// SPDX-License-Identifier: GPL-3.0-or-later
// #1016 — FE8U MagicSplit (FE8UMAGIC) value-fidelity tests for the Core
// MagicSplitUtil Get/Write{Unit,Class}{Base,Grow}MagicExtends helpers.
//
// These prove the id-indexed magic-extends helpers round-trip a non-zero
// signed value for at least two distinct ids. They plant BOTH:
//   1. the FE8UMAGIC *detection* signature at 0x2BB44 (so SearchMagicSplit
//      returns FE8UMAGIC), AND
//   2. the FE8UInit *code* signature (88-byte `bin`, copied verbatim from
//      MagicSplitUtil.FE8UInit) at a scratch offset past
//      compress_image_borderline_address, followed by two valid in-ROM
//      pointers so UnitTag / ClassTag resolve to scratch table regions inside
//      rom.Data.
//
// The CSV-layer value round-trip lives in the Avalonia tests; these Core tests
// prove the helpers + the id indexing in isolation.
using System;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class MagicSplitUtilFE8UValueTests
    {
        // The 88-byte FE8UInit code signature — copied verbatim from
        // FEBuilderGBA.Core/MagicSplitUtil.cs FE8UInit() (the pre-2022 variant).
        static readonly byte[] FE8UInitSignature =
        {
            0x00, 0xB5, 0x13, 0x48, 0x86, 0x46, 0x38, 0x68, 0x00, 0x79, 0x40, 0x00, 0x12, 0x49, 0x40, 0x18,
            0x40, 0x78, 0x50, 0x44, 0x00, 0xF8, 0x01, 0xB4, 0x0E, 0x48, 0x86, 0x46, 0xF8, 0x7A, 0x00, 0xF8,
            0x41, 0x68, 0x3A, 0x30, 0x00, 0x78, 0x09, 0x79, 0x89, 0x00, 0x0C, 0x4A, 0x52, 0x18, 0x92, 0x78,
            0x02, 0xBC, 0x76, 0x18, 0x43, 0x18, 0x93, 0x42, 0x00, 0xDD, 0x11, 0x1A, 0x38, 0x1C, 0x7A, 0x30,
            0x01, 0x70, 0x01, 0xBC, 0x00, 0x99, 0x03, 0x91, 0x01, 0x9B, 0x02, 0x93, 0xC2, 0x46, 0x00, 0x47,
            0xA0, 0xB9, 0x02, 0x08, 0x30, 0x94, 0x01, 0x08,
        };

        // Scratch offsets (all 4-aligned, all past FE8U
        // compress_image_borderline_address = 0xDB000, all < 16 MB ROM size).
        const uint SigOffset = 0x100000;   // 88-byte FE8UInit signature.
        const uint UnitTagOffset = 0x200000;
        const uint ClassTagOffset = 0x300000;

        static ROM MakeFullyPlantedFE8URom()
        {
            byte[] data = new byte[0x1000000]; // 16 MB
            // GBA header game code "BE8E01" → FE8U.
            byte[] versionBytes = System.Text.Encoding.ASCII.GetBytes("BE8E01");
            Array.Copy(versionBytes, 0, data, 0xAC, versionBytes.Length);

            // (1) FE8UMAGIC detection signature @ 0x2BB44.
            byte[] det = { 0x01, 0x4B, 0xA5, 0xF0, 0xC1, 0xFE };
            Array.Copy(det, 0, data, 0x2BB44, det.Length);

            // (2) FE8UInit code signature @ SigOffset, followed by the two tag
            //     pointers at SigOffset+88 and SigOffset+92.
            Array.Copy(FE8UInitSignature, 0, data, (int)SigOffset, FE8UInitSignature.Length);
            uint p = SigOffset + (uint)FE8UInitSignature.Length;
            WritePtr(data, p, UnitTagOffset);       // UnitTag = p32(p)
            WritePtr(data, p + 4, ClassTagOffset);  // ClassTag = p32(p+4)

            var rom = new ROM();
            rom.LoadLow("test.gba", data, "BE8E01");
            return rom;
        }

        static Undo.UndoData MakeUndo(ROM rom) => new Undo.UndoData
        {
            time = DateTime.Now,
            name = "test",
            list = new System.Collections.Generic.List<Undo.UndoPostion>(),
            filesize = (uint)rom.Data.Length,
        };

        // Encode a signed magic value into the (uint)(byte) form the Write
        // helpers expect (matches the CSV manager's `(uint)(byte)mv`). A method
        // (not a constant cast) so negative values don't trip the C# checked
        // constant-conversion rule.
        static uint SByteToWriteValue(int v) => (uint)(byte)(sbyte)v;

        static void WritePtr(byte[] data, uint at, uint offset)
        {
            uint ptr = offset + 0x08000000;
            int i = (int)at;
            data[i + 0] = (byte)(ptr & 0xFF);
            data[i + 1] = (byte)((ptr >> 8) & 0xFF);
            data[i + 2] = (byte)((ptr >> 16) & 0xFF);
            data[i + 3] = (byte)((ptr >> 24) & 0xFF);
        }

        [Fact]
        public void FE8UInit_ResolvesTags_DetectionIsFE8UMAGIC()
        {
            var prev = CoreState.ROM;
            try
            {
                var rom = MakeFullyPlantedFE8URom();
                CoreState.ROM = rom;
                MagicSplitUtil.ClearCache();

                Assert.Equal(MagicSplitUtil.magic_split_enum.FE8UMAGIC, MagicSplitUtil.SearchMagicSplit());
                // GetUnitTag() should equal the planted UnitTag offset (proves
                // FE8UInit walked the signature and read the pointer).
                Assert.Equal(UnitTagOffset, MagicSplitUtil.GetUnitTag());
            }
            finally
            {
                CoreState.ROM = prev;
                MagicSplitUtil.ClearCache();
            }
        }

        [Fact]
        public void WriteThenGet_UnitBaseMagic_RoundTrips_TwoDistinctIds()
        {
            var prev = CoreState.ROM;
            try
            {
                var rom = MakeFullyPlantedFE8URom();
                CoreState.ROM = rom;
                MagicSplitUtil.ClearCache();
                Assert.Equal(MagicSplitUtil.magic_split_enum.FE8UMAGIC, MagicSplitUtil.SearchMagicSplit());

                var undo = MakeUndo(rom);
                // Two distinct ids with distinct signed values, to prove the
                // uid*2 indexing addresses different bytes per id.
                MagicSplitUtil.WriteUnitBaseMagicExtends(1, 0, SByteToWriteValue(7), undo);
                MagicSplitUtil.WriteUnitBaseMagicExtends(5, 0, SByteToWriteValue(-3), undo);

                Assert.Equal((sbyte)7, (sbyte)MagicSplitUtil.GetUnitBaseMagicExtends(1, 0));
                Assert.Equal((sbyte)(-3), (sbyte)MagicSplitUtil.GetUnitBaseMagicExtends(5, 0));
                // Distinct ids don't alias.
                Assert.NotEqual(
                    (sbyte)MagicSplitUtil.GetUnitBaseMagicExtends(1, 0),
                    (sbyte)MagicSplitUtil.GetUnitBaseMagicExtends(5, 0));
            }
            finally
            {
                CoreState.ROM = prev;
                MagicSplitUtil.ClearCache();
            }
        }

        [Fact]
        public void WriteThenGet_UnitGrowMagic_RoundTrips()
        {
            var prev = CoreState.ROM;
            try
            {
                var rom = MakeFullyPlantedFE8URom();
                CoreState.ROM = rom;
                MagicSplitUtil.ClearCache();
                Assert.Equal(MagicSplitUtil.magic_split_enum.FE8UMAGIC, MagicSplitUtil.SearchMagicSplit());

                var undo = MakeUndo(rom);
                MagicSplitUtil.WriteUnitGrowMagicExtends(2, 0, SByteToWriteValue(40), undo);
                MagicSplitUtil.WriteUnitGrowMagicExtends(9, 0, SByteToWriteValue(25), undo);

                Assert.Equal((sbyte)40, (sbyte)MagicSplitUtil.GetUnitGrowMagicExtends(2, 0));
                Assert.Equal((sbyte)25, (sbyte)MagicSplitUtil.GetUnitGrowMagicExtends(9, 0));
            }
            finally
            {
                CoreState.ROM = prev;
                MagicSplitUtil.ClearCache();
            }
        }

        [Fact]
        public void WriteThenGet_ClassBaseMagic_RoundTrips_TwoDistinctIds()
        {
            var prev = CoreState.ROM;
            try
            {
                var rom = MakeFullyPlantedFE8URom();
                CoreState.ROM = rom;
                MagicSplitUtil.ClearCache();
                Assert.Equal(MagicSplitUtil.magic_split_enum.FE8UMAGIC, MagicSplitUtil.SearchMagicSplit());

                var undo = MakeUndo(rom);
                // Class indexing is cid*4 (4 magic bytes per class).
                MagicSplitUtil.WriteClassBaseMagicExtends(1, 0, SByteToWriteValue(6), undo);
                MagicSplitUtil.WriteClassBaseMagicExtends(4, 0, SByteToWriteValue(-2), undo);

                Assert.Equal((sbyte)6, (sbyte)MagicSplitUtil.GetClassBaseMagicExtends(1, 0));
                Assert.Equal((sbyte)(-2), (sbyte)MagicSplitUtil.GetClassBaseMagicExtends(4, 0));
            }
            finally
            {
                CoreState.ROM = prev;
                MagicSplitUtil.ClearCache();
            }
        }

        [Fact]
        public void WriteThenGet_ClassGrowMagic_RoundTrips()
        {
            var prev = CoreState.ROM;
            try
            {
                var rom = MakeFullyPlantedFE8URom();
                CoreState.ROM = rom;
                MagicSplitUtil.ClearCache();
                Assert.Equal(MagicSplitUtil.magic_split_enum.FE8UMAGIC, MagicSplitUtil.SearchMagicSplit());

                var undo = MakeUndo(rom);
                MagicSplitUtil.WriteClassGrowMagicExtends(3, 0, SByteToWriteValue(35), undo);
                MagicSplitUtil.WriteClassGrowMagicExtends(7, 0, SByteToWriteValue(20), undo);

                Assert.Equal((sbyte)35, (sbyte)MagicSplitUtil.GetClassGrowMagicExtends(3, 0));
                Assert.Equal((sbyte)20, (sbyte)MagicSplitUtil.GetClassGrowMagicExtends(7, 0));
            }
            finally
            {
                CoreState.ROM = prev;
                MagicSplitUtil.ClearCache();
            }
        }
    }
}
