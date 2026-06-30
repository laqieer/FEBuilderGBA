using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// #1722: <see cref="U.ToOneLineCaption(string)"/> sanitizes a decoded ROM
    /// string into a single clean caption line, stripping control / non-printable
    /// code points (which render as tofu (box) on macOS) while preserving
    /// legitimate printable glyphs (incl. non-ASCII).
    ///
    /// The synthetic strings below use the EXACT offending code points captured
    /// empirically from FE8U's terrain-name table:
    ///   - terrain names decode with a trailing U+001F control byte
    ///     (e.g. FETextDecode.Direct returns "Plain" for terrain 0x01);
    ///   - empty slots decode to U+007F U+007F (two DELETE chars).
    /// We embed these via \u escapes so the compiler bakes the real control
    /// code points into the test input (the test would be meaningless otherwise).
    /// </summary>
    public class UToOneLineCaptionTests
    {
        // The exact two offending code points found in the FE8U dump.
        const char US = (char)0x1F;   // UNIT SEPARATOR — trailing on terrain names
        const char DEL = (char)0x7F;  // DELETE — empty terrain slots (x2)

        [Fact]
        public void StripsTrailingU001FControlByte_FE8UTerrainNameCase()
        {
            // "Plain" — the EXACT shape FETextDecode.Direct returns for FE8U
            // terrain name 0x01. The trailing U+001F is the tofu source on macOS.
            string raw = "Plain" + US;
            Assert.Contains(raw, char.IsControl); // precondition: input is dirty
            string clean = U.ToOneLineCaption(raw);
            Assert.Equal("Plain", clean);
            AssertNoControlChars(clean);
        }

        [Fact]
        public void EmptySlotOfTwoDeleteChars_BecomesEmpty()
        {
            // Empty terrain slots (0x00, 0x2C, 0x2E) decode to U+007F U+007F — two
            // DELETE chars that render as two tofu boxes. They must collapse away.
            string raw = new string(new[] { DEL, DEL });
            Assert.Contains(raw, char.IsControl); // precondition: input is dirty
            string clean = U.ToOneLineCaption(raw);
            Assert.Equal("", clean);
            AssertNoControlChars(clean);
        }

        [Fact]
        public void TakesOnlyFirstLine_OfMultiLineInput()
        {
            string raw = "First line\r\nSecond line\nThird";
            string clean = U.ToOneLineCaption(raw);
            Assert.Equal("First line", clean);
            AssertNoControlChars(clean);
        }

        [Fact]
        public void StripsEmbeddedControlChars_KeepsSurroundingText()
        {
            // Embedded control bytes anywhere (not just trailing) are removed,
            // while the printable text around them is preserved.
            string raw = "Vil" + US + "lage" + US;
            string clean = U.ToOneLineCaption(raw);
            Assert.Equal("Village", clean);
            AssertNoControlChars(clean);
        }

        [Fact]
        public void PreservesPrintableNonAsciiGlyphs()
        {
            // Localized names (CJK, accented Latin) must survive intact — we only
            // strip control/non-printable, NOT legitimate glyphs.
            string raw = "草原" + US;        // "草原" + trailing tofu byte
            string clean = U.ToOneLineCaption(raw);
            Assert.Equal("草原", clean);
            AssertNoControlChars(clean);

            string accented = "Forêt" + US;     // "Forêt" + tofu byte
            Assert.Equal("Forêt", U.ToOneLineCaption(accented));
        }

        [Fact]
        public void PreservesInteriorSpaces_TrimsOuterWhitespace()
        {
            string raw = "  Sand Bar  " + US;
            string clean = U.ToOneLineCaption(raw);
            Assert.Equal("Sand Bar", clean);
            AssertNoControlChars(clean);
        }

        [Fact]
        public void PreservesSupplementaryPlaneSurrogatePair()
        {
            // A real supplementary-plane glyph (😀, U+1F600) is a surrogate pair —
            // it must NOT be dropped as a "surrogate" non-printable.
            string raw = "Map \U0001F600" + US;
            string clean = U.ToOneLineCaption(raw);
            Assert.Equal("Map \U0001F600", clean);
            AssertNoControlChars(clean);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void NullOrEmpty_ReturnsEmpty(string? input)
        {
            string clean = U.ToOneLineCaption(input!);
            Assert.Equal("", clean);
        }

        static void AssertNoControlChars(string s)
        {
            foreach (char c in s)
                Assert.False(char.IsControl(c),
                    $"sanitized caption must contain no control chars, found U+{((int)c):X4}");
        }
    }
}
