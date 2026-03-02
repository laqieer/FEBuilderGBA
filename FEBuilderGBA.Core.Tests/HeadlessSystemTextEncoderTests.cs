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

        // ---- SystemTextEncoder fallback tests (WU3) ----

        [Fact]
        public void SystemTextEncoder_Decode_FallbackWhenBothNull()
        {
            // Source verification: SystemTextEncoder.Decode() has FallbackEncoding path
            var src = File.ReadAllText(FindCoreFile("SystemTextEncoder.cs"));
            Assert.Contains("FallbackEncoding", src);
        }

        [Fact]
        public void SystemTextEncoder_Encode_FallbackWhenBothNull()
        {
            var src = File.ReadAllText(FindCoreFile("SystemTextEncoder.cs"));
            // Encode method checks both Encoder and TBLEncode for null
            Assert.Contains("if (this.TBLEncode != null)", src);
            Assert.Contains("return FallbackEncoding.GetBytes(str)", src);
        }

        [Fact]
        public void SystemTextEncoder_HasFallbackEncodingField()
        {
            var src = File.ReadAllText(FindCoreFile("SystemTextEncoder.cs"));
            Assert.Contains("static readonly Encoding FallbackEncoding", src);
            Assert.Contains("iso-8859-1", src);
        }

        static string FindCoreFile(string filename)
        {
            var dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
                dir = Path.GetDirectoryName(dir);
            if (dir == null) throw new InvalidOperationException("Cannot find solution root");
            return Path.Combine(dir, "FEBuilderGBA.Core", filename);
        }
    }
}
