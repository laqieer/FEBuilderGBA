// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for the shared EditorJumpAddressHelper extracted in the #948 review.
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Core;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    public class EditorJumpAddressHelperTests
    {
        // ---- Guard paths (no ROM needed, fully deterministic) ----

        [Fact]
        public void UnitAddrFor_NullRom_ReturnsZero()
        {
            Assert.Equal(0u, EditorJumpAddressHelper.UnitAddrFor(null, 1));
        }

        [Fact]
        public void UnitAddrFor_IdZero_ReturnsZero()
        {
            // 0 is the 1-based "ANY/no unit" sentinel — must never resolve to an address.
            Assert.Equal(0u, EditorJumpAddressHelper.UnitAddrFor(null, 0));
        }

        [Fact]
        public void TextRowAddrFor_NullRom_ReturnsZero()
        {
            Assert.Equal(0u, EditorJumpAddressHelper.TextRowAddrFor(null, 1));
        }

        // ---- ROM-backed behavior (skipped when no ROM is available) ----

        [Fact]
        public void UnitAddrFor_RealRom_FirstUnit_IsNonZeroAndInBounds()
        {
            using var fx = new RomFixture();
            if (!fx.IsAvailable) return; // skip: no ROM
            ROM rom = fx.ROM!;

            uint addr = EditorJumpAddressHelper.UnitAddrFor(rom, 1);
            Assert.NotEqual(0u, addr);
            Assert.True(U.isSafetyOffset(addr, rom));
        }

        [Fact]
        public void TextRowAddrFor_RealRom_IsTextBasePlusIdTimesFour()
        {
            using var fx = new RomFixture();
            if (!fx.IsAvailable) return; // skip: no ROM
            ROM rom = fx.ROM!;

            uint textBase = rom.p32(rom.RomInfo.text_pointer);
            // id 0 -> the table base; id 5 -> base + 20 (one pointer per id).
            Assert.Equal(textBase, EditorJumpAddressHelper.TextRowAddrFor(rom, 0));
            Assert.Equal(textBase + 20u, EditorJumpAddressHelper.TextRowAddrFor(rom, 5));
        }
    }
}
