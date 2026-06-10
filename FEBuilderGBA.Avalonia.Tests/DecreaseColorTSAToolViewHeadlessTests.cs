using System.Linq;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// Headless structural tests for the Color Reduction Tool view (#998 PR 2).
    /// </summary>
    [Collection("SharedState")]
    public class DecreaseColorTSAToolViewHeadlessTests
    {
        static T? FindById<T>(Control root, string id) where T : Control =>
            root.GetLogicalDescendants().OfType<T>()
                .FirstOrDefault(c => AutomationProperties.GetAutomationId(c) == id);

        [AvaloniaFact]
        public void View_CanInstantiate()
        {
            var view = new DecreaseColorTSAToolView();
            Assert.NotNull(view);
        }

        [AvaloniaFact]
        public void View_HasNoAddressList()
        {
            // The placeholder shell used an AddressListControl; the real
            // file-utility view must NOT have one.
            var view = new DecreaseColorTSAToolView();
            var lists = view.GetLogicalDescendants()
                .OfType<FEBuilderGBA.Avalonia.Controls.AddressListControl>()
                .ToList();
            Assert.Empty(lists);
        }

        [AvaloniaFact]
        public void Combos_HaveValidInitialSelection()
        {
            // Regression: the Size/Reserve combos previously showed no selection
            // because their TwoWay {Binding SelectedIndex} stayed at -1 when the
            // bound value matched its default while items were still empty.
            var view = new DecreaseColorTSAToolView();

            var method = FindById<ComboBox>(view, "DecreaseColorTSATool_Method_Combo");
            var size = FindById<ComboBox>(view, "DecreaseColorTSATool_SizeMethod_Combo");
            var reserve = FindById<ComboBox>(view, "DecreaseColorTSATool_Reserve_Combo");

            Assert.NotNull(method);
            Assert.NotNull(size);
            Assert.NotNull(reserve);

            // All three must have a real selection (not -1) and populated items.
            Assert.True(method!.ItemCount == 11, $"Method combo should have 11 items, got {method.ItemCount}");
            Assert.True(method.SelectedIndex >= 0, "Method combo must have a selection.");
            Assert.True(size!.ItemCount == 2, $"Size combo should have 2 items, got {size.ItemCount}");
            Assert.True(size.SelectedIndex >= 0, "Size method combo must have a selection.");
            Assert.True(reserve!.ItemCount == 2, $"Reserve combo should have 2 items, got {reserve.ItemCount}");
            Assert.True(reserve.SelectedIndex >= 0, "Reserve combo must have a selection.");

            // Default (method 1 / BG & CG) = scale (1) + reserve (1).
            Assert.Equal(1, method.SelectedIndex);
            Assert.Equal(1, size.SelectedIndex);
            Assert.Equal(1, reserve.SelectedIndex);
        }

        [AvaloniaFact]
        public void InitMethod_SelectsComboAndAppliesPreset()
        {
            var view = new DecreaseColorTSAToolView();

            // Method 7 = single-image map chips: 512x512, 5 banks, resize (no scale).
            view.InitMethod(7);

            var method = FindById<ComboBox>(view, "DecreaseColorTSATool_Method_Combo");
            var size = FindById<ComboBox>(view, "DecreaseColorTSATool_SizeMethod_Combo");
            Assert.NotNull(method);
            Assert.NotNull(size);
            Assert.Equal(7, method!.SelectedIndex);
            // Method 7 is non-scalable → size index 0 (resize).
            Assert.Equal(0, size!.SelectedIndex);
        }
    }
}
