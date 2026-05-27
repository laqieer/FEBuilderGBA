// SPDX-License-Identifier: GPL-3.0-or-later
// Bounds-check regression tests for PreviewIconHelper.ResolveUnitPortraitIdByOneBasedId.
//
// Three classes of inputs must produce a defined zero result without wrapping
// inside the ROM:
//   1. oneBasedUnitId == 0 — no unit; must short-circuit BEFORE the
//      `oneBasedUnitId - 1` step (otherwise u32 underflow → wraps to 0xFFFFFFFF).
//   2. oneBasedUnitId > rom.RomInfo.unit_maxcount — out-of-table; must reject.
//   3. oneBasedUnitId == 0xFFFF (the common "no unit" sentinel a u16 field can
//      hold) — must reject; without the maxcount guard a (oneBasedUnitId - 1) *
//      unitSize computation in u32 can wrap and land inside the ROM, returning
//      a wrong portrait.
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Services;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class PreviewIconHelperOneBasedIdBoundsTests : IClassFixture<RomFixture>
    {
        private readonly RomFixture _fixture;
        private readonly ITestOutputHelper _output;

        public PreviewIconHelperOneBasedIdBoundsTests(RomFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        [Fact]
        public void ZeroId_ReturnsZero_NoUnderflow()
        {
            if (!_fixture.IsAvailable)
            {
                _output.WriteLine("SKIP: no ROM available");
                return;
            }

            // The pre-fix code did `oneBasedUnitId - 1` in u32 arithmetic before
            // returning, which on uid==0 underflows to 0xFFFFFFFF and then
            // multiplies by unitSize — landing on a wrapped, in-range address.
            // After the fix the method MUST short-circuit and return 0.
            uint portraitId = PreviewIconHelper.ResolveUnitPortraitIdByOneBasedId(0);
            Assert.Equal(0u, portraitId);
        }

        [Fact]
        public void AboveUnitMaxCount_ReturnsZero()
        {
            if (!_fixture.IsAvailable || _fixture.ROM == null)
            {
                _output.WriteLine("SKIP: no ROM available");
                return;
            }

            uint maxCount = _fixture.ROM.RomInfo.unit_maxcount;
            Assert.True(maxCount > 0,
                "ROMFEINFO.unit_maxcount must be set for this test to be meaningful");

            // First definitely-out-of-range 1-based id.
            uint outOfRange = maxCount + 1;
            uint portraitId = PreviewIconHelper.ResolveUnitPortraitIdByOneBasedId(outOfRange);
            Assert.Equal(0u, portraitId);
        }

        [Fact]
        public void U16Sentinel0xFFFF_ReturnsZero_NoWrap()
        {
            if (!_fixture.IsAvailable)
            {
                _output.WriteLine("SKIP: no ROM available");
                return;
            }

            // 0xFFFF is the common "no unit" sentinel in u16 support/talk fields.
            // Without an explicit maxcount guard the u32 arithmetic
            //   unitAddr = unitBase + (0xFFFF - 1) * unitSize
            // can wrap and land inside the ROM. The method must reject.
            uint portraitId = PreviewIconHelper.ResolveUnitPortraitIdByOneBasedId(0xFFFF);
            Assert.Equal(0u, portraitId);
        }
    }
}
