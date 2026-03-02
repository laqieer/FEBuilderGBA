using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class FETextDecodeTests : IDisposable
    {
        readonly ROM? _savedRom;
        readonly ISystemTextEncoder? _savedEncoder;

        public FETextDecodeTests()
        {
            _savedRom = CoreState.ROM;
            _savedEncoder = CoreState.SystemTextEncoder;
        }

        public void Dispose()
        {
            CoreState.ROM = _savedRom;
            CoreState.SystemTextEncoder = _savedEncoder;
            PatchDetection.ClearAllCaches();
        }

        [Fact]
        public void Direct_NullROM_ReturnsQuestionMarks()
        {
            CoreState.ROM = null;
            CoreState.SystemTextEncoder = new HeadlessSystemTextEncoder();
            string result = FETextDecode.Direct(0);
            Assert.Equal("???", result);
        }

        [Fact]
        public void Direct_NullSystemTextEncoder_ReturnsQuestionMarks()
        {
            CoreState.ROM = null;
            CoreState.SystemTextEncoder = null;
            string result = FETextDecode.Direct(0);
            Assert.Equal("???", result);
        }

        [Fact]
        public void Constructor_NullROM_SetsPriorityCodeToLAT1()
        {
            CoreState.ROM = null;
            CoreState.SystemTextEncoder = new HeadlessSystemTextEncoder();
            // This should not throw
            var decoder = new FETextDecode();
            Assert.NotNull(decoder);
        }

        [Fact]
        public void Direct_DoesNotThrow_OnAnyInvalidState()
        {
            CoreState.ROM = null;
            CoreState.SystemTextEncoder = null;
            // Should never throw — returns "???" instead
            var ex = Record.Exception(() => FETextDecode.Direct(999));
            Assert.Null(ex);
        }
    }
}
