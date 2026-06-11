// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for U.GrepPatternMatch bounds safety (#1028 Slice B, Copilot finding 3):
// a caller-supplied end > data.Length must NOT cause an IndexOutOfRangeException
// (the inner data[i + n] read could otherwise run past the buffer), and matches /
// non-matches near the tail must stay correct after the clamp.

using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    public class GrepPatternMatchTests
    {
        static bool[] NoSkip(int len)
        {
            var m = new bool[len];
            return m; // all false = exact match
        }

        [Fact]
        public void EndBeyondDataLength_DoesNotThrow_AndFindsTailMatch()
        {
            // 8-byte buffer; the needle sits at the very tail (offset 5..7).
            byte[] data = { 0x00, 0x11, 0x22, 0x33, 0x44, 0xAA, 0xBB, 0xCC };
            byte[] need = { 0xAA, 0xBB, 0xCC };

            // end deliberately far past the buffer end.
            uint found = U.GrepPatternMatch(data, need, NoSkip(need.Length), start: 0, end: 0x1000, blocksize: 1);
            Assert.Equal(5u, found);
        }

        [Fact]
        public void EndBeyondDataLength_NoMatch_ReturnsNotFound_NoThrow()
        {
            byte[] data = { 0x00, 0x11, 0x22, 0x33, 0x44, 0xAA, 0xBB, 0xCC };
            byte[] need = { 0xDE, 0xAD, 0xBE };

            uint found = U.GrepPatternMatch(data, need, NoSkip(need.Length), start: 0, end: 0xFFFFFFF0, blocksize: 1);
            Assert.Equal(U.NOT_FOUND, found);
        }

        [Fact]
        public void NeedleAtExactTail_WithExactEnd_Matches()
        {
            byte[] data = { 0x01, 0x02, 0x03, 0x04 };
            byte[] need = { 0x03, 0x04 };
            // end == data.Length: last legal start = 4 - 2 = 2.
            uint found = U.GrepPatternMatch(data, need, NoSkip(need.Length), start: 0, end: (uint)data.Length, blocksize: 1);
            Assert.Equal(2u, found);
        }

        [Fact]
        public void NeedleLongerThanBuffer_ReturnsNotFound()
        {
            byte[] data = { 0x01, 0x02 };
            byte[] need = { 0x01, 0x02, 0x03, 0x04 };
            uint found = U.GrepPatternMatch(data, need, NoSkip(need.Length), start: 0, end: 0x1000, blocksize: 1);
            Assert.Equal(U.NOT_FOUND, found);
        }

        [Fact]
        public void WildcardMask_MatchesWithDontCareBytes()
        {
            byte[] data = { 0x00, 0x11, 0x99, 0x33, 0x44 };
            byte[] need = { 0x11, 0xEE, 0x33 };           // 0xEE = wildcard
            bool[] mask = U.MakeMask2(need, 0xFF, 0xEE);  // position 1 is a wildcard
            uint found = U.GrepPatternMatch(data, need, mask, start: 0, end: 0x1000, blocksize: 1);
            Assert.Equal(1u, found);
        }

        [Fact]
        public void NullData_ReturnsNotFound_NoThrow()
        {
            byte[] need = { 0x01 };
            uint found = U.GrepPatternMatch(null, need, NoSkip(need.Length), 0, 0x1000, 1);
            Assert.Equal(U.NOT_FOUND, found);
        }

        [Fact]
        public void EndZero_ScansWholeBuffer()
        {
            byte[] data = { 0x00, 0x11, 0x22, 0xAB, 0xCD };
            byte[] need = { 0xAB, 0xCD };
            // end == 0 => whole buffer.
            uint found = U.GrepPatternMatch(data, need, NoSkip(need.Length), start: 0, end: 0, blocksize: 1);
            Assert.Equal(3u, found);
        }
    }
}
