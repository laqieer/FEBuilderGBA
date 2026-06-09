// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for MagicEffectExportCore (#878 PR1).
//
// Coverage:
//   ScanMagicFrames:
//     - single 0x86 frame → 1 frame in list
//     - 0x80 terminator stops scan
//     - 0x85 commands collected separately; not counted as frames
//     - 0x00 0x01 0x00 0x80 miss-terminator does NOT stop; frame after is found
//     - near-EOF no throw
//     - bad obj ptr (non-pointer) → returns false, partial list
//   ExportMagicScript:
//     - expected lines (O/B/wait / ~~~ / End) for a single frame
//     - enableComment=true adds header lines
//     - deduplication: two frames sharing same OBJ hash → same slot
//     - 0x85 command emits C<hex>
//   RenderObjFrameSlot / RenderBgFrameSlot:
//     - non-magic ROM → null (FE-gate via MagicEffectRendererCore)
//     - out-of-range slot → null
//     - valid synthetic ROM with planted LZ77 → non-null + correct dims
//   CountUniqueObjSlots / CountUniqueBgSlots:
//     - exact deduplication count
//
// [Collection("SharedState")] required because tests mutate CoreState.ROM
// and CoreState.ImageService.
using System;
using System.Collections.Generic;
using System.Linq;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class MagicEffectExportCoreTests
    {
        // ---------------------------------------------------------------
        // ScanMagicFrames
        // ---------------------------------------------------------------

        [Fact]
        public void ScanMagicFrames_SingleFrame_ReturnsOneFrame()
        {
            var rom = MakeMinimalRom();
            uint baseOff = 0x300u; // >= 0x200 for isSafetyOffset
            Build86Record(rom.Data, baseOff, 1);
            // Terminator.
            rom.Data[baseOff + 28 + 3] = 0x80;

            List<MagicFrameMeta> frames;
            List<MagicCommandMeta> cmds;
            bool ok = MagicEffectExportCore.ScanMagicFrames(
                rom, baseOff, 0u, 0u, out frames, out cmds);

            Assert.True(ok);
            Assert.Single(frames);
            Assert.Empty(cmds);
        }

        [Fact]
        public void ScanMagicFrames_TerminatorStops_CorrectCount()
        {
            var rom = MakeMinimalRom();
            uint baseOff = 0x400u;
            // 2 frames then terminator then 3rd frame (should NOT be included).
            Build86Record(rom.Data, baseOff, 2);
            rom.Data[baseOff + 2 * 28 + 3] = 0x80; // plain terminator
            Build86Record(rom.Data, baseOff + 2 * 28 + 4, 1); // after terminator

            List<MagicFrameMeta> frames;
            List<MagicCommandMeta> cmds;
            MagicEffectExportCore.ScanMagicFrames(
                rom, baseOff, 0u, 0u, out frames, out cmds);

            Assert.Equal(2, frames.Count);
        }

        [Fact]
        public void ScanMagicFrames_X85Commands_CollectedSeparately()
        {
            var rom = MakeMinimalRom();
            uint baseOff = 0x500u;
            // One 0x85 command then one 0x86 frame then terminator.
            rom.Data[baseOff + 3] = 0x85;
            rom.Data[baseOff + 0] = 0x48; // sound command (low byte = 0x48)
            Build86Record(rom.Data, baseOff + 4, 1);
            rom.Data[baseOff + 4 + 28 + 3] = 0x80;

            List<MagicFrameMeta> frames;
            List<MagicCommandMeta> cmds;
            MagicEffectExportCore.ScanMagicFrames(
                rom, baseOff, 0u, 0u, out frames, out cmds);

            Assert.Single(frames);
            Assert.Single(cmds);
            Assert.True(cmds[0].IsSound);
        }

        [Fact]
        public void ScanMagicFrames_MissContinuation_DoesNotStop()
        {
            var rom = MakeMinimalRom();
            uint baseOff = 0x600u;
            // Miss-terminator (0x00 0x01 0x00 0x80) then a 0x86 frame.
            rom.Data[baseOff + 0] = 0x00;
            rom.Data[baseOff + 1] = 0x01;
            rom.Data[baseOff + 2] = 0x00;
            rom.Data[baseOff + 3] = 0x80;
            Build86Record(rom.Data, baseOff + 4, 1);
            rom.Data[baseOff + 4 + 28 + 3] = 0x80; // plain terminator

            List<MagicFrameMeta> frames;
            List<MagicCommandMeta> cmds;
            MagicEffectExportCore.ScanMagicFrames(
                rom, baseOff, 0u, 0u, out frames, out cmds);

            // Should have found the frame after the miss-terminator.
            Assert.Single(frames);
        }

        [Fact]
        public void ScanMagicFrames_NearEOF_DoesNotThrow()
        {
            var rom = MakeMinimalRomSize(0x1000);
            // Place scan start 3 bytes from EOF — not enough for a 4-byte guard.
            uint nearEOF = (uint)(rom.Data.Length - 3);

            List<MagicFrameMeta> frames;
            List<MagicCommandMeta> cmds;
            // Should not throw.
            MagicEffectExportCore.ScanMagicFrames(
                rom, nearEOF, 0u, 0u, out frames, out cmds);

            // No frames — everything is EOF or invalid.
            Assert.Empty(frames);
        }

        [Fact]
        public void ScanMagicFrames_NullRom_ReturnsFalse()
        {
            // ScanMagicFrames with null ROM returns false immediately.
            List<MagicFrameMeta> frames;
            List<MagicCommandMeta> cmds;
            bool ok = MagicEffectExportCore.ScanMagicFrames(
                null, 0x300u, 0u, 0u, out frames, out cmds);

            Assert.False(ok);
            Assert.Empty(frames);
        }

        // ---------------------------------------------------------------
        // ExportMagicScript
        // ---------------------------------------------------------------

        [Fact]
        public void ExportMagicScript_SingleFrame_ProducesCorrectLines()
        {
            var rom = MakeMinimalRom();

            // Build synthetic frame list.
            var frames = new List<MagicFrameMeta>
            {
                new MagicFrameMeta
                {
                    RecordOffset = 0,
                    Wait = 3,
                    ObjImageOffset = 0x100u,
                    OamAbsoStart = 0u,
                    OamBGAbsoStart = 0u,
                    BgImageOffset = 0x200u,
                    ObjPaletteOffset = 0x300u,
                    BgPaletteOffset = 0x400u,
                    RawObjImagePtr = 0x08000100u,
                    RawBgImagePtr  = 0x08000200u,
                    RawObjPalPtr   = 0x08000300u,
                    RawBgPalPtr    = 0x08000400u,
                }
            };
            var cmds = new List<MagicCommandMeta>();

            List<int> objIdx, bgIdx;
            var lines = MagicEffectExportCore.ExportMagicScript(
                rom, frames, cmds, "test_", false, false,
                out objIdx, out bgIdx);

            // Verify output line sequence.
            var texts = lines.Select(l => l.Text).ToList();
            Assert.Contains("/// - Start Animation", texts);
            Assert.Contains("O  p- test_o_000.png", texts);
            Assert.Contains("B  p- test_b_000.png", texts);
            Assert.Contains("3", texts);
            Assert.Contains("/// - End of animation", texts);
            // No header comments.
            Assert.DoesNotContain(texts, t => t.StartsWith("#"));
            // Indices.
            Assert.Single(objIdx);
            Assert.Equal(0, objIdx[0]);
            Assert.Single(bgIdx);
            Assert.Equal(0, bgIdx[0]);
        }

        [Fact]
        public void ExportMagicScript_EnableComment_AddsHeaderLines()
        {
            var rom = MakeMinimalRom();
            var frames = new List<MagicFrameMeta>();
            var cmds = new List<MagicCommandMeta>();

            List<int> objIdx, bgIdx;
            var lines = MagicEffectExportCore.ExportMagicScript(
                rom, frames, cmds, "test_", true, false,
                out objIdx, out bgIdx);

            // First 4 lines should be comment header.
            var texts = lines.Select(l => l.Text).ToList();
            Assert.True(texts.Count >= 6, $"Expected >=6 lines, got {texts.Count}");
            Assert.StartsWith("#", texts[0]);
            Assert.StartsWith("#", texts[1]);
            Assert.StartsWith("#", texts[2]);
            Assert.StartsWith("#", texts[3]);
        }

        [Fact]
        public void ExportMagicScript_ScanHadContinuation_EmitsTerminatorLine()
        {
            var rom = MakeMinimalRom();
            var frames = new List<MagicFrameMeta>();
            var cmds = new List<MagicCommandMeta>();

            List<int> objIdx, bgIdx;
            var lines = MagicEffectExportCore.ExportMagicScript(
                rom, frames, cmds, "test_", false, true,
                out objIdx, out bgIdx);

            // Should have a ~~~ line.
            var termLine = lines.FirstOrDefault(l => l.Kind == MagicScriptLineKind.Terminator);
            Assert.NotNull(termLine);
            Assert.StartsWith("~~~", termLine.Text);
        }

        [Fact]
        public void ExportMagicScript_TwoFramesSameObjHash_SameSlot()
        {
            var rom = MakeMinimalRom();
            // Same RawObjImagePtr + same OamAbsoStart → same hash → same OBJ slot.
            var frames = new List<MagicFrameMeta>
            {
                new MagicFrameMeta { RecordOffset=0, Wait=1,
                    RawObjImagePtr=0x08000100u, OamAbsoStart=0u, RawBgImagePtr=0x08000200u,
                    ObjImageOffset=0x100u, BgImageOffset=0x200u,
                    ObjPaletteOffset=0x300u, BgPaletteOffset=0x400u },
                new MagicFrameMeta { RecordOffset=28, Wait=2,
                    RawObjImagePtr=0x08000100u, OamAbsoStart=0u, RawBgImagePtr=0x08000300u,
                    ObjImageOffset=0x100u, BgImageOffset=0x300u,
                    ObjPaletteOffset=0x400u, BgPaletteOffset=0x500u },
            };
            var cmds = new List<MagicCommandMeta>();

            List<int> objIdx, bgIdx;
            MagicEffectExportCore.ExportMagicScript(
                rom, frames, cmds, "t_", false, false,
                out objIdx, out bgIdx);

            // Both frames share OBJ slot 0; BG slots differ.
            Assert.Equal(2, objIdx.Count);
            Assert.Equal(0, objIdx[0]);
            Assert.Equal(0, objIdx[1]); // same hash → same slot
            Assert.Equal(2, bgIdx.Count);
            Assert.Equal(0, bgIdx[0]);
            Assert.Equal(1, bgIdx[1]); // different BG ptr → different slot
        }

        [Fact]
        public void ExportMagicScript_CommandEmitted_CLinePresent()
        {
            var rom = MakeMinimalRom();
            var frames = new List<MagicFrameMeta>();
            var cmds = new List<MagicCommandMeta>
            {
                new MagicCommandMeta { RecordOffset = 0, Command24 = 0x00000048u } // sound
            };

            List<int> objIdx, bgIdx;
            var lines = MagicEffectExportCore.ExportMagicScript(
                rom, frames, cmds, "t_", false, false,
                out objIdx, out bgIdx);

            var cLine = lines.FirstOrDefault(l => l.Kind == MagicScriptLineKind.Command);
            Assert.NotNull(cLine);
            Assert.StartsWith("C", cLine.Text);
        }

        // ---------------------------------------------------------------
        // RenderObjFrameSlot / RenderBgFrameSlot
        // ---------------------------------------------------------------

        [Fact]
        public void RenderObjFrameSlot_NullFrameList_ReturnsNull()
        {
            var prevSvc = CoreState.ImageService;
            try
            {
                CoreState.ImageService = new StubImageService();
                var result = MagicEffectExportCore.RenderObjFrameSlot(
                    MakeMinimalRom(), null, 0, 0u, 0u);
                Assert.Null(result);
            }
            finally { CoreState.ImageService = prevSvc; }
        }

        [Fact]
        public void RenderObjFrameSlot_OutOfRangeSlot_ReturnsNull()
        {
            var prevSvc = CoreState.ImageService;
            try
            {
                CoreState.ImageService = new StubImageService();
                var frames = new List<MagicFrameMeta>
                {
                    new MagicFrameMeta { RawObjImagePtr = 0x08000100u, OamAbsoStart = 0u }
                };
                // Slot 1 doesn't exist (only slot 0).
                var result = MagicEffectExportCore.RenderObjFrameSlot(
                    MakeMinimalRom(), frames, 1, 0u, 0u);
                Assert.Null(result);
            }
            finally { CoreState.ImageService = prevSvc; }
        }

        [Fact]
        public void RenderBgFrameSlot_OutOfRangeSlot_ReturnsNull()
        {
            var prevSvc = CoreState.ImageService;
            try
            {
                CoreState.ImageService = new StubImageService();
                var frames = new List<MagicFrameMeta>
                {
                    new MagicFrameMeta { RawBgImagePtr = 0x08000200u }
                };
                var result = MagicEffectExportCore.RenderBgFrameSlot(
                    MakeMinimalRom(), frames, 5);
                Assert.Null(result);
            }
            finally { CoreState.ImageService = prevSvc; }
        }

        [Fact]
        public void RenderObjFrameSlot_ValidSyntheticRom_ReturnsCorrectDims()
        {
            var prevRom = CoreState.ROM;
            var prevSvc = CoreState.ImageService;
            try
            {
                var rom = MakeSyntheticRomWithFrameData();
                CoreState.ROM = rom;
                CoreState.ImageService = new StubImageService();

                var frames = new List<MagicFrameMeta>
                {
                    new MagicFrameMeta
                    {
                        RecordOffset    = 0,
                        RawObjImagePtr  = 0x08000500u,
                        ObjImageOffset  = 0x500u,
                        OamAbsoStart    = 0u,
                        OamBGAbsoStart  = 0u,
                        RawBgImagePtr   = 0x08000600u,
                        BgImageOffset   = 0x600u,
                        ObjPaletteOffset = 0x700u,
                        BgPaletteOffset  = 0x800u,
                        RawObjPalPtr    = 0x08000700u,
                        RawBgPalPtr     = 0x08000800u,
                    }
                };

                // Plant LZ77 for OBJ at 0x500 (enough for a 256×64 sheet = 256*64/2=8192 bytes).
                PlantSmallLZ77(rom.Data, 0x500u, 8192);
                PlantRawPalette(rom.Data, 0x700u);
                PlantRawPalette(rom.Data, 0x800u);

                var img = MagicEffectExportCore.RenderObjFrameSlot(
                    rom, frames, 0, 0u, 0u);

                Assert.NotNull(img);
                Assert.Equal(MagicEffectExportCore.OBJ_EXPORT_WIDTH,  img.Width);
                Assert.Equal(MagicEffectExportCore.OBJ_EXPORT_HEIGHT, img.Height);
            }
            finally { CoreState.ROM = prevRom; CoreState.ImageService = prevSvc; }
        }

        [Fact]
        public void RenderBgFrameSlot_ValidSyntheticRom_ReturnsCorrectDims()
        {
            var prevRom = CoreState.ROM;
            var prevSvc = CoreState.ImageService;
            try
            {
                var rom = MakeSyntheticRomWithFrameData();
                CoreState.ROM = rom;
                CoreState.ImageService = new StubImageService();

                var frames = new List<MagicFrameMeta>
                {
                    new MagicFrameMeta
                    {
                        RawBgImagePtr   = 0x08000600u,
                        BgImageOffset   = 0x600u,
                        BgPaletteOffset = 0x800u,
                        RawBgPalPtr     = 0x08000800u,
                    }
                };

                // Plant LZ77 for BG at 0x600 (256*64/2 = 8192 bytes).
                PlantSmallLZ77(rom.Data, 0x600u, 8192);
                PlantRawPalette(rom.Data, 0x800u);

                // With the shared anime-hash (FIX 1), BG is at slot 1 (after OBJ slot 0).
                var img = MagicEffectExportCore.RenderBgFrameSlot(rom, frames, 1);

                Assert.NotNull(img);
                Assert.Equal(MagicEffectExportCore.BG_EXPORT_WIDTH,  img.Width);
                Assert.Equal(MagicEffectExportCore.BG_EXPORT_HEIGHT, img.Height);
            }
            finally { CoreState.ROM = prevRom; CoreState.ImageService = prevSvc; }
        }

        // ---------------------------------------------------------------
        // CountUniqueObjSlots / CountUniqueBgSlots
        // ---------------------------------------------------------------

        [Fact]
        public void CountUniqueObjSlots_TwoFramesSameHash_Returns1()
        {
            var frames = new List<MagicFrameMeta>
            {
                new MagicFrameMeta { RawObjImagePtr = 0x08000100u, OamAbsoStart = 0u },
                new MagicFrameMeta { RawObjImagePtr = 0x08000100u, OamAbsoStart = 0u },
            };
            Assert.Equal(1, MagicEffectExportCore.CountUniqueObjSlots(frames));
        }

        [Fact]
        public void CountUniqueObjSlots_TwoFramesDifferentHash_Returns2()
        {
            var frames = new List<MagicFrameMeta>
            {
                new MagicFrameMeta { RawObjImagePtr = 0x08000100u, OamAbsoStart = 0u },
                new MagicFrameMeta { RawObjImagePtr = 0x08000200u, OamAbsoStart = 0u },
            };
            Assert.Equal(2, MagicEffectExportCore.CountUniqueObjSlots(frames));
        }

        [Fact]
        public void CountUniqueBgSlots_TwoFramesSamePtr_Returns1()
        {
            var frames = new List<MagicFrameMeta>
            {
                new MagicFrameMeta { RawBgImagePtr = 0x08000200u },
                new MagicFrameMeta { RawBgImagePtr = 0x08000200u },
            };
            Assert.Equal(1, MagicEffectExportCore.CountUniqueBgSlots(frames));
        }

        [Fact]
        public void CountUniqueBgSlots_TwoFramesDifferentPtr_Returns2()
        {
            var frames = new List<MagicFrameMeta>
            {
                new MagicFrameMeta { RawBgImagePtr = 0x08000200u },
                new MagicFrameMeta { RawBgImagePtr = 0x08000300u },
            };
            Assert.Equal(2, MagicEffectExportCore.CountUniqueBgSlots(frames));
        }


        // ---------------------------------------------------------------
        // ExportMagicScriptLines — WF-exact single-walk API (FIX 1+2+3)
        // ---------------------------------------------------------------

        [Fact]
        public void ExportMagicScriptLines_SharedHash_BgSlotAfterObjSlot()
        {
            // FIX 1: WF shared anime-hash: frame 0 new OBJ → slot 0 (o_000.png);
            // frame 0 new BG → slot 1 (b_001.png) because OBJ slot 0 was already added.
            var rom = MakeMinimalRom();
            uint baseOff = 0x700u;
            // Write one 0x86 frame at baseOff with distinct OBJ/BG pointers.
            rom.Data[baseOff + 3] = 0x86;
            WriteU32Le(rom.Data, baseOff + 4,  0x08000100u); // OBJ ptr
            WriteU32Le(rom.Data, baseOff + 8,  0u);          // OAMAbsoStart
            WriteU32Le(rom.Data, baseOff + 12, 0u);
            WriteU32Le(rom.Data, baseOff + 16, 0x08000200u); // BG ptr (different)
            WriteU32Le(rom.Data, baseOff + 20, 0x08000300u);
            WriteU32Le(rom.Data, baseOff + 24, 0x08000400u);
            // Terminator
            rom.Data[baseOff + 28 + 3] = 0x80;

            List<int> objSlots, bgSlots;
            List<MagicFrameMeta> frames;
            var lines = MagicEffectExportCore.ExportMagicScriptLines(
                rom, baseOff, "t_", false,
                out objSlots, out bgSlots, out frames);

            // OBJ gets slot 0, BG gets slot 1 (shared index space — FIX 1).
            Assert.Single(objSlots);
            Assert.Equal(0, objSlots[0]);
            Assert.Single(bgSlots);
            Assert.Equal(1, bgSlots[0]); // NOT 0 — shared hash, BG follows OBJ

            // Filenames in script must reflect shared indices.
            var objLine = lines.Find(l => l.Kind == MagicScriptLineKind.ObjImage);
            var bgLine  = lines.Find(l => l.Kind == MagicScriptLineKind.BgImage);
            Assert.NotNull(objLine);
            Assert.Contains("o_000.png", objLine.Text);
            Assert.NotNull(bgLine);
            Assert.Contains("b_001.png", bgLine.Text); // shared slot 1, not b_000
        }

        [Fact]
        public void ExportMagicScriptLines_TwoFramesSameObjDiffBg_SharedIndices()
        {
            // FIX 1: Two frames sharing OBJ hash (slot 0) but different BG hash.
            // Frame 0: OBJ → slot 0, BG (ptr A) → slot 1.
            // Frame 1: OBJ → slot 0 (same hash), BG (ptr B) → slot 2.
            var rom = MakeMinimalRom();
            uint baseOff = 0x800u;

            // Frame 0: OBJ 0x08000100, OAM 0, BG 0x08000200
            rom.Data[baseOff + 3] = 0x86;
            WriteU32Le(rom.Data, baseOff + 4,  0x08000100u);
            WriteU32Le(rom.Data, baseOff + 8,  0u);
            WriteU32Le(rom.Data, baseOff + 12, 0u);
            WriteU32Le(rom.Data, baseOff + 16, 0x08000200u);
            WriteU32Le(rom.Data, baseOff + 20, 0x08000300u);
            WriteU32Le(rom.Data, baseOff + 24, 0x08000400u);

            // Frame 1: OBJ 0x08000100 (same), OAM 0 (same), BG 0x08000300 (diff)
            uint off1 = baseOff + 28;
            rom.Data[off1 + 3] = 0x86;
            WriteU32Le(rom.Data, off1 + 4,  0x08000100u); // same OBJ
            WriteU32Le(rom.Data, off1 + 8,  0u);
            WriteU32Le(rom.Data, off1 + 12, 0u);
            WriteU32Le(rom.Data, off1 + 16, 0x08000300u); // different BG
            WriteU32Le(rom.Data, off1 + 20, 0x08000300u);
            WriteU32Le(rom.Data, off1 + 24, 0x08000400u);

            rom.Data[off1 + 28 + 3] = 0x80;

            List<int> objSlots, bgSlots;
            List<MagicFrameMeta> frames;
            MagicEffectExportCore.ExportMagicScriptLines(
                rom, baseOff, "t_", false,
                out objSlots, out bgSlots, out frames);

            Assert.Equal(2, objSlots.Count);
            Assert.Equal(0, objSlots[0]); // frame 0 OBJ: slot 0
            Assert.Equal(0, objSlots[1]); // frame 1 OBJ: same slot (dedup)

            Assert.Equal(2, bgSlots.Count);
            Assert.Equal(1, bgSlots[0]); // frame 0 BG: slot 1 (after OBJ slot 0)
            Assert.Equal(2, bgSlots[1]); // frame 1 BG: slot 2 (new)
        }

        [Fact]
        public void ExportMagicScriptLines_MissContinuationInlineAfterFrames()
        {
            // FIX 2+3: ~~~ emitted INLINE at stream position of 0x00 0x01 0x00 0x80,
            // even when it comes AFTER frames (not just before).
            // Stream: [frame][frame][0x00 0x01 0x00 0x80][0x80]
            // Expected lines: Start, O, B, wait, O, B, wait, ~~~, ~~~, End
            var rom = MakeMinimalRom();
            uint baseOff = 0x900u;

            // Frame 0
            rom.Data[baseOff + 3] = 0x86;
            WriteU32Le(rom.Data, baseOff + 4,  0x08000100u);
            WriteU32Le(rom.Data, baseOff + 8,  0u);
            WriteU32Le(rom.Data, baseOff + 12, 0u);
            WriteU32Le(rom.Data, baseOff + 16, 0x08000200u);
            WriteU32Le(rom.Data, baseOff + 20, 0x08000300u);
            WriteU32Le(rom.Data, baseOff + 24, 0x08000400u);

            // Frame 1 (different OBJ ptr to get distinct slot)
            uint off1 = baseOff + 28;
            rom.Data[off1 + 3] = 0x86;
            WriteU32Le(rom.Data, off1 + 4,  0x08000150u); // different OBJ
            WriteU32Le(rom.Data, off1 + 8,  0u);
            WriteU32Le(rom.Data, off1 + 12, 0u);
            WriteU32Le(rom.Data, off1 + 16, 0x08000250u); // different BG
            WriteU32Le(rom.Data, off1 + 20, 0x08000300u);
            WriteU32Le(rom.Data, off1 + 24, 0x08000400u);

            // Miss terminator (0x00 0x01 0x00 0x80) — comes AFTER two frames.
            uint missOff = off1 + 28;
            rom.Data[missOff + 0] = 0x00;
            rom.Data[missOff + 1] = 0x01;
            rom.Data[missOff + 2] = 0x00;
            rom.Data[missOff + 3] = 0x80;

            // Plain terminator (second 0x80).
            rom.Data[missOff + 4 + 3] = 0x80;

            List<int> objSlots, bgSlots;
            List<MagicFrameMeta> frames;
            var lines = MagicEffectExportCore.ExportMagicScriptLines(
                rom, baseOff, "t_", false,
                out objSlots, out bgSlots, out frames);

            // Must have found 2 frames.
            Assert.Equal(2, frames.Count);

            // ~~~ lines: only the miss terminator is emitted (WF L602-603 + L679).
            // The subsequent plain 0x80 ([n+1] != 0x01) just breaks — no extra ~~~.
            var termLines = lines.FindAll(l => l.Kind == MagicScriptLineKind.Terminator);
            Assert.Single(termLines);
            Assert.StartsWith("~~~", termLines[0].Text);

            // The miss terminator ~~~ MUST appear AFTER the two frame blocks
            // (i.e. after the second Wait line) — INLINE, not at start (FIX 2).
            int lastWaitIdx  = lines.FindLastIndex(l => l.Kind == MagicScriptLineKind.Wait);
            int firstTermIdx = lines.FindIndex(l => l.Kind == MagicScriptLineKind.Terminator);
            Assert.True(firstTermIdx > lastWaitIdx,
                $"~~~ (idx {firstTermIdx}) should come after last Wait (idx {lastWaitIdx})");
        }

        [Fact]
        public void ExportMagicScriptLines_PlainFirstTerminator_NoTilde()
        {
            // Plain first 0x80 (no continuation byte) → no ~~~ emitted (WF L679 commented out).
            var rom = MakeMinimalRom();
            uint baseOff = 0xA00u;
            Build86Record(rom.Data, baseOff, 1);
            rom.Data[baseOff + 28 + 3] = 0x80; // plain terminator

            List<int> objSlots, bgSlots;
            List<MagicFrameMeta> frames;
            var lines = MagicEffectExportCore.ExportMagicScriptLines(
                rom, baseOff, "t_", false,
                out objSlots, out bgSlots, out frames);

            // No ~~~ line should be emitted for a plain first terminator.
            var termLines = lines.FindAll(l => l.Kind == MagicScriptLineKind.Terminator);
            Assert.Empty(termLines);
        }

        [Fact]
        public void ExportMagicScriptLines_CountersMatchSharedHash()
        {
            // CountUniqueObjSlots / CountUniqueBgSlots must agree with shared hash.
            // Two distinct frames, each with unique OBJ and BG: expect 2 OBJ, 2 BG slots.
            var rom = MakeMinimalRom();
            uint baseOff = 0xB00u;

            rom.Data[baseOff + 3] = 0x86;
            WriteU32Le(rom.Data, baseOff + 4,  0x08000100u);
            WriteU32Le(rom.Data, baseOff + 16, 0x08000200u);
            WriteU32Le(rom.Data, baseOff + 20, 0x08000300u);
            WriteU32Le(rom.Data, baseOff + 24, 0x08000400u);

            uint off1 = baseOff + 28;
            rom.Data[off1 + 3] = 0x86;
            WriteU32Le(rom.Data, off1 + 4,  0x08000150u); // different OBJ
            WriteU32Le(rom.Data, off1 + 16, 0x08000250u); // different BG
            WriteU32Le(rom.Data, off1 + 20, 0x08000300u);
            WriteU32Le(rom.Data, off1 + 24, 0x08000400u);

            rom.Data[off1 + 28 + 3] = 0x80;

            List<int> objSlots, bgSlots;
            List<MagicFrameMeta> frames;
            MagicEffectExportCore.ExportMagicScriptLines(
                rom, baseOff, "t_", false,
                out objSlots, out bgSlots, out frames);

            Assert.Equal(2, MagicEffectExportCore.CountUniqueObjSlots(frames));
            Assert.Equal(2, MagicEffectExportCore.CountUniqueBgSlots(frames));

            // Shared hash: OBJ slots 0,2; BG slots 1,3 (interleaved in shared space).
            Assert.Equal(0, objSlots[0]);
            Assert.Equal(1, bgSlots[0]); // BG follows OBJ in shared hash
            Assert.Equal(2, objSlots[1]);
            Assert.Equal(3, bgSlots[1]);
        }
        // ---------------------------------------------------------------
        // Helpers (mirrors MagicEffectRendererCoreTests helpers)
        // ---------------------------------------------------------------

        static ROM MakeMinimalRom() => MakeMinimalRomSize(0x1100000);

        static ROM MakeMinimalRomSize(int size)
        {
            var rom = new ROM();
            rom.LoadLow("synthetic.gba", new byte[size], "");
            return rom;
        }

        static ROM MakeSyntheticRomWithFrameData()
        {
            var rom = MakeMinimalRomSize(0x1100000);
            return rom;
        }

        static void Build86Record(byte[] data, uint baseOffset, int frameCount)
        {
            for (int i = 0; i < frameCount; i++)
            {
                uint off = baseOffset + (uint)(i * 28);
                if (off + 28 > data.Length) break;
                data[off + 3] = 0x86;
                WriteU32Le(data, off + 4,  0x08000100u);
                WriteU32Le(data, off + 8,  0u);
                WriteU32Le(data, off + 12, 0u);
                WriteU32Le(data, off + 16, 0x08000200u);
                WriteU32Le(data, off + 20, 0x08000300u);
                WriteU32Le(data, off + 24, 0x08000400u);
            }
        }

        static void PlantSmallLZ77(byte[] data, uint offset, int uncompressedSize)
        {
            if (offset + 4 + uncompressedSize > data.Length) return;
            data[offset + 0] = 0x10;
            data[offset + 1] = (byte)(uncompressedSize & 0xFF);
            data[offset + 2] = (byte)((uncompressedSize >> 8) & 0xFF);
            data[offset + 3] = (byte)((uncompressedSize >> 16) & 0xFF);
            int remaining = uncompressedSize;
            int pos = (int)offset + 4;
            while (remaining > 0 && pos + 1 < data.Length)
            {
                int count = remaining < 8 ? remaining : 8;
                data[pos++] = 0x00; // 8 literals flag
                for (int k = 0; k < count && pos < data.Length; k++, remaining--)
                    data[pos++] = 0x00;
                for (int k = count; k < 8 && remaining > 0; k++) remaining--;
            }
        }

        static void PlantRawPalette(byte[] data, uint offset)
        {
            if (offset + 0x20 > data.Length) return;
            for (int i = 0; i < 16; i++)
            {
                ushort color = (i == 0) ? (ushort)0 : (ushort)0x001F;
                data[offset + i * 2 + 0] = (byte)(color & 0xFF);
                data[offset + i * 2 + 1] = (byte)((color >> 8) & 0xFF);
            }
        }

        static void WriteU32Le(byte[] data, uint offset, uint value)
        {
            if (offset + 4 > data.Length) return;
            data[offset + 0] = (byte)(value & 0xFF);
            data[offset + 1] = (byte)((value >> 8) & 0xFF);
            data[offset + 2] = (byte)((value >> 16) & 0xFF);
            data[offset + 3] = (byte)((value >> 24) & 0xFF);
        }

        // ---------------------------------------------------------------
        // CSA Creator (#886) — ExportMagicScriptLines(isCsa=true)
        // ---------------------------------------------------------------

        // Build a 32-byte CSA frame record (adds +28 TSA pointer).
        static void BuildCsa86Record(byte[] data, uint baseOffset, int frameCount)
        {
            for (int i = 0; i < frameCount; i++)
            {
                uint off = baseOffset + (uint)(i * 32);
                if (off + 32 > data.Length) break;
                data[off + 3] = 0x86;
                WriteU32Le(data, off + 4,  0x08000100u); // OBJ img
                WriteU32Le(data, off + 8,  0u);          // OAMAbsoStart
                WriteU32Le(data, off + 12, 0u);          // OAMBGAbsoStart
                WriteU32Le(data, off + 16, 0x08000200u); // BG img
                WriteU32Le(data, off + 20, 0x08000300u); // OBJ pal
                WriteU32Le(data, off + 24, 0x08000400u); // BG pal
                WriteU32Le(data, off + 28, 0x08000500u); // TSA (+28 — CSA only)
            }
        }

        [Fact]
        public void ExportMagicScriptLines_CsaMode_Reads28TsaPointer()
        {
            // CSA frame records are 32 bytes: the +28 TSA pointer must be read
            // into MagicFrameMeta.RawBgTsaPtr and BgTsaOffset.
            var rom = MakeMinimalRom();
            uint baseOff = 0xC00u;
            BuildCsa86Record(rom.Data, baseOff, 1);
            rom.Data[baseOff + 32 + 3] = 0x80; // terminator after one 32-byte frame

            List<int> objSlots, bgSlots;
            List<MagicFrameMeta> frames;
            MagicEffectExportCore.ExportMagicScriptLines(
                rom, baseOff, "t_", false,
                out objSlots, out bgSlots, out frames,
                isCsa: true);

            Assert.Single(frames);
            Assert.Equal(0x08000500u, frames[0].RawBgTsaPtr);
            // BgTsaOffset = toOffset(0x08000500) = 0x500
            Assert.Equal(0x500u, frames[0].BgTsaOffset);
        }

        [Fact]
        public void ExportMagicScriptLines_CsaMode_BgHashUsesBgPlusTsa()
        {
            // CSA BG hash = rawBgPtr + rawTsaPtr (mirrors WF ExportBGFrameImage).
            // Two CSA frames sharing the same BG+TSA pointers → same BG slot.
            var rom = MakeMinimalRom();
            uint baseOff = 0xD00u;
            BuildCsa86Record(rom.Data, baseOff, 2); // 2 frames, both same ptrs
            rom.Data[baseOff + 64 + 3] = 0x80;

            List<int> objSlots, bgSlots;
            List<MagicFrameMeta> frames;
            MagicEffectExportCore.ExportMagicScriptLines(
                rom, baseOff, "t_", false,
                out objSlots, out bgSlots, out frames,
                isCsa: true);

            Assert.Equal(2, frames.Count);
            // Both frames have the same BG+TSA hash → same BG slot index.
            Assert.Equal(2, bgSlots.Count);
            Assert.Equal(bgSlots[0], bgSlots[1]);
        }

        [Fact]
        public void ExportMagicScriptLines_CsaMode_DiffBgOrTsa_DiffSlot()
        {
            // Two CSA frames differing only in TSA pointer → different BG slots.
            var rom = MakeMinimalRom();
            uint baseOff = 0xE00u;

            // Frame 0: BG=0x08000200, TSA=0x08000500
            uint off0 = baseOff;
            rom.Data[off0 + 3] = 0x86;
            WriteU32Le(rom.Data, off0 + 4,  0x08000100u);
            WriteU32Le(rom.Data, off0 + 16, 0x08000200u);
            WriteU32Le(rom.Data, off0 + 20, 0x08000300u);
            WriteU32Le(rom.Data, off0 + 24, 0x08000400u);
            WriteU32Le(rom.Data, off0 + 28, 0x08000500u); // TSA A

            // Frame 1: same BG, different TSA
            uint off1 = baseOff + 32;
            rom.Data[off1 + 3] = 0x86;
            WriteU32Le(rom.Data, off1 + 4,  0x08000100u);
            WriteU32Le(rom.Data, off1 + 16, 0x08000200u); // same BG
            WriteU32Le(rom.Data, off1 + 20, 0x08000300u);
            WriteU32Le(rom.Data, off1 + 24, 0x08000400u);
            WriteU32Le(rom.Data, off1 + 28, 0x08000600u); // TSA B (different)

            rom.Data[off1 + 32 + 3] = 0x80;

            List<int> objSlots, bgSlots;
            List<MagicFrameMeta> frames;
            MagicEffectExportCore.ExportMagicScriptLines(
                rom, baseOff, "t_", false,
                out objSlots, out bgSlots, out frames,
                isCsa: true);

            Assert.Equal(2, frames.Count);
            Assert.NotEqual(bgSlots[0], bgSlots[1]);
        }

        [Fact]
        public void ExportMagicScriptLines_CsaMode_StrideIs32Bytes()
        {
            // CSA frame stride is 32 bytes; a terminator placed at offset+32
            // (not offset+28) must be found after exactly one frame.
            var rom = MakeMinimalRom();
            uint baseOff = 0xF00u;
            BuildCsa86Record(rom.Data, baseOff, 1);
            // Terminator at baseOff+32 (would be missed if stride=28 were used).
            rom.Data[baseOff + 32 + 3] = 0x80;

            List<int> objSlots, bgSlots;
            List<MagicFrameMeta> frames;
            MagicEffectExportCore.ExportMagicScriptLines(
                rom, baseOff, "t_", false,
                out objSlots, out bgSlots, out frames,
                isCsa: true);

            Assert.Single(frames);
        }

        [Fact]
        public void CountUniqueBgSlots_CsaMode_UsesHashBgPlusTsa()
        {
            // isCsa=true: hash = rawBgPtr + rawBgTsaPtr.
            // Two frames with same BG but different TSA → 2 unique BG slots.
            var frames = new List<MagicFrameMeta>
            {
                new MagicFrameMeta { RawBgImagePtr = 0x08000200u, RawBgTsaPtr = 0x08000500u },
                new MagicFrameMeta { RawBgImagePtr = 0x08000200u, RawBgTsaPtr = 0x08000600u },
            };
            Assert.Equal(2, MagicEffectExportCore.CountUniqueBgSlots(frames, isCsa: true));
        }

        [Fact]
        public void CountUniqueBgSlots_CsaMode_SameBgSameTsa_Returns1()
        {
            var frames = new List<MagicFrameMeta>
            {
                new MagicFrameMeta { RawBgImagePtr = 0x08000200u, RawBgTsaPtr = 0x08000500u },
                new MagicFrameMeta { RawBgImagePtr = 0x08000200u, RawBgTsaPtr = 0x08000500u },
            };
            Assert.Equal(1, MagicEffectExportCore.CountUniqueBgSlots(frames, isCsa: true));
        }

        [Fact]
        public void RenderCsaBgFrameSlot_ValidSyntheticRom_ReturnsCorrectDims()
        {
            // Synthetic CSA frame with LZ77 BG tilesheet + LZ77 TSA.
            // 240x160 output: CalcHeightByTsa(240, tsaLen) >= 160 → height=160.
            var prevRom = CoreState.ROM;
            var prevSvc = CoreState.ImageService;
            try
            {
                var rom = MakeMinimalRomSize(0x1100000);
                CoreState.ROM = rom;
                CoreState.ImageService = new StubImageService();

                // BG tilesheet at 0x500 (240x160 = 38400 pixels, 4bpp = 19200 bytes of tile data).
                PlantSmallLZ77(rom.Data, 0x500u, 19200);
                // TSA at 0x2000: 240/8 * 160/8 = 30*20 = 600 entries * 2 bytes = 1200 bytes.
                // CalcHeightByTsa(240, 1200) = (1200/2 / 30) * 8 = 160
                PlantSmallLZ77(rom.Data, 0x2000u, 1200);
                PlantRawPalette(rom.Data, 0x800u);

                var frames = new List<MagicFrameMeta>
                {
                    new MagicFrameMeta
                    {
                        RawObjImagePtr  = 0x08000100u,
                        OamAbsoStart    = 0u,
                        RawBgImagePtr   = 0x08000500u,
                        BgImageOffset   = 0x500u,
                        RawBgTsaPtr     = 0x08002000u,
                        BgTsaOffset     = 0x2000u,
                        BgPaletteOffset = 0x800u,
                        RawBgPalPtr     = 0x08000800u,
                    }
                };

                // Slot index for the unique BG in CSA mode:
                // OBJ hash added first (slot 0), BG hash = rawBg+rawTsa → slot 1.
                var img = MagicEffectExportCore.RenderCsaBgFrameSlot(rom, frames, 1);

                Assert.NotNull(img);
                Assert.Equal(MagicEffectExportCore.CSA_BG_EXPORT_WIDTH, img.Width);
                Assert.Equal(MagicEffectExportCore.CSA_BG_EXPORT_HEIGHT_FULL, img.Height);
            }
            finally { CoreState.ROM = prevRom; CoreState.ImageService = prevSvc; }
        }

        [Fact]
        public void RenderCsaBgFrameSlot_SmallTsa_Returns64Height()
        {
            // When CalcHeightByTsa < 160 → output height = 64.
            // TSA for 240x64: 30*8=240 entries * 2 = 480 bytes.
            // CalcHeightByTsa(240, 480) = (480/2 / 30)*8 = 64 < 160 → height=64.
            var prevRom = CoreState.ROM;
            var prevSvc = CoreState.ImageService;
            try
            {
                var rom = MakeMinimalRomSize(0x1100000);
                CoreState.ROM = rom;
                CoreState.ImageService = new StubImageService();

                PlantSmallLZ77(rom.Data, 0x500u, 8192);  // BG tilesheet
                PlantSmallLZ77(rom.Data, 0x3000u, 480);  // small TSA → 64px height
                PlantRawPalette(rom.Data, 0x800u);

                var frames = new List<MagicFrameMeta>
                {
                    new MagicFrameMeta
                    {
                        RawObjImagePtr  = 0x08000100u,
                        OamAbsoStart    = 0u,
                        RawBgImagePtr   = 0x08000500u,
                        BgImageOffset   = 0x500u,
                        RawBgTsaPtr     = 0x08003000u,
                        BgTsaOffset     = 0x3000u,
                        BgPaletteOffset = 0x800u,
                        RawBgPalPtr     = 0x08000800u,
                    }
                };

                var img = MagicEffectExportCore.RenderCsaBgFrameSlot(rom, frames, 1);

                Assert.NotNull(img);
                Assert.Equal(MagicEffectExportCore.CSA_BG_EXPORT_WIDTH, img.Width);
                Assert.Equal(MagicEffectExportCore.CSA_BG_EXPORT_HEIGHT_SMALL, img.Height);
            }
            finally { CoreState.ROM = prevRom; CoreState.ImageService = prevSvc; }
        }

        [Fact]
        public void RenderCsaBgFrameSlot_OutOfRangeSlot_ReturnsNull()
        {
            var prevSvc = CoreState.ImageService;
            try
            {
                CoreState.ImageService = new StubImageService();
                var frames = new List<MagicFrameMeta>
                {
                    new MagicFrameMeta
                    {
                        RawBgImagePtr = 0x08000200u, RawBgTsaPtr = 0x08000500u
                    }
                };
                // Only slot 1 (after OBJ slot 0) — requesting slot 5 → null.
                var result = MagicEffectExportCore.RenderCsaBgFrameSlot(
                    MakeMinimalRom(), frames, 5);
                Assert.Null(result);
            }
            finally { CoreState.ImageService = prevSvc; }
        }

        [Fact]
        public void ExportMagicScriptLines_CsaObjLines_MatchFEditorFormat()
        {
            // OBJ render and script lines for CSA must be identical to FEditor
            // (same "O  p- basename_o_NNN.png" prefix).
            var rom = MakeMinimalRom();
            uint baseOff = 0x1000u;
            BuildCsa86Record(rom.Data, baseOff, 1);
            rom.Data[baseOff + 32 + 3] = 0x80;

            List<int> objSlots, bgSlots;
            List<MagicFrameMeta> frames;
            var csaLines = MagicEffectExportCore.ExportMagicScriptLines(
                rom, baseOff, "t_", false,
                out objSlots, out bgSlots, out frames,
                isCsa: true);

            var objLine = csaLines.Find(l => l.Kind == MagicScriptLineKind.ObjImage);
            Assert.NotNull(objLine);
            Assert.StartsWith("O  p- t_o_", objLine.Text);
        }


        // ---------------------------------------------------------------
        // #886 integration: shared-space BG slot index matches filename + renderer
        // ---------------------------------------------------------------

        /// <summary>
        /// Regression test for #886: the BG export loop MUST pass the SHARED-space
        /// slot index (from sharedBgSlots) to RenderCsaBgFrameSlot, NOT a 0-based BG
        /// counter. With 1 OBJ + 1 BG frame, the BG gets shared-space index 1
        /// (OBJ took slot 0), the .txt script says "b_001.png", and the renderer
        /// must be invoked with slotIndex=1 (not 0).
        ///
        /// The fix in DoExport: enumerate uniqueBgSharedIndices from sharedBgSlots
        /// (dedup, insertion-order) and pass each sharedSlot to RenderCsaBgFrameSlot.
        /// </summary>
        [Fact]
        public void CsaExport_BgSharedSlotIndex_MatchesFilenameAndRenderer_Integration()
        {
            var prevRom = CoreState.ROM;
            var prevSvc = CoreState.ImageService;
            try
            {
                var rom = MakeMinimalRomSize(0x1100000);
                CoreState.ROM = rom;
                CoreState.ImageService = new StubImageService();

                // Plant LZ77 BG tilesheet at 0x500 and TSA at 0x2000.
                // TSA for 240x160: 30*20=600 entries * 2 = 1200 bytes.
                PlantSmallLZ77(rom.Data, 0x500u, 19200);
                PlantSmallLZ77(rom.Data, 0x2000u, 1200);
                PlantRawPalette(rom.Data, 0x800u);

                // One CSA frame: distinct OBJ ptr + BG ptr + TSA ptr.
                var frames = new System.Collections.Generic.List<MagicFrameMeta>
                {
                    new MagicFrameMeta
                    {
                        RawObjImagePtr  = 0x08000100u,  // OBJ hash → slot 0
                        OamAbsoStart    = 0u,
                        RawBgImagePtr   = 0x08000500u,  // BG+TSA hash → slot 1
                        BgImageOffset   = 0x500u,
                        RawBgTsaPtr     = 0x08002000u,
                        BgTsaOffset     = 0x2000u,
                        BgPaletteOffset = 0x800u,
                        RawBgPalPtr     = 0x08000800u,
                    }
                };

                // Step 1: ExportMagicScriptLines returns sharedBgSlots.
                // For 1 OBJ + 1 BG, the BG must occupy shared-space index 1 (not 0).
                // This is the same slot index used in the "b_001.png" filename.
                // Build the frame data in ROM for the scan.
                uint baseOff = 0x1200u;
                BuildCsa86Record(rom.Data, baseOff, 1);
                // Override BG+TSA pointers in the on-ROM record.
                WriteU32Le(rom.Data, baseOff + 16, 0x08000500u);
                WriteU32Le(rom.Data, baseOff + 28, 0x08002000u);
                rom.Data[baseOff + 32 + 3] = 0x80;

                System.Collections.Generic.List<int> sharedObjSlots, sharedBgSlots;
                System.Collections.Generic.List<MagicFrameMeta> scannedFrames;
                var scriptLines = MagicEffectExportCore.ExportMagicScriptLines(
                    rom, baseOff, "t_", false,
                    out sharedObjSlots, out sharedBgSlots, out scannedFrames,
                    isCsa: true);

                // Assert: OBJ got slot 0, BG got slot 1 (shared-space, not 0-based BG counter).
                Assert.Single(sharedObjSlots);
                Assert.Equal(0, sharedObjSlots[0]);
                Assert.Single(sharedBgSlots);
                Assert.Equal(1, sharedBgSlots[0]); // NOT 0 — the bug was passing 0 here

                // Assert: the .txt script references "b_001.png" (slot 1, not b_000).
                var bgLine = scriptLines.Find(l => l.Kind == MagicScriptLineKind.BgImage);
                Assert.NotNull(bgLine);
                Assert.Contains("b_001.png", bgLine.Text);
                Assert.DoesNotContain("b_000.png", bgLine.Text);

                // Step 2: RenderCsaBgFrameSlot with the CORRECT shared-space index (1)
                // must succeed, while with the buggy index (0) it must return null
                // (slot 0 is the OBJ hash, not a BG hash).
                // This proves that passing s=0 (old buggy code) renders the WRONG slot.
                var imgCorrect = MagicEffectExportCore.RenderCsaBgFrameSlot(
                    rom, scannedFrames, sharedBgSlots[0]); // correct: slot 1
                Assert.NotNull(imgCorrect);

                var imgWrong = MagicEffectExportCore.RenderCsaBgFrameSlot(
                    rom, scannedFrames, 0); // buggy: slot 0 (OBJ slot) → no BG frame at slot 0
                Assert.Null(imgWrong); // slot 0 is the OBJ hash, BG is at slot 1

                // Step 3: Simulate the fixed export-loop logic — deduplicate sharedBgSlots
                // to get unique shared-space indices (mirrors the view's DoExport fix).
                var seenBgSlots = new System.Collections.Generic.HashSet<int>();
                var uniqueBgSharedIndices = new System.Collections.Generic.List<int>();
                foreach (int si in sharedBgSlots)
                {
                    if (seenBgSlots.Add(si))
                        uniqueBgSharedIndices.Add(si);
                }
                Assert.Single(uniqueBgSharedIndices);
                Assert.Equal(1, uniqueBgSharedIndices[0]); // shared-space index, not 0
            }
            finally { CoreState.ROM = prevRom; CoreState.ImageService = prevSvc; }
        }

        // ---------------------------------------------------------------
        // #1021 — RenderCsaFramePreview (live 240×128 CSA composite)
        // ---------------------------------------------------------------

        /// <summary>
        /// Plant a complete 2-frame CSA frame-data stream where frame 1's shared
        /// BG slot index is &gt; 1 (proving slotIndex ≠ frameIndex). Both frames
        /// have planted LZ77 OBJ/BG/TSA + raw palettes so the render succeeds.
        ///   Frame 0: OBJ 0x500, BG 0x600, TSA 0x2000  → OBJ slot 0, BG slot 1.
        ///   Frame 1: OBJ 0x900 (distinct), BG 0xA00, TSA 0x4000 → OBJ slot 2, BG slot 3.
        /// </summary>
        static uint PlantTwoFrameCsaStream(ROM rom, uint baseOff)
        {
            byte[] d = rom.Data;

            // Frame 0 (32-byte CSA record).
            d[baseOff + 3] = 0x86;
            WriteU32Le(d, baseOff + 4,  0x08000500u); // OBJ img
            WriteU32Le(d, baseOff + 8,  0u);          // OAMAbsoStart (front)
            WriteU32Le(d, baseOff + 12, 0u);          // OAMBGAbsoStart (back)
            WriteU32Le(d, baseOff + 16, 0x08000600u); // BG img
            WriteU32Le(d, baseOff + 20, 0x08000700u); // OBJ pal
            WriteU32Le(d, baseOff + 24, 0x08000800u); // BG pal
            WriteU32Le(d, baseOff + 28, 0x08002000u); // TSA (+28)

            // Frame 1 (distinct OBJ/BG/TSA → its shared BG slot is 3, NOT 1).
            uint off1 = baseOff + 32;
            d[off1 + 3] = 0x86;
            WriteU32Le(d, off1 + 4,  0x08000900u);
            WriteU32Le(d, off1 + 8,  0u);
            WriteU32Le(d, off1 + 12, 0u);
            WriteU32Le(d, off1 + 16, 0x08000A00u);
            WriteU32Le(d, off1 + 20, 0x08000700u);
            WriteU32Le(d, off1 + 24, 0x08000800u);
            WriteU32Le(d, off1 + 28, 0x08004000u);

            // Terminator after the 2nd 32-byte frame.
            d[off1 + 32 + 3] = 0x80;

            // Plant LZ77 + palettes for BOTH frames.
            // BG tilesheet (19200 bytes), TSA for 240x160 (1200 bytes) → height 160.
            PlantSmallLZ77(d, 0x500u, 8192);    // OBJ frame 0
            PlantSmallLZ77(d, 0x600u, 19200);   // BG frame 0
            PlantSmallLZ77(d, 0x2000u, 1200);   // TSA frame 0
            PlantSmallLZ77(d, 0x900u, 8192);    // OBJ frame 1
            PlantSmallLZ77(d, 0xA00u, 19200);   // BG frame 1
            PlantSmallLZ77(d, 0x4000u, 1200);   // TSA frame 1
            PlantRawPalette(d, 0x700u);         // OBJ pal
            PlantRawPalette(d, 0x800u);         // BG pal

            return baseOff;
        }

        [Fact]
        public void RenderCsaFramePreview_Frame1SlotGreaterThan1_UsesSharedSlotMapping()
        {
            // slot-vs-frame regression: frame 1's shared BG slot is 3 (> 1), so
            // passing frameIndex 1 directly as a slot index would render the WRONG
            // slot. RenderCsaFramePreview must map frame 1 → its shared slots via
            // ExportMagicScriptLines (isCsa:true) and still produce a non-null image.
            var prevRom = CoreState.ROM;
            var prevSvc = CoreState.ImageService;
            try
            {
                var rom = MakeMinimalRomSize(0x1100000);
                CoreState.ROM = rom;
                CoreState.ImageService = new StubImageService();

                uint baseOff = 0x10000u;
                PlantTwoFrameCsaStream(rom, baseOff);

                // Cross-check the shared-slot mapping the renderer relies on:
                // frame 1 maps to OBJ slot 2 + BG slot 3 (NOT slot 1).
                System.Collections.Generic.List<int> objSlots, bgSlots;
                System.Collections.Generic.List<MagicFrameMeta> frames;
                MagicEffectExportCore.ExportMagicScriptLines(
                    rom, baseOff, "t_", false,
                    out objSlots, out bgSlots, out frames, isCsa: true);
                Assert.Equal(2, frames.Count);
                Assert.Equal(2, objSlots[1]); // frame 1 OBJ shared slot
                Assert.Equal(3, bgSlots[1]);  // frame 1 BG shared slot (> 1)

                // Render frame 1 → non-null (proves the mapping, not frameIndex-as-slot).
                var img = MagicEffectExportCore.RenderCsaFramePreview(
                    rom, baseOff, 1u, 0u, 0u);
                Assert.NotNull(img);
            }
            finally { CoreState.ROM = prevRom; CoreState.ImageService = prevSvc; }
        }

        [Fact]
        public void RenderCsaFramePreview_CsaAwareScan_PopulatesBgTsaForFrame1()
        {
            // CSA-aware scan: ExportMagicScriptLines(isCsa:true) reads the +28 BG TSA
            // field for frame 1 (proving the 32-byte CSA stride, not 28-byte FEditor).
            var prevRom = CoreState.ROM;
            var prevSvc = CoreState.ImageService;
            try
            {
                var rom = MakeMinimalRomSize(0x1100000);
                CoreState.ROM = rom;
                CoreState.ImageService = new StubImageService();

                uint baseOff = 0x11000u;
                PlantTwoFrameCsaStream(rom, baseOff);

                System.Collections.Generic.List<int> objSlots, bgSlots;
                System.Collections.Generic.List<MagicFrameMeta> frames;
                MagicEffectExportCore.ExportMagicScriptLines(
                    rom, baseOff, "t_", false,
                    out objSlots, out bgSlots, out frames, isCsa: true);

                Assert.Equal(2, frames.Count);
                // Frame 1's +28 TSA pointer must be populated (CSA stride proof).
                Assert.Equal(0x08004000u, frames[1].RawBgTsaPtr);
                Assert.Equal(0x4000u, frames[1].BgTsaOffset);
            }
            finally { CoreState.ROM = prevRom; CoreState.ImageService = prevSvc; }
        }

        [Fact]
        public void RenderCsaFramePreview_CompositeIs240x128()
        {
            // composite dims: the live preview is a 240×128 image regardless of the
            // BG/OBJ source dims.
            var prevRom = CoreState.ROM;
            var prevSvc = CoreState.ImageService;
            try
            {
                var rom = MakeMinimalRomSize(0x1100000);
                CoreState.ROM = rom;
                CoreState.ImageService = new StubImageService();

                uint baseOff = 0x12000u;
                PlantTwoFrameCsaStream(rom, baseOff);

                var img = MagicEffectExportCore.RenderCsaFramePreview(
                    rom, baseOff, 0u, 0u, 0u);

                Assert.NotNull(img);
                Assert.Equal(MagicEffectExportCore.CSA_PREVIEW_WIDTH,  img.Width);
                Assert.Equal(MagicEffectExportCore.CSA_PREVIEW_HEIGHT, img.Height);
                Assert.Equal(240, img.Width);
                Assert.Equal(128, img.Height);
            }
            finally { CoreState.ROM = prevRom; CoreState.ImageService = prevSvc; }
        }

        [Fact]
        public void RenderCsaFramePreview_OutOfRangeFrame_ReturnsNull()
        {
            // guards: out-of-range frameIndex → null.
            var prevRom = CoreState.ROM;
            var prevSvc = CoreState.ImageService;
            try
            {
                var rom = MakeMinimalRomSize(0x1100000);
                CoreState.ROM = rom;
                CoreState.ImageService = new StubImageService();

                uint baseOff = 0x13000u;
                PlantTwoFrameCsaStream(rom, baseOff); // only 2 frames (0,1)

                var img = MagicEffectExportCore.RenderCsaFramePreview(
                    rom, baseOff, 5u, 0u, 0u); // frame 5 doesn't exist
                Assert.Null(img);
            }
            finally { CoreState.ROM = prevRom; CoreState.ImageService = prevSvc; }
        }

        [Fact]
        public void RenderCsaFramePreview_NullRom_ReturnsNull()
        {
            // guards: null ROM → null (no throw).
            var prevSvc = CoreState.ImageService;
            try
            {
                CoreState.ImageService = new StubImageService();
                var img = MagicEffectExportCore.RenderCsaFramePreview(
                    null, 0x10000u, 0u, 0u, 0u);
                Assert.Null(img);
            }
            finally { CoreState.ImageService = prevSvc; }
        }

        [Fact]
        public void RenderCsaFramePreview_NoImageService_ReturnsNull()
        {
            // guards: no ImageService → null (no throw).
            var prevRom = CoreState.ROM;
            var prevSvc = CoreState.ImageService;
            try
            {
                var rom = MakeMinimalRomSize(0x1100000);
                CoreState.ROM = rom;
                CoreState.ImageService = null;

                uint baseOff = 0x14000u;
                PlantTwoFrameCsaStream(rom, baseOff);

                var img = MagicEffectExportCore.RenderCsaFramePreview(
                    rom, baseOff, 0u, 0u, 0u);
                Assert.Null(img);
            }
            finally { CoreState.ROM = prevRom; CoreState.ImageService = prevSvc; }
        }

        [Fact]
        public void RenderCsaFramePreview_NoMutation_RomByteIdentical()
        {
            // no-mutation: RenderCsaFramePreview is strictly read-only.
            var prevRom = CoreState.ROM;
            var prevSvc = CoreState.ImageService;
            try
            {
                var rom = MakeMinimalRomSize(0x1100000);
                CoreState.ROM = rom;
                CoreState.ImageService = new StubImageService();

                uint baseOff = 0x15000u;
                PlantTwoFrameCsaStream(rom, baseOff);

                byte[] before = (byte[])rom.Data.Clone();

                // Render both frames (and an out-of-range one) — none may mutate.
                MagicEffectExportCore.RenderCsaFramePreview(rom, baseOff, 0u, 0u, 0u);
                MagicEffectExportCore.RenderCsaFramePreview(rom, baseOff, 1u, 0u, 0u);
                MagicEffectExportCore.RenderCsaFramePreview(rom, baseOff, 9u, 0u, 0u);

                Assert.Equal(before, rom.Data);
            }
            finally { CoreState.ROM = prevRom; CoreState.ImageService = prevSvc; }
        }

        // ---------------------------------------------------------------
        // #1076 FIX 1 — slot-namespace mismatch regression
        // ---------------------------------------------------------------

        /// <summary>
        /// Plant a 3-frame CSA stream where frames 0 and 1 SHARE the same BG image
        /// pointer (0x600) but have DIFFERENT +28 BG TSA pointers (0x2000 vs 0x4000),
        /// and frame 2 is fully distinct. This makes the CSA hash namespace (BG hash
        /// = BgPtr+TsaPtr) DIVERGE from the renderer namespace
        /// (<see cref="MagicEffectExportCore.RenderObjFrameSlot"/> uses the NON-CSA BG
        /// hash = BgPtr only, so frames 0 and 1 collide to ONE BG slot):
        ///
        ///   CSA namespace:      F0 OBJ→0 BG(0x600+0x2000)→1 | F1 OBJ→2 BG(0x600+0x4000)→3 | F2 OBJ→4 BG→5
        ///   Renderer namespace: F0 OBJ→0 BG(0x600)→1        | F1 OBJ→2 BG(0x600)=slot1   | F2 OBJ→3 BG→4
        ///
        /// So the CSA <c>sharedObjSlots[2] = 4</c> points PAST the renderer's OBJ slots
        /// {0,2,3} — the OLD code (indexing sharedObjSlots) would render OBJ slot 4,
        /// find no frame, and silently drop the OBJ layer.
        /// </summary>
        static uint PlantSharedBgDifferentTsaStream(ROM rom, uint baseOff)
        {
            byte[] d = rom.Data;

            // Frame 0: OBJ 0x500, BG 0x600, TSA 0x2000.
            uint f0 = baseOff;
            d[f0 + 3] = 0x86;
            WriteU32Le(d, f0 + 4,  0x08000500u);
            WriteU32Le(d, f0 + 8,  0u);
            WriteU32Le(d, f0 + 12, 0u);
            WriteU32Le(d, f0 + 16, 0x08000600u); // BG (SHARED)
            WriteU32Le(d, f0 + 20, 0x08000700u);
            WriteU32Le(d, f0 + 24, 0x08000800u);
            WriteU32Le(d, f0 + 28, 0x08002000u); // TSA A

            // Frame 1: OBJ 0x900 (distinct), BG 0x600 (SAME as F0), TSA 0x4000 (different).
            uint f1 = baseOff + 32;
            d[f1 + 3] = 0x86;
            WriteU32Le(d, f1 + 4,  0x08000900u);
            WriteU32Le(d, f1 + 8,  0u);
            WriteU32Le(d, f1 + 12, 0u);
            WriteU32Le(d, f1 + 16, 0x08000600u); // BG (SHARED with F0)
            WriteU32Le(d, f1 + 20, 0x08000700u);
            WriteU32Le(d, f1 + 24, 0x08000800u);
            WriteU32Le(d, f1 + 28, 0x08004000u); // TSA B (different → CSA inserts an extra BG slot)

            // Frame 2: fully distinct OBJ 0xD00, BG 0xE00, TSA 0x6000.
            uint f2 = baseOff + 64;
            d[f2 + 3] = 0x86;
            WriteU32Le(d, f2 + 4,  0x08000D00u);
            WriteU32Le(d, f2 + 8,  0u);
            WriteU32Le(d, f2 + 12, 0u);
            WriteU32Le(d, f2 + 16, 0x08000E00u);
            WriteU32Le(d, f2 + 20, 0x08000700u);
            WriteU32Le(d, f2 + 24, 0x08000800u);
            WriteU32Le(d, f2 + 28, 0x08006000u);

            // Terminator after the 3rd 32-byte frame.
            d[f2 + 32 + 3] = 0x80;

            // Plant LZ77 + palettes so each frame's OBJ/BG/TSA render succeeds.
            PlantSmallLZ77(d, 0x500u, 8192);    // OBJ F0
            PlantSmallLZ77(d, 0x600u, 19200);   // BG (shared F0+F1)
            PlantSmallLZ77(d, 0x2000u, 1200);   // TSA A
            PlantSmallLZ77(d, 0x900u, 8192);    // OBJ F1
            PlantSmallLZ77(d, 0x4000u, 1200);   // TSA B
            PlantSmallLZ77(d, 0xD00u, 8192);    // OBJ F2
            PlantSmallLZ77(d, 0xE00u, 19200);   // BG F2
            PlantSmallLZ77(d, 0x6000u, 1200);   // TSA F2
            PlantRawPalette(d, 0x700u);         // OBJ pal (shared)
            PlantRawPalette(d, 0x800u);         // BG pal (shared)

            return baseOff;
        }

        [Fact]
        public void RenderCsaFramePreview_SharedBgDifferentTsa_LaterFrame_KeepsObjLayer()
        {
            // #1076 FIX 1 regression: when earlier CSA frames share a BG image but
            // differ in the +28 TSA pointer, the CSA hash namespace diverges from the
            // renderer namespace. The OLD code (indexing sharedObjSlots[frameIndex] and
            // passing it to RenderObjFrameSlot over the FULL frame list) would render a
            // slot that does NOT exist in the renderer namespace → OBJ layer dropped.
            // The fix renders the SELECTED frame via a single-frame list (OBJ slot 0).
            var prevRom = CoreState.ROM;
            var prevSvc = CoreState.ImageService;
            try
            {
                var rom = MakeMinimalRomSize(0x1100000);
                CoreState.ROM = rom;
                CoreState.ImageService = new StubImageService();

                uint baseOff = 0x20000u;
                PlantSharedBgDifferentTsaStream(rom, baseOff);

                System.Collections.Generic.List<int> objSlots, bgSlots;
                System.Collections.Generic.List<MagicFrameMeta> frames;
                MagicEffectExportCore.ExportMagicScriptLines(
                    rom, baseOff, "t_", false,
                    out objSlots, out bgSlots, out frames, isCsa: true);

                // Prove the divergence the CLI flagged: the CSA scan gives frame 2's
                // OBJ shared slot = 4 (because F1's distinct-TSA BG took an extra slot).
                Assert.Equal(3, frames.Count);
                Assert.Equal(4, objSlots[2]);

                // OLD-code path: passing the CSA slot index (4) to RenderObjFrameSlot
                // over the FULL list renders NOTHING — slot 4 is past the renderer's
                // OBJ slots {0,2,3} (F0,F1 share a BG slot in the non-CSA namespace).
                var objViaCsaSlot = MagicEffectExportCore.RenderObjFrameSlot(
                    rom, frames, objSlots[2], 0u, 0u);
                Assert.Null(objViaCsaSlot); // OBJ layer would be SILENTLY DROPPED

                // FIX path: render frame 2's OBJ via a single-frame list at slot 0.
                var single = new System.Collections.Generic.List<MagicFrameMeta> { frames[2] };
                var objViaSingle = MagicEffectExportCore.RenderObjFrameSlot(
                    rom, single, 0, 0u, 0u);
                Assert.NotNull(objViaSingle); // OBJ layer rendered correctly

                // End-to-end: RenderCsaFramePreview for the later frame returns a
                // non-null composite (no longer drops the OBJ layer).
                var img = MagicEffectExportCore.RenderCsaFramePreview(
                    rom, baseOff, 2u, 0u, 0u);
                Assert.NotNull(img);
            }
            finally { CoreState.ROM = prevRom; CoreState.ImageService = prevSvc; }
        }

        // ---------------------------------------------------------------
        // #1076 FIX 2 — bgPalette[0] OPAQUE canvas fill (WF parity)
        // ---------------------------------------------------------------

        /// <summary>Plant a 16-color BG palette whose color 0 is a distinctive
        /// (non-black) GBA color, so the canvas fill is detectable. The
        /// StubImageService maps each 5-bit channel via <c>&lt;&lt;3</c>.</summary>
        static void PlantBgPaletteWithColor0(byte[] data, uint offset, ushort gbaColor0)
        {
            if (offset + 0x20 > data.Length) return;
            data[offset + 0] = (byte)(gbaColor0 & 0xFF);
            data[offset + 1] = (byte)((gbaColor0 >> 8) & 0xFF);
            for (int i = 1; i < 16; i++)
            {
                // Remaining colors: opaque red so any BG-covered pixel differs from fill.
                ushort c = 0x001F;
                data[offset + i * 2 + 0] = (byte)(c & 0xFF);
                data[offset + i * 2 + 1] = (byte)((c >> 8) & 0xFF);
            }
        }

        [Fact]
        public void RenderCsaFramePreview_CanvasBackground_IsBgPalette0_NotTransparentBlack()
        {
            // #1076 FIX 2: WF fills the 240×128 canvas with bgPalette[0] (OPAQUE)
            // BEFORE drawing the BG. With a 64px-tall BG (small TSA), the bottom rows
            // (64..127) are NOT covered by the BG blit, so they must carry the
            // palette[0] fill — NOT transparent black (all-zero RGBA).
            var prevRom = CoreState.ROM;
            var prevSvc = CoreState.ImageService;
            try
            {
                var rom = MakeMinimalRomSize(0x1100000);
                CoreState.ROM = rom;
                CoreState.ImageService = new StubImageService();

                uint baseOff = 0x30000u;
                byte[] d = rom.Data;

                // One CSA frame with a SMALL TSA (480 bytes → 64px BG height).
                d[baseOff + 3] = 0x86;
                WriteU32Le(d, baseOff + 4,  0x08000500u); // OBJ img
                WriteU32Le(d, baseOff + 8,  0u);
                WriteU32Le(d, baseOff + 12, 0u);
                WriteU32Le(d, baseOff + 16, 0x08000600u); // BG img
                WriteU32Le(d, baseOff + 20, 0x08000700u); // OBJ pal
                WriteU32Le(d, baseOff + 24, 0x08000800u); // BG pal
                WriteU32Le(d, baseOff + 28, 0x08003000u); // TSA (small → 64px)
                d[baseOff + 32 + 3] = 0x80;               // terminator

                PlantSmallLZ77(d, 0x500u, 8192);   // OBJ tilesheet
                PlantSmallLZ77(d, 0x600u, 8192);   // BG tilesheet
                PlantSmallLZ77(d, 0x3000u, 480);   // small TSA → 64px BG height
                PlantRawPalette(d, 0x700u);        // OBJ palette
                // Distinctive BG palette color 0 = GBA 0x03E0 (green=31) → RGBA (0,248,0,255).
                ushort gbaGreen = 0x03E0;
                PlantBgPaletteWithColor0(d, 0x800u, gbaGreen);

                var img = MagicEffectExportCore.RenderCsaFramePreview(
                    rom, baseOff, 0u, 0u, 0u);
                Assert.NotNull(img);

                byte[] px = img.GetPixelData();
                Assert.NotNull(px);

                // Expected fill RGBA from the stub's GBAColorToRGBA(<<3):
                //   r = (0x03E0 & 0x1F) << 3 = 0
                //   g = ((0x03E0 >> 5) & 0x1F) << 3 = 31 << 3 = 248
                //   b = ((0x03E0 >> 10) & 0x1F) << 3 = 0
                byte expR = 0, expG = 248, expB = 0, expA = 255;

                // Sample a pixel in the UNCOVERED region (row 100, col 120) — below the
                // 64px BG. It must be the OPAQUE palette[0] fill, not transparent black.
                int row = 100, col = 120;
                int idx = (row * MagicEffectExportCore.CSA_PREVIEW_WIDTH + col) * 4;
                Assert.Equal(expR, px[idx + 0]);
                Assert.Equal(expG, px[idx + 1]);
                Assert.Equal(expB, px[idx + 2]);
                Assert.Equal(expA, px[idx + 3]);

                // And it is explicitly NOT transparent black (the old all-zero canvas).
                bool isTransparentBlack = px[idx + 0] == 0 && px[idx + 1] == 0
                    && px[idx + 2] == 0 && px[idx + 3] == 0;
                Assert.False(isTransparentBlack);
            }
            finally { CoreState.ROM = prevRom; CoreState.ImageService = prevSvc; }
        }

    }
}
