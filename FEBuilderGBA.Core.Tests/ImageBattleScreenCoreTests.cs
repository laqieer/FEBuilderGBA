// SPDX-License-Identifier: GPL-3.0-or-later
// Phase 1/2/4/5 gap-sweep regression tests for ImageBattleScreenView (#393).
//
// Covers FEBuilderGBA.Core/ImageBattleScreenCore — the TSA + palette + image-pointer
// I/O helpers extracted from the WinForms ImageBattleScreenForm. Tests verify:
//   - LoadBattleScreen reads the 32x20 TSA map correctly across the 5 TSA regions.
//   - WriteBattleScreen round-trips the map and respects the ambient UndoScope.
//   - WriteBattleScreen + RunUndo restores the original bytes (push-then-undo).
//   - ReadPaletteBlock / WritePaletteBlock delegate to PaletteCore (uncompressed 16-color block).
//   - WriteImagePointer stores GBA-prefixed (0x08...) pointers.
//
// Per Plan v2 Finding #2: ImageBattleScreenCore methods take NO Undo.UndoData
// parameter — they rely on the [ThreadStatic] ROM.BeginUndoScope ambient
// context (matches PaletteCore, ImageBattleAnimePaletteCore precedent).
using System.Collections.Generic;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class ImageBattleScreenCoreTests
    {
        // ROM offsets for the 5 TSA regions + palette block + 5 image pointers
        // planted into a synthetic FE8U ROM. These are arbitrary offsets in the
        // ROM body (well past the header) chosen so we have predictable storage
        // for the test.
        const uint TSA1_OFFSET     = 0x100000;
        const uint TSA2_OFFSET     = 0x101000;
        const uint TSA3_OFFSET     = 0x102000;
        const uint TSA4_OFFSET     = 0x103000;
        const uint TSA5_OFFSET     = 0x104000;
        const uint PALETTE_OFFSET  = 0x105000;
        const uint IMAGE1_OFFSET   = 0x106000;
        const uint IMAGE2_OFFSET   = 0x107000;
        const uint IMAGE3_OFFSET   = 0x108000;
        const uint IMAGE4_OFFSET   = 0x109000;
        const uint IMAGE5_OFFSET   = 0x10A000;

        // -----------------------------------------------------------------
        // LoadBattleScreen
        // -----------------------------------------------------------------

        [Fact]
        public void LoadBattleScreen_ReadsCorrectMap()
        {
            var rom = MakeRom();
            // Plant distinct u16 markers in each TSA region.
            //  TSA1 covers y=[0..5], x=[1..15] → 90 cells, 0x10..0x69
            //  TSA2 covers y=[0..5], x=[16..30] → 90 cells, 0x70..0xC9
            //  TSA3 covers y=[13..19], x=[1..15] → 105 cells, 0xD0..0x138
            //  TSA4 covers y=[13..19], x=[16..31] → 112 cells, 0x140..0x1AF
            //  TSA5 covers y=[0..19], x=[31..32] → 40 cells, 0x200..0x227 (x=32 wraps to x=0)
            PlantTSA1(rom);
            PlantTSA2(rom);
            PlantTSA3(rom);
            PlantTSA4(rom);
            PlantTSA5(rom);

            ushort[] map = ImageBattleScreenCore.LoadBattleScreen(rom);
            Assert.NotNull(map);
            Assert.Equal(32 * 20, map.Length);

            // Spot-check one cell from each region.
            Assert.Equal((ushort)0x0010, map[0 * 32 + 1]);   // TSA1 first cell (y=0, x=1)
            Assert.Equal((ushort)0x0070, map[0 * 32 + 16]);  // TSA2 first cell (y=0, x=16)
            Assert.Equal((ushort)0x00D0, map[13 * 32 + 1]);  // TSA3 first cell (y=13, x=1)
            Assert.Equal((ushort)0x0140, map[13 * 32 + 16]); // TSA4 first cell (y=13, x=16)
            Assert.Equal((ushort)0x0200, map[0 * 32 + 31]);  // TSA5 first cell (y=0, x=31)
            // TSA5 also wraps the x=32 column back to x=0.
            Assert.Equal((ushort)0x0201, map[0 * 32 + 0]);   // TSA5 second cell (y=0, x=32 → x=0)
        }

        [Fact]
        public void LoadBattleScreen_NullRom_ReturnsNull()
        {
            ushort[] map = ImageBattleScreenCore.LoadBattleScreen(null);
            Assert.Null(map);
        }

        // -----------------------------------------------------------------
        // WriteBattleScreen
        // -----------------------------------------------------------------

        [Fact]
        public void WriteBattleScreen_RoundTripsThroughCore_UnderAmbientScope()
        {
            var rom = MakeRom();
            PlantTSA1(rom); PlantTSA2(rom); PlantTSA3(rom); PlantTSA4(rom); PlantTSA5(rom);

            ushort[] map = ImageBattleScreenCore.LoadBattleScreen(rom);
            Assert.NotNull(map);

            // Mutate every cell that the editor controls.
            map[0 * 32 + 1] = 0xABCD;        // TSA1
            map[0 * 32 + 16] = 0xDEF0;       // TSA2
            map[13 * 32 + 1] = 0x1234;       // TSA3
            map[13 * 32 + 16] = 0x5678;      // TSA4
            map[0 * 32 + 31] = 0x9ABC;       // TSA5 (x=31)
            map[0 * 32 + 0] = 0xCAFE;        // TSA5 (x=32 wraps)

            var ud = new Undo.UndoData
            {
                time = System.DateTime.Now,
                name = "test",
                list = new List<Undo.UndoPostion>(),
                filesize = (uint)rom.Data.Length
            };
            bool ok;
            using (ROM.BeginUndoScope(ud))
            {
                ok = ImageBattleScreenCore.WriteBattleScreen(rom, map);
            }
            Assert.True(ok);

            // The ambient scope should have captured a write for every changed cell.
            // We don't assert exact count (90+90+105+112+40 = 437 cells) but it
            // must be at least 6 (the 6 we mutated, conservatively).
            Assert.True(ud.list.Count >= 6,
                $"Ambient scope expected at least 6 tracked writes; got {ud.list.Count}");

            // Round-trip: re-read the map; mutated cells survive.
            ushort[] roundtrip = ImageBattleScreenCore.LoadBattleScreen(rom);
            Assert.NotNull(roundtrip);
            Assert.Equal((ushort)0xABCD, roundtrip[0 * 32 + 1]);
            Assert.Equal((ushort)0xDEF0, roundtrip[0 * 32 + 16]);
            Assert.Equal((ushort)0x1234, roundtrip[13 * 32 + 1]);
            Assert.Equal((ushort)0x5678, roundtrip[13 * 32 + 16]);
            Assert.Equal((ushort)0x9ABC, roundtrip[0 * 32 + 31]);
            Assert.Equal((ushort)0xCAFE, roundtrip[0 * 32 + 0]);
        }

        [Fact]
        public void WriteBattleScreen_RestoresViaRunUndoAfterPush()
        {
            var rom = MakeRom();
            PlantTSA1(rom); PlantTSA2(rom); PlantTSA3(rom); PlantTSA4(rom); PlantTSA5(rom);

            // Undo.NewUndoData needs CoreState.ROM (it reads .Data.Length for filesize).
            var prevRom = CoreState.ROM;
            try
            {
                CoreState.ROM = rom;

                var undo = new Undo();
                var undoBefore = new byte[8];
                uint readAddr = rom.RomInfo.battle_screen_TSA1_pointer;
                uint tsa1Addr = rom.p32(readAddr);
                for (int i = 0; i < 8; i++) undoBefore[i] = rom.Data[tsa1Addr + i];

                ushort[] map = ImageBattleScreenCore.LoadBattleScreen(rom);
                map[0 * 32 + 1] = 0xABCD;
                map[0 * 32 + 2] = 0xCAFE;

                var ud = undo.NewUndoData("test");
                using (ROM.BeginUndoScope(ud))
                {
                    ImageBattleScreenCore.WriteBattleScreen(rom, map);
                }
                Assert.NotEmpty(ud.list);
                undo.Push(ud);

                // After write the bytes differ from the original.
                Assert.NotEqual(undoBefore[0], rom.Data[tsa1Addr + 0]);

                // RunUndo restores the original bytes.
                undo.RunUndo();
                for (int i = 0; i < 8; i++)
                {
                    Assert.Equal(undoBefore[i], rom.Data[tsa1Addr + i]);
                }
            }
            finally
            {
                CoreState.ROM = prevRom;
            }
        }

        [Fact]
        public void WriteBattleScreen_NullArgs_ReturnsFalse()
        {
            var rom = MakeRom();
            Assert.False(ImageBattleScreenCore.WriteBattleScreen(null, new ushort[640]));
            Assert.False(ImageBattleScreenCore.WriteBattleScreen(rom, null));
            Assert.False(ImageBattleScreenCore.WriteBattleScreen(rom, new ushort[100])); // wrong size
        }

        /// <summary>
        /// Per Copilot bot PR #594 review: a corrupt TSA pointer slot
        /// (zero, NOT_FOUND, or out-of-bounds) must not crash the editor.
        /// LoadBattleScreen returns the map with that region zero-filled.
        /// </summary>
        [Fact]
        public void LoadBattleScreen_CorruptTsa1Pointer_ReturnsZeroMapWithoutThrowing()
        {
            var rom = MakeRom();
            // Zero out the TSA1 pointer slot.
            U.write_u32(rom.Data, rom.RomInfo.battle_screen_TSA1_pointer, U.toPointer(0));
            ushort[] map = ImageBattleScreenCore.LoadBattleScreen(rom);
            Assert.NotNull(map);
            Assert.Equal(640, map.Length);
            // TSA1 region cells must all be 0 (skipped); other regions
            // should not be affected.
            for (int y = 0; y <= 5; y++)
            {
                for (int x = 1; x <= 15; x++)
                {
                    Assert.Equal((ushort)0, map[y * 32 + x]);
                }
            }
        }

        /// <summary>
        /// Per Copilot bot PR #594 review: a corrupt TSA pointer slot must
        /// cause WriteBattleScreen to return false BEFORE any byte is
        /// written (so the editor stays consistent inside its undo scope).
        /// </summary>
        [Fact]
        public void WriteBattleScreen_CorruptTsa1Pointer_ReturnsFalseWithoutPartialWrites()
        {
            var rom = MakeRom();
            PlantTSA1(rom); PlantTSA2(rom); PlantTSA3(rom); PlantTSA4(rom); PlantTSA5(rom);

            // Stash original bytes from each TSA region so we can detect any partial write.
            uint tsa1Addr = rom.p32(rom.RomInfo.battle_screen_TSA1_pointer);
            uint tsa2Addr = rom.p32(rom.RomInfo.battle_screen_TSA2_pointer);
            byte[] tsa1Before = new byte[16];
            byte[] tsa2Before = new byte[16];
            for (int i = 0; i < 16; i++) tsa1Before[i] = rom.Data[tsa1Addr + i];
            for (int i = 0; i < 16; i++) tsa2Before[i] = rom.Data[tsa2Addr + i];

            // Corrupt TSA3 to an out-of-bounds offset.
            U.write_u32(rom.Data, rom.RomInfo.battle_screen_TSA3_pointer, U.toPointer(0x1FFFFFE0));

            ushort[] map = new ushort[640];
            for (int i = 0; i < 640; i++) map[i] = 0xABCD;

            var ud = new Undo.UndoData
            {
                time = System.DateTime.Now,
                name = "test",
                list = new List<Undo.UndoPostion>(),
                filesize = (uint)rom.Data.Length
            };
            bool ok;
            using (ROM.BeginUndoScope(ud))
            {
                ok = ImageBattleScreenCore.WriteBattleScreen(rom, map);
            }
            Assert.False(ok);

            // The pre-validation must have rejected the call before ANY
            // tracked write happened.
            Assert.Empty(ud.list);
            // TSA1/TSA2 regions must be unchanged.
            for (int i = 0; i < 16; i++)
            {
                Assert.Equal(tsa1Before[i], rom.Data[tsa1Addr + i]);
                Assert.Equal(tsa2Before[i], rom.Data[tsa2Addr + i]);
            }
        }

        // -----------------------------------------------------------------
        // Palette I/O (delegating to PaletteCore)
        // -----------------------------------------------------------------

        [Fact]
        public void ReadPaletteBlock_DelegatesToPaletteCore()
        {
            var rom = MakeRom();
            // Plant 4 GBA u16 palette colors at the palette slot 0.
            // Slot 0 colors: red, green, blue, white.
            uint paletteAddr = rom.p32(rom.RomInfo.battle_screen_palette_pointer);
            U.write_u16(rom.Data, paletteAddr + 0, 0x001F);   // R=31 G=0 B=0
            U.write_u16(rom.Data, paletteAddr + 2, 0x03E0);   // R=0 G=31 B=0
            U.write_u16(rom.Data, paletteAddr + 4, 0x7C00);   // R=0 G=0 B=31
            U.write_u16(rom.Data, paletteAddr + 6, 0x7FFF);   // R=31 G=31 B=31

            var colors = ImageBattleScreenCore.ReadPaletteBlock(rom, paletteIndex: 0);
            Assert.NotNull(colors);
            Assert.Equal(16, colors.Length);
            Assert.Equal(248, colors[0].r); // 31 << 3 = 248
            Assert.Equal(0, colors[0].g);
            Assert.Equal(0, colors[0].b);
            Assert.Equal(0, colors[1].r);
            Assert.Equal(248, colors[1].g);
            Assert.Equal(0, colors[2].r);
            Assert.Equal(0, colors[2].g);
            Assert.Equal(248, colors[2].b);
            Assert.Equal(248, colors[3].r);
            Assert.Equal(248, colors[3].g);
            Assert.Equal(248, colors[3].b);
        }

        [Fact]
        public void WritePaletteBlock_RoundTrip_PreservesAllChannels()
        {
            var rom = MakeRom();
            var colors = new (byte r, byte g, byte b)[16];
            colors[0] = (248, 0, 0);
            colors[1] = (0, 248, 0);
            colors[2] = (0, 0, 248);
            colors[15] = (248, 248, 248);

            var ud = new Undo.UndoData
            {
                time = System.DateTime.Now,
                name = "test",
                list = new List<Undo.UndoPostion>(),
                filesize = (uint)rom.Data.Length
            };
            bool ok;
            using (ROM.BeginUndoScope(ud))
            {
                ok = ImageBattleScreenCore.WritePaletteBlock(rom, paletteIndex: 1, colors);
            }
            Assert.True(ok);
            // The ambient scope should have captured the write_range call.
            Assert.NotEmpty(ud.list);

            var roundtrip = ImageBattleScreenCore.ReadPaletteBlock(rom, paletteIndex: 1);
            Assert.Equal(248, roundtrip[0].r);
            Assert.Equal(248, roundtrip[1].g);
            Assert.Equal(248, roundtrip[2].b);
            Assert.Equal(248, roundtrip[15].r);
            Assert.Equal(248, roundtrip[15].g);
            Assert.Equal(248, roundtrip[15].b);
        }

        // -----------------------------------------------------------------
        // WriteImagePointer
        // -----------------------------------------------------------------

        [Fact]
        public void WriteImagePointer_WritesGbaPointer()
        {
            var rom = MakeRom();
            uint slot = rom.RomInfo.battle_screen_image1_pointer;
            uint newAddr = 0x123456;

            var ud = new Undo.UndoData
            {
                time = System.DateTime.Now,
                name = "test",
                list = new List<Undo.UndoPostion>(),
                filesize = (uint)rom.Data.Length
            };
            bool ok;
            using (ROM.BeginUndoScope(ud))
            {
                ok = ImageBattleScreenCore.WriteImagePointer(rom, slot, newAddr);
            }
            Assert.True(ok);
            Assert.NotEmpty(ud.list);

            // Pointer slot should store the GBA-prefixed pointer (0x08...).
            uint stored = rom.u32(slot);
            Assert.Equal(U.toPointer(newAddr), stored);
            // Resolving the pointer brings us back to the original offset.
            uint resolved = rom.p32(slot);
            Assert.Equal(newAddr, resolved);
        }

        /// <summary>
        /// Per Copilot bot PR #594 review round 2: WriteImagePointer must
        /// validate the pointerSlot is a safe ROM offset before calling
        /// rom.write_p32 (which would otherwise throw).
        /// </summary>
        [Fact]
        public void WriteImagePointer_InvalidPointerSlot_ReturnsFalse()
        {
            var rom = MakeRom();
            // pointerSlot at 0 (header), at NOT_FOUND, near end of ROM.
            Assert.False(ImageBattleScreenCore.WriteImagePointer(rom, 0x00, 0x123456));
            Assert.False(ImageBattleScreenCore.WriteImagePointer(rom, U.NOT_FOUND, 0x123456));
            // 4-byte span overflowing the ROM end.
            uint nearEnd = (uint)rom.Data.Length - 3;
            Assert.False(ImageBattleScreenCore.WriteImagePointer(rom, nearEnd, 0x123456));
        }

        [Fact]
        public void ReadImagePointer_ReadsPointerSlot()
        {
            var rom = MakeRom();
            uint slot = rom.RomInfo.battle_screen_image1_pointer;
            uint expected = rom.p32(slot);
            uint actual = ImageBattleScreenCore.ReadImagePointer(rom, slot);
            Assert.Equal(expected, actual);
        }

        // -----------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------

        /// <summary>
        /// Build a synthetic FE8U ROM with planted TSA/palette/image pointers.
        /// </summary>
        static ROM MakeRom()
        {
            var rom = new ROM();
            byte[] data = new byte[0x2000000];
            // 0xFF-fill the entire ROM. The pointer slots live in ROMFE8U's
            // hardcoded constant table; we cannot relocate them, so we plant
            // pointers in the slots themselves and the data at our chosen
            // offsets.
            // Use Array.Fill for the 32MB free-space init (Copilot bot
            // PR #594 round 3 finding #3 perf/readability).
            System.Array.Fill(data, (byte)0xFF);
            rom.LoadLow("synth.gba", data, "BE8E01");

            // Plant the 5 TSA pointer slots → our TSA storage offsets.
            U.write_u32(rom.Data, rom.RomInfo.battle_screen_TSA1_pointer, U.toPointer(TSA1_OFFSET));
            U.write_u32(rom.Data, rom.RomInfo.battle_screen_TSA2_pointer, U.toPointer(TSA2_OFFSET));
            U.write_u32(rom.Data, rom.RomInfo.battle_screen_TSA3_pointer, U.toPointer(TSA3_OFFSET));
            U.write_u32(rom.Data, rom.RomInfo.battle_screen_TSA4_pointer, U.toPointer(TSA4_OFFSET));
            U.write_u32(rom.Data, rom.RomInfo.battle_screen_TSA5_pointer, U.toPointer(TSA5_OFFSET));

            // Plant the palette pointer slot.
            U.write_u32(rom.Data, rom.RomInfo.battle_screen_palette_pointer, U.toPointer(PALETTE_OFFSET));
            // Plant a 0x80-byte palette block (4 slots x 0x20 bytes) zero-filled.
            for (uint i = 0; i < 0x80; i++) rom.Data[PALETTE_OFFSET + i] = 0;

            // Plant the 5 image pointer slots.
            U.write_u32(rom.Data, rom.RomInfo.battle_screen_image1_pointer, U.toPointer(IMAGE1_OFFSET));
            U.write_u32(rom.Data, rom.RomInfo.battle_screen_image2_pointer, U.toPointer(IMAGE2_OFFSET));
            U.write_u32(rom.Data, rom.RomInfo.battle_screen_image3_pointer, U.toPointer(IMAGE3_OFFSET));
            U.write_u32(rom.Data, rom.RomInfo.battle_screen_image4_pointer, U.toPointer(IMAGE4_OFFSET));
            U.write_u32(rom.Data, rom.RomInfo.battle_screen_image5_pointer, U.toPointer(IMAGE5_OFFSET));

            // Clear out the TSA blocks at our planted offsets (4KB per region
            // is plenty for the small actual blocks).
            for (uint i = 0; i < 0x1000; i++)
            {
                rom.Data[TSA1_OFFSET + i] = 0;
                rom.Data[TSA2_OFFSET + i] = 0;
                rom.Data[TSA3_OFFSET + i] = 0;
                rom.Data[TSA4_OFFSET + i] = 0;
                rom.Data[TSA5_OFFSET + i] = 0;
            }
            return rom;
        }

        static void PlantTSA1(ROM rom)
        {
            uint addr = rom.p32(rom.RomInfo.battle_screen_TSA1_pointer);
            ushort marker = 0x0010;
            for (int y = 0; y <= 5; y++)
            {
                for (int x = 1; x <= 15; x++)
                {
                    U.write_u16(rom.Data, addr, marker++);
                    addr += 2;
                }
            }
        }
        static void PlantTSA2(ROM rom)
        {
            uint addr = rom.p32(rom.RomInfo.battle_screen_TSA2_pointer);
            ushort marker = 0x0070;
            for (int y = 0; y <= 5; y++)
            {
                for (int x = 16; x <= 30; x++)
                {
                    U.write_u16(rom.Data, addr, marker++);
                    addr += 2;
                }
            }
        }
        static void PlantTSA3(ROM rom)
        {
            uint addr = rom.p32(rom.RomInfo.battle_screen_TSA3_pointer);
            ushort marker = 0x00D0;
            for (int y = 13; y <= 19; y++)
            {
                for (int x = 1; x <= 15; x++)
                {
                    U.write_u16(rom.Data, addr, marker++);
                    addr += 2;
                }
            }
        }
        static void PlantTSA4(ROM rom)
        {
            uint addr = rom.p32(rom.RomInfo.battle_screen_TSA4_pointer);
            ushort marker = 0x0140;
            for (int y = 13; y <= 19; y++)
            {
                for (int x = 16; x <= 31; x++)
                {
                    U.write_u16(rom.Data, addr, marker++);
                    addr += 2;
                }
            }
        }
        static void PlantTSA5(ROM rom)
        {
            uint addr = rom.p32(rom.RomInfo.battle_screen_TSA5_pointer);
            ushort marker = 0x0200;
            for (int y = 0; y <= 19; y++)
            {
                for (int x = 31; x <= 32; x++)
                {
                    U.write_u16(rom.Data, addr, marker++);
                    addr += 2;
                }
            }
        }
    }
}
