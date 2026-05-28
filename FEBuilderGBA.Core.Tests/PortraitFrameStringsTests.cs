// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for FEBuilderGBA.PortraitFrameStrings (#707 Slice A).
// Mirrors the WF ImagePortraitImporterForm.X_Frame selector strings.
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    public class PortraitFrameStringsTests
    {
        [Theory]
        // Assert the FULL expected string per frame so any drift
        // (whitespace, punctuation, Japanese label edits) trips the test
        // immediately. PortraitFrameStrings is a fixed 11-entry mapping;
        // permissive Contains/StartsWith assertions would let regressions
        // slip through (Copilot PR review).
        [InlineData(0,  "Normal (通常時)")]
        [InlineData(1,  "Half-blink (半目)")]
        [InlineData(2,  "Closed eyes (とじ目)")]
        [InlineData(3,  "Mouth 1 (口1)")]
        [InlineData(4,  "Mouth 2 (口2)")]
        [InlineData(5,  "Mouth 3 (口3)")]
        // Frame 6 is intentionally the "Status screen" variant in WF —
        // see PortraitFrameStrings.cs XML doc.
        [InlineData(6,  "Status screen Mouth 4 (ステータス画面 口4)")]
        [InlineData(7,  "Mouth 5 (口5)")]
        [InlineData(8,  "Mouth 6 (口6)")]
        [InlineData(9,  "Mouth 7 (口7)")]
        [InlineData(10, "Position check (位置確認用)")]
        public void GetWfModeString_ReturnsExpectedString(int frame, string expected)
        {
            Assert.Equal(expected, PortraitFrameStrings.GetWfModeString(frame));
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(11)]
        [InlineData(100)]
        public void GetWfModeString_OutOfRange_ReturnsQuestionMark(int frame)
        {
            Assert.Equal("?", PortraitFrameStrings.GetWfModeString(frame));
        }
    }
}
