using System;
using System.Collections.Generic;
using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Tests.Unit
{
    /// <summary>
    /// Tests for batch 10 Core migration: PatchDetection, FETextEncode, FETextDecode
    /// moved to FEBuilderGBA.Core.
    /// </summary>
    [Collection("SharedState")]
    public class CoreBatch10Tests
    {
        // ---- PatchDetection enums ----

        [Fact]
        public void PatchDetection_PRIORITY_CODE_HasExpectedValues()
        {
            Assert.Equal(0, (int)PatchDetection.PRIORITY_CODE.LAT1);
            Assert.Equal(1, (int)PatchDetection.PRIORITY_CODE.SJIS);
            Assert.Equal(2, (int)PatchDetection.PRIORITY_CODE.UTF8);
        }

        [Fact]
        public void PatchDetection_draw_font_enum_HasExpectedValues()
        {
            Assert.Equal(0, (int)PatchDetection.draw_font_enum.NO);
            Assert.Equal(1, (int)PatchDetection.draw_font_enum.DrawMultiByte);
            Assert.Equal(2, (int)PatchDetection.draw_font_enum.DrawSingleByte);
            Assert.Equal(3, (int)PatchDetection.draw_font_enum.DrawUTF8);
            Assert.Equal((int)PatchDetection.NO_CACHE, (int)PatchDetection.draw_font_enum.NoCache);
        }

        [Fact]
        public void PatchDetection_TextEngineRework_enum_HasExpectedValues()
        {
            Assert.Equal(0, (int)PatchDetection.TextEngineRework_enum.NO);
            Assert.Equal(1, (int)PatchDetection.TextEngineRework_enum.TeqTextEngineRework);
            Assert.Equal((int)PatchDetection.NO_CACHE, (int)PatchDetection.TextEngineRework_enum.NoCache);
        }

        [Fact]
        public void PatchDetection_NO_CACHE_Is0xFF()
        {
            Assert.Equal(0xFFu, PatchDetection.NO_CACHE);
        }

        [Fact]
        public void PatchDetection_ClearAllCaches_DoesNotThrow()
        {
            PatchDetection.ClearAllCaches();
        }

        [Fact]
        public void PatchDetection_PatchTableSt_CanBeCreated()
        {
            var st = new PatchDetection.PatchTableSt
            {
                name = "test",
                ver = "FE8U",
                addr = 0x1234,
                data = new byte[] { 0x01, 0x02 }
            };
            Assert.Equal("test", st.name);
            Assert.Equal("FE8U", st.ver);
            Assert.Equal(0x1234u, st.addr);
            Assert.Equal(new byte[] { 0x01, 0x02 }, st.data);
        }

        // ---- FETextEncode types in Core ----

        [Fact]
        public void FETextEncode_CharCounter_CanBeCreated()
        {
            var cc = new FETextEncode.CharCounter();
            cc.mojiBin = 0x41;
            cc.count = 5;
            cc.length = 10;
            Assert.Equal(0x41u, cc.mojiBin);
            Assert.Equal(5, cc.count);
            Assert.Equal(10, cc.length);
        }

        [Fact]
        public void FETextEncode_huffman_value_st_CanBeCreated()
        {
            var hv = new FETextEncode.huffman_value_st(0xAB, 7);
            Assert.Equal(0xABu, hv.bit);
            Assert.Equal(7, hv.bitcount);
        }

        [Fact]
        public void FETextEncode_IsUnHuffmanPatchPointer_Checks()
        {
            Assert.True(FETextEncode.IsUnHuffmanPatchPointer(0x88000000));
            Assert.True(FETextEncode.IsUnHuffmanPatchPointer(0x89FFFFFF));
            Assert.False(FETextEncode.IsUnHuffmanPatchPointer(0x08000000));
            Assert.False(FETextEncode.IsUnHuffmanPatchPointer(0x8B000000));
        }

        [Fact]
        public void FETextEncode_ConvertUnHuffmanPatchToPointer_Works()
        {
            Assert.Equal(0x08100000u, FETextEncode.ConvertUnHuffmanPatchToPointer(0x88100000));
            // Non-unhuffman pointer returned as-is
            Assert.Equal(0x08000000u, FETextEncode.ConvertUnHuffmanPatchToPointer(0x08000000));
        }

        [Fact]
        public void FETextEncode_ConvertPointerToUnHuffmanPatchPointer_Works()
        {
            Assert.Equal(0x88100000u, FETextEncode.ConvertPointerToUnHuffmanPatchPointer(0x08100000));
            // Non-pointer returned as-is
            Assert.Equal(0x00001234u, FETextEncode.ConvertPointerToUnHuffmanPatchPointer(0x00001234));
        }

        [Fact]
        public void FETextEncode_IsUnHuffmanPatch_IW_RAMPointer_Checks()
        {
            Assert.True(FETextEncode.IsUnHuffmanPatch_IW_RAMPointer(0x83000000));
            Assert.True(FETextEncode.IsUnHuffmanPatch_IW_RAMPointer(0x83007FFF));
            Assert.False(FETextEncode.IsUnHuffmanPatch_IW_RAMPointer(0x83008000));
            Assert.False(FETextEncode.IsUnHuffmanPatch_IW_RAMPointer(0x08000000));
        }

        [Fact]
        public void FETextEncode_IsUnHuffmanPatch_EW_RAMPointer_Checks()
        {
            Assert.True(FETextEncode.IsUnHuffmanPatch_EW_RAMPointer(0x82000000));
            Assert.True(FETextEncode.IsUnHuffmanPatch_EW_RAMPointer(0x8203FFFF));
            Assert.False(FETextEncode.IsUnHuffmanPatch_EW_RAMPointer(0x82040000));
        }

        [Fact]
        public void FETextEncode_at_code_to_binary_ParsesHex()
        {
            byte[] input = System.Text.Encoding.ASCII.GetBytes("@00FF");
            int newI;
            uint result = FETextEncode.at_code_to_binary(input, 0, out newI);
            Assert.Equal(0xFFu, result);
            Assert.Equal(5, newI);
        }

        [Fact]
        public void FETextEncode_at_code_to_binary_StopsAtNonHex()
        {
            byte[] input = System.Text.Encoding.ASCII.GetBytes("@AB_Z");
            int newI;
            uint result = FETextEncode.at_code_to_binary(input, 0, out newI);
            Assert.Equal(0xABu, result);
            Assert.Equal(3, newI);
        }

        [Fact]
        public void FETextEncode_ConvertSPMoji_NoReplacementsWithoutInit()
        {
            // Without ROM init, the SP code table is empty, so text passes through unchanged.
            // This verifies the static method doesn't crash even without initialization.
            string result = FETextEncode.ConvertSPMoji("hello world");
            Assert.Equal("hello world", result);
        }

        [Fact]
        public void FETextEncode_RevConvertSPMoji_NoReplacementsWithoutInit()
        {
            // Without ROM init, reverse conversion also passes through unchanged.
            string result = FETextEncode.RevConvertSPMoji("hello@0001world");
            Assert.Equal("hello@0001world", result);
        }

        // ---- FETextDecode types in Core ----

        [Fact]
        public void FETextDecode_FETextException_CanBeCreated()
        {
            var ex = new FETextDecode.FETextException("test error");
            Assert.Equal("test error", ex.Message);
            Assert.IsAssignableFrom<Exception>(ex);
        }

        [Fact]
        public void FETextDecode_huffman_count_st_CanBeCreated()
        {
            var st = new FETextDecode.huffman_count_st();
            Assert.Equal(0u, st.count);
            Assert.Empty(st.unsing_text_addr);
            Assert.Equal(-1, st.code_number);
        }

        // ---- ISystemTextEncoder expanded interface ----

        [Fact]
        public void ISystemTextEncoder_HasAllRequiredMethods()
        {
            // Verify the interface has all 4 methods
            var type = typeof(ISystemTextEncoder);
            Assert.NotNull(type.GetMethod("Decode", new[] { typeof(byte[]) }));
            Assert.NotNull(type.GetMethod("Decode", new[] { typeof(byte[]), typeof(int), typeof(int) }));
            Assert.NotNull(type.GetMethod("Encode", new[] { typeof(string) }));
            Assert.NotNull(type.GetMethod("GetTBLEncodeDicLow"));
        }

        // ---- CoreState.FETextEncoder is now concrete FETextEncode ----

        [Fact]
        public void CoreState_FETextEncoder_PropertyType_IsFETextEncode()
        {
            var prop = typeof(CoreState).GetProperty("FETextEncoder");
            Assert.NotNull(prop);
            Assert.Equal(typeof(FETextEncode), prop.PropertyType);
        }
    }
}
