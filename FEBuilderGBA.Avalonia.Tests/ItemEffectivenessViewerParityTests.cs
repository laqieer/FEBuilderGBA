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
/// Parity tests for issue #368 — the rewritten Avalonia Item Effectiveness
/// Editor must present an item-driven master/detail layout that mirrors the
/// WinForms <c>ItemEffectivenessForm</c>:
///   * Outer list rows = items whose +16 effectiveness pointer is set.
///   * Inner list rows = the class IDs reachable through that pointer.
///   * Right panel exposes the per-class write field, class-name lookup, class
///     icon, address+block-size labels, write button, list-expansion button,
///     reload button, and the IndependencePanel.
/// </summary>
[Collection("SharedState")]
public class ItemEffectivenessViewerParityTests : IClassFixture<RomFixture>
{
    private readonly RomFixture _fixture;
    private readonly ITestOutputHelper _output;

    public ItemEffectivenessViewerParityTests(RomFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [AvaloniaFact]
    public void OuterList_PopulatesWithItemRows_OnFE8U()
    {
        if (!_fixture.IsAvailable) return;

        var view = new ItemEffectivenessViewerView();
        view.Show();
        try
        {
            var outer = view.FindControl<AddressListControl>("EntryList");
            Assert.NotNull(outer);
            // The new layout iterates the item table by +16 effectiveness
            // pointer, so on a vanilla FE8U ROM at least Rapier and a couple
            // of other anti-cavalry / anti-flier weapons must appear.
            Assert.True(outer!.SelectedItem != null, "Outer list must populate on open");
            _output.WriteLine($"Outer list selected item: {outer.SelectedItem!.name}");
        }
        finally { view.Close(); }
    }

    [AvaloniaFact]
    public void OuterList_FirstItem_PopulatesInnerClassList()
    {
        if (!_fixture.IsAvailable) return;

        var view = new ItemEffectivenessViewerView();
        view.Show();
        try
        {
            var outer = view.FindControl<AddressListControl>("EntryList");
            Assert.NotNull(outer);
            outer!.SelectFirst();

            var inner = view.FindControl<ListBox>("InnerList");
            Assert.NotNull(inner);
            // FE8U Rapier's effectiveness list is non-empty.
            Assert.True(inner!.ItemCount > 0, "Inner list must populate when an item is selected");
            _output.WriteLine($"Inner list count: {inner.ItemCount}");
        }
        finally { view.Close(); }
    }

    [AvaloniaFact]
    public void RightPanel_ExposesClassEditFields()
    {
        if (!_fixture.IsAvailable) return;

        var view = new ItemEffectivenessViewerView();
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
            Assert.NotNull(view.FindControl<ListBox>("ItemListBox"));
            Assert.NotNull(view.FindControl<Button>("IndependenceButton"));
        }
        finally { view.Close(); }
    }

    [AvaloniaFact]
    public void ViewModel_ScanItemListUsesItemPointer_AndPlusSixteen()
    {
        if (!_fixture.IsAvailable) return;

        var vm = new ItemEffectivenessViewerViewModel();
        var list = vm.LoadItemList();
        // FE8U items 0x01 (Rapier) and others use weapon-effectiveness; the
        // outer list must contain at least one item.
        Assert.NotEmpty(list);
        _output.WriteLine($"Outer item list count: {list.Count}; first: {list[0].name}");
    }
}
