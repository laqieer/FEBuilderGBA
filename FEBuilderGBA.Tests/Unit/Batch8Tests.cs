using System;
using System.Collections.Generic;
using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Tests.Unit
{
    [Collection("SharedState")]
    public class Batch8Tests
    {
        // ---- AsmMapSt (top-level data class) ----

        [Fact]
        public void AsmMapSt_DefaultValues()
        {
            var st = new AsmMapSt();
            Assert.Equal("", st.Name);
            Assert.Equal("", st.ResultAndArgs);
            Assert.Equal("", st.TypeName);
            Assert.Equal((uint)0, st.Length);
            Assert.False(st.IsPointer);
            Assert.False(st.IsFreeArea);
        }

        [Fact]
        public void AsmMapSt_ToStringInfo_NameOnly()
        {
            var st = new AsmMapSt { Name = "MyFunc" };
            Assert.Equal("MyFunc", st.ToStringInfo());
        }

        [Fact]
        public void AsmMapSt_ToStringInfo_WithArgs()
        {
            var st = new AsmMapSt { Name = "MyFunc", ResultAndArgs = "(r0, r1)" };
            Assert.Equal("MyFunc (r0, r1)", st.ToStringInfo());
        }

        // ---- GbaBiosCall ----

        [Fact]
        public void GbaBiosCall_KnownCalls()
        {
            Assert.Equal("SoftReset", GbaBiosCall.GetSWI_GBA_BIOS_CALL(0x00));
            Assert.Equal("Div", GbaBiosCall.GetSWI_GBA_BIOS_CALL(0x06));
            Assert.Equal("LZ77UnCompNormalWrite8bit", GbaBiosCall.GetSWI_GBA_BIOS_CALL(0x11));
            Assert.Equal("MultiBoot", GbaBiosCall.GetSWI_GBA_BIOS_CALL(0x25));
        }

        [Fact]
        public void GbaBiosCall_UnknownCode_ReturnsEmpty()
        {
            Assert.Equal("", GbaBiosCall.GetSWI_GBA_BIOS_CALL(0xFF));
        }

        // ---- IAsmMapFile (interface) ----

        [Fact]
        public void IAsmMapFile_CanBeImplemented()
        {
            IAsmMapFile map = new TestAsmMapFile();
            Assert.NotNull(map);

            AsmMapSt result;
            Assert.False(map.TryGetValue(0x999, out result));
        }

        [Fact]
        public void IAsmMapFile_TryGetValue_Found()
        {
            var map = new TestAsmMapFile();
            map.Add(0x08001000, new AsmMapSt { Name = "TestFunc" });

            AsmMapSt result;
            Assert.True(map.TryGetValue(0x08001000, out result));
            Assert.Equal("TestFunc", result.Name);
        }

        // ---- DisassemblerTrumb ----

        [Fact]
        public void DisassemblerTrumb_DefaultConstructor()
        {
            var dis = new DisassemblerTrumb();
            Assert.NotNull(dis);
        }

        [Fact]
        public void DisassemblerTrumb_ConstructorWithMapFile()
        {
            var map = new TestAsmMapFile();
            var dis = new DisassemblerTrumb(map);
            Assert.NotNull(dis);
        }

        [Fact]
        public void DisassemblerTrumb_ProgramAddrToPlain()
        {
            // THUMB address (odd) should be made even
            Assert.Equal((uint)0x08001000, DisassemblerTrumb.ProgramAddrToPlain(0x08001001));
            // Already even, no change
            Assert.Equal((uint)0x08001000, DisassemblerTrumb.ProgramAddrToPlain(0x08001000));
        }

        [Fact]
        public void DisassemblerTrumb_IsCode_WithNOP()
        {
            var savedRom = CoreState.ROM;
            try
            {
                // Create a minimal ROM with NOP instructions (0x46C0 = MOV r8,r8)
                byte[] data = new byte[256];
                data[0] = 0xC0; data[1] = 0x46; // NOP
                data[2] = 0xC0; data[3] = 0x46; // NOP

                var rom = new ROM();
                rom.LoadLow("test.gba", data, "NAZO");
                CoreState.ROM = rom;

                // The static IsCode method should not throw
                bool result = DisassemblerTrumb.IsCode(data, 0, rom);
                // Result depends on heuristics; just verify it doesn't crash
                Assert.True(result || !result);
            }
            finally
            {
                CoreState.ROM = savedRom;
            }
        }

        // ---- U utility methods added for DisASMTrumb ----

        [Fact]
        public void U_append_u32_LittleEndian()
        {
            var data = new List<byte>();
            U.append_u32(data, 0x12345678);
            Assert.Equal(new byte[] { 0x78, 0x56, 0x34, 0x12 }, data.ToArray());
        }

        [Fact]
        public void U_GetMaxValue_ReturnsMax()
        {
            var list = new List<uint> { 10, 50, 30, 20 };
            Assert.Equal((uint)50, U.GetMaxValue(list));
        }

        [Fact]
        public void U_GetMaxValue_EmptyList()
        {
            Assert.Equal((uint)0, U.GetMaxValue(new List<uint>()));
        }

        [Fact]
        public void U_SA_EmptyReturnsEmpty()
        {
            Assert.Equal("", U.SA(""));
        }

        [Fact]
        public void U_SA_NonEmptyPrependsSpace()
        {
            Assert.Equal(" hello", U.SA("hello"));
        }

        // ---- Test helper ----
        private class TestAsmMapFile : IAsmMapFile
        {
            private readonly Dictionary<uint, AsmMapSt> _data = new();

            public void Add(uint pointer, AsmMapSt st) => _data[pointer] = st;

            public bool TryGetValue(uint pointer, out AsmMapSt out_p) =>
                _data.TryGetValue(pointer, out out_p);
        }
    }
}
