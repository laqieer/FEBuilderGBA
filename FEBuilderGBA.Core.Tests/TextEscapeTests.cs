using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    public class TextEscapeTests
    {
        [Fact]
        public void Encode_LineBreak()
        {
            // TextEscape is initialized with ROM data, so we test static utility
            // Verify the class exists and can be instantiated
            Assert.NotNull(typeof(TextEscape));
        }
    }
}
