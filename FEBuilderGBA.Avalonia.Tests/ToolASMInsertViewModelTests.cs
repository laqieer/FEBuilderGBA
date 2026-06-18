using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// Unit tests for <see cref="ToolASMInsertViewModel"/>, the form-field holder for
    /// the Avalonia "Add-via-ASM/C" tool. The compile/insert work itself lives in the
    /// GUI-free Core helper <c>AsmCompileCore</c> (covered by AsmCompileCoreTests and
    /// NOT re-run here — devkitARM is not built in CI). These tests cover the VM's
    /// pure field-mapping logic: the index→enum projection, the WF-default indices,
    /// and the hex address parser.
    /// </summary>
    public class ToolASMInsertViewModelTests
    {
        [Fact]
        public void Defaults_MirrorWinFormsCtorIndices()
        {
            var vm = new ToolASMInsertViewModel();

            Assert.Equal(0, vm.CompileMethodIndex);              // dump binary
            Assert.Equal(0, vm.InsertMethodIndex);               // compile-only
            Assert.Equal(3, vm.HookRegisterIndex);               // r3 (WF default)
            Assert.Equal(3, vm.DebugSymbolIndex);                // Save both (WF default)
            Assert.True(vm.CheckMissingLabel);                   // on (WF default)
            Assert.False(vm.CanUndo);
            Assert.Equal("", vm.SourcePath);
        }

        [Theory]
        [InlineData(0, AsmCompileCore.CompileMethod.DumpBinary)]
        [InlineData(1, AsmCompileCore.CompileMethod.KeepElf)]
        [InlineData(2, AsmCompileCore.CompileMethod.ConvertLyn)]
        public void CompileMethod_MapsFromIndex(int idx, AsmCompileCore.CompileMethod expected)
        {
            var vm = new ToolASMInsertViewModel { CompileMethodIndex = idx };
            Assert.Equal(expected, vm.CompileMethod);
        }

        [Theory]
        [InlineData(0, AsmCompileCore.InsertMethod.CompileOnly)]
        [InlineData(1, AsmCompileCore.InsertMethod.WriteAtAddress)]
        [InlineData(2, AsmCompileCore.InsertMethod.HookInject)]
        public void InsertMethod_MapsFromIndex(int idx, AsmCompileCore.InsertMethod expected)
        {
            var vm = new ToolASMInsertViewModel { InsertMethodIndex = idx };
            Assert.Equal(expected, vm.InsertMethod);
        }

        [Theory]
        [InlineData(0, SymbolUtil.DebugSymbol.None)]
        [InlineData(1, SymbolUtil.DebugSymbol.SaveSymTxt)]
        public void StoreSymbol_MapsFromIndex(int idx, SymbolUtil.DebugSymbol expected)
        {
            var vm = new ToolASMInsertViewModel { DebugSymbolIndex = idx };
            Assert.Equal(expected, vm.StoreSymbol);
        }

        [Theory]
        [InlineData("0x08000000", 0x08000000u)]
        [InlineData("08000000", 0x08000000u)]
        [InlineData("0X100", 0x100u)]
        [InlineData("  0x1F ", 0x1Fu)]
        [InlineData("", 0u)]
        [InlineData("   ", 0u)]
        [InlineData("not-hex", 0u)]
        public void ParseHex_ReadsHexAddresses(string text, uint expected)
        {
            Assert.Equal(expected, ToolASMInsertViewModel.ParseHex(text));
        }

        [Theory]
        [InlineData(0, 0u)]
        [InlineData(3, 3u)]
        [InlineData(8, 8u)]
        public void HookRegister_ClampedFromIndex(int idx, uint expected)
        {
            var vm = new ToolASMInsertViewModel { HookRegisterIndex = idx };
            Assert.Equal(expected, vm.HookRegister);
        }

        [Fact]
        public void SourceExists_FalseForBlankOrMissingPath()
        {
            var vm = new ToolASMInsertViewModel();
            Assert.False(vm.SourceExists);

            vm.SourcePath = "Z:\\does\\not\\exist\\foo.s";
            Assert.False(vm.SourceExists);
        }
    }
}
