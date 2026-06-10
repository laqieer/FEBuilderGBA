// SPDX-License-Identifier: GPL-3.0-or-later
// #1029 Core tests for the JP-font-wipe flow ported from the WF ROM Translation
// Tool (ToolTranslateROMWipeJPFont / ToolTranslateROMWipeJPChapterName /
// ToolTranslateROMWipeJPClassReelFont), driven through the Form-free
// ToolTranslateROMCore.WipeJPFont / WipeJPTitle / WipeJPClassReelFont and
// SimpleFireTranslate orchestration.
//
// The synthetic FE8J ROM plants:
//   * a text-font hash table at font_serif_address (0x593F74) with one keep-glyph
//     node ("０", SJIS 0x4F82) reachable through the hash chain;
//   * a custom-font node inside the wipeable text-font region (0x5942F4..0x5B8CDC);
//   * a 3-row chapter-title table + image_chapter_title_pointer;
//   * a contiguous OP-class-font slot table + op_class_font_pointer + the
//     0xB7890 == 0x4B00 signature;
//   * the ChapterNameToText patch signature (so the default precondition wipes).
using System;
using System.Collections.Generic;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class ToolTranslateROMWipeJPCoreTests
    {
        // --- Font hash table layout (FE8J) ---
        const uint TextFontTop = 0x593F74;     // font_serif_address
        const uint ItemFontTop = 0x57994C;     // font_item_address
        // "０" (U+FF10) Shift-JIS = 0x82 0x4F -> little-endian uint 0x4F82.
        const uint Moji0 = 0x4F82;
        const uint KeepNodeOff = 0x00C00000u;  // keep-glyph node (outside wipe regions)
        const uint CustomNodeOff = 0x005A0000u; // custom font inside the text-font wipe region

        // --- Chapter-title table ---
        const uint ChTableBase = 0x00700000u;
        const uint ChImgLast = 0x00710000u;
        const uint ChImgA = 0x00720000u;
        const uint ChImgB = 0x00730000u;

        // --- OP-class-font table ---
        const uint OpTableBase = 0x00740000u;
        const uint OpGlyph0 = 0x00750000u;
        const uint OpGlyph1 = 0x00760000u;

        // ChapterNameToText FE8J patch signature site.
        const uint ChapterNameTextPatchAddr = 0x8B894;

        sealed class Harness : IDisposable
        {
            readonly ROM _prevRom;
            readonly Func<byte[], Undo.UndoData, uint> _prevAppend;
            readonly ISystemTextEncoder _prevEnc;
            readonly TextEncodingEnum _prevTextEnc;
            public ROM Rom { get; }
            public Undo Undo { get; }

            public Harness(bool installChapterNameTextPatch = true,
                bool installClassReelSignature = true)
            {
                _prevRom = CoreState.ROM;
                _prevAppend = CoreState.AppendBinaryData;
                _prevEnc = CoreState.SystemTextEncoder;
                _prevTextEnc = CoreState.TextEncoding;

                Rom = MakeRom(installChapterNameTextPatch, installClassReelSignature);
                CoreState.ROM = Rom;
                CoreState.SystemTextEncoder = new HeadlessSystemTextEncoder(Rom);
                CoreState.TextEncoding = TextEncodingEnum.Auto;
                CoreState.AppendBinaryData = (data, undo) =>
                {
                    uint at = (uint)Rom.Data.Length;
                    Rom.write_resize_data(at + (uint)data.Length);
                    if (undo != null) Rom.write_range(at, data, undo);
                    else Rom.write_range(at, data);
                    return at;
                };
                Undo = new Undo();
            }

            public void Dispose()
            {
                CoreState.ROM = _prevRom;
                CoreState.AppendBinaryData = _prevAppend;
                CoreState.SystemTextEncoder = _prevEnc;
                CoreState.TextEncoding = _prevTextEnc;
            }
        }

        static ROM MakeRom(bool installChapterNameTextPatch, bool installClassReelSignature)
        {
            var rom = new ROM();
            byte[] data = new byte[0x1000000];
            rom.LoadLow("x.gba", data, "BE8J01");

            // ---- Text-font hash chain for "０" (moji 0x4F82) ----
            // FindFontDataSJIS: moji1=0x4F, list = top + (0x4F<<2) - 0x100.
            uint listHead = TextFontTop + (((Moji0 >> 8) & 0xff) << 2) - 0x100;
            // head -> keep node
            U.write_u32(rom.Data, listHead, U.toPointer(KeepNodeOff));
            // keep node: next=0, moji2=0x82, width=8, then 64-byte bitmap.
            U.write_u32(rom.Data, KeepNodeOff + 0, 0);
            rom.Data[KeepNodeOff + 4] = 0x82;   // moji2
            rom.Data[KeepNodeOff + 5] = 8;      // width
            for (int i = 0; i < 64; i++) rom.Data[KeepNodeOff + 8 + i] = (byte)(i + 1);

            // ---- A custom-font node INSIDE the text-font wipe region ----
            // (a separate hash bucket head also inside the cleared hash table)
            const uint customMoji1 = 0x50;
            uint customListHead = TextFontTop + (customMoji1 << 2) - 0x100;
            U.write_u32(rom.Data, customListHead, U.toPointer(CustomNodeOff));
            U.write_u32(rom.Data, CustomNodeOff + 0, 0); // next=0
            rom.Data[CustomNodeOff + 4] = 0x99;          // moji2 (arbitrary)
            rom.Data[CustomNodeOff + 5] = 8;
            for (int i = 0; i < 64; i++) rom.Data[CustomNodeOff + 8 + i] = 0x55;

            // ---- Chapter-title table (3 rows; last is the keep entry) ----
            U.write_u32(rom.Data, ChTableBase + 0 * 12 + 0, U.toPointer(ChImgA));
            U.write_u32(rom.Data, ChTableBase + 0 * 12 + 4, U.toPointer(ChImgA + 0x100));
            U.write_u32(rom.Data, ChTableBase + 0 * 12 + 8, U.toPointer(ChImgA + 0x200));
            U.write_u32(rom.Data, ChTableBase + 1 * 12 + 0, U.toPointer(ChImgB));
            U.write_u32(rom.Data, ChTableBase + 1 * 12 + 4, U.toPointer(ChImgB + 0x100));
            U.write_u32(rom.Data, ChTableBase + 1 * 12 + 8, U.toPointer(ChImgB + 0x200));
            U.write_u32(rom.Data, ChTableBase + 2 * 12 + 0, U.toPointer(ChImgLast));
            U.write_u32(rom.Data, ChTableBase + 2 * 12 + 4, U.toPointer(ChImgLast + 0x100));
            U.write_u32(rom.Data, ChTableBase + 2 * 12 + 8, U.toPointer(ChImgLast + 0x200));
            U.write_u32(rom.Data, ChTableBase + 3 * 12 + 0, 0); // terminator
            U.write_u32(rom.Data, rom.RomInfo.image_chapter_title_pointer, U.toPointer(ChTableBase));
            // LZ77-stub the chapter images so AddLZ77Pointer can size them.
            WriteLz77Stub(rom, ChImgA); WriteLz77Stub(rom, ChImgA + 0x100); WriteLz77Stub(rom, ChImgA + 0x200);
            WriteLz77Stub(rom, ChImgB); WriteLz77Stub(rom, ChImgB + 0x100); WriteLz77Stub(rom, ChImgB + 0x200);
            WriteLz77Stub(rom, ChImgLast); WriteLz77Stub(rom, ChImgLast + 0x100); WriteLz77Stub(rom, ChImgLast + 0x200);

            // ---- OP-class-font table (2 slots, slot0 is the keep entry) ----
            U.write_u32(rom.Data, OpTableBase + 0 * 4, U.toPointer(OpGlyph0));
            U.write_u32(rom.Data, OpTableBase + 1 * 4, U.toPointer(OpGlyph1));
            U.write_u32(rom.Data, OpTableBase + 2 * 4, 0); // terminator
            U.write_u32(rom.Data, rom.RomInfo.op_class_font_pointer, U.toPointer(OpTableBase));
            WriteLz77Stub(rom, OpGlyph0); WriteLz77Stub(rom, OpGlyph1);
            if (installClassReelSignature) U.write_u16(rom.Data, 0xB7890, 0x4B00);

            // ChapterNameToText FE8J patch signature: 00 4B 18 47 at 0x8B894.
            if (installChapterNameTextPatch)
            {
                rom.Data[ChapterNameTextPatchAddr + 0] = 0x00;
                rom.Data[ChapterNameTextPatchAddr + 1] = 0x4B;
                rom.Data[ChapterNameTextPatchAddr + 2] = 0x18;
                rom.Data[ChapterNameTextPatchAddr + 3] = 0x47;
            }

            return rom;
        }

        // A minimal valid LZ77 stream (type 0x10, size 0, no blocks) so
        // LZ77.getCompressedSize returns a small value.
        static void WriteLz77Stub(ROM rom, uint off)
        {
            rom.Data[off + 0] = 0x10; // LZ77 type
            rom.Data[off + 1] = 0x08; // size low (8 bytes uncompressed)
            rom.Data[off + 2] = 0x00;
            rom.Data[off + 3] = 0x00;
            rom.Data[off + 4] = 0x00; // flag byte: 0 literals
            rom.Data[off + 5] = 0x41;
            rom.Data[off + 6] = 0x42;
            rom.Data[off + 7] = 0x43;
            rom.Data[off + 8] = 0x44;
            rom.Data[off + 9] = 0x00;
            rom.Data[off + 10] = 0x45;
            rom.Data[off + 11] = 0x46;
            rom.Data[off + 12] = 0x47;
            rom.Data[off + 13] = 0x48;
        }

        // ============================================================
        // WipeJPFont — keep-glyph restoration + table clears
        // ============================================================

        [Fact]
        public void WipeJPFont_ClearsFontHashTables_AndRestoresKeepGlyph()
        {
            using var h = new Harness();
            var undo = h.Undo.NewUndoData("test");

            // Sanity: the keep glyph "０" IS found before the wipe.
            uint before = FontCore.FindFontData(FontCore.GetFontPointer(false, h.Rom),
                Moji0, out _, h.Rom, PriorityCodeUtil.SearchPriorityCode(h.Rom));
            Assert.NotEqual(U.NOT_FOUND, before);

            var recycle = new RecycleAddress();
            ToolTranslateROMCore.WipeJPFont(h.Rom, recycle, undo);

            // The item-font hash-pointer table (896 bytes @ 0x57994C) is zeroed.
            for (uint a = 0x57994C; a < 0x57994C + 896; a++)
                Assert.Equal(0, h.Rom.Data[a]);
            // The text-font hash-pointer table (896 bytes @ 0x593F74) is zeroed
            // EXCEPT the keep-glyph head which WriteBackFont re-pointed.
            uint listHead = TextFontTop + (((Moji0 >> 8) & 0xff) << 2) - 0x100;
            uint reHead = h.Rom.u32(listHead);
            Assert.True(U.isPointer(reHead),
                "WriteBackFont should re-point the keep-glyph hash head to a new node");

            // The re-appended node carries the preserved bitmap + width.
            uint newNode = U.toOffset(reHead);
            Assert.Equal(8u, h.Rom.u8(newNode + 5)); // width preserved
            for (int i = 0; i < 64; i++)
                Assert.Equal((byte)(i + 1), h.Rom.Data[newNode + 8 + i]); // bitmap preserved
        }

        // ============================================================
        // WipeJPTitle — chapter-name pointer rewrites + precondition
        // ============================================================

        [Fact]
        public void WipeJPTitle_RepointsAllButLast_ToLastImage()
        {
            using var h = new Harness(installChapterNameTextPatch: true);
            var undo = h.Undo.NewUndoData("test");

            uint lastImg = h.Rom.u32(ChTableBase + 2 * 12 + 0);

            var recycle = new RecycleAddress();
            ToolTranslateROMCore.WipeJPTitle(h.Rom, recycle, undo);

            // Rows 0 and 1 +0 now point at the last image; +4/+8 are zeroed.
            Assert.Equal(lastImg, h.Rom.u32(ChTableBase + 0 * 12 + 0));
            Assert.Equal(lastImg, h.Rom.u32(ChTableBase + 1 * 12 + 0));
            Assert.Equal(0u, h.Rom.u32(ChTableBase + 0 * 12 + 4));
            Assert.Equal(0u, h.Rom.u32(ChTableBase + 0 * 12 + 8));
            Assert.Equal(0u, h.Rom.u32(ChTableBase + 1 * 12 + 4));
            Assert.Equal(0u, h.Rom.u32(ChTableBase + 1 * 12 + 8));
            // The last row is preserved.
            Assert.Equal(lastImg, h.Rom.u32(ChTableBase + 2 * 12 + 0));
        }

        [Fact]
        public void WipeJPTitle_PatchAbsent_DefaultPrecondition_SkipsWipe()
        {
            using var h = new Harness(installChapterNameTextPatch: false);
            var undo = h.Undo.NewUndoData("test");

            uint row0Before = h.Rom.u32(ChTableBase + 0 * 12 + 0);
            uint row0Num = h.Rom.u32(ChTableBase + 0 * 12 + 4);

            var recycle = new RecycleAddress();
            // Default precondition = SearchChapterNameToTextPatch (absent) -> skip.
            ToolTranslateROMCore.WipeJPTitle(h.Rom, recycle, undo);

            Assert.Equal(row0Before, h.Rom.u32(ChTableBase + 0 * 12 + 0)); // unchanged
            Assert.Equal(row0Num, h.Rom.u32(ChTableBase + 0 * 12 + 4));    // unchanged
        }

        [Fact]
        public void WipeJPTitle_InjectedPreconditionFalse_SkipsWipe_EvenIfPatchPresent()
        {
            using var h = new Harness(installChapterNameTextPatch: true);
            var undo = h.Undo.NewUndoData("test");

            uint row0Num = h.Rom.u32(ChTableBase + 0 * 12 + 4);
            bool invoked = false;

            var recycle = new RecycleAddress();
            ToolTranslateROMCore.WipeJPTitle(h.Rom, recycle, undo,
                () => { invoked = true; return false; });

            Assert.True(invoked, "the injected precondition must be invoked");
            Assert.Equal(row0Num, h.Rom.u32(ChTableBase + 0 * 12 + 4)); // unchanged
        }

        [Fact]
        public void WipeJPTitle_InjectedPreconditionTrue_Wipes()
        {
            using var h = new Harness(installChapterNameTextPatch: false);
            var undo = h.Undo.NewUndoData("test");

            var recycle = new RecycleAddress();
            ToolTranslateROMCore.WipeJPTitle(h.Rom, recycle, undo, () => true);

            // Even with the patch absent, an explicit-true precondition wipes.
            Assert.Equal(0u, h.Rom.u32(ChTableBase + 0 * 12 + 4));
        }

        // ============================================================
        // WipeJPClassReelFont — OP-class-font pointer rewrites
        // ============================================================

        [Fact]
        public void WipeJPClassReelFont_RepointsAllButFirst_ToFirstImage()
        {
            using var h = new Harness();
            var undo = h.Undo.NewUndoData("test");

            uint firstImg = h.Rom.u32(OpTableBase + 0 * 4);

            var recycle = new RecycleAddress();
            ToolTranslateROMCore.WipeJPClassReelFont(h.Rom, recycle, undo);

            Assert.Equal(firstImg, h.Rom.u32(OpTableBase + 0 * 4)); // first preserved
            Assert.Equal(firstImg, h.Rom.u32(OpTableBase + 1 * 4)); // second repointed
        }

        [Fact]
        public void WipeJPClassReelFont_NoSignature_SkipsWipe()
        {
            using var h = new Harness(installClassReelSignature: false);
            var undo = h.Undo.NewUndoData("test");

            uint slot1Before = h.Rom.u32(OpTableBase + 1 * 4);

            var recycle = new RecycleAddress();
            ToolTranslateROMCore.WipeJPClassReelFont(h.Rom, recycle, undo);

            Assert.Equal(slot1Before, h.Rom.u32(OpTableBase + 1 * 4)); // unchanged
        }

        // ============================================================
        // Guards — wrong version / null don't crash
        // ============================================================

        [Fact]
        public void WipeJPFont_NonFE8_NoOp_NoThrow()
        {
            var prev = CoreState.ROM;
            try
            {
                var rom = new ROM();
                byte[] data = new byte[0x1000000];
                rom.LoadLow("x.gba", data, "AE7E01"); // FE7U
                CoreState.ROM = rom;
                var undo = new Undo().NewUndoData("test");
                var recycle = new RecycleAddress();
                ToolTranslateROMCore.WipeJPFont(rom, recycle, undo); // no throw
            }
            finally { CoreState.ROM = prev; }
        }

        [Fact]
        public void WipeJP_NullRom_NoOp_NoThrow()
        {
            var recycle = new RecycleAddress();
            ToolTranslateROMCore.WipeJPFont(null, recycle, null);
            ToolTranslateROMCore.WipeJPTitle(null, recycle, null);
            ToolTranslateROMCore.WipeJPClassReelFont(null, recycle, null);
        }

        // ============================================================
        // ChapterNameText precondition default (blocker-1 parity)
        // ============================================================

        [Fact]
        public void WipeJPTitle_PatchPresent_DefaultPrecondition_Wipes()
        {
            // Default (null) precondition = SearchChapterNameToTextPatch; patch
            // present -> wipe proceeds.
            using var h = new Harness(installChapterNameTextPatch: true);
            var undo = h.Undo.NewUndoData("test");
            var recycle = new RecycleAddress();
            ToolTranslateROMCore.WipeJPTitle(h.Rom, recycle, undo);
            Assert.Equal(0u, h.Rom.u32(ChTableBase + 0 * 12 + 4)); // wiped
        }

        [Fact]
        public void SearchChapterNameToTextPatch_DetectsSignature()
        {
            using var present = new Harness(installChapterNameTextPatch: true);
            Assert.True(PatchDetection.SearchChapterNameToTextPatch(present.Rom));
            using var absent = new Harness(installChapterNameTextPatch: false);
            Assert.False(PatchDetection.SearchChapterNameToTextPatch(absent.Rom));
        }

        // ============================================================
        // ZH_TBL guard (blocker-2 parity)
        // ============================================================

        [Fact]
        public void WipeJPFont_ZhTbl_NoOp()
        {
            using var h = new Harness();
            var prevTextEnc = CoreState.TextEncoding;
            try
            {
                CoreState.TextEncoding = TextEncodingEnum.ZH_TBL;
                byte itemFontByte = h.Rom.Data[0x57994C];

                var undo = h.Undo.NewUndoData("test");
                var recycle = new RecycleAddress();
                ToolTranslateROMCore.WipeJPFont(h.Rom, recycle, undo);

                // ZH font system guard -> AddJPFonts is a no-op, the item-font
                // hash table is NOT cleared.
                Assert.Equal(itemFontByte, h.Rom.Data[0x57994C]);
            }
            finally { CoreState.TextEncoding = prevTextEnc; }
        }

        [Fact]
        public void WipeJPFont_AutoEncoding_Proceeds()
        {
            using var h = new Harness(); // TextEncoding = Auto
            var undo = h.Undo.NewUndoData("test");
            var recycle = new RecycleAddress();
            ToolTranslateROMCore.WipeJPFont(h.Rom, recycle, undo);
            // Auto != ZH_TBL -> the item-font hash table IS cleared.
            for (uint a = 0x57994C; a < 0x57994C + 896; a++)
                Assert.Equal(0, h.Rom.Data[a]);
        }
    }
}
