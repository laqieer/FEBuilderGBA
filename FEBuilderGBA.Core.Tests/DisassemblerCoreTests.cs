using System.IO;
using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class DisassemblerCoreTests
    {
        [Fact]
        public void DisassemblerTrumb_DisassemblesBasicInstruction()
        {
            var disasm = new DisassemblerTrumb();
            var vm = new DisassemblerTrumb.VM();

            // NOP = MOV r8, r8 = 0x46C0
            byte[] bin = new byte[] { 0xC0, 0x46 };
            var code = disasm.Disassembler(bin, 0, 2, vm);

            Assert.NotNull(code);
            Assert.NotNull(code.ASM);
            Assert.True(code.GetLength() > 0);
        }

        [Fact]
        public void DictionaryAsmMapFile_LookupWorks()
        {
            // Test that the AsmMapSt type is usable
            var st = new AsmMapSt
            {
                Name = "TestFunc",
                ResultAndArgs = "RET=void",
                Length = 0x20,
            };
            Assert.Equal("TestFunc", st.Name);
            Assert.Equal("RET=void", st.ResultAndArgs);
        }

        [Fact]
        public void DisassemblerCore_ThrowsWithNoRom()
        {
            var origRom = CoreState.ROM;
            try
            {
                CoreState.ROM = null;
                var core = new DisassemblerCore();
                Assert.Throws<System.InvalidOperationException>(() =>
                    core.DisassembleToFile(Path.GetTempFileName()));
            }
            finally
            {
                CoreState.ROM = origRom;
            }
        }

        [Fact]
        public void DisassembleToLines_ThrowsWithNoRom()
        {
            var origRom = CoreState.ROM;
            try
            {
                CoreState.ROM = null;
                var core = new DisassemblerCore();
                Assert.Throws<System.InvalidOperationException>(() => core.DisassembleToLines());
            }
            finally
            {
                CoreState.ROM = origRom;
            }
        }

        [Fact]
        public void ExportIDAMapLines_ThrowsWithNoRom()
        {
            var origRom = CoreState.ROM;
            try
            {
                CoreState.ROM = null;
                var core = new DisassemblerCore();
                Assert.Throws<System.InvalidOperationException>(() => core.ExportIDAMapLines());
            }
            finally
            {
                CoreState.ROM = origRom;
            }
        }

        [Fact]
        public void ExportNoCashSymLines_ThrowsWithNoRom()
        {
            var origRom = CoreState.ROM;
            try
            {
                CoreState.ROM = null;
                var core = new DisassemblerCore();
                Assert.Throws<System.InvalidOperationException>(() => core.ExportNoCashSymLines());
            }
            finally
            {
                CoreState.ROM = origRom;
            }
        }

        static ROM MakeMinimalRom(int size = 256)
        {
            byte[] data = new byte[size];
            // Fill with NOP (0x46C0) instructions
            for (int i = 0; i < data.Length; i += 2)
            {
                data[i] = 0xC0;
                data[i + 1] = 0x46;
            }
            var rom = new ROM();
            rom.SwapNewROMDataDirect(data);
            return rom;
        }

        [Fact]
        public void DisassembleToLines_ProducesOutput_WithMinimalRom()
        {
            var origRom = CoreState.ROM;
            var origBase = CoreState.BaseDirectory;
            try
            {
                CoreState.ROM = MakeMinimalRom();
                CoreState.BaseDirectory = ""; // no config dir

                var core = new DisassemblerCore();
                var lines = core.DisassembleToLines();

                Assert.NotNull(lines);
                Assert.True(lines.Count > 4, "Should have header + instruction lines");
                // Header line should mention "Disassembly of"
                Assert.Contains("Disassembly of", lines[0]);
            }
            finally
            {
                CoreState.ROM = origRom;
                CoreState.BaseDirectory = origBase;
            }
        }

        [Fact]
        public void ExportIDAMapLines_HasHeader_WithMinimalRom()
        {
            var origRom = CoreState.ROM;
            var origBase = CoreState.BaseDirectory;
            try
            {
                CoreState.ROM = MakeMinimalRom(16);
                CoreState.BaseDirectory = "";

                var core = new DisassemblerCore();
                var lines = core.ExportIDAMapLines();

                Assert.NotNull(lines);
                Assert.True(lines.Count >= 1);
                Assert.Contains("Start", lines[0]);
            }
            finally
            {
                CoreState.ROM = origRom;
                CoreState.BaseDirectory = origBase;
            }
        }

        [Fact]
        public void ExportNoCashSymLines_ReturnsEmpty_WithMinimalRom()
        {
            var origRom = CoreState.ROM;
            var origBase = CoreState.BaseDirectory;
            try
            {
                CoreState.ROM = MakeMinimalRom(16);
                CoreState.BaseDirectory = "";

                var core = new DisassemblerCore();
                var lines = core.ExportNoCashSymLines();

                Assert.NotNull(lines);
                // With no symbol map, should be empty
                Assert.Empty(lines);
            }
            finally
            {
                CoreState.ROM = origRom;
                CoreState.BaseDirectory = origBase;
            }
        }
    }
}
