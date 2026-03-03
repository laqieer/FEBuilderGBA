using System.Collections.Generic;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    public class PointerCalcCoreTests
    {
        [Fact]
        public void MakeSkipDataByPointer_FindsGBAPointers()
        {
            // Create synthetic ROM with a GBA pointer at offset 0
            byte[] rom = new byte[256];
            // Write GBA pointer 0x08000010 at offset 0
            uint ptr = 0x08000010;
            rom[0] = (byte)(ptr & 0xFF);
            rom[1] = (byte)((ptr >> 8) & 0xFF);
            rom[2] = (byte)((ptr >> 16) & 0xFF);
            rom[3] = (byte)((ptr >> 24) & 0xFF);

            var skip = PointerCalcCore.MakeSkipDataByPointer(rom);
            Assert.Contains(0x10u, skip); // The offset the pointer references
        }

        [Fact]
        public void MakeSkipDataByPointer_IgnoresNonGBAPointers()
        {
            byte[] rom = new byte[256];
            // Write a non-GBA value
            rom[0] = 0x12; rom[1] = 0x34; rom[2] = 0x56; rom[3] = 0x00;

            var skip = PointerCalcCore.MakeSkipDataByPointer(rom);
            Assert.Empty(skip);
        }

        [Fact]
        public void MakeSkipDataByPointer_NullReturnsEmpty()
        {
            var skip = PointerCalcCore.MakeSkipDataByPointer(null);
            Assert.Empty(skip);
        }

        [Fact]
        public void MakeSkipDataByCode_FindsLDRReferences()
        {
            byte[] rom = new byte[256];
            // Write a Thumb LDR Rn, [PC, #imm] at offset 0
            // LDR R0, [PC, #4] = 0x4801 (opcode 0x48, immediate 1 word = 4 bytes)
            rom[0] = 0x01; rom[1] = 0x48;

            var skip = PointerCalcCore.MakeSkipDataByCode(rom);
            // PC = (0 + 4) & ~3 = 4, offset = 1*4 = 4, target = 4+4 = 8
            Assert.Contains(8u, skip);
        }

        [Fact]
        public void SearchAddresses_FindsDataMatch()
        {
            // Create source and target with same data at different offsets
            byte[] source = new byte[256];
            byte[] target = new byte[256];

            // Write distinctive pattern at offset 0x10 in source
            for (int i = 0; i < 16; i++)
                source[0x10 + i] = (byte)(0xAA + i);

            // Write same pattern at offset 0x40 in target
            for (int i = 0; i < 16; i++)
                target[0x40 + i] = (byte)(0xAA + i);

            var results = PointerCalcCore.SearchAddresses(source, target,
                new List<uint> { 0x10 }, searchLength: 16);

            Assert.NotEmpty(results);
            Assert.Contains(results, r => r.TargetAddress == 0x40 && r.MatchType == "Data");
        }

        [Fact]
        public void ParseAddressList_ParsesHexAddresses()
        {
            var addrs = PointerCalcCore.ParseAddressList("0x100,0x200,0x300");
            Assert.Equal(3, addrs.Count);
            Assert.Equal(0x100u, addrs[0]);
            Assert.Equal(0x200u, addrs[1]);
            Assert.Equal(0x300u, addrs[2]);
        }

        [Fact]
        public void ParseAddressList_ParsesWithoutPrefix()
        {
            var addrs = PointerCalcCore.ParseAddressList("100,200");
            Assert.Equal(2, addrs.Count);
            Assert.Equal(0x100u, addrs[0]);
            Assert.Equal(0x200u, addrs[1]);
        }

        [Fact]
        public void ParseAddressList_EmptyReturnsEmpty()
        {
            var addrs = PointerCalcCore.ParseAddressList("");
            Assert.Empty(addrs);
        }

        [Fact]
        public void ParseAddressList_NullReturnsEmpty()
        {
            var addrs = PointerCalcCore.ParseAddressList(null);
            Assert.Empty(addrs);
        }
    }
}
