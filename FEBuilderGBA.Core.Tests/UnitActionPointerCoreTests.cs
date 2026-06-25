using System;
using System.IO;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Tests for <see cref="UnitActionPointerCore"/> (#1415 — the shared, rework-aware base/predicate/id
    /// resolver behind the Avalonia Unit Action Pointer editor + ListParityHelper). The synthetic FE8U
    /// ROM idiom mirrors <see cref="RebuildProducerCoreTests"/>; the rework gate is planted via the
    /// per-version <c>patch_unitaction_rework_hack</c> accessor, and the real <c>ApplyAction.bin</c>
    /// grep base-resolution path is exercised against a staged temp config tree.
    /// </summary>
    [Collection("SharedState")]
    public class UnitActionPointerCoreTests
    {
        static uint Ptr(uint offset) => offset | 0x08000000;

        static ROM MakeFE8U()
        {
            var rom = new ROM();
            var data = new byte[0x200_0000]; // 32MB — survives ROMFE ctor patch probing
            bool ok = rom.LoadLow("fake.gba", data, "BE8E01");
            Assert.True(ok, "LoadLow did not recognize FE8U version string");
            return rom;
        }

        static void PlantReworkGate(ROM rom)
        {
            uint expected;
            uint hackAddr = rom.RomInfo.patch_unitaction_rework_hack(out expected);
            rom.write_u32(hackAddr, expected);
            Assert.True(PatchDetection.SearchUnitActionReworkPatch(rom));
        }

        // ---- IsRework -----------------------------------------------------

        [Fact]
        public void IsRework_NullRom_ReturnsFalse()
        {
            Assert.False(UnitActionPointerCore.IsRework(null));
        }

        [Fact]
        public void IsRework_VanillaRom_ReturnsFalse()
        {
            var rom = MakeFE8U();
            Assert.False(UnitActionPointerCore.IsRework(rom));
        }

        [Fact]
        public void IsRework_PatchedRom_ReturnsTrue()
        {
            var rom = MakeFE8U();
            PlantReworkGate(rom);
            Assert.True(UnitActionPointerCore.IsRework(rom));
        }

        // ---- ResolveBaseSlot / ResolveBaseAddress -------------------------

        [Fact]
        public void ResolveBaseSlot_NonRework_ReturnsRomInfoSlot()
        {
            var savedRom = CoreState.ROM;
            try
            {
                var rom = MakeFE8U();
                CoreState.ROM = rom;
                Assert.Equal(rom.RomInfo.unitaction_function_pointer,
                    UnitActionPointerCore.ResolveBaseSlot(rom));
            }
            finally { CoreState.ROM = savedRom; }
        }

        [Fact]
        public void ResolveBaseAddress_NonRework_DereferencesSlot()
        {
            var savedRom = CoreState.ROM;
            try
            {
                var rom = MakeFE8U();
                CoreState.ROM = rom;
                uint slot = rom.RomInfo.unitaction_function_pointer;
                uint table = 0x101000;
                rom.write_u32(slot, Ptr(table));
                Assert.Equal(table, UnitActionPointerCore.ResolveBaseAddress(rom));
            }
            finally { CoreState.ROM = savedRom; }
        }

        [Fact]
        public void ResolveBaseAddress_NullRom_ReturnsZero()
        {
            Assert.Equal(0u, UnitActionPointerCore.ResolveBaseAddress(null));
        }

        [Fact]
        public void ResolveBaseAddress_UnsafeDereferencedPointer_ReturnsZero()
        {
            var savedRom = CoreState.ROM;
            try
            {
                var rom = MakeFE8U();
                CoreState.ROM = rom;
                uint slot = rom.RomInfo.unitaction_function_pointer;
                rom.write_u32(slot, 0x00000001); // not a safe pointer -> base resolves to 0
                Assert.Equal(0u, UnitActionPointerCore.ResolveBaseAddress(rom));
            }
            finally { CoreState.ROM = savedRom; }
        }

        [Fact]
        public void ResolveBaseSlot_Rework_NoConfig_ReturnsZero()
        {
            var savedRom = CoreState.ROM;
            var savedBase = CoreState.BaseDirectory;
            try
            {
                var rom = MakeFE8U();
                CoreState.ROM = rom;
                CoreState.BaseDirectory = null; // missing config tree -> WF File.Exists false -> 0
                PlantReworkGate(rom);
                Assert.Equal(0u, UnitActionPointerCore.ResolveBaseSlot(rom));
                // And no config slot -> no table base -> empty editor (WF ReInitPointer(0)).
                Assert.Equal(0u, UnitActionPointerCore.ResolveBaseAddress(rom));
            }
            finally { CoreState.ROM = savedRom; CoreState.BaseDirectory = savedBase; }
        }

        /// <summary>
        /// Copilot review gap #2/#4: exercise the ACTUAL rework base-resolution path — stage a temp
        /// <c>ApplyAction.bin</c>, plant the same bytes in the ROM at a block-4 offset ≥
        /// <c>unitaction_function_pointer - 0x100</c> with the relocated table SLOT immediately after,
        /// and prove the helper greps the slot then dereferences the relocated table base.
        /// </summary>
        [Fact]
        public void ResolveBaseSlotAndAddress_Rework_ResolvesRelocatedTableFromApplyActionBin()
        {
            var savedRom = CoreState.ROM;
            var savedBase = CoreState.BaseDirectory;
            string tempBase = Path.Combine(Path.GetTempPath(),
                "fe1415_" + Guid.NewGuid().ToString("N"));
            try
            {
                var rom = MakeFE8U();
                CoreState.ROM = rom;
                PlantReworkGate(rom);

                // Signature bytes -> staged config file.
                byte[] sig = { 0xDE, 0xAD, 0xBE, 0xEF, 0x10, 0x20, 0x30, 0x40 };
                string asmDir = Path.Combine(tempBase, "config", "patch2",
                    rom.RomInfo.VersionToFilename, "UnitActionRework", "UnitActionRework", "asm");
                Directory.CreateDirectory(asmDir);
                File.WriteAllBytes(Path.Combine(asmDir, "ApplyAction.bin"), sig);
                CoreState.BaseDirectory = tempBase;

                // Plant the signature in the ROM at a block-4 offset >= hint (= uafp - 0x100), with the
                // relocated table SLOT immediately after it (GrepEnd returns matchOffset + sig.Length).
                uint uafp = rom.RomInfo.unitaction_function_pointer; // FE8U: 0x3205C
                uint sigOffset = 0x40000; // > hint, and (0x40000 - hint) % 4 == 0
                Assert.True(sigOffset > uafp - 0x100);
                Assert.Equal(0u, (sigOffset - (uafp - 0x100)) % 4);
                for (uint k = 0; k < sig.Length; k++) rom.write_u8(sigOffset + k, sig[k]);

                uint slotAddr = sigOffset + (uint)sig.Length; // == GrepEnd result
                uint relocatedTable = 0x500000;
                rom.write_u32(slotAddr, Ptr(relocatedTable));

                Assert.Equal(slotAddr, UnitActionPointerCore.ResolveBaseSlot(rom));
                Assert.Equal(relocatedTable, UnitActionPointerCore.ResolveBaseAddress(rom));
                // The relocated base must DIFFER from the vanilla slot's dereference (the whole bug).
                Assert.NotEqual(rom.RomInfo.unitaction_function_pointer, slotAddr);
            }
            finally
            {
                CoreState.ROM = savedRom;
                CoreState.BaseDirectory = savedBase;
                try { if (Directory.Exists(tempBase)) Directory.Delete(tempBase, true); } catch { }
            }
        }

        // ---- IsDataExists -------------------------------------------------

        [Fact]
        public void IsDataExists_NonRework_OnlySafePointers()
        {
            var rom = MakeFE8U();
            uint addr = 0x101000;
            rom.write_u32(addr + 0, Ptr(0x120000));
            rom.write_u32(addr + 4, 0x00000001); // not a safe pointer
            rom.write_u32(addr + 8, 0x00000000); // 0 is NOT valid in non-rework

            Assert.True(UnitActionPointerCore.IsDataExists(rom, addr + 0, false));
            Assert.False(UnitActionPointerCore.IsDataExists(rom, addr + 4, false));
            Assert.False(UnitActionPointerCore.IsDataExists(rom, addr + 8, false));
        }

        [Fact]
        public void IsDataExists_Rework_AcceptsZeroAndMaskedPointer_RejectsNotFound()
        {
            var rom = MakeFE8U();
            uint addr = 0x101000;
            rom.write_u32(addr + 0, 0x00000000);  // NULL routine -> VALID in rework
            rom.write_u32(addr + 4, 0x18120000);   // high nibble = abForcedYeild flag; & 0x0FFFFFFF = 0x08120000 -> safe
            rom.write_u32(addr + 8, U.NOT_FOUND);  // 0xFFFFFFFF -> terminator
            rom.write_u32(addr + 12, 0x00000003);  // masked = 0x00000003 -> NOT a safe pointer

            Assert.True(UnitActionPointerCore.IsDataExists(rom, addr + 0, true));
            Assert.True(UnitActionPointerCore.IsDataExists(rom, addr + 4, true));
            Assert.False(UnitActionPointerCore.IsDataExists(rom, addr + 8, true));
            Assert.False(UnitActionPointerCore.IsDataExists(rom, addr + 12, true));
        }

        [Fact]
        public void IsDataExists_NullRom_ReturnsFalse()
        {
            Assert.False(UnitActionPointerCore.IsDataExists(null, 0x100, false));
            Assert.False(UnitActionPointerCore.IsDataExists(null, 0x100, true));
        }

        [Fact]
        public void IsDataExists_PastEof_ReturnsFalse()
        {
            var rom = MakeFE8U();
            uint pastEof = (uint)rom.Data.Length; // addr + 4 > Length
            Assert.False(UnitActionPointerCore.IsDataExists(rom, pastEof, false));
            Assert.False(UnitActionPointerCore.IsDataExists(rom, pastEof, true));
        }

        // ---- ResolveActionId / ResolveActionIdFromAddr --------------------

        [Theory]
        [InlineData(0, false, 1u)]   // non-rework: ids start at 1
        [InlineData(1, false, 2u)]
        [InlineData(0, true, 0u)]    // rework: ids are 0-based
        [InlineData(1, true, 1u)]
        public void ResolveActionId_AppliesOriginRule(int index, bool isRework, uint expected)
        {
            Assert.Equal(expected, UnitActionPointerCore.ResolveActionId(index, isRework));
        }

        [Fact]
        public void ResolveActionIdFromAddr_NonRework_FirstRowIsOne()
        {
            uint baseAddr = 0x101000;
            Assert.Equal(1u, UnitActionPointerCore.ResolveActionIdFromAddr(baseAddr + 0, baseAddr, false));
            Assert.Equal(2u, UnitActionPointerCore.ResolveActionIdFromAddr(baseAddr + 4, baseAddr, false));
        }

        [Fact]
        public void ResolveActionIdFromAddr_Rework_FirstRowIsZero()
        {
            uint baseAddr = 0x101000;
            Assert.Equal(0u, UnitActionPointerCore.ResolveActionIdFromAddr(baseAddr + 0, baseAddr, true));
            Assert.Equal(1u, UnitActionPointerCore.ResolveActionIdFromAddr(baseAddr + 4, baseAddr, true));
        }

        [Fact]
        public void ResolveActionIdFromAddr_BelowBase_ReturnsZero()
        {
            uint baseAddr = 0x101000;
            Assert.Equal(0u, UnitActionPointerCore.ResolveActionIdFromAddr(baseAddr - 4, baseAddr, false));
            Assert.Equal(0u, UnitActionPointerCore.ResolveActionIdFromAddr(0x100, 0, false));
        }
    }
}
