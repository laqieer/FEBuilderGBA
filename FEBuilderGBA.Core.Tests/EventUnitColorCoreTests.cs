using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Pure tests for <see cref="EventUnitColorCore"/> — the event-script
    /// UNIT_COLOR pack/unpack + friendly-label helper (#1444). No ROM, no
    /// CoreState mutation.
    /// </summary>
    public class EventUnitColorCoreTests
    {
        // ------------------------------------------------------------------
        // Pack
        // ------------------------------------------------------------------

        [Theory]
        [InlineData(0u, 0u, 0u, 0u, 0x0000u)]
        [InlineData(1u, 0u, 0u, 0u, 0x0001u)]
        [InlineData(0u, 2u, 0u, 0u, 0x0020u)]
        [InlineData(0u, 0u, 3u, 0u, 0x0300u)]
        [InlineData(0u, 0u, 0u, 4u, 0x4000u)]
        [InlineData(1u, 2u, 3u, 4u, 0x4321u)]
        public void Pack_PacksNibblesLowToHigh(uint a, uint b, uint c, uint d, uint expected)
        {
            Assert.Equal(expected, EventUnitColorCore.Pack(a, b, c, d));
        }

        [Fact]
        public void Pack_MasksEachSlotToNibble_NoCrossContamination()
        {
            // Out-of-range slot inputs must not bleed into adjacent nibbles
            // (each slot is masked to its low nibble before shifting).
            Assert.Equal(0x000Fu, EventUnitColorCore.Pack(0xFF, 0, 0, 0));   // a: 0xFF & 0xF = 0xF
            Assert.Equal(0x00F0u, EventUnitColorCore.Pack(0, 0x3F, 0, 0));   // b: 0x3F & 0xF = 0xF -> 0xF0
            Assert.Equal(0xF000u, EventUnitColorCore.Pack(0, 0, 0, 0x2F));   // d: 0x2F & 0xF = 0xF -> 0xF000
        }

        // ------------------------------------------------------------------
        // Unpack
        // ------------------------------------------------------------------

        [Fact]
        public void Unpack_SplitsAllFourNibbles()
        {
            var (a, b, c, d) = EventUnitColorCore.Unpack(0x4321u);
            Assert.Equal(1u, a);
            Assert.Equal(2u, b);
            Assert.Equal(3u, c);
            Assert.Equal(4u, d); // corrected 4th slot (WinForms JumpTo line-60 bug)
        }

        [Fact]
        public void Unpack_HighBitsAboveNibble15_AreIgnored()
        {
            var (a, b, c, d) = EventUnitColorCore.Unpack(0xFFFF4321u);
            Assert.Equal(1u, a);
            Assert.Equal(2u, b);
            Assert.Equal(3u, c);
            Assert.Equal(4u, d);
        }

        // ------------------------------------------------------------------
        // Round-trip across the full 5^4 valid slot space
        // ------------------------------------------------------------------

        [Fact]
        public void PackUnpack_RoundTrips_AllValidSlotCombinations()
        {
            for (uint a = 0; a < EventUnitColorCore.ColorOptionCount; a++)
            for (uint b = 0; b < EventUnitColorCore.ColorOptionCount; b++)
            for (uint c = 0; c < EventUnitColorCore.ColorOptionCount; c++)
            for (uint d = 0; d < EventUnitColorCore.ColorOptionCount; d++)
            {
                uint packed = EventUnitColorCore.Pack(a, b, c, d);
                var (ra, rb, rc, rd) = EventUnitColorCore.Unpack(packed);
                Assert.Equal(a, ra);
                Assert.Equal(b, rb);
                Assert.Equal(c, rc);
                Assert.Equal(d, rd);
            }
        }

        // ------------------------------------------------------------------
        // GetUNIT_COLOR friendly label (structure — translation-agnostic)
        // ------------------------------------------------------------------

        [Fact]
        public void GetUNIT_COLOR_Zero_ReturnsNoChangeSentence()
        {
            // 0 = "no change" — a non-empty sentence distinct from the per-slot list.
            string s = EventUnitColorCore.GetUNIT_COLOR(0);
            Assert.False(string.IsNullOrEmpty(s));
            Assert.DoesNotContain(",", s); // single sentence, never a slot list
        }

        [Fact]
        public void GetUNIT_COLOR_SingleSlot_ReturnsNonEmpty_NoComma()
        {
            string s = EventUnitColorCore.GetUNIT_COLOR(0x0001u); // Player -> blue
            Assert.False(string.IsNullOrEmpty(s));
            Assert.DoesNotContain(",", s);
        }

        [Fact]
        public void GetUNIT_COLOR_MultiSlot_JoinsWithComma()
        {
            // Two active slots -> two clauses joined by a comma.
            string s = EventUnitColorCore.GetUNIT_COLOR(0x0021u); // Player->blue, Enemy->red
            Assert.Contains(",", s);
        }

        [Fact]
        public void GetUNIT_COLOR_AllFourSlots_HasThreeCommas()
        {
            string s = EventUnitColorCore.GetUNIT_COLOR(0x4321u);
            int commas = 0;
            foreach (char ch in s) if (ch == ',') commas++;
            Assert.Equal(3, commas); // four clauses -> three separators
        }

        [Fact]
        public void GetUNIT_COLORSub_MapsKnownSlots_UnknownReturnsEmpty()
        {
            Assert.NotEqual("", EventUnitColorCore.GetUNIT_COLORSub(1));
            Assert.NotEqual("", EventUnitColorCore.GetUNIT_COLORSub(2));
            Assert.NotEqual("", EventUnitColorCore.GetUNIT_COLORSub(3));
            Assert.NotEqual("", EventUnitColorCore.GetUNIT_COLORSub(4));
            Assert.Equal("", EventUnitColorCore.GetUNIT_COLORSub(0));
            Assert.Equal("", EventUnitColorCore.GetUNIT_COLORSub(5));
        }
    }
}
