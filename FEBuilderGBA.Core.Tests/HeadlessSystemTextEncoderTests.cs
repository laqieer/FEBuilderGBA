using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    public class HeadlessSystemTextEncoderTests
    {
        [Fact]
        public void ImplementsISystemTextEncoder()
        {
            ISystemTextEncoder encoder = new HeadlessSystemTextEncoder();
            Assert.NotNull(encoder);
        }

        [Fact]
        public void Encode_And_Decode_RoundTrips()
        {
            var encoder = new HeadlessSystemTextEncoder();
            string original = "Hello World";
            byte[] encoded = encoder.Encode(original);
            string decoded = encoder.Decode(encoded);
            Assert.Equal(original, decoded);
        }

        [Fact]
        public void Decode_WithOffset_Works()
        {
            var encoder = new HeadlessSystemTextEncoder();
            byte[] data = encoder.Encode("Hello World");
            string partial = encoder.Decode(data, 0, 5);
            Assert.Equal("Hello", partial);
        }

        [Fact]
        public void Decode_EmptyArray_ReturnsEmpty()
        {
            var encoder = new HeadlessSystemTextEncoder();
            Assert.Equal("", encoder.Decode(new byte[0]));
            Assert.Equal("", encoder.Decode(null));
        }

        [Fact]
        public void Encode_EmptyString_ReturnsEmpty()
        {
            var encoder = new HeadlessSystemTextEncoder();
            Assert.Empty(encoder.Encode(""));
            Assert.Empty(encoder.Encode(null));
        }

        [Fact]
        public void GetTBLEncodeDicLow_ReturnsEmptyDict()
        {
            var encoder = new HeadlessSystemTextEncoder();
            var dic = encoder.GetTBLEncodeDicLow();
            Assert.NotNull(dic);
            Assert.Empty(dic);
        }
    }
}
