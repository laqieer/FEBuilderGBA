// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for FEBuilderGBA.PortraitFrameStrings (#707 Slice A).
// Mirrors the WF ImagePortraitImporterForm.X_Frame selector strings.
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    public class PortraitFrameStringsTests
    {
        [Theory]
        [InlineData(0, "Normal")]
        [InlineData(1, "Half-blink")]
        [InlineData(2, "Closed eyes")]
        [InlineData(3, "Mouth 1")]
        [InlineData(4, "Mouth 2")]
        [InlineData(5, "Mouth 3")]
        [InlineData(6, "Mouth 4")]
        [InlineData(7, "Mouth 5")]
        [InlineData(8, "Mouth 6")]
        [InlineData(9, "Mouth 7")]
        [InlineData(10, "Position check")]
        public void GetWfModeString_ReturnsExpectedString(int frame, string expectedPrefix)
        {
            string result = PortraitFrameStrings.GetWfModeString(frame);
            Assert.StartsWith(expectedPrefix, result);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(11)]
        [InlineData(100)]
        public void GetWfModeString_OutOfRange_ReturnsQuestionMark(int frame)
        {
            Assert.Equal("?", PortraitFrameStrings.GetWfModeString(frame));
        }

        [Fact]
        public void GetWfModeString_AllFrames_IncludeJapaneseLabel()
        {
            // Each WF mode string in the wizard includes the original
            // Japanese label in parens — ensures bilingual labels are
            // preserved when ported (regression guard).
            Assert.Contains("通常時", PortraitFrameStrings.GetWfModeString(0));
            Assert.Contains("半目", PortraitFrameStrings.GetWfModeString(1));
            Assert.Contains("とじ目", PortraitFrameStrings.GetWfModeString(2));
            Assert.Contains("口1", PortraitFrameStrings.GetWfModeString(3));
            Assert.Contains("口7", PortraitFrameStrings.GetWfModeString(9));
            Assert.Contains("位置確認用", PortraitFrameStrings.GetWfModeString(10));
        }
    }
}
