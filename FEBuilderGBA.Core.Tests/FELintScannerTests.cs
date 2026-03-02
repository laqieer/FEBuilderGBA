using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class FELintScannerTests
    {
        [Fact]
        public void Scan_WithNoRom_ReturnsEmptyList()
        {
            var origRom = CoreState.ROM;
            try
            {
                CoreState.ROM = null;
                var scanner = new FELintScanner();
                var errors = scanner.Scan();
                Assert.NotNull(errors);
                Assert.Empty(errors);
            }
            finally
            {
                CoreState.ROM = origRom;
            }
        }

        [Fact]
        public void FELintCore_ErrorSt_HasProperties()
        {
            var error = new FELintCore.ErrorSt(FELintCore.Type.UNIT, 0x100, "Test error");
            Assert.Equal(FELintCore.Type.UNIT, error.DataType);
            Assert.Equal(0x100u, error.Addr);
            Assert.Equal("Test error", error.ErrorMessage);
            Assert.Equal("Test error", error.Info);
            Assert.Equal(FELintCore.ErrorType.ERROR, error.Severity);
        }

        [Fact]
        public void FELintCore_ErrorSt_WithWarning()
        {
            var error = new FELintCore.ErrorSt(FELintCore.Type.ITEM, 0x200, "Test warning",
                FELintCore.ErrorType.WARNING);
            Assert.Equal(FELintCore.ErrorType.WARNING, error.Severity);
        }

        [Fact]
        public void FELintCore_IsAligned4_Works()
        {
            Assert.True(FELintCore.IsAligned4(0x100));
            Assert.True(FELintCore.IsAligned4(0));
            Assert.False(FELintCore.IsAligned4(0x101));
            Assert.False(FELintCore.IsAligned4(0x102));
        }
    }
}
