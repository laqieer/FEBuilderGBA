// SPDX-License-Identifier: GPL-3.0-or-later
// #1681: the five clone dialogs that share the same top-row pattern
// (Grid "*,Auto" with a fixed Height="40" MESSAGE Border + an Apply/OK Button)
// must vertically center both the Border and the Button within the single grid
// row, so the Apply button no longer sits flush against the top of the taller
// row. These tests construct each Window headlessly and assert the ApplyButton's
// VerticalAlignment == Center.
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.Layout;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    public class ApplyButtonAlignmentTests
    {
        static void AssertApplyButtonCentered(Window view)
        {
            var apply = view.FindControl<Button>("ApplyButton");
            Assert.NotNull(apply);
            Assert.Equal(VerticalAlignment.Center, apply!.VerticalAlignment);
        }

        [AvaloniaFact]
        public void EventUnitColorView_ApplyButton_IsVerticallyCentered()
        {
            // Primary issue target — must always pass.
            var view = new EventUnitColorView();
            AssertApplyButtonCentered(view);
        }

        [AvaloniaFact]
        public void PackedMemorySlotView_ApplyButton_IsVerticallyCentered()
        {
            // ROM-independent clone dialog (constructs its own ViewModel), so an
            // unexpected construction failure SHOULD fail this regression guard.
            var view = new PackedMemorySlotView();
            AssertApplyButtonCentered(view);
        }

        [AvaloniaFact]
        public void UbyteBitFlagView_ApplyButton_IsVerticallyCentered()
        {
            var view = new UbyteBitFlagView();
            AssertApplyButtonCentered(view);
        }

        [AvaloniaFact]
        public void UshortBitFlagView_ApplyButton_IsVerticallyCentered()
        {
            var view = new UshortBitFlagView();
            AssertApplyButtonCentered(view);
        }

        [AvaloniaFact]
        public void UwordBitFlagView_ApplyButton_IsVerticallyCentered()
        {
            var view = new UwordBitFlagView();
            AssertApplyButtonCentered(view);
        }
    }
}
