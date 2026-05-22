using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests;

/// <summary>
/// Parity tests for issue #368 — the rewritten Avalonia Item Promotion Editor
/// must present an item-driven master/detail layout that mirrors the WinForms
/// <c>ItemPromotionForm</c>:
///   * Outer list rows = the fixed CC items (Hero Crest, Knight Crest, Orion
///     Bolt, Elysian Whip, Guiding Ring, plus the FE7+ extras).
///   * Inner list rows = the class IDs reachable through the selected CC
///     item's promotion array pointer.
///   * Right panel exposes the per-class write field, class-name lookup, class
///     icon, address+block-size labels, write button, list-expansion button,
///     reload button, and the X_IER_Patch warning label.
/// </summary>
[Collection("SharedState")]
public class ItemPromotionViewerParityTests : IClassFixture<RomFixture>
{
    private readonly RomFixture _fixture;
    private readonly ITestOutputHelper _output;

    public ItemPromotionViewerParityTests(RomFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [AvaloniaFact]
    public void OuterList_PopulatesWithCCItemRows_OnFE8U()
    {
        if (!_fixture.IsAvailable) return;

        var view = new ItemPromotionViewerView();
        view.Show();
        try
        {
            var outer = view.FindControl<AddressListControl>("EntryList");
            Assert.NotNull(outer);
            Assert.True(outer!.SelectedItem != null, "Outer list must populate on open");
            _output.WriteLine($"Outer list selected item: {outer.SelectedItem!.name}");
        }
        finally { view.Close(); }
    }

    [AvaloniaFact]
    public void OuterList_FirstItem_PopulatesInnerClassList()
    {
        if (!_fixture.IsAvailable) return;

        var view = new ItemPromotionViewerView();
        view.Show();
        try
        {
            var outer = view.FindControl<AddressListControl>("EntryList");
            Assert.NotNull(outer);
            outer!.SelectFirst();

            var inner = view.FindControl<ListBox>("InnerList");
            Assert.NotNull(inner);
            // FE8U Hero Crest promotes at least one class (Lord -> Great Lord etc.).
            Assert.True(inner!.ItemCount > 0, "Inner list must populate when a CC item is selected");
            _output.WriteLine($"Inner list count: {inner.ItemCount}");
        }
        finally { view.Close(); }
    }

    [AvaloniaFact]
    public void RightPanel_ExposesClassEditFields()
    {
        if (!_fixture.IsAvailable) return;

        var view = new ItemPromotionViewerView();
        view.Show();
        try
        {
            Assert.NotNull(view.FindControl<NumericUpDown>("ClassIdInput"));
            Assert.NotNull(view.FindControl<TextBlock>("ClassNameLabel"));
            Assert.NotNull(view.FindControl<Image>("ClassIconImage"));
            Assert.NotNull(view.FindControl<TextBlock>("AddressLabel"));
            Assert.NotNull(view.FindControl<TextBlock>("BlockSizeLabel"));
            Assert.NotNull(view.FindControl<Button>("WriteButton"));
            Assert.NotNull(view.FindControl<Button>("ListExpandsButton"));
            Assert.NotNull(view.FindControl<Button>("ReloadListButton"));
            Assert.NotNull(view.FindControl<TextBlock>("X_IER_Patch"));
        }
        finally { view.Close(); }
    }

    [AvaloniaFact]
    public void ViewModel_OuterListSourcesFromCCItemPointers()
    {
        if (!_fixture.IsAvailable) return;

        var vm = new ItemPromotionViewerViewModel();
        var list = vm.LoadItemList();
        // FE8U has all 10 CC items (Hero Crest .. Sun Bracelet).
        Assert.NotEmpty(list);
        _output.WriteLine($"Outer CC item list count: {list.Count}; first: {list[0].name}");
    }
}
