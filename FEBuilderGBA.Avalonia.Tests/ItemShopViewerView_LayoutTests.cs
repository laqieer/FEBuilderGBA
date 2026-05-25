using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// Headless Avalonia layout tests for <see cref="ItemShopViewerView"/>.
    /// Enforces the #369 parity layout: 3-column Grid with ShopList +
    /// SlotList + editor pane, plus the four action buttons (Write,
    /// Append Slot, Remove Last Slot, Reload).
    /// </summary>
    public class ItemShopViewerView_LayoutTests
    {
        [AvaloniaFact]
        public void ItemShopViewerView_CanInstantiate()
        {
            var view = new ItemShopViewerView();
            Assert.NotNull(view);
        }

        [AvaloniaFact]
        public void ItemShopViewerView_HasThreeColumnGrid()
        {
            var view = new ItemShopViewerView();
            // The root content is the 3-column Grid (320, 280, *).
            var rootGrid = view.GetLogicalDescendants().OfType<Grid>().FirstOrDefault();
            Assert.NotNull(rootGrid);
            Assert.Equal(3, rootGrid!.ColumnDefinitions.Count);
        }

        [AvaloniaFact]
        public void ItemShopViewerView_HasShopAndSlotLists()
        {
            var view = new ItemShopViewerView();
            var shopList = view.FindControl<AddressListControl>("ShopList");
            var slotList = view.FindControl<AddressListControl>("SlotList");
            Assert.NotNull(shopList);
            Assert.NotNull(slotList);
        }

        [AvaloniaFact]
        public void ItemShopViewerView_HasFourActionButtons()
        {
            var view = new ItemShopViewerView();
            Assert.NotNull(view.FindControl<Button>("WriteButton"));
            Assert.NotNull(view.FindControl<Button>("AppendSlotButton"));
            Assert.NotNull(view.FindControl<Button>("RemoveLastSlotButton"));
            Assert.NotNull(view.FindControl<Button>("ReloadButton"));
        }

        [AvaloniaFact]
        public void ItemShopViewerView_HasItemIdAndQuantityInputs()
        {
            var view = new ItemShopViewerView();
            // #360 final: ItemIdBox migrated from NumericUpDown to IdFieldControl
            // (which bundles NumericUpDown + Jump + Pick + name preview). The
            // QuantityBox stays a NumericUpDown.
            Assert.NotNull(view.FindControl<IdFieldControl>("ItemIdBox"));
            Assert.NotNull(view.FindControl<NumericUpDown>("QuantityBox"));
        }

        /// <summary>
        /// Source-grep proof that after relocation, AppendSlot_Click reselects
        /// the relocated shop (preserving the user's editing context) rather
        /// than letting LoadShopList()'s SelectFirst() jump to shop 0.
        /// Covers the Copilot CLI follow-up review on PR #465.
        /// </summary>
        [AvaloniaFact]
        public void ItemShopViewerView_RelocationPreservesShopSelection()
        {
            string srcPath = System.IO.Path.Combine(
                System.AppDomain.CurrentDomain.BaseDirectory,
                "..", "..", "..", "..",
                "FEBuilderGBA.Avalonia", "Views", "ItemShopViewerView.axaml.cs");
            string src = System.IO.File.ReadAllText(srcPath);
            // After Relocated outcome, the handler must call the
            // shop-preserving reload helper (not raw LoadShopList).
            Assert.Contains("ReloadShopListAndSelect(newShopAddr", src);
            // The helper must select the new shop address.
            Assert.Contains("ShopList.SelectAddress(shopAddrToSelect)", src);
            // The helper must reselect the appended slot inside the relocated shop.
            Assert.Contains("SlotList.SelectAddress(slot.addr)", src);
        }
    }
}
