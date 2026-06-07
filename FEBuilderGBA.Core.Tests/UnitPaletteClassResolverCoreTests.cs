// SPDX-License-Identifier: GPL-3.0-or-later
// #985 tests for the cross-platform port of WinForms
// ImageUnitPaletteForm.MakeClassList palette->class resolution:
//   - UnitPaletteClassResolverCore.ResolveDefaultPreviewClass (FE8 dedicated
//     color/class tables; FE6/FE7 in-unit-record palette ids)
//   - UnitPaletteClassResolverCore.FindFirstClassWithAnime (fallback scan)
//
// Uses a synthetic ROM with a custom RomInfo (StubRomInfo) so the unit/class
// table pointers + the FE8 unit-palette color/class tables are deterministic.
using System;
using FEBuilderGBA;
using FEBuilderGBA.Core;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class UnitPaletteClassResolverCoreTests : IDisposable
    {
        readonly ROM _prevRom;

        public UnitPaletteClassResolverCoreTests()
        {
            _prevRom = CoreState.ROM;
        }

        public void Dispose()
        {
            CoreState.ROM = _prevRom;
        }

        // Synthetic ROM layout (arbitrary free-space offsets).
        const uint UNIT_PTR_SLOT      = 0x100;
        const uint UNIT_BASE          = 0x1000;
        const uint UNIT_DATASIZE      = 44;      // FE6/7/8 unit entry size
        const uint UNIT_MAXCOUNT      = 16;
        const uint CLASS_PTR_SLOT     = 0x110;
        const uint CLASS_BASE         = 0x2000;
        const uint CLASS_DATASIZE     = 84;
        const uint COLOR_PTR_SLOT     = 0x120;   // FE8 unit_palette_color_pointer
        const uint COLOR_BASE         = 0x3000;
        const uint CLASSTBL_PTR_SLOT  = 0x130;   // FE8 unit_palette_class_pointer
        const uint CLASSTBL_BASE      = 0x4000;

        // ================================================================
        // FE8 — dedicated color + class tables, 7 palettes per unit
        // ================================================================

        static ROM MakeFE8Rom()
        {
            var rom = new ROM();
            byte[] data = new byte[0x100000];
            Array.Fill(data, (byte)0x00);
            rom.LoadLow("synth.gba", data, "BE8E01");
            SetRomInfo(rom, new StubRomInfo(version: 8,
                unitPointer: UNIT_PTR_SLOT, unitDataSize: UNIT_DATASIZE, unitMaxCount: UNIT_MAXCOUNT,
                classPointer: CLASS_PTR_SLOT, classDataSize: CLASS_DATASIZE,
                colorPointer: COLOR_PTR_SLOT, classTablePointer: CLASSTBL_PTR_SLOT));

            U.write_u32(rom.Data, COLOR_PTR_SLOT, U.toPointer(COLOR_BASE));
            U.write_u32(rom.Data, CLASSTBL_PTR_SLOT, U.toPointer(CLASSTBL_BASE));
            return rom;
        }

        // Plant: unit i, palette n -> (paletteId, classId).
        static void PlantFE8(ROM rom, int i, int n, byte paletteId, byte classId)
        {
            U.write_u8(rom.Data, COLOR_BASE + (uint)i * 7 + (uint)n, paletteId);
            U.write_u8(rom.Data, CLASSTBL_BASE + (uint)i * 7 + (uint)n, classId);
        }

        [Fact]
        public void FE8_ResolvesClassForSlot()
        {
            ROM rom = MakeFE8Rom();
            // unit 2, palette slot index 4 (paletteId = 5) -> class 0x33.
            PlantFE8(rom, 2, 0, paletteId: 5, classId: 0x33);
            // ResolveDefaultPreviewClass takes the 0-based slot index (paletteId-1).
            Assert.Equal(0x33u, UnitPaletteClassResolverCore.ResolveDefaultPreviewClass(rom, 4));
        }

        [Fact]
        public void FE8_DifferentSlotsResolveDifferentClasses()
        {
            // Regression for the selection-change bug: two DISTINCT slots must map
            // to two DISTINCT classes (deterministic — we control the data).
            ROM rom = MakeFE8Rom();
            PlantFE8(rom, 1, 0, paletteId: 3, classId: 0x10); // slot index 2
            PlantFE8(rom, 5, 2, paletteId: 8, classId: 0x20); // slot index 7

            uint a = UnitPaletteClassResolverCore.ResolveDefaultPreviewClass(rom, 2);
            uint b = UnitPaletteClassResolverCore.ResolveDefaultPreviewClass(rom, 7);
            Assert.Equal(0x10u, a);
            Assert.Equal(0x20u, b);
            Assert.NotEqual(a, b);
        }

        [Fact]
        public void FE8_FirstMatchWins()
        {
            ROM rom = MakeFE8Rom();
            // Two units both reference slot index 2 (paletteId 3); the scan order
            // is unit-ascending then palette-ascending, so unit 1 wins.
            PlantFE8(rom, 1, 0, paletteId: 3, classId: 0xAA);
            PlantFE8(rom, 3, 0, paletteId: 3, classId: 0xBB);
            Assert.Equal(0xAAu, UnitPaletteClassResolverCore.ResolveDefaultPreviewClass(rom, 2));
        }

        [Fact]
        public void FE8_NoMatch_ReturnsZero()
        {
            ROM rom = MakeFE8Rom();
            PlantFE8(rom, 0, 0, paletteId: 1, classId: 0x05); // slot index 0
            // Ask for a slot nobody uses.
            Assert.Equal(0u, UnitPaletteClassResolverCore.ResolveDefaultPreviewClass(rom, 9));
        }

        [Fact]
        public void FE8_ZeroDereferencedBase_ReturnsZero_NoBogusScan()
        {
            // The color pointer LOCATION is valid (in-bounds) but it DEREFERENCES
            // to 0 (an invalid base). The guard must reject the unsafe base and
            // return 0 instead of scanning unrelated ROM bytes (~0x200+) and
            // possibly returning a bogus class id.
            ROM rom = MakeFE8Rom();
            // Overwrite the color pointer slot with a 0 pointer.
            U.write_u32(rom.Data, COLOR_PTR_SLOT, 0);
            // Plant a "class" byte at the location a naive scan (colorBase=0 ->
            // reads near 0) might trip on — must NOT be returned.
            Assert.Equal(0u, UnitPaletteClassResolverCore.ResolveDefaultPreviewClass(rom, 0));
        }

        [Fact]
        public void FE8_PaletteIdZero_Ignored()
        {
            ROM rom = MakeFE8Rom();
            // paletteId 0 means "none" — even though slotIndex -1 != it, the loop
            // skips paletteId<=0. Plant a real one at a different palette slot.
            PlantFE8(rom, 0, 0, paletteId: 0, classId: 0x99);
            PlantFE8(rom, 0, 1, paletteId: 1, classId: 0x77); // slot index 0
            Assert.Equal(0x77u, UnitPaletteClassResolverCore.ResolveDefaultPreviewClass(rom, 0));
        }

        // ================================================================
        // FE6/FE7 — palette ids inside each unit record (+35 / +36)
        // ================================================================

        static ROM MakeFE67Rom(int version)
        {
            var rom = new ROM();
            byte[] data = new byte[0x100000];
            Array.Fill(data, (byte)0x00);
            rom.LoadLow("synth.gba", data, version == 6 ? "AFEJ01" : "AE7E01");
            SetRomInfo(rom, new StubRomInfo(version: version,
                unitPointer: UNIT_PTR_SLOT, unitDataSize: UNIT_DATASIZE, unitMaxCount: UNIT_MAXCOUNT,
                classPointer: CLASS_PTR_SLOT, classDataSize: CLASS_DATASIZE,
                colorPointer: 0, classTablePointer: 0));

            U.write_u32(rom.Data, UNIT_PTR_SLOT, U.toPointer(UNIT_BASE));
            U.write_u32(rom.Data, CLASS_PTR_SLOT, U.toPointer(CLASS_BASE));
            return rom;
        }

        // Unit record base for uid (uid 1-based; record = base + (uid-1)*size).
        static uint UnitAddr(uint uid) => UNIT_BASE + (uid - 1) * UNIT_DATASIZE;
        // Class record base for cid (cid indexes directly).
        static uint ClassAddr(uint cid) => CLASS_BASE + cid * CLASS_DATASIZE;

        [Fact]
        public void FE7_LowPalette_ResolvesBaseClass()
        {
            ROM rom = MakeFE67Rom(7);
            // unit uid=2: low-palette id (+35) = 4 (slot index 3), base class (+5) = 0x12.
            U.write_u8(rom.Data, UnitAddr(2) + 35, 4);
            U.write_u8(rom.Data, UnitAddr(2) + 5, 0x12);
            Assert.Equal(0x12u, UnitPaletteClassResolverCore.ResolveDefaultPreviewClass(rom, 3));
        }

        [Fact]
        public void FE7_HighPalette_BaseClassAlreadyHigh_ReturnsBaseClass()
        {
            ROM rom = MakeFE67Rom(7);
            // unit uid=2: high-palette id (+36) = 6 (slot index 5), base class (+5) = 0x40.
            U.write_u8(rom.Data, UnitAddr(2) + 36, 6);
            U.write_u8(rom.Data, UnitAddr(2) + 5, 0x40);
            // class 0x40 is already a high class (FE7 flag at class+41 bit0 = 1).
            U.write_u8(rom.Data, ClassAddr(0x40) + 41, 0x01);
            Assert.Equal(0x40u, UnitPaletteClassResolverCore.ResolveDefaultPreviewClass(rom, 5));
        }

        [Fact]
        public void FE7_HighPalette_PromotesViaChangeClass()
        {
            ROM rom = MakeFE67Rom(7);
            // unit uid=3: high-palette id (+36) = 2 (slot index 1), base class (+5) = 0x20.
            U.write_u8(rom.Data, UnitAddr(3) + 36, 2);
            U.write_u8(rom.Data, UnitAddr(3) + 5, 0x20);
            // class 0x20 is a LOW class (flag at +41 bit0 = 0) with a change(CC)
            // class (class+5) = 0x21, which IS a high class.
            U.write_u8(rom.Data, ClassAddr(0x20) + 41, 0x00);
            U.write_u8(rom.Data, ClassAddr(0x20) + 5, 0x21);
            U.write_u8(rom.Data, ClassAddr(0x21) + 41, 0x01);
            Assert.Equal(0x21u, UnitPaletteClassResolverCore.ResolveDefaultPreviewClass(rom, 1));
        }

        [Fact]
        public void FE6_LowPalette_ResolvesBaseClass()
        {
            ROM rom = MakeFE67Rom(6);
            U.write_u8(rom.Data, UnitAddr(4) + 35, 7); // slot index 6
            U.write_u8(rom.Data, UnitAddr(4) + 5, 0x09);
            Assert.Equal(0x09u, UnitPaletteClassResolverCore.ResolveDefaultPreviewClass(rom, 6));
        }

        [Fact]
        public void FE6_HighPalette_PromotesViaChangeClass_UsesPlus37Flag()
        {
            ROM rom = MakeFE67Rom(6);
            U.write_u8(rom.Data, UnitAddr(5) + 36, 3); // slot index 2
            U.write_u8(rom.Data, UnitAddr(5) + 5, 0x30);
            // FE6 high-class flag lives at class+37 (NOT +41). Base 0x30 is low,
            // CC class (class+5) = 0x31 is high (flag +37 bit0 = 1).
            U.write_u8(rom.Data, ClassAddr(0x30) + 37, 0x00);
            U.write_u8(rom.Data, ClassAddr(0x30) + 5, 0x31);
            U.write_u8(rom.Data, ClassAddr(0x31) + 37, 0x01);
            Assert.Equal(0x31u, UnitPaletteClassResolverCore.ResolveDefaultPreviewClass(rom, 2));
        }

        // ================================================================
        // Guards — never throw, return 0 on bad input
        // ================================================================

        [Fact]
        public void NullRom_ReturnsZero()
        {
            Assert.Equal(0u, UnitPaletteClassResolverCore.ResolveDefaultPreviewClass(null, 0));
        }

        [Fact]
        public void NegativeSlotIndex_ReturnsZero()
        {
            ROM rom = MakeFE8Rom();
            PlantFE8(rom, 0, 0, paletteId: 1, classId: 0x05);
            Assert.Equal(0u, UnitPaletteClassResolverCore.ResolveDefaultPreviewClass(rom, -1));
        }

        [Fact]
        public void OutOfRangeSlot_ReturnsZero()
        {
            ROM rom = MakeFE8Rom();
            PlantFE8(rom, 0, 0, paletteId: 1, classId: 0x05);
            Assert.Equal(0u, UnitPaletteClassResolverCore.ResolveDefaultPreviewClass(rom, 250));
        }

        // ================================================================
        // FindFirstClassWithAnime — fallback
        // ================================================================

        [Fact]
        public void FindFirstClassWithAnime_NullRom_ReturnsZero()
        {
            Assert.Equal(0u, UnitPaletteClassResolverCore.FindFirstClassWithAnime(null));
        }

        [Fact]
        public void FindFirstClassWithAnime_ReturnsFirstClassWithAnime()
        {
            ROM rom = MakeFE8Rom();
            U.write_u32(rom.Data, UNIT_PTR_SLOT, U.toPointer(UNIT_BASE));
            U.write_u32(rom.Data, CLASS_PTR_SLOT, U.toPointer(CLASS_BASE));

            // Build a small class table: classes 1..3 exist (flag at +4 != 0).
            // Class 2 has a battle anime; class 1 does not.
            for (uint cid = 1; cid <= 3; cid++)
            {
                U.write_u8(rom.Data, ClassAddr(cid) + 4, 0x01); // mark entry as existing
            }
            // Class 2 anime-setting pointer at class+52 (FE8) -> a setting block
            // whose +2 holds a non-zero anime id.
            const uint settingBlock = 0x5000;
            U.write_u32(rom.Data, ClassAddr(2) + 52, U.toPointer(settingBlock));
            U.write_u16(rom.Data, settingBlock + 2, 0x2A);

            Assert.Equal(2u, UnitPaletteClassResolverCore.FindFirstClassWithAnime(rom));
        }

        // ================================================================
        // Stub RomInfo
        // ================================================================

        sealed class StubRomInfo : ROMFEINFO
        {
            public StubRomInfo(int version, uint unitPointer, uint unitDataSize, uint unitMaxCount,
                uint classPointer, uint classDataSize, uint colorPointer, uint classTablePointer)
            {
                this.version = version;
                this.unit_pointer = unitPointer;
                this.unit_datasize = unitDataSize;
                this.unit_maxcount = unitMaxCount;
                this.class_pointer = classPointer;
                this.class_datasize = classDataSize;
                this.unit_palette_color_pointer = colorPointer;
                this.unit_palette_class_pointer = classTablePointer;
            }
        }

        static void SetRomInfo(ROM rom, ROMFEINFO info)
        {
            var prop = typeof(ROM).GetProperty("RomInfo");
            prop?.GetSetMethod(true)?.Invoke(rom, new object[] { info });
        }
    }
}
