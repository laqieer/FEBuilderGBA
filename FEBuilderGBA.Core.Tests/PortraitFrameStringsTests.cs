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
        // Frame 6 is intentionally the "Status screen" variant in WF —
        // see PortraitFrameStrings.cs XML doc.
        [InlineData(6, "Status screen Mouth 4")]
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

        [Theory]
        // Maps every frame index (0-10) to the WF Japanese substring
        // expected inside the returned bilingual label. Ensures the
        // original WF labels are preserved verbatim and that frame 6
        // keeps its special "ステータス画面" prefix (WF parity).
        [InlineData(0, "通常時")]
        [InlineData(1, "半目")]
        [InlineData(2, "とじ目")]
        [InlineData(3, "口1")]
        [InlineData(4, "口2")]
        [InlineData(5, "口3")]
        [InlineData(6, "ステータス画面 口4")]
        [InlineData(7, "口5")]
        [InlineData(8, "口6")]
        [InlineData(9, "口7")]
        [InlineData(10, "位置確認用")]
        public void GetWfModeString_EveryFrame_IncludesJapaneseLabel(int frame, string expectedJapaneseSubstring)
        {
            Assert.Contains(expectedJapaneseSubstring, PortraitFrameStrings.GetWfModeString(frame));
        }
    }
}
