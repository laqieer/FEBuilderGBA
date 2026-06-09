// SPDX-License-Identifier: GPL-3.0-or-later
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Hermetic tests for the bounds-checked frame accessor
    /// <see cref="SkillSystemsAnimeExportCore.GetFrameImage"/> (#1010).
    ///
    /// No ROM and no CoreState mutation — every test builds a synthetic
    /// <see cref="SkillAnimeExportResult"/> directly and asserts the routing.
    /// The in-range identity check uses the shared <c>StubImage</c> sentinel so
    /// we can assert the EXACT stored reference is returned (no clone).
    /// </summary>
    public class SkillSystemsAnimeExportCoreGetFrameTests
    {
        [Fact]
        public void GetFrameImage_NullResult_ReturnsNull()
        {
            Assert.Null(SkillSystemsAnimeExportCore.GetFrameImage(null, 0));
        }

        [Fact]
        public void GetFrameImage_ResultWithError_ReturnsNull()
        {
            var r = new SkillAnimeExportResult { Error = "BAD ANIME ADDRESS" };
            r.Frames.Add(new SkillAnimeFrame { Id = 1, Wait = 3, Image = new StubImage(8, 8) });
            Assert.Null(SkillSystemsAnimeExportCore.GetFrameImage(r, 0));
        }

        [Fact]
        public void GetFrameImage_FramesNullResult_ReturnsNull()
        {
            var r = new SkillAnimeExportResult { Frames = null };
            Assert.Null(SkillSystemsAnimeExportCore.GetFrameImage(r, 0));
        }

        [Fact]
        public void GetFrameImage_NegativeIndex_ReturnsNull()
        {
            var r = new SkillAnimeExportResult();
            r.Frames.Add(new SkillAnimeFrame { Id = 1, Wait = 3, Image = new StubImage(8, 8) });
            Assert.Null(SkillSystemsAnimeExportCore.GetFrameImage(r, -1));
        }

        [Fact]
        public void GetFrameImage_IndexAtCount_ReturnsNull()
        {
            var r = new SkillAnimeExportResult();
            r.Frames.Add(new SkillAnimeFrame { Id = 1, Wait = 3, Image = new StubImage(8, 8) });
            // index == Count (one past the last valid index) is out of range.
            Assert.Null(SkillSystemsAnimeExportCore.GetFrameImage(r, 1));
        }

        [Fact]
        public void GetFrameImage_IndexBeyondCount_ReturnsNull()
        {
            var r = new SkillAnimeExportResult();
            r.Frames.Add(new SkillAnimeFrame { Id = 1, Wait = 3, Image = new StubImage(8, 8) });
            Assert.Null(SkillSystemsAnimeExportCore.GetFrameImage(r, 99));
        }

        [Fact]
        public void GetFrameImage_InRange_ReturnsExactStoredReference()
        {
            var img0 = new StubImage(8, 8);
            var img1 = new StubImage(16, 16);
            var r = new SkillAnimeExportResult();
            r.Frames.Add(new SkillAnimeFrame { Id = 1, Wait = 3, Image = img0 });
            r.Frames.Add(new SkillAnimeFrame { Id = 2, Wait = 5, Image = img1 });

            // Must return the SAME reference stored at each index (no clone).
            Assert.Same(img0, SkillSystemsAnimeExportCore.GetFrameImage(r, 0));
            Assert.Same(img1, SkillSystemsAnimeExportCore.GetFrameImage(r, 1));
        }

        [Fact]
        public void GetFrameImage_InRange_NullStoredImage_ReturnsNull()
        {
            // A frame whose Image is explicitly null routes through (index in
            // range) and returns that null reference — the routing, not a
            // bounds rejection.
            var r = new SkillAnimeExportResult();
            r.Frames.Add(new SkillAnimeFrame { Id = 1, Wait = 3, Image = null });
            Assert.Null(SkillSystemsAnimeExportCore.GetFrameImage(r, 0));
        }
    }
}
