// SPDX-License-Identifier: GPL-3.0-or-later
// #1444 — The Avalonia "Unit Color" view was a dead placeholder (addr list +
// label). These headless tests drive the real ported EventUnitColorViewModel:
//   * Seed(value) unpacks a packed UNIT_COLOR into the four slot combos.
//   * Pack()/Result repacks the current selection (a | b<<4 | c<<8 | d<<12).
//   * The 4th slot uses nibble d (corrected WinForms JumpTo line-60 bug).
//   * FriendlyText updates live as slots change.
//   * EventScriptPopupViewModel.ResolveDisplayName routes UNIT_COLOR through the
//     shared Core friendly-label helper.

using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    public class EventUnitColorViewModelTests
    {
        [Fact]
        public void New_Default_IsZeroAllSlots()
        {
            var vm = new EventUnitColorViewModel();
            vm.Initialize();
            Assert.Equal(0, vm.PlayerIndex);
            Assert.Equal(0, vm.EnemyIndex);
            Assert.Equal(0, vm.NpcIndex);
            Assert.Equal(0, vm.FourthIndex);
            Assert.Equal(0u, vm.Result);
        }

        [Fact]
        public void HasFiveSlotOptions()
        {
            var vm = new EventUnitColorViewModel();
            Assert.Equal(EventUnitColorCore.ColorOptionCount, vm.SlotItems.Count);
        }

        [Fact]
        public void Seed_UnpacksAllFourSlots()
        {
            var vm = new EventUnitColorViewModel();
            vm.Seed(0x4321u);
            Assert.Equal(1, vm.PlayerIndex);
            Assert.Equal(2, vm.EnemyIndex);
            Assert.Equal(3, vm.NpcIndex);
            Assert.Equal(4, vm.FourthIndex); // 4th slot from nibble d, not c.
        }

        [Fact]
        public void Pack_ReflectsSelectedIndices()
        {
            var vm = new EventUnitColorViewModel();
            vm.PlayerIndex = 1;
            vm.EnemyIndex = 2;
            vm.NpcIndex = 3;
            vm.FourthIndex = 4;
            Assert.Equal(0x4321u, vm.Pack());
            Assert.Equal(0x4321u, vm.Result);
        }

        [Fact]
        public void SeedThenPack_RoundTrips_AllValidSlotValues()
        {
            // Only values whose every slot is a valid colour (0..4) round-trip,
            // because the picker clamps out-of-range nibbles to "no change" (0)
            // — there are exactly five selectable options per slot.
            var vm = new EventUnitColorViewModel();
            for (uint a = 0; a < EventUnitColorCore.ColorOptionCount; a++)
            for (uint b = 0; b < EventUnitColorCore.ColorOptionCount; b++)
            for (uint c = 0; c < EventUnitColorCore.ColorOptionCount; c++)
            for (uint d = 0; d < EventUnitColorCore.ColorOptionCount; d++)
            {
                uint v = EventUnitColorCore.Pack(a, b, c, d);
                vm.Seed(v);
                Assert.Equal(v, vm.Result);
            }
        }

        [Fact]
        public void Seed_AnyOutOfRangeSlot_ClampsThatSlotToNoChange()
        {
            // A packed value with an out-of-range slot (>4) drops that slot to 0.
            var vm = new EventUnitColorViewModel();
            vm.Seed(0x0005u); // Player nibble = 5 (invalid) → index 0.
            Assert.Equal(0, vm.PlayerIndex);
            Assert.Equal(0u, vm.Result);
        }

        [Fact]
        public void Seed_OutOfRangeNibble_ClampsToZero()
        {
            var vm = new EventUnitColorViewModel();
            vm.Seed(0x000Fu); // Player nibble = 0xF (>4) → clamp to index 0.
            Assert.Equal(0, vm.PlayerIndex);
        }

        [Fact]
        public void FriendlyText_UpdatesOnSlotChange()
        {
            var vm = new EventUnitColorViewModel();
            vm.Initialize();
            string zero = vm.FriendlyText;
            vm.PlayerIndex = 1;
            Assert.NotEqual(zero, vm.FriendlyText);
            Assert.False(string.IsNullOrEmpty(vm.FriendlyText));
        }

        [Fact]
        public void FriendlyText_MatchesCoreHelper()
        {
            var vm = new EventUnitColorViewModel();
            vm.Seed(0x4321u);
            Assert.Equal(EventUnitColorCore.GetUNIT_COLOR(0x4321u), vm.FriendlyText);
        }

        [Fact]
        public void ResolveDisplayName_UnitColor_UsesCoreHelper()
        {
            string actual = CommandArgEntry.ResolveDisplayName(EventScript.ArgType.UNIT_COLOR, 0x4321u);
            Assert.Equal(EventUnitColorCore.GetUNIT_COLOR(0x4321u), actual);
        }

        [Fact]
        public void ResolveDisplayName_UnitColorZero_NotEmpty()
        {
            string actual = CommandArgEntry.ResolveDisplayName(EventScript.ArgType.UNIT_COLOR, 0u);
            Assert.Equal(EventUnitColorCore.GetUNIT_COLOR(0u), actual);
            Assert.False(string.IsNullOrEmpty(actual));
        }
    }
}
