using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Tests for the slice-2ab ROM-rebuild producer UnitCustomBattleAnimeForm port
    /// (<see cref="RebuildProducerCore.EmitUnitCustomBattleAnimeAt"/>). FE7-only. A two-level pointer
    /// table: the N2 main pointer-table IFR (base = the FE7 pointer slot, block 4, rule
    /// <c>i==0?true:isPointer(u32)</c>, PI {0}), then per N2 entry a ReInitPointer'd inner IFR (block 4,
    /// rule <c>u32(addr)!=0</c>, PI {}). Synthetic NULL-RomInfo ROMs drive the explicit-address seam.
    /// </summary>
    [Collection("SharedState")]
    public class RebuildProducerUnitCustomBattleAnimeTests : IDisposable
    {
        readonly ROM _savedRom = CoreState.ROM;
        readonly ISystemTextEncoder _savedEncoder = CoreState.SystemTextEncoder;

        public void Dispose()
        {
            CoreState.ROM = _savedRom;
            CoreState.SystemTextEncoder = _savedEncoder;
        }

        static ROM CreateTestRom(int size = 0x8000)
        {
            var rom = new ROM();
            rom.SwapNewROMDataDirect(new byte[size]);
            CoreState.ROM = rom;
            CoreState.SystemTextEncoder = new HeadlessSystemTextEncoder(rom);
            return rom;
        }

        static uint Ptr(uint offset) => offset | 0x08000000u;

        [Fact]
        public void EmitUnitCustomBattleAnime_EmitsN2MainIfrAndPerEntryInnerIfr()
        {
            var rom = CreateTestRom();
            uint n2Pointer = 0x0400;     // unit_custom_battle_anime_pointer slot (synthetic)
            uint n2Table = 0x1000;       // N2 base = p32(n2Pointer)
            rom.write_u32(n2Pointer, Ptr(n2Table));

            // N2 rule: i==0 ? true : isPointer(u32(addr)). Two entries then a NULL terminator at i=2.
            uint inner0 = 0x2000, inner1 = 0x2200;
            rom.write_u32(n2Table + 0, Ptr(inner0)); // i=0 (always counted)
            rom.write_u32(n2Table + 4, Ptr(inner1)); // i=1 (isPointer -> counted)
            rom.write_u32(n2Table + 8, 0);           // i=2 (NOT a pointer -> terminates count at 2)

            // Inner IFR (block 4, rule u32(addr)!=0). inner0 has 3 valid records, inner1 has 1.
            rom.write_u32(inner0 + 0, 0x11111111);
            rom.write_u32(inner0 + 4, 0x22222222);
            rom.write_u32(inner0 + 8, 0x33333333);
            rom.write_u32(inner0 + 12, 0);            // terminator
            rom.write_u32(inner1 + 0, 0x44444444);
            rom.write_u32(inner1 + 4, 0);             // terminator

            var list = new List<Address>();
            RebuildProducerCore.EmitUnitCustomBattleAnimeAt(rom, list, n2Pointer);

            // N2 main IFR: addr = n2Table, pointer = n2Pointer slot, block 4, PI {0}, count 2 -> length 4*3.
            Address main = list.Single(a => a.Addr == n2Table);
            Assert.Equal(n2Pointer, main.Pointer);
            Assert.Equal(4u, main.BlockSize);
            Assert.Equal(new uint[] { 0 }, main.PointerIndexes);
            Assert.Equal(4u * (2 + 1), main.Length);
            Assert.Equal(Address.DataTypeEnum.InputFormRef, main.DataType);

            // Inner IFR for entry 0: addr = inner0, pointer = the N2 SLOT (n2Table+0), PI {}, count 3.
            Address i0 = list.Single(a => a.Addr == inner0);
            Assert.Equal(n2Table + 0, i0.Pointer);
            Assert.Equal(4u, i0.BlockSize);
            Assert.Empty(i0.PointerIndexes);
            Assert.Equal(4u * (3 + 1), i0.Length);

            // Inner IFR for entry 1: addr = inner1, pointer = the N2 SLOT (n2Table+4), count 1.
            Address i1 = list.Single(a => a.Addr == inner1);
            Assert.Equal(n2Table + 4, i1.Pointer);
            Assert.Equal(4u * (1 + 1), i1.Length);
        }

        [Fact]
        public void EmitUnitCustomBattleAnime_UnsafeSlot_EmitsNothing()
        {
            var rom = CreateTestRom();
            var list = new List<Address>();
            // n2Pointer 0 -> toOffset(0)=0; isSafetyOffset(0+3) false -> immediate return.
            RebuildProducerCore.EmitUnitCustomBattleAnimeAt(rom, list, 0);
            Assert.Empty(list);
        }

        [Fact]
        public void EmitUnitCustomBattleAnime_ZeroedBase_EmitsNoInnerIfr()
        {
            var rom = CreateTestRom();
            uint n2Pointer = 0x0400;
            rom.write_u32(n2Pointer, 0); // p32 -> 0 (unsafe base) -> AddAddressInstantIFR emits nothing;
                                         // getBlockDataCount(0,...) -> 0 -> no inner loop iterations.

            var list = new List<Address>();
            RebuildProducerCore.EmitUnitCustomBattleAnimeAt(rom, list, n2Pointer);
            Assert.Empty(list);
        }

        [Fact]
        public void EmitUnitCustomBattleAnime_NearEof_DoesNotThrow()
        {
            // N2 base points near EOF so the per-entry inner pointer slots run past where a 4-byte read
            // fits. EmitNestedIfrSub guards p+3 and emits nothing there; no throw.
            var rom = CreateTestRom(0x1010);
            uint n2Pointer = 0x0400;
            uint n2Table = (uint)rom.Data.Length - 8; // only 2 four-byte slots before EOF
            rom.write_u32(n2Pointer, Ptr(n2Table));
            rom.write_u32(n2Table + 0, Ptr(0x0500)); // i=0 counted

            var list = new List<Address>();
            var ex = Record.Exception(() => RebuildProducerCore.EmitUnitCustomBattleAnimeAt(rom, list, n2Pointer));
            Assert.Null(ex);
        }

        [Fact]
        public void EmitUnitCustomBattleAnime_FirstEntryAlwaysCounted_EvenIfNotPointer()
        {
            // N2 rule i==0 -> true unconditionally; so a non-pointer first slot still counts (then the
            // i==1 isPointer check terminates). Proves the i==0 special case is reproduced.
            var rom = CreateTestRom();
            uint n2Pointer = 0x0400;
            uint n2Table = 0x1000;
            rom.write_u32(n2Pointer, Ptr(n2Table));
            rom.write_u32(n2Table + 0, 0x00000005); // i=0: NOT a pointer, but counted (i==0 -> true)
            rom.write_u32(n2Table + 4, 0);           // i=1: not a pointer -> terminates at count 1

            var list = new List<Address>();
            RebuildProducerCore.EmitUnitCustomBattleAnimeAt(rom, list, n2Pointer);

            Address main = list.Single(a => a.Addr == n2Table);
            Assert.Equal(4u * (1 + 1), main.Length); // count 1
        }
    }
}
