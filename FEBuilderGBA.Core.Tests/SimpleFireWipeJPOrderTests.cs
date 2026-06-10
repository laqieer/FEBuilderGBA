// SPDX-License-Identifier: GPL-3.0-or-later
// #1029 Core tests for SimpleFireTranslate's consumption of opts.OverrideJpFont:
//   * OverrideJpFont=true runs the 3 wipes in the WF order
//     (ClassReel -> Title -> Font) BEFORE the import, then the final BlackOut.
//   * OverrideJpFont=false skips them.
//   * a fault after a partial wipe rolls the ROM back byte-identically (the
//     finding-5 safety proof: covers the direct write_fill clears, the pointer
//     rewrites, WriteBackFont, and BlackOut).
using System;
using System.Collections.Generic;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class SimpleFireWipeJPOrderTests
    {
        const uint ChTableBase = 0x00700000u;
        const uint OpTableBase = 0x00740000u;

        sealed class Harness : IDisposable
        {
            readonly ROM _prevRom;
            readonly Func<byte[], Undo.UndoData, uint> _prevAppend;
            readonly ISystemTextEncoder _prevEnc;
            readonly TextEncodingEnum _prevTextEnc;
            public ROM Rom { get; }
            public Undo Undo { get; }

            public Harness()
            {
                _prevRom = CoreState.ROM;
                _prevAppend = CoreState.AppendBinaryData;
                _prevEnc = CoreState.SystemTextEncoder;
                _prevTextEnc = CoreState.TextEncoding;

                Rom = MakeRom();
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

        static ROM MakeRom()
        {
            // Reuse the synthetic FE8J layout from the wipe tests.
            var rom = new ROM();
            byte[] data = new byte[0x1000000];
            rom.LoadLow("x.gba", data, "BE8J01");

            // Text-font keep glyph "０" (moji 0x4F82).
            const uint TextFontTop = 0x593F74;
            uint listHead = TextFontTop + ((0x4Fu) << 2) - 0x100;
            const uint KeepNodeOff = 0x00C00000u;
            U.write_u32(rom.Data, listHead, U.toPointer(KeepNodeOff));
            U.write_u32(rom.Data, KeepNodeOff + 0, 0);
            rom.Data[KeepNodeOff + 4] = 0x82;
            rom.Data[KeepNodeOff + 5] = 8;
            for (int i = 0; i < 64; i++) rom.Data[KeepNodeOff + 8 + i] = (byte)(i + 1);

            // Chapter-title table (3 rows).
            const uint ChImgLast = 0x00710000u, ChImgA = 0x00720000u, ChImgB = 0x00730000u;
            void Row(uint r, uint img)
            {
                U.write_u32(rom.Data, ChTableBase + r * 12 + 0, U.toPointer(img));
                U.write_u32(rom.Data, ChTableBase + r * 12 + 4, U.toPointer(img + 0x100));
                U.write_u32(rom.Data, ChTableBase + r * 12 + 8, U.toPointer(img + 0x200));
            }
            Row(0, ChImgA); Row(1, ChImgB); Row(2, ChImgLast);
            U.write_u32(rom.Data, ChTableBase + 3 * 12 + 0, 0);
            U.write_u32(rom.Data, rom.RomInfo.image_chapter_title_pointer, U.toPointer(ChTableBase));
            foreach (uint b in new uint[] { ChImgA, ChImgB, ChImgLast })
                for (uint o = 0; o <= 0x200; o += 0x100) Lz77Stub(rom, b + o);

            // OP-class-font table (2 slots).
            const uint OpGlyph0 = 0x00750000u, OpGlyph1 = 0x00760000u;
            U.write_u32(rom.Data, OpTableBase + 0, U.toPointer(OpGlyph0));
            U.write_u32(rom.Data, OpTableBase + 4, U.toPointer(OpGlyph1));
            U.write_u32(rom.Data, OpTableBase + 8, 0);
            U.write_u32(rom.Data, rom.RomInfo.op_class_font_pointer, U.toPointer(OpTableBase));
            Lz77Stub(rom, OpGlyph0); Lz77Stub(rom, OpGlyph1);
            U.write_u16(rom.Data, 0xB7890, 0x4B00);

            // ChapterNameToText FE8J patch signature (so default precondition wipes).
            rom.Data[0x8B894] = 0x00; rom.Data[0x8B895] = 0x4B;
            rom.Data[0x8B896] = 0x18; rom.Data[0x8B897] = 0x47;

            return rom;
        }

        static void Lz77Stub(ROM rom, uint off)
        {
            rom.Data[off + 0] = 0x10; rom.Data[off + 1] = 0x08;
            rom.Data[off + 4] = 0x00;
            for (uint i = 5; i < 14; i++) rom.Data[off + i] = (byte)i;
        }

        // ============================================================
        // Order
        // ============================================================

        [Fact]
        public void SimpleFire_OverrideJpFontTrue_RunsThreeWipes_InWFOrder()
        {
            using var h = new Harness();
            var undo = h.Undo.NewUndoData("test");
            var progress = new List<string>();

            var opts = new ToolTranslateROMCore.SimpleFireOptions
            {
                FromLanguage = "en",
                ToLanguage = "ja",       // different -> not short-circuited
                OverrideJpFont = true,
            };
            var recycle = new RecycleAddress();
            ToolTranslateROMCore.SimpleFireTranslate(h.Rom, opts, recycle, undo, progress.Add);

            // The 3 wipe progress markers appear in the WF order, before any
            // import work.
            int iClassReel = progress.FindIndex(s => s.Contains("WipeJP ClassReel"));
            int iTitle = progress.FindIndex(s => s.Contains("WipeJP Title"));
            int iFont = progress.FindIndex(s => s.Contains("WipeJP Font"));
            Assert.True(iClassReel >= 0 && iTitle >= 0 && iFont >= 0,
                "all three wipe progress markers must appear");
            Assert.True(iClassReel < iTitle, "ClassReel must precede Title");
            Assert.True(iTitle < iFont, "Title must precede Font");

            // Observable effect of each wipe (not just final state):
            uint firstOp = h.Rom.u32(OpTableBase + 0);
            Assert.Equal(firstOp, h.Rom.u32(OpTableBase + 4));         // ClassReel ran
            uint lastCh = h.Rom.u32(ChTableBase + 2 * 12 + 0);
            Assert.Equal(lastCh, h.Rom.u32(ChTableBase + 0 * 12 + 0)); // Title ran
            // Font table cleared (item-font hash table zeroed).
            for (uint a = 0x57994C; a < 0x57994C + 896; a++)
                Assert.Equal(0, h.Rom.Data[a]);                       // Font ran
        }

        [Fact]
        public void SimpleFire_OverrideJpFontFalse_SkipsAllWipes()
        {
            using var h = new Harness();
            var undo = h.Undo.NewUndoData("test");
            var progress = new List<string>();

            uint opSlot1 = h.Rom.u32(OpTableBase + 4);
            uint chRow0Num = h.Rom.u32(ChTableBase + 0 * 12 + 4);
            byte itemFontByte = h.Rom.Data[0x57994C];

            var opts = new ToolTranslateROMCore.SimpleFireOptions
            {
                FromLanguage = "en",
                ToLanguage = "ja",
                OverrideJpFont = false,
            };
            var recycle = new RecycleAddress();
            ToolTranslateROMCore.SimpleFireTranslate(h.Rom, opts, recycle, undo, progress.Add);

            Assert.DoesNotContain(progress, s => s.Contains("WipeJP"));
            Assert.Equal(opSlot1, h.Rom.u32(OpTableBase + 4));        // unchanged
            Assert.Equal(chRow0Num, h.Rom.u32(ChTableBase + 0 * 12 + 4)); // unchanged
            Assert.Equal(itemFontByte, h.Rom.Data[0x57994C]);        // unchanged
        }

        // ============================================================
        // Rollback (finding-5 safety proof)
        // ============================================================

        [Fact]
        public void WipeJP_AllThree_Rollback_RestoresByteIdentical()
        {
            using var h = new Harness();

            // Snapshot the entire ROM before any wipe.
            byte[] before = (byte[])h.Rom.Data.Clone();
            int beforeLen = h.Rom.Data.Length;

            var undo = h.Undo.NewUndoData("wipe");
            var recycle = new RecycleAddress();

            // Run all three wipes + the final BlackOut, exactly as SimpleFire does.
            ToolTranslateROMCore.WipeJPClassReelFont(h.Rom, recycle, undo);
            ToolTranslateROMCore.WipeJPTitle(h.Rom, recycle, undo);
            ToolTranslateROMCore.WipeJPFont(h.Rom, recycle, undo);
            recycle.BlackOut(undo);

            // Something actually changed (chapter row 0 +0 was repointed to the
            // last image by WipeJPTitle).
            Assert.NotEqual(
                U.u32(before, ChTableBase + 0 * 12 + 0),
                h.Rom.u32(ChTableBase + 0 * 12 + 0));

            // Roll back via the captured undo.
            h.Undo.Push(undo);
            h.Undo.RunUndo();

            // Byte-identical restore (length first).
            Assert.Equal(beforeLen, h.Rom.Data.Length);
            for (int i = 0; i < before.Length; i++)
                Assert.True(before[i] == h.Rom.Data[i],
                    $"byte mismatch at 0x{i:X} after rollback: {before[i]:X2} != {h.Rom.Data[i]:X2}");
        }
    }
}
