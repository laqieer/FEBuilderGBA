// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for SupportUnitNavigation.UnitAddrForOneBased / OneBasedIdFromPickIndex
// (#937) — the 1-based ROM-unit-id → unit-table-address conversion (and its
// Pick-index inverse) that fixes the off-by-one in 8 Avalonia editors that
// previously passed a raw 1-based unit ID into base + unitId * dataSize.
//
// Uses the same synthetic-ROM-blob pattern as SupportUnitNavigationTests /
// NameResolverTests: a 16 MiB zero blob loaded via ROM.LoadLow with a known
// version signature, with the unit_pointer field planted to point at a chosen
// table base. No real ROM file, Huffman, or text init is required — the
// address math and the name-equality assertions both read the planted bytes
// directly, so the tests run unconditionally (no skip-when-no-ROM needed).
using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class UnitAddrForOneBasedTests
    {
        const uint RawBase = 0x200000;

        static void WriteU32(byte[] data, uint addr, uint value)
        {
            int i = checked((int)addr);
            data[i + 0] = (byte)(value & 0xFF);
            data[i + 1] = (byte)((value >> 8) & 0xFF);
            data[i + 2] = (byte)((value >> 16) & 0xFF);
            data[i + 3] = (byte)((value >> 24) & 0xFF);
        }

        static ROM MakeRom(string sig)
        {
            var rom = new ROM();
            rom.LoadLow("test.gba", new byte[0x1000000], sig);
            Assert.NotNull(rom.RomInfo);
            return rom;
        }

        // Point unit_pointer at a synthetic table base so GetUnitTableBase
        // resolves to a safe, known offset (FE6 then adds unit_datasize).
        static ROM MakeRomWithUnitTable(string sig)
        {
            var rom = MakeRom(sig);
            WriteU32(rom.Data, rom.RomInfo.unit_pointer, RawBase | 0x08000000);
            return rom;
        }

        // ---- UnitAddrForOneBased --------------------------------------------

        [Fact]
        public void UnitAddrForOneBased_NullRom_ReturnsZero()
        {
            Assert.Equal(0u, SupportUnitNavigation.UnitAddrForOneBased(null!, 1));
        }

        [Fact]
        public void UnitAddrForOneBased_ZeroId_ReturnsZero()
        {
            var rom = MakeRomWithUnitTable("BE8E01"); // FE8U
            Assert.Equal(0u, SupportUnitNavigation.UnitAddrForOneBased(rom, 0));
        }

        [Fact]
        public void UnitAddrForOneBased_FE6_IdOne_EqualsLogicalTableBase()
        {
            // FE6: the table effectively starts at p32(unit_pointer) +
            // unit_datasize (dummy entry at raw index 0). 1-based id 1 → the
            // first LOGICAL unit, i.e. exactly GetUnitTableBase (the logical
            // base), NOT the dummy raw entry.
            var rom = MakeRomWithUnitTable("AFEJ01");
            Assert.Equal(6, rom.RomInfo.version);

            uint dataSize = rom.RomInfo.unit_datasize;
            uint expected = RawBase + dataSize; // == GetUnitTableBase on FE6
            Assert.Equal(expected, SupportUnitNavigation.UnitAddrForOneBased(rom, 1));
        }

        [Theory]
        [InlineData("AFEJ01")] // FE6
        [InlineData("AE7E01")] // FE7U
        [InlineData("BE8E01")] // FE8U
        public void UnitAddrForOneBased_SampleIds_EqualBasePlusZeroBasedTimesSize(string sig)
        {
            var rom = MakeRomWithUnitTable(sig);
            uint baseAddr = SupportUnitNavigation.GetUnitTableBase(rom);
            uint dataSize = rom.RomInfo.unit_datasize;
            Assert.True(baseAddr != 0);
            Assert.True(dataSize != 0);

            foreach (uint id in new uint[] { 1, 2, 5, 0x10 })
            {
                uint expected = baseAddr + (id - 1) * dataSize;
                Assert.Equal(expected, SupportUnitNavigation.UnitAddrForOneBased(rom, id));
            }
        }

        // ---- Name parity: GetUnitNameByOneBasedId(id) == ResolveUnitTableName(rom, id-1) ----

        [Theory]
        [InlineData("AFEJ01")] // FE6
        [InlineData("AE7E01")] // FE7U
        [InlineData("BE8E01")] // FE8U
        public void GetUnitNameByOneBasedId_MatchesResolveUnitTableNameAtIdMinusOne(string sig)
        {
            var savedRom = CoreState.ROM;
            try
            {
                var rom = MakeRomWithUnitTable(sig);
                CoreState.ROM = rom;

                foreach (uint id in new uint[] { 1, 2, 5, 0x10 })
                {
                    // GetUnitNameByOneBasedId is cached under ("unit1", id);
                    // clear so each comparison reflects the planted ROM bytes.
                    NameResolver.ClearCache();
                    string oneBased = NameResolver.GetUnitNameByOneBasedId(id);
                    string zeroBased = SupportUnitNavigation.ResolveUnitTableName(rom, id - 1);
                    Assert.Equal(zeroBased, oneBased);
                }
            }
            finally
            {
                CoreState.ROM = savedRom;
                NameResolver.ClearCache();
            }
        }

        // ---- OneBasedIdFromPickIndex (pure, no ROM) -------------------------

        [Fact]
        public void OneBasedIdFromPickIndex_ZeroIndex_ReturnsOne()
        {
            Assert.Equal(1u, SupportUnitNavigation.OneBasedIdFromPickIndex(0));
        }

        [Fact]
        public void OneBasedIdFromPickIndex_FifteenIndex_ReturnsSixteen()
        {
            Assert.Equal(16u, SupportUnitNavigation.OneBasedIdFromPickIndex(15));
        }
    }
}
