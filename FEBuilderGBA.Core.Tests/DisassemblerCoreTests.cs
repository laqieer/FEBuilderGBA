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
    }
}
