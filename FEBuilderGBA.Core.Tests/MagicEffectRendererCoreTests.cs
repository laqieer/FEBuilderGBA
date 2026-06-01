// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for MagicEffectRendererCore — the cross-platform magic-effect
// frame renderer added for the Avalonia ImageMagicFEditorView (#852).
//
// Coverage (matches plan spec):
//   FindMagicFrame:
//     - Nth-frame selection on a synthetic 28-byte-record buffer
//     - 0x85 skip case (not counted as a frame)
//     - 00 01 00 80 continuation (does NOT stop)
//     - plain 0x80 stop
//     - out-of-range index → NOT_FOUND
//     - near-EOF buffer → no overrun/throw
//   TryReadMagicFrameHeader:
//     - all 7 fields parsed (pointers toOffset'd, +8/+12 relative)
//   CalcHeight:
//     - basic round-trip
//   RenderMagicFrame:
//     - no-magic-system (FE-gate) → null
//     - null ROM → null
//     - near-EOF frameData → no throw
//     - truncated BG/OBJ LZ77 → null
//     - truncated raw palette → null
//     - valid synthetic ROM with planted LZ77 + palette → 240×128 non-null
//
// [Collection("SharedState")] required because tests mutate
// CoreState.ROM and CoreState.ImageService.
using System;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class MagicEffectRendererCoreTests
    {
        // ---------------------------------------------------------------
        // FindMagicFrame
        // ---------------------------------------------------------------

        /// <summary>
        /// Buffer with one 0x86 record: frameIndex=0 returns offset 0.
        /// </summary>
        [Fact]
        public void FindMagicFrame_SingleFrame_ReturnsOffset0()
        {
            var rom = MakeMinimalRom();
            // Build a 28-byte 0x86 record at ROM offset 0x100.
            uint baseOffset = 0x100u;
            Build86Record(rom.Data, baseOffset, 1); // 1 frame record
            // Write plain terminator after.
            rom.Data[baseOffset + 28 + 3] = 0x80;

            uint result = MagicEffectRendererCore.FindMagicFrame(rom, baseOffset, 0);
            Assert.Equal(baseOffset, result);
        }

        /// <summary>
        /// With three 0x86 records, frameIndex=2 returns the third one.
        /// </summary>
        [Fact]
        public void FindMagicFrame_ThreeFrames_Index2_ReturnsThird()
        {
            var rom = MakeMinimalRom();
            uint baseOffset = 0x100u;
            Build86Record(rom.Data, baseOffset, 3);
            // Terminator after all three.
            rom.Data[baseOffset + 3 * 28 + 3] = 0x80;

            uint result = MagicEffectRendererCore.FindMagicFrame(rom, baseOffset, 2);
            Assert.Equal(baseOffset + 2 * 28, result);
        }

        /// <summary>
        /// A 0x85 command is skipped (not counted as a frame). The 0x86
        /// frame that follows is still index 0.
        /// </summary>
        [Fact]
        public void FindMagicFrame_SkipsX85_FrameAfterIsIndex0()
        {
            var rom = MakeMinimalRom();
            uint baseOffset = 0x100u;
            // Plant a 0x85 4-byte command at baseOffset.
            rom.Data[baseOffset + 3] = 0x85;
            // Plant a 0x86 frame record starting at baseOffset+4.
            Build86Record(rom.Data, baseOffset + 4, 1);
            // Terminator.
            rom.Data[baseOffset + 4 + 28 + 3] = 0x80;

            uint result = MagicEffectRendererCore.FindMagicFrame(rom, baseOffset, 0);
            Assert.Equal(baseOffset + 4, result);
        }

        /// <summary>
        /// Multiple consecutive 0x85 commands are all skipped.
        /// </summary>
        [Fact]
        public void FindMagicFrame_MultipleX85_AllSkipped()
        {
            var rom = MakeMinimalRom();
            uint baseOffset = 0x100u;
            // Three 0x85 commands (12 bytes total).
            rom.Data[baseOffset + 3] = 0x85;
            rom.Data[baseOffset + 7] = 0x85;
            rom.Data[baseOffset + 11] = 0x85;
            // Then one 0x86 frame.
            Build86Record(rom.Data, baseOffset + 12, 1);
            // Terminator.
            rom.Data[baseOffset + 12 + 28 + 3] = 0x80;

            uint result = MagicEffectRendererCore.FindMagicFrame(rom, baseOffset, 0);
            Assert.Equal(baseOffset + 12, result);
        }

        /// <summary>
        /// The 0x00 0x01 0x00 0x80 continuation record does NOT stop
        /// the scan; the 0x86 frame that follows is index 0.
        /// </summary>
        [Fact]
        public void FindMagicFrame_ContinuationX80_DoesNotStop()
        {
            var rom = MakeMinimalRom();
            uint baseOffset = 0x100u;
            // Plant the continuation: bytes 0x00 0x01 0x00 0x80.
            rom.Data[baseOffset + 0] = 0x00;
            rom.Data[baseOffset + 1] = 0x01;
            rom.Data[baseOffset + 2] = 0x00;
            rom.Data[baseOffset + 3] = 0x80;
            // 0x86 frame immediately after (at +4).
            Build86Record(rom.Data, baseOffset + 4, 1);
            // Plain 0x80 terminator.
            rom.Data[baseOffset + 4 + 28 + 3] = 0x80;

            uint result = MagicEffectRendererCore.FindMagicFrame(rom, baseOffset, 0);
            Assert.Equal(baseOffset + 4, result);
        }

        /// <summary>
        /// A plain 0x80 terminator (without [n+1]==0x01) stops the scan.
        /// Out-of-range index returns NOT_FOUND.
        /// </summary>
        [Fact]
        public void FindMagicFrame_PlainX80_StopsAndReturnsNotFound()
        {
            var rom = MakeMinimalRom();
            uint baseOffset = 0x100u;
            // One 0x86 frame then plain 0x80.
            Build86Record(rom.Data, baseOffset, 1);
            // Plain 0x80 terminator (at +28).
            rom.Data[baseOffset + 28 + 0] = 0x00;
            rom.Data[baseOffset + 28 + 1] = 0x00; // NOT 0x01 → plain stop
            rom.Data[baseOffset + 28 + 2] = 0x00;
            rom.Data[baseOffset + 28 + 3] = 0x80;

            // frameIndex=1 doesn't exist → NOT_FOUND.
            uint result = MagicEffectRendererCore.FindMagicFrame(rom, baseOffset, 1);
            Assert.Equal(U.NOT_FOUND, result);
        }

        /// <summary>
        /// Requesting out-of-range frameIndex on an empty stream returns NOT_FOUND.
        /// </summary>
        [Fact]
        public void FindMagicFrame_EmptyStream_OutOfRange_ReturnsNotFound()
        {
            var rom = MakeMinimalRom();
            uint baseOffset = 0x100u;
            // Immediate 0x80 terminator.
            rom.Data[baseOffset + 3] = 0x80;

            uint result = MagicEffectRendererCore.FindMagicFrame(rom, baseOffset, 0);
            Assert.Equal(U.NOT_FOUND, result);
        }

        /// <summary>
        /// Buffer near EOF: the loop must not throw on a 4-byte overrun.
        /// Returns NOT_FOUND gracefully.
        /// </summary>
        [Fact]
        public void FindMagicFrame_NearEOF_DoesNotThrow()
        {
            // ROM of exactly 0x200 bytes; place frame offset right at the boundary.
            var rom = MakeMinimalRomSize(0x200);
            uint baseOffset = (uint)(rom.Data.Length - 3); // 3 bytes before EOF

            // This must not throw (the EOF guard fires before any [n+3] read).
            uint result = MagicEffectRendererCore.FindMagicFrame(rom, baseOffset, 0);
            Assert.Equal(U.NOT_FOUND, result);
        }

        /// <summary>
        /// Null ROM returns NOT_FOUND without throwing.
        /// </summary>
        [Fact]
        public void FindMagicFrame_NullRom_ReturnsNotFound()
        {
            uint result = MagicEffectRendererCore.FindMagicFrame(null, 0x100u, 0);
            Assert.Equal(U.NOT_FOUND, result);
        }

        // ---------------------------------------------------------------
        // TryReadMagicFrameHeader
        // ---------------------------------------------------------------

        /// <summary>
        /// TryReadMagicFrameHeader reads all 7 fields. Pointers at +4/+16/+20/+24
        /// are GBA pointers (toOffset applied). Fields at +8/+12 are raw u32 relative
        /// offsets (NOT GBA pointers).
        /// Note: isSafetyPointer uses CoreState.ROM.Data.Length for bounds check,
        /// so we must set CoreState.ROM before calling.
        /// </summary>
        [Fact]
        public void TryReadMagicFrameHeader_ReadsAllSevenFields()
        {
            var prevRom = CoreState.ROM;
            try
            {
                var rom = MakeMinimalRom();
                CoreState.ROM = rom; // required for isSafetyPointer bounds check

                uint recordOff = 0x100u;

                // Build a valid 28-byte record.
                // +0: frame command header (0x86 at [+3]).
                rom.Data[recordOff + 3] = 0x86;
                // +4: objImagePointer — GBA pointer to 0x00200000.
                uint objImgGba = 0x00200000u | 0x08000000u;
                WriteU32Le(rom.Data, recordOff + 4, objImgGba);
                // +8: OAMAbsoStart — raw u32 (e.g. 0x300).
                WriteU32Le(rom.Data, recordOff + 8, 0x300u);
                // +12: OAMBGAbsoStart — raw u32 (e.g. 0x400).
                WriteU32Le(rom.Data, recordOff + 12, 0x400u);
                // +16: bgImagePointer — GBA pointer to 0x00300000.
                uint bgImgGba = 0x00300000u | 0x08000000u;
                WriteU32Le(rom.Data, recordOff + 16, bgImgGba);
                // +20: objPalettePointer — GBA pointer to 0x00400000.
                uint objPalGba = 0x00400000u | 0x08000000u;
                WriteU32Le(rom.Data, recordOff + 20, objPalGba);
                // +24: bgPalettePointer — GBA pointer to 0x00500000.
                uint bgPalGba = 0x00500000u | 0x08000000u;
                WriteU32Le(rom.Data, recordOff + 24, bgPalGba);

                uint objImgOff, oamStart, oamBGStart, bgImgOff, objPalOff, bgPalOff;
                bool ok = MagicEffectRendererCore.TryReadMagicFrameHeader(
                    rom, recordOff,
                    out objImgOff, out oamStart, out oamBGStart,
                    out bgImgOff, out objPalOff, out bgPalOff);

                Assert.True(ok);
                // GBA pointers are toOffset'd (strip 0x08000000).
                Assert.Equal(0x00200000u, objImgOff);
                Assert.Equal(0x00300000u, bgImgOff);
                Assert.Equal(0x00400000u, objPalOff);
                Assert.Equal(0x00500000u, bgPalOff);
                // Relative offsets are returned as-is (raw u32).
                Assert.Equal(0x300u, oamStart);
                Assert.Equal(0x400u, oamBGStart);
            }
            finally { CoreState.ROM = prevRom; }
        }

        /// <summary>
        /// If a pointer field is invalid (not isSafetyPointer), returns false.
        /// </summary>
        [Fact]
        public void TryReadMagicFrameHeader_BadPointer_ReturnsFalse()
        {
            var rom = MakeMinimalRom();
            uint recordOff = 0x100u;
            rom.Data[recordOff + 3] = 0x86;
            // Plant an invalid value (0x12345678 is not a GBA ROM pointer) at +4.
            WriteU32Le(rom.Data, recordOff + 4, 0x12345678u);

            uint o1, o2, o3, o4, o5, o6;
            bool ok = MagicEffectRendererCore.TryReadMagicFrameHeader(
                rom, recordOff, out o1, out o2, out o3, out o4, out o5, out o6);
            Assert.False(ok);
        }

        /// <summary>
        /// Record within 28 bytes of EOF returns false.
        /// </summary>
        [Fact]
        public void TryReadMagicFrameHeader_TooCloseToEOF_ReturnsFalse()
        {
            var rom = MakeMinimalRomSize(0x120);
            uint recordOff = 0x110u; // 0x120 - 0x110 = 16 bytes, need 28.
            uint o1, o2, o3, o4, o5, o6;
            bool ok = MagicEffectRendererCore.TryReadMagicFrameHeader(
                rom, recordOff, out o1, out o2, out o3, out o4, out o5, out o6);
            Assert.False(ok);
        }

        // ---------------------------------------------------------------
        // CalcHeight
        // ---------------------------------------------------------------

        // CalcHeight: height = imageSize / (width/2), rounded up to align.
        // width=256, imageSize=0x800(2048): height = 2048/(256/2) = 2048/128 = 16. already div8 → 16.
        // width=256, imageSize=0x1000(4096): height = 4096/128 = 32. → 32.
        // width=256, imageSize=4: height = 4/128 = 0 → rounds up to 1, then align → 8.
        [Theory]
        [InlineData(256, 0x800, 8, 16)]
        [InlineData(256, 0x1000, 8, 32)]
        [InlineData(256, 4, 8, 8)]       // very small → rounds up to align
        public void CalcHeight_RoundsUpToAlignment(int width, int imageSize, int align, int expected)
        {
            int result = MagicEffectRendererCore.CalcHeight(width, imageSize, align);
            Assert.Equal(expected, result);
        }

        // ---------------------------------------------------------------
        // RenderMagicFrame
        // ---------------------------------------------------------------

        /// <summary>
        /// No magic system patch → RenderMagicFrame returns null.
        /// </summary>
        [Fact]
        public void RenderMagicFrame_NoMagicSystem_ReturnsNull()
        {
            var prevRom = CoreState.ROM;
            var prevSvc = CoreState.ImageService;
            try
            {
                CoreState.ROM = MakeMinimalFe8uRom_NoMagic();
                CoreState.ImageService = new StubImageService();
                string log;
                var img = MagicEffectRendererCore.RenderMagicFrame(
                    CoreState.ROM, 0x100u, 0u, 0x200u, 0x300u, out log);
                Assert.Null(img);
                Assert.NotEmpty(log);
            }
            finally { CoreState.ROM = prevRom; CoreState.ImageService = prevSvc; }
        }

        /// <summary>
        /// Null ROM → returns null without throwing.
        /// </summary>
        [Fact]
        public void RenderMagicFrame_NullRom_ReturnsNull()
        {
            var prevSvc = CoreState.ImageService;
            try
            {
                CoreState.ImageService = new StubImageService();
                string log;
                var img = MagicEffectRendererCore.RenderMagicFrame(
                    null, 0x100u, 0u, 0x200u, 0x300u, out log);
                Assert.Null(img);
            }
            finally { CoreState.ImageService = prevSvc; }
        }

        /// <summary>
        /// frameDataAddr near EOF → no throw, returns null gracefully.
        /// </summary>
        [Fact]
        public void RenderMagicFrame_NearEOFFrameData_DoesNotThrow()
        {
            var prevRom = CoreState.ROM;
            var prevSvc = CoreState.ImageService;
            try
            {
                var rom = MakeFe8uRomWithMagic();
                CoreState.ROM = rom;
                CoreState.ImageService = new StubImageService();
                // frameDataAddr at ROM end − 3 bytes (not enough for 4-byte guard).
                uint nearEOF = (uint)(rom.Data.Length - 3);
                string log;
                var img = MagicEffectRendererCore.RenderMagicFrame(
                    rom, nearEOF, 0u, 0x200000u, 0x300000u, out log);
                Assert.Null(img); // expected: no frame found → null
            }
            finally { CoreState.ROM = prevRom; CoreState.ImageService = prevSvc; }
        }

        /// <summary>
        /// Truncated BG LZ77 stream → returns null.
        /// </summary>
        [Fact]
        public void RenderMagicFrame_TruncatedBgLZ77_ReturnsNull()
        {
            var prevRom = CoreState.ROM;
            var prevSvc = CoreState.ImageService;
            try
            {
                var rom = MakeFe8uRomWithMagic();
                CoreState.ROM = rom;
                CoreState.ImageService = new StubImageService();

                // Build a frame record that points to an invalid BG LZ77 (empty header).
                uint frameBase = 0x400u;
                PlantMinimalFrameRecord(rom, frameBase,
                    objImgOffset: 0x500u,       // has valid LZ77
                    bgImgOffset:  0x600u,        // will NOT have valid LZ77
                    objPalOffset: 0x700u,
                    bgPalOffset:  0x800u,
                    oamAbso: 0u, oamBGAbso: 0u);
                // Plant valid LZ77 for OBJ at 0x500.
                PlantSmallLZ77(rom.Data, 0x500u, 64 * 32); // 64 tiles of 32 bytes
                // Do NOT plant valid LZ77 at 0x600 (BG) — leave zeros → invalid.
                // Plant raw palettes.
                PlantRawPalette(rom.Data, 0x700u);
                PlantRawPalette(rom.Data, 0x800u);

                string log;
                var img = MagicEffectRendererCore.RenderMagicFrame(
                    rom, frameBase, 0u, 0x10000u, 0x20000u, out log);
                Assert.Null(img);
            }
            finally { CoreState.ROM = prevRom; CoreState.ImageService = prevSvc; }
        }

        /// <summary>
        /// Truncated OBJ LZ77 stream → returns null.
        /// </summary>
        [Fact]
        public void RenderMagicFrame_TruncatedObjLZ77_ReturnsNull()
        {
            var prevRom = CoreState.ROM;
            var prevSvc = CoreState.ImageService;
            try
            {
                var rom = MakeFe8uRomWithMagic();
                CoreState.ROM = rom;
                CoreState.ImageService = new StubImageService();

                uint frameBase = 0x400u;
                PlantMinimalFrameRecord(rom, frameBase,
                    objImgOffset: 0x500u,   // will NOT have valid LZ77
                    bgImgOffset:  0x600u,
                    objPalOffset: 0x700u,
                    bgPalOffset:  0x800u,
                    oamAbso: 0u, oamBGAbso: 0u);
                // Plant valid BG LZ77 at 0x600.
                PlantSmallLZ77(rom.Data, 0x600u, 32 * 8);
                PlantRawPalette(rom.Data, 0x700u);
                PlantRawPalette(rom.Data, 0x800u);
                // Do NOT plant valid LZ77 at 0x500 (OBJ).

                string log;
                var img = MagicEffectRendererCore.RenderMagicFrame(
                    rom, frameBase, 0u, 0x10000u, 0x20000u, out log);
                Assert.Null(img);
            }
            finally { CoreState.ROM = prevRom; CoreState.ImageService = prevSvc; }
        }

        /// <summary>
        /// Truncated raw palette (not enough bytes for 0x20) → returns null.
        /// </summary>
        [Fact]
        public void RenderMagicFrame_TruncatedRawPalette_ReturnsNull()
        {
            var prevRom = CoreState.ROM;
            var prevSvc = CoreState.ImageService;
            try
            {
                // Use a very small ROM so the palette pointer is out of bounds.
                var rom = MakeFe8uRomWithMagic(0x1000);
                CoreState.ROM = rom;
                CoreState.ImageService = new StubImageService();

                // Place palette at near-end so the 0x20-byte read would overflow.
                uint frameBase = 0x400u;
                uint palOff = (uint)(rom.Data.Length - 8); // only 8 bytes left, need 0x20
                PlantMinimalFrameRecord(rom, frameBase,
                    objImgOffset: 0x500u,
                    bgImgOffset:  0x600u,
                    objPalOffset: palOff,   // truncated
                    bgPalOffset:  0x700u,
                    oamAbso: 0u, oamBGAbso: 0u);

                string log;
                var img = MagicEffectRendererCore.RenderMagicFrame(
                    rom, frameBase, 0u, 0x10000u, 0x20000u, out log);
                Assert.Null(img);
            }
            finally { CoreState.ROM = prevRom; CoreState.ImageService = prevSvc; }
        }

        /// <summary>
        /// Valid synthetic ROM with all data planted → renders a 240×128 non-null image.
        /// This is the primary "happy path" test proving the canvas size is correct.
        /// </summary>
        [Fact]
        public void RenderMagicFrame_ValidData_Returns240x128NonNull()
        {
            var prevRom = CoreState.ROM;
            var prevSvc = CoreState.ImageService;
            try
            {
                var rom = MakeFe8uRomWithMagic();
                CoreState.ROM = rom;
                CoreState.ImageService = new StubImageService();

                // Place a valid frame record at 0x400 followed by 0x80 terminator.
                uint frameBase = 0x400u;
                uint objImgOff = 0x1000u;
                uint bgImgOff  = 0x2000u;
                uint objPalOff = 0x3000u;
                uint bgPalOff  = 0x4000u;

                PlantMinimalFrameRecord(rom, frameBase,
                    objImgOffset: objImgOff,
                    bgImgOffset:  bgImgOff,
                    objPalOffset: objPalOff,
                    bgPalOffset:  bgPalOff,
                    oamAbso: 0u, oamBGAbso: 0u);
                // Terminator.
                rom.Data[frameBase + 28 + 3] = 0x80;

                // Plant valid LZ77 OBJ tiles (256×64 sheet minimum).
                int objTileBytes = 64 * 32; // 64 tiles × 32 bytes/tile
                PlantSmallLZ77(rom.Data, objImgOff, objTileBytes);
                // Plant valid LZ77 BG tiles (256×64 sheet).
                int bgTileBytes = 32 * 8 * 32; // 256 tiles × 32 bytes/tile (256×64 sheet)
                PlantSmallLZ77(rom.Data, bgImgOff, bgTileBytes);
                // Plant raw 16-color palettes (0x20 bytes each).
                PlantRawPalette(rom.Data, objPalOff);
                PlantRawPalette(rom.Data, bgPalOff);

                // OAM base offsets (pointing to 0x00 region where the default
                // 0x01 terminator byte of the ROM is already zeroed — the first
                // byte at offset 0 is the first byte of the ROM header, which
                // for our synthetic ROM is 0x00 or something non-0x01;  the
                // OAM parser terminates on the first byte == 0x01 or unknown
                // format byte — result is empty OAM = no sprites, valid).
                // Use offsets that are well within the ROM.
                uint objOAM = 0u; // offset 0 (no GBA pointer, raw offset per WF Draw())
                uint bgOAM  = 0u;

                string log;
                var img = MagicEffectRendererCore.RenderMagicFrame(
                    rom,
                    frameBase,   // GBA pointer pointing to ROM offset frameBase
                    0u,          // frame index 0
                    objOAM,      // objRightToLeftOAM (raw offset, not GBA ptr)
                    bgOAM,       // objBGRightToLeftOAM (raw offset)
                    out log);

                Assert.NotNull(img);
                Assert.Equal(MagicEffectRendererCore.MAGIC_CANVAS_WIDTH,  img.Width);   // 240
                Assert.Equal(MagicEffectRendererCore.MAGIC_CANVAS_HEIGHT, img.Height);  // 128
            }
            finally { CoreState.ROM = prevRom; CoreState.ImageService = prevSvc; }
        }

        // ---------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------

        static ROM MakeMinimalRom() => MakeMinimalRomSize(0x1100000);

        static ROM MakeMinimalRomSize(int size)
        {
            var rom = new ROM();
            rom.LoadLow("synthetic.gba", new byte[size], "");
            return rom;
        }

        static ROM MakeMinimalFe8uRom_NoMagic()
        {
            var rom = new ROM();
            rom.LoadLow("synthetic-fe8u.gba", new byte[0x1100000], "BE8E01");
            return rom;
        }

        static ROM MakeFe8uRomWithMagic(int size = 0x1100000)
        {
            var data = new byte[size];
            // FEditor / FE8U patch signature at 0x95d780.
            byte[] sig = {
                0x01, 0x00, 0x00, 0x00, 0x90, 0xD7, 0x95, 0x08,
                0x03, 0x00, 0x00, 0x00, 0x39, 0xD9, 0x95, 0x08,
            };
            if (0x95d780 + sig.Length <= size)
                Array.Copy(sig, 0, data, 0x95d780, sig.Length);

            // FEditor CSA spell table pattern at 0x00200000 + pointer to 0x00100000.
            byte[] csaPat = {
                0x01, 0xB4, 0x7D, 0xE7, 0x34, 0xFF, 0x03, 0x02,
                0x80, 0xD7, 0x95, 0x08, 0x1A, 0xE1, 0x03, 0x02,
            };
            uint csaPos = 0x00200000u;
            if ((int)csaPos + csaPat.Length + 4 <= size)
            {
                Array.Copy(csaPat, 0, data, (int)csaPos, csaPat.Length);
                BitConverter.GetBytes(0x00100000u | 0x08000000u)
                    .CopyTo(data, (int)csaPos + csaPat.Length);
            }

            var rom = new ROM();
            rom.LoadLow("synthetic-fe8u.gba", data, "BE8E01");
            return rom;
        }

        /// <summary>
        /// Build <paramref name="frameCount"/> consecutive 28-byte 0x86 frame records
        /// at <paramref name="baseOffset"/> in <paramref name="data"/>.
        /// Fields at +4/+16/+20/+24 are set to GBA pointers within the ROM's 1MB range.
        /// Fields at +8/+12 are set to 0 (relative offsets).
        /// </summary>
        static void Build86Record(byte[] data, uint baseOffset, int frameCount)
        {
            for (int i = 0; i < frameCount; i++)
            {
                uint off = baseOffset + (uint)(i * 28);
                if (off + 28 > data.Length) break;
                // Command byte at +3 = 0x86.
                data[off + 3] = 0x86;
                // Dummy valid GBA pointers (within 1 MB from 0x08000000).
                WriteU32Le(data, off + 4,  0x08000100u); // objImagePointer
                WriteU32Le(data, off + 8,  0u);          // OAMAbsoStart (relative)
                WriteU32Le(data, off + 12, 0u);          // OAMBGAbsoStart (relative)
                WriteU32Le(data, off + 16, 0x08000200u); // bgImagePointer
                WriteU32Le(data, off + 20, 0x08000300u); // objPalettePointer
                WriteU32Le(data, off + 24, 0x08000400u); // bgPalettePointer
            }
        }

        /// <summary>
        /// Plant a complete 28-byte 0x86 frame record pointing at given ROM offsets
        /// (the offsets are written as GBA pointers i.e. OR'd with 0x08000000).
        /// +8 and +12 are written as raw u32 relative offsets.
        /// </summary>
        static void PlantMinimalFrameRecord(
            ROM rom, uint frameBase,
            uint objImgOffset, uint bgImgOffset,
            uint objPalOffset, uint bgPalOffset,
            uint oamAbso, uint oamBGAbso)
        {
            byte[] d = rom.Data;
            d[frameBase + 3] = 0x86;
            WriteU32Le(d, frameBase + 4,  objImgOffset | 0x08000000u);
            WriteU32Le(d, frameBase + 8,  oamAbso);
            WriteU32Le(d, frameBase + 12, oamBGAbso);
            WriteU32Le(d, frameBase + 16, bgImgOffset  | 0x08000000u);
            WriteU32Le(d, frameBase + 20, objPalOffset | 0x08000000u);
            WriteU32Le(d, frameBase + 24, bgPalOffset  | 0x08000000u);
        }

        /// <summary>
        /// Plant a minimal LZ77-compressed block of <paramref name="uncompressedSize"/>
        /// bytes at the given offset. Writes a 4-byte LZ77 header followed by raw
        /// uncompressed data wrapped in an LZ77 literal stream.
        /// </summary>
        static void PlantSmallLZ77(byte[] data, uint offset, int uncompressedSize)
        {
            if (offset + 4 + uncompressedSize > data.Length) return;
            // LZ77 type byte 0x10 + 3-byte little-endian uncompressed size.
            data[offset + 0] = 0x10;
            data[offset + 1] = (byte)(uncompressedSize & 0xFF);
            data[offset + 2] = (byte)((uncompressedSize >> 8) & 0xFF);
            data[offset + 3] = (byte)((uncompressedSize >> 16) & 0xFF);
            // The real LZ77 decompressor needs valid compressed data; use a
            // block of 0x00 bytes which decompresses to all-zero output of the
            // correct size. We need flag bytes + literal bytes.
            // Simplest valid sequence: all literals (flag byte 0x00 = 8 literals).
            int remaining = uncompressedSize;
            int pos = (int)offset + 4;
            while (remaining > 0 && pos + 1 < data.Length)
            {
                int count = Math.Min(remaining, 8);
                // Flag byte: 0x00 = 8 literals (bit=0 → copy literal byte).
                data[pos++] = 0x00;
                for (int k = 0; k < count && pos < data.Length; k++, remaining--)
                {
                    data[pos++] = 0x00; // literal byte value = 0
                }
                // Pad remaining 8-8 literals of this flag group (ignored by decoder).
                for (int k = count; k < 8 && remaining > 0; k++) { remaining--; pos++; }
            }
        }

        /// <summary>
        /// Plant a 0x20-byte (16 GBA color) palette at the given offset.
        /// </summary>
        static void PlantRawPalette(byte[] data, uint offset)
        {
            if (offset + 0x20 > data.Length) return;
            // Plant a recognizable but non-zero palette (all red = 0x001F in GBA BGR555).
            for (int i = 0; i < 16; i++)
            {
                ushort color = (i == 0) ? (ushort)0 : (ushort)0x001F; // index 0 = transparent
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
    }
}
