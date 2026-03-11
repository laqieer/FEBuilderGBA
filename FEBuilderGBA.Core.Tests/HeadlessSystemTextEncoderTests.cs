using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
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

        // ---- Japanese text encoding tests ----

        [Fact]
        public void Constructor_WithShiftJIS_DecodesJapaneseText()
        {
            var encoder = new HeadlessSystemTextEncoder("Shift_JIS");
            // エイリーク (Eirika) in Shift_JIS: 0x83 0x47 0x83 0x43 0x83 0x8A 0x81 0x5B 0x83 0x4E
            byte[] sjisBytes = { 0x83, 0x47, 0x83, 0x43, 0x83, 0x8A, 0x81, 0x5B, 0x83, 0x4E };
            string decoded = encoder.Decode(sjisBytes);
            Assert.Contains("エ", decoded); // First character should be エ
            Assert.DoesNotContain("\uFFFD", decoded); // No replacement characters
        }

        [Fact]
        public void Default_Constructor_WithoutRom_UsesISO8859()
        {
            // Without a ROM loaded, defaults to ISO-8859-1
            var saved = CoreState.ROM;
            try
            {
                CoreState.ROM = null;
                var encoder = new HeadlessSystemTextEncoder();
                Assert.Equal("iso-8859-1", encoder.EncodingName);
            }
            finally
            {
                CoreState.ROM = saved;
            }
        }

        [Fact]
        public void EncodingName_Property_ReportsEncoding()
        {
            var encoder = new HeadlessSystemTextEncoder("Shift_JIS");
            Assert.Equal("shift_jis", encoder.EncodingName);
        }

        [Fact]
        public void EncodingName_ISO8859_ReportsCorrectly()
        {
            var encoder = new HeadlessSystemTextEncoder();
            // Without ROM, should be iso-8859-1
            Assert.Contains("8859", encoder.EncodingName);
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

        [Fact]
        public void SystemTextEncoder_Build_RegistersCodePages()
        {
            var src = File.ReadAllText(FindCoreFile("SystemTextEncoder.cs"));
            Assert.Contains("Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)", src);
        }

        [Fact]
        public void HeadlessSystemTextEncoder_HasRomConstructor()
        {
            var src = File.ReadAllText(FindCoreFile("HeadlessSystemTextEncoder.cs"));
            Assert.Contains("public HeadlessSystemTextEncoder(ROM rom)", src);
            Assert.Contains("DetectEncodingFromRom", src);
        }

        [Fact]
        public void HeadlessSystemTextEncoder_DetectsMultibyte()
        {
            var src = File.ReadAllText(FindCoreFile("HeadlessSystemTextEncoder.cs"));
            Assert.Contains("is_multibyte", src);
            Assert.Contains("Shift_JIS", src);
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
