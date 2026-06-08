// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for the #1033 32-color-mode detector in BattleAnimeRendererCore:
//   - MaxOamPaletteBank: the OAM palette-bank scan (non-affine, non-bug, with
//     the same parse loop + terminators as DrawOAMSprites).
//   - CountAnimationPaletteBanks: the animation-wide max(bank)+1 detector that
//     replaces WF ImageUtil.GetPalette16Count(DrawBitmap).
//
// Reuses the synthetic-battle-anime-ROM scaffold pattern from
// BattleAnimeSamplePreviewTests (rom.LoadLow + U.write_u32 + LZ77.compress).
using System;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class BattleAnimePaletteBankCountTests : IDisposable
    {
        readonly ROM _prevRom;

        public BattleAnimePaletteBankCountTests()
        {
            _prevRom = CoreState.ROM;
        }

        public void Dispose()
        {
            CoreState.ROM = _prevRom;
        }

        // ================================================================
        // MaxOamPaletteBank — direct byte[] unit tests (no ROM needed).
        // MaxOamPaletteBank is internal; the Core.Tests assembly has
        // InternalsVisibleTo, so it is reachable from here.
        // ================================================================

        [Fact]
        public void MaxOamPaletteBank_AllSpritesBank0_ReturnsZero()
        {
            byte[] oam = new byte[36];
            WriteSprite(oam, 0, bank: 0);
            WriteSprite(oam, 12, bank: 0);
            oam[24] = 0x01; // terminator
            Assert.Equal(0, BattleAnimeRendererCore.MaxOamPaletteBank(oam, 0));
        }

        [Fact]
        public void MaxOamPaletteBank_NonAffineBank1_ReturnsOne()
        {
            byte[] oam = new byte[24];
            WriteSprite(oam, 0, bank: 1);
            oam[12] = 0x01; // terminator
            Assert.Equal(1, BattleAnimeRendererCore.MaxOamPaletteBank(oam, 0));
        }

        [Fact]
        public void MaxOamPaletteBank_NonAffineBank3_ReturnsThree()
        {
            byte[] oam = new byte[24];
            WriteSprite(oam, 0, bank: 3);
            oam[12] = 0x01; // terminator
            Assert.Equal(3, BattleAnimeRendererCore.MaxOamPaletteBank(oam, 0));
        }

        [Fact]
        public void MaxOamPaletteBank_BankAtOrAbove4_IsSkipped_ReturnsZero()
        {
            // A lone bank-5 sprite is a bug frame (>= 4) and is skipped, so the
            // result is 0 (no qualifying entry).
            byte[] oam = new byte[24];
            WriteSprite(oam, 0, bank: 5);
            oam[12] = 0x01; // terminator
            Assert.Equal(0, BattleAnimeRendererCore.MaxOamPaletteBank(oam, 0));
        }

        [Fact]
        public void MaxOamPaletteBank_AffineSprite_IsExcluded_ReturnsZero()
        {
            // An AFFINE sprite (align bit0 set) with bank 2 must NOT count;
            // all other sprites are bank 0 => result 0.
            byte[] oam = new byte[36];
            WriteSprite(oam, 0, bank: 2, affine: true); // excluded
            WriteSprite(oam, 12, bank: 0);                    // bank 0
            oam[24] = 0x01; // terminator
            Assert.Equal(0, BattleAnimeRendererCore.MaxOamPaletteBank(oam, 0));
        }

        [Fact]
        public void MaxOamPaletteBank_NormalTerminator_EndsWalk()
        {
            // A 0x01 terminator BEFORE the bank-2 sprite stops the walk, so the
            // bank-2 entry past it is never seen => 0.
            byte[] oam = new byte[36];
            oam[0] = 0x01; // immediate terminator
            WriteSprite(oam, 12, bank: 2);
            Assert.Equal(0, BattleAnimeRendererCore.MaxOamPaletteBank(oam, 0));
        }

        [Fact]
        public void MaxOamPaletteBank_FEditorAltTerminator_EndsWalk()
        {
            // 00 FF FF FF alternate terminator ends the walk before the bank-2
            // sprite that follows.
            byte[] oam = new byte[36];
            oam[0] = 0x00; oam[1] = 0xFF; oam[2] = 0xFF; oam[3] = 0xFF; // alt terminator
            WriteSprite(oam, 12, bank: 2);
            Assert.Equal(0, BattleAnimeRendererCore.MaxOamPaletteBank(oam, 0));
        }

        [Fact]
        public void MaxOamPaletteBank_AffineMatrixEntry_IsSkipped_WalkContinues()
        {
            // An affine-MATRIX entry ([2..3]==FFFF, first byte != 0) is NOT a
            // terminator: the walk must continue past it and still find the
            // bank-2 sprite after it.
            byte[] oam = new byte[36];
            // matrix entry: byte0 non-zero so the alt-terminator (00 FF FF FF)
            // branch does NOT fire; bytes [2..3] == FFFF marks it a matrix.
            oam[0] = 0x10; oam[2] = 0xFF; oam[3] = 0xFF;
            WriteSprite(oam, 12, bank: 2);
            oam[24] = 0x01; // terminator
            Assert.Equal(2, BattleAnimeRendererCore.MaxOamPaletteBank(oam, 0));
        }

        [Fact]
        public void MaxOamPaletteBank_NullOrOutOfRange_ReturnsZero()
        {
            Assert.Equal(0, BattleAnimeRendererCore.MaxOamPaletteBank(null, 0));
            Assert.Equal(0, BattleAnimeRendererCore.MaxOamPaletteBank(new byte[12], 100));
        }

        // ================================================================
        // CountAnimationPaletteBanks — integration tests (synthetic ROM).
        // ================================================================

        [Fact]
        public void CountAnimationPaletteBanks_AllBanks0_ReturnsOne()
        {
            ROM rom = MakeAnimeRom(sec0Bank: 0, sec1Bank: 0);
            CoreState.ROM = rom;
            Assert.Equal(1, BattleAnimeRendererCore.CountAnimationPaletteBanks(RECORD_OFFSET));
        }

        [Fact]
        public void CountAnimationPaletteBanks_Bank1InSection0_ReturnsTwo()
        {
            ROM rom = MakeAnimeRom(sec0Bank: 1, sec1Bank: 0);
            CoreState.ROM = rom;
            Assert.Equal(2, BattleAnimeRendererCore.CountAnimationPaletteBanks(RECORD_OFFSET));
        }

        [Fact]
        public void CountAnimationPaletteBanks_Bank1OnlyInLaterSection_ReturnsTwo()
        {
            // The banked sprite is reachable ONLY via section 1 (section 0 is
            // bank 0). The animation-wide scan must still find it => 2. This
            // proves the detector is animation-wide, not sample-only.
            ROM rom = MakeAnimeRom(sec0Bank: 0, sec1Bank: 1);
            CoreState.ROM = rom;
            Assert.Equal(2, BattleAnimeRendererCore.CountAnimationPaletteBanks(RECORD_OFFSET));
        }

        [Fact]
        public void CountAnimationPaletteBanks_OnlyAffineBank1Sprite_ReturnsOne()
        {
            // Both sprites are AFFINE with bank 1; affine sprites are excluded
            // (WF renders them with palette shift 0) => single bank => 1.
            ROM rom = MakeAnimeRom(sec0Bank: 1, sec1Bank: 1, affine: true);
            CoreState.ROM = rom;
            Assert.Equal(1, BattleAnimeRendererCore.CountAnimationPaletteBanks(RECORD_OFFSET));
        }

        [Fact]
        public void CountAnimationPaletteBanks_ZeroPointers_ReturnsOne()
        {
            ROM rom = MakeAnimeRom(sec0Bank: 1, sec1Bank: 1);
            // Wipe section/frame/OAM pointers => unresolvable => safe default 1.
            U.write_u32(rom.Data, RECORD_OFFSET + 12, 0);
            U.write_u32(rom.Data, RECORD_OFFSET + 16, 0);
            U.write_u32(rom.Data, RECORD_OFFSET + 20, 0);
            CoreState.ROM = rom;
            Assert.Equal(1, BattleAnimeRendererCore.CountAnimationPaletteBanks(RECORD_OFFSET));
        }

        [Fact]
        public void CountAnimationPaletteBanks_NullRom_ReturnsOne()
        {
            CoreState.ROM = null;
            Assert.Equal(1, BattleAnimeRendererCore.CountAnimationPaletteBanks(RECORD_OFFSET));
        }

        [Fact]
        public void CountAnimationPaletteBanks_RecordPastEndOfRom_ReturnsOne()
        {
            ROM rom = MakeAnimeRom(sec0Bank: 1, sec1Bank: 0);
            CoreState.ROM = rom;
            uint badOffset = (uint)rom.Data.Length - 4; // record would overrun
            Assert.Equal(1, BattleAnimeRendererCore.CountAnimationPaletteBanks(badOffset));
        }

        [Fact]
        public void CountAnimationPaletteBanks_SectionTableNearEof_ReturnsOne_NoThrow()
        {
            // #1051 review: a section pointer 2 bytes before EOF passes
            // U.isSafetyOffset (base only) but GetSectionRange's rom.u32 would read
            // past EOF and THROW IndexOutOfRangeException without the 48-byte
            // section-table bounds guard. Must honor the contract: return 1, no throw.
            ROM rom = MakeAnimeRom(sec0Bank: 1, sec1Bank: 1);
            uint nearEof = (uint)rom.Data.Length - 2;
            U.write_u32(rom.Data, RECORD_OFFSET + 12, U.toPointer(nearEof));
            CoreState.ROM = rom;
            int result = BattleAnimeRendererCore.CountAnimationPaletteBanks(RECORD_OFFSET);
            Assert.Equal(1, result);
        }

        // ================================================================
        // Synthetic anime ROM construction (mirrors BattleAnimeSamplePreview).
        // ================================================================

        const uint RECORD_OFFSET   = 0x200000;
        const uint SECTION_OFFSET  = 0x201000;
        const uint FRAME_OFFSET    = 0x202000;
        const uint OAM_OFFSET      = 0x203000;
        const uint PALETTE_OFFSET  = 0x204000;
        const uint GFX_OFFSET      = 0x210000;

        // OAM byte offsets inside the (uncompressed) OAM data we author.
        const uint OAM_SEC0_FRAME0 = 0;
        const uint OAM_SEC1_FRAME0 = 24;

        static ROM MakeAnimeRom(int sec0Bank, int sec1Bank, bool affine = false)
        {
            var rom = new ROM();
            byte[] data = new byte[0x2000000];
            Array.Fill(data, (byte)0x00); // zero free space (0xFF looks like pointers)
            rom.LoadLow("synth.gba", data, "BE8E01");

            // ---- 32-byte animation record ----
            U.write_u32(rom.Data, RECORD_OFFSET + 12, U.toPointer(SECTION_OFFSET));
            U.write_u32(rom.Data, RECORD_OFFSET + 16, U.toPointer(FRAME_OFFSET));
            U.write_u32(rom.Data, RECORD_OFFSET + 20, U.toPointer(OAM_OFFSET));
            U.write_u32(rom.Data, RECORD_OFFSET + 24, U.toPointer(OAM_OFFSET));
            U.write_u32(rom.Data, RECORD_OFFSET + 28, U.toPointer(PALETTE_OFFSET));

            // ---- Frame stream: section 0 + section 1, one frame each ----
            byte[] frameStream = new byte[24];
            frameStream[3] = 0x86;
            U.write_u32(frameStream, 4, U.toPointer(GFX_OFFSET));
            U.write_u32(frameStream, 8, OAM_SEC0_FRAME0);
            frameStream[15] = 0x86;
            U.write_u32(frameStream, 16, U.toPointer(GFX_OFFSET));
            U.write_u32(frameStream, 20, OAM_SEC1_FRAME0);
            PlantCompressed(rom, FRAME_OFFSET, frameStream);

            // ---- Section array: section 0 = [0,12), section 1 = [12,24) ----
            for (int s = 0; s < 12; s++)
            {
                uint start = s == 0 ? 0u : (s == 1 ? 12u : 24u);
                U.write_u32(rom.Data, SECTION_OFFSET + (uint)(s * 4), start);
            }

            // ---- OAM data: one sprite per section, each at its bank ----
            byte[] oam = new byte[48];
            WriteSprite(oam, (int)OAM_SEC0_FRAME0, sec0Bank, affine);
            oam[12] = 0x01; // terminator for section 0's list
            WriteSprite(oam, (int)OAM_SEC1_FRAME0, sec1Bank, affine);
            oam[36] = 0x01; // terminator for section 1's list
            PlantCompressed(rom, OAM_OFFSET, oam);

            // ---- Graphics: one 4bpp tile (content irrelevant to the scan) ----
            byte[] tile = new byte[32];
            for (int i = 0; i < 32; i++) tile[i] = 0x55;
            PlantCompressed(rom, GFX_OFFSET, tile);

            // ---- Palette: 2 blocks x 16 colors ----
            byte[] pal = new byte[64];
            PlantCompressed(rom, PALETTE_OFFSET, pal);

            return rom;
        }

        // Write a "square 1x1 tile" OAM sprite entry at byte offset `at`, with
        // the given 16-color palette bank in byte[5] high nibble. When `affine`
        // is true, align bit0 is set (affine sprite => excluded by the scan).
        static void WriteSprite(byte[] oam, int at, int bank, bool affine = false)
        {
            oam[at + 0] = 0x00;                                  // normal entry
            oam[at + 1] = (byte)(affine ? 0x01 : 0x00);          // align: bit0 = affine
            oam[at + 2] = 0x00;                                  // not a matrix entry
            oam[at + 3] = 0x00;                                  // area
            oam[at + 4] = 0x00;                                  // sheet tile x/y
            oam[at + 5] = (byte)((bank & 0x0F) << 4);            // palette bank selector
            oam[at + 6] = 0x00; oam[at + 7] = 0x00;
            oam[at + 8] = 0x00; oam[at + 9] = 0x00;
            oam[at + 10] = 0x00; oam[at + 11] = 0x00;
        }

        static void PlantCompressed(ROM rom, uint offset, byte[] raw)
        {
            byte[] comp = LZ77.compress(raw);
            Array.Copy(comp, 0, rom.Data, offset, comp.Length);
        }
    }
}
