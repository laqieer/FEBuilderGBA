// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for U.ParseUnitGrowGrow + U.MakeUnitGrowB3 (#431).
//
// FE6/FE7 event unit block byte 3 ("B3" / UnitInfo) packs three fields:
//   bit 0       — Growth flag (0 = no growth, 1 = class-dependent)
//   bits 1-2    — Allegiance  (0 = Player, 1 = Ally, 2 = Enemy, 3 = Disappear)
//   bits 3-7    — Level       (0-31)
// Verified against InputFormRef.cs:993-1083 (the WF UNITGROW binding) and
// EventUnitFE7Form.Designer.cs combo-box items.
using Xunit;

namespace FEBuilderGBA.Core.Tests;

public class UnitGrowTests
{
    [Fact]
    public void ParseUnitGrowLV_ExtractsBits3Through7()
    {
        // bit 3-7 = 0x0A (decimal 10) — value 0x50 has the level slot set to 10
        Assert.Equal(10u, U.ParseUnitGrowLV(0x50));
    }

    [Fact]
    public void ParseUnitGrowAssign_ExtractsBits1Through2()
    {
        // bit 1-2 = 0x02 (Enemy) — value 0x04 has the allegiance slot = 2
        Assert.Equal(2u, U.ParseUnitGrowAssign(0x04));
    }

    [Fact]
    public void ParseUnitGrowGrow_ExtractsBit0()
    {
        // bit 0 = 1 (class-dependent growth) — value 0x01 has the grow flag set
        Assert.Equal(1u, U.ParseUnitGrowGrow(0x01));
        Assert.Equal(0u, U.ParseUnitGrowGrow(0x00));
        Assert.Equal(1u, U.ParseUnitGrowGrow(0xFF));   // bit 0 still set in 0xFF
        Assert.Equal(0u, U.ParseUnitGrowGrow(0xFE));   // bit 0 cleared
    }

    [Fact]
    public void MakeUnitGrowB3_ComposesAllThreeFields()
    {
        // LV=10, Allegiance=2 (Enemy), Grow=1 → 1 | (2<<1) | (10<<3) = 1 | 4 | 80 = 0x55
        Assert.Equal(0x55u, U.MakeUnitGrowB3(lv: 10, assign: 2, grow: 1));
    }

    [Fact]
    public void MakeUnitGrowB3_TruncatesLevelTo5BitsAllegianceTo2BitsGrowTo1Bit()
    {
        // LV=0xFF (overflow) → 0x1F mask; Allegiance=0xFF → 0x3 mask; Grow=0xFF → 0x1 mask.
        // Result: 1 | (3<<1) | (31<<3) = 1 | 6 | 248 = 0xFF (saturates to 8 bits)
        Assert.Equal(0xFFu, U.MakeUnitGrowB3(lv: 0xFF, assign: 0xFF, grow: 0xFF));
    }

    [Fact]
    public void MakeUnitGrowB3_RoundTripsThroughAllLegalCombinations()
    {
        // Bijection on the masked-bit space: for every (lv, assign, grow)
        // tuple in their valid ranges, the parse helpers must invert the
        // compose helper.
        for (uint lv = 0; lv <= 31; lv++)
        {
            for (uint assign = 0; assign <= 3; assign++)
            {
                for (uint grow = 0; grow <= 1; grow++)
                {
                    uint b3 = U.MakeUnitGrowB3(lv, assign, grow);
                    Assert.Equal(lv, U.ParseUnitGrowLV(b3));
                    Assert.Equal(assign, U.ParseUnitGrowAssign(b3));
                    Assert.Equal(grow, U.ParseUnitGrowGrow(b3));
                }
            }
        }
    }
}
