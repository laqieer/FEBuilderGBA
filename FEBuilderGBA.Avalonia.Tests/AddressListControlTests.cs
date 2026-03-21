using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.Tests;

/// <summary>
/// Headless UI tests for AddressListControl.
/// Verifies instantiation, item population, selection events, filtering,
/// navigation, and property binding without needing a ROM.
/// </summary>
public class AddressListControlTests
{
    /// <summary>Helper to build a list of AddrResult items.</summary>
    static List<AddrResult> MakeItems(int count, uint baseAddr = 0x1000, string prefix = "Item")
    {
        var list = new List<AddrResult>();
        for (int i = 0; i < count; i++)
            list.Add(new AddrResult(baseAddr + (uint)(i * 4), $"{prefix} {i}"));
        return list;
    }

    // ---------------------------------------------------------------
    // 1. Control instantiation and default state
    // ---------------------------------------------------------------

    [AvaloniaFact]
    public void Constructor_CreatesControl()
    {
        var control = new AddressListControl();
        Assert.NotNull(control);
    }

    [AvaloniaFact]
    public void DefaultState_NoSelectedItem()
    {
        var control = new AddressListControl();
        Assert.Null(control.SelectedItem);
    }

    [AvaloniaFact]
    public void DefaultState_SelectedOriginalIndex_IsNegativeOne()
    {
        var control = new AddressListControl();
        Assert.Equal(-1, control.SelectedOriginalIndex);
    }

    [AvaloniaFact]
    public void DefaultState_ListBoxExists()
    {
        var control = new AddressListControl();
        var listBox = control.FindControl<ListBox>("AddressList");
        Assert.NotNull(listBox);
    }

    [AvaloniaFact]
    public void DefaultState_SearchBoxExists()
    {
        var control = new AddressListControl();
        var searchBox = control.FindControl<TextBox>("SearchBox");
        Assert.NotNull(searchBox);
    }

    [AvaloniaFact]
    public void DefaultState_CountLabelExists()
    {
        var control = new AddressListControl();
        var label = control.FindControl<TextBlock>("CountLabel");
        Assert.NotNull(label);
    }

    // ---------------------------------------------------------------
    // 2. Setting Items property populates the list
    // ---------------------------------------------------------------

    [AvaloniaFact]
    public void SetItems_PopulatesList()
    {
        var control = new AddressListControl();
        var items = MakeItems(5);
        control.SetItems(items);

        var listBox = control.FindControl<ListBox>("AddressList");
        Assert.NotNull(listBox);
        Assert.Equal(5, listBox!.ItemCount);
    }

    [AvaloniaFact]
    public void SetItems_UpdatesCountLabel()
    {
        var control = new AddressListControl();
        var items = MakeItems(7);
        control.SetItems(items);

        var label = control.FindControl<TextBlock>("CountLabel");
        Assert.NotNull(label);
        Assert.Equal("7 items", label!.Text);
    }

    [AvaloniaFact]
    public void SetItems_AutoSelectsFirstItem()
    {
        var control = new AddressListControl();
        var items = MakeItems(3);
        control.SetItems(items);

        var listBox = control.FindControl<ListBox>("AddressList");
        Assert.NotNull(listBox);
        Assert.Equal(0, listBox!.SelectedIndex);
    }

    [AvaloniaFact]
    public void SetItems_Null_TreatedAsEmpty()
    {
        var control = new AddressListControl();
        control.SetItems(null!);

        var listBox = control.FindControl<ListBox>("AddressList");
        Assert.NotNull(listBox);
        Assert.Equal(0, listBox!.ItemCount);
    }

    [AvaloniaFact]
    public void SetItems_ReplacesExistingItems()
    {
        var control = new AddressListControl();
        control.SetItems(MakeItems(5));
        control.SetItems(MakeItems(3));

        var listBox = control.FindControl<ListBox>("AddressList");
        Assert.NotNull(listBox);
        Assert.Equal(3, listBox!.ItemCount);
    }

    [AvaloniaFact]
    public void SetItemsWithIcons_PopulatesList()
    {
        var control = new AddressListControl();
        var items = MakeItems(4);
        control.SetItemsWithIcons(items, _ => null);

        var listBox = control.FindControl<ListBox>("AddressList");
        Assert.NotNull(listBox);
        Assert.Equal(4, listBox!.ItemCount);
    }

    // ---------------------------------------------------------------
    // 3. SelectedAddressChanged event fires on selection
    // ---------------------------------------------------------------

    [AvaloniaFact]
    public void SelectionChanged_FiresEvent()
    {
        var control = new AddressListControl();
        var items = MakeItems(5);
        control.SetItems(items);

        uint? firedAddress = null;
        control.SelectedAddressChanged += addr => firedAddress = addr;

        var listBox = control.FindControl<ListBox>("AddressList");
        Assert.NotNull(listBox);
        listBox!.SelectedIndex = 2;

        Assert.NotNull(firedAddress);
        Assert.Equal(items[2].addr, firedAddress!.Value);
    }

    [AvaloniaFact]
    public void SelectionChanged_FiresDifferentAddressForDifferentSelections()
    {
        var control = new AddressListControl();
        var items = MakeItems(5);
        control.SetItems(items);

        var firedAddresses = new List<uint>();
        control.SelectedAddressChanged += addr => firedAddresses.Add(addr);

        var listBox = control.FindControl<ListBox>("AddressList");
        Assert.NotNull(listBox);
        listBox!.SelectedIndex = 1;
        listBox.SelectedIndex = 3;

        Assert.Equal(2, firedAddresses.Count);
        Assert.Equal(items[1].addr, firedAddresses[0]);
        Assert.Equal(items[3].addr, firedAddresses[1]);
    }

    [AvaloniaFact]
    public void SelectedItem_ReturnsCorrectAddrResult()
    {
        var control = new AddressListControl();
        var items = MakeItems(3);
        control.SetItems(items);

        var listBox = control.FindControl<ListBox>("AddressList");
        Assert.NotNull(listBox);
        listBox!.SelectedIndex = 1;

        var selected = control.SelectedItem;
        Assert.NotNull(selected);
        Assert.Equal(items[1].addr, selected!.addr);
        Assert.Equal(items[1].name, selected.name);
    }

    [AvaloniaFact]
    public void SelectedOriginalIndex_ReturnsCorrectIndex()
    {
        var control = new AddressListControl();
        var items = MakeItems(5);
        control.SetItems(items);

        var listBox = control.FindControl<ListBox>("AddressList");
        Assert.NotNull(listBox);
        listBox!.SelectedIndex = 3;

        Assert.Equal(3, control.SelectedOriginalIndex);
    }

    // ---------------------------------------------------------------
    // 4. Empty list handling
    // ---------------------------------------------------------------

    [AvaloniaFact]
    public void EmptyList_SelectedItem_IsNull()
    {
        var control = new AddressListControl();
        control.SetItems(new List<AddrResult>());
        Assert.Null(control.SelectedItem);
    }

    [AvaloniaFact]
    public void EmptyList_SelectedOriginalIndex_IsNegativeOne()
    {
        var control = new AddressListControl();
        control.SetItems(new List<AddrResult>());
        Assert.Equal(-1, control.SelectedOriginalIndex);
    }

    [AvaloniaFact]
    public void EmptyList_CountLabel_ShowsZero()
    {
        var control = new AddressListControl();
        control.SetItems(new List<AddrResult>());

        var label = control.FindControl<TextBlock>("CountLabel");
        Assert.NotNull(label);
        Assert.Equal("0 items", label!.Text);
    }

    [AvaloniaFact]
    public void EmptyList_PageUp_DoesNotThrow()
    {
        var control = new AddressListControl();
        control.SetItems(new List<AddrResult>());
        var ex = Record.Exception(() => control.PageUp());
        Assert.Null(ex);
    }

    [AvaloniaFact]
    public void EmptyList_PageDown_DoesNotThrow()
    {
        var control = new AddressListControl();
        control.SetItems(new List<AddrResult>());
        var ex = Record.Exception(() => control.PageDown());
        Assert.Null(ex);
    }

    // ---------------------------------------------------------------
    // 5. FilterText filtering
    // ---------------------------------------------------------------

    [AvaloniaFact]
    public void SearchFilter_FiltersItems()
    {
        var control = new AddressListControl();
        var items = new List<AddrResult>
        {
            new AddrResult(0x1000, "Alpha Unit"),
            new AddrResult(0x1004, "Beta Class"),
            new AddrResult(0x1008, "Alpha Class"),
            new AddrResult(0x100C, "Gamma Unit"),
        };
        control.SetItems(items);

        // Apply filter via SearchBox + manual trigger
        var searchBox = control.FindControl<TextBox>("SearchBox");
        Assert.NotNull(searchBox);
        searchBox!.Text = "Alpha";

        // Trigger the filter by clicking the search button
        var searchButton = FindSearchButton(control);
        Assert.NotNull(searchButton);
        // Simulate search by raising Click through the button command
        searchButton!.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

        var listBox = control.FindControl<ListBox>("AddressList");
        Assert.NotNull(listBox);
        Assert.Equal(2, listBox!.ItemCount);
    }

    [AvaloniaFact]
    public void SearchFilter_CaseInsensitive()
    {
        var control = new AddressListControl();
        var items = new List<AddrResult>
        {
            new AddrResult(0x1000, "ALPHA"),
            new AddrResult(0x1004, "alpha"),
            new AddrResult(0x1008, "Beta"),
        };
        control.SetItems(items);

        var searchBox = control.FindControl<TextBox>("SearchBox");
        Assert.NotNull(searchBox);
        searchBox!.Text = "alpha";

        var searchButton = FindSearchButton(control);
        Assert.NotNull(searchButton);
        searchButton!.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

        var listBox = control.FindControl<ListBox>("AddressList");
        Assert.NotNull(listBox);
        Assert.Equal(2, listBox!.ItemCount);
    }

    [AvaloniaFact]
    public void SearchFilter_EmptyString_ShowsAll()
    {
        var control = new AddressListControl();
        var items = MakeItems(5);
        control.SetItems(items);

        // First apply a filter
        var searchBox = control.FindControl<TextBox>("SearchBox");
        Assert.NotNull(searchBox);
        searchBox!.Text = "Item 1";
        var searchButton = FindSearchButton(control);
        Assert.NotNull(searchButton);
        searchButton!.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

        // Clear the filter
        searchBox.Text = "";
        searchButton.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

        var listBox = control.FindControl<ListBox>("AddressList");
        Assert.NotNull(listBox);
        Assert.Equal(5, listBox!.ItemCount);
    }

    [AvaloniaFact]
    public void SearchFilter_NoMatch_ShowsEmptyList()
    {
        var control = new AddressListControl();
        var items = MakeItems(5);
        control.SetItems(items);

        var searchBox = control.FindControl<TextBox>("SearchBox");
        Assert.NotNull(searchBox);
        searchBox!.Text = "ZZZZZ_NO_MATCH";
        var searchButton = FindSearchButton(control);
        Assert.NotNull(searchButton);
        searchButton!.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

        var listBox = control.FindControl<ListBox>("AddressList");
        Assert.NotNull(listBox);
        Assert.Equal(0, listBox!.ItemCount);
    }

    [AvaloniaFact]
    public void SearchFilter_SelectedOriginalIndex_MapsCorrectly()
    {
        var control = new AddressListControl();
        var items = new List<AddrResult>
        {
            new AddrResult(0x1000, "Alpha"),
            new AddrResult(0x1004, "Beta"),
            new AddrResult(0x1008, "Alpha2"),
            new AddrResult(0x100C, "Gamma"),
        };
        control.SetItems(items);

        // Filter to "Alpha" items (indices 0 and 2 in original)
        var searchBox = control.FindControl<TextBox>("SearchBox");
        Assert.NotNull(searchBox);
        searchBox!.Text = "Alpha";
        var searchButton = FindSearchButton(control);
        Assert.NotNull(searchButton);
        searchButton!.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

        var listBox = control.FindControl<ListBox>("AddressList");
        Assert.NotNull(listBox);
        Assert.Equal(2, listBox!.ItemCount);

        // Select the second filtered item (should be original index 2)
        listBox.SelectedIndex = 1;
        Assert.Equal(2, control.SelectedOriginalIndex);
        Assert.Equal(0x1008u, control.SelectedItem!.addr);
    }

    // ---------------------------------------------------------------
    // 6. NavigateTo / SelectAddress scrolls to the correct item
    // ---------------------------------------------------------------

    [AvaloniaFact]
    public void SelectAddress_SelectsCorrectItem()
    {
        var control = new AddressListControl();
        var items = MakeItems(10);
        control.SetItems(items);

        control.SelectAddress(items[5].addr);

        var listBox = control.FindControl<ListBox>("AddressList");
        Assert.NotNull(listBox);
        Assert.Equal(5, listBox!.SelectedIndex);
    }

    [AvaloniaFact]
    public void SelectAddress_NonExistent_DoesNotChangeSelection()
    {
        var control = new AddressListControl();
        var items = MakeItems(5);
        control.SetItems(items);

        // Selection is at 0 after SetItems
        var listBox = control.FindControl<ListBox>("AddressList");
        Assert.NotNull(listBox);
        Assert.Equal(0, listBox!.SelectedIndex);

        // Try to select a non-existent address
        control.SelectAddress(0xDEADBEEF);
        Assert.Equal(0, listBox.SelectedIndex);
    }

    [AvaloniaFact]
    public void SelectFirst_SelectsIndexZero()
    {
        var control = new AddressListControl();
        var items = MakeItems(5);
        control.SetItems(items);

        var listBox = control.FindControl<ListBox>("AddressList");
        Assert.NotNull(listBox);
        listBox!.SelectedIndex = 3;

        control.SelectFirst();
        Assert.Equal(0, listBox.SelectedIndex);
    }

    [AvaloniaFact]
    public void SelectLast_SelectsLastIndex()
    {
        var control = new AddressListControl();
        var items = MakeItems(5);
        control.SetItems(items);

        control.SelectLast();

        var listBox = control.FindControl<ListBox>("AddressList");
        Assert.NotNull(listBox);
        Assert.Equal(4, listBox!.SelectedIndex);
    }

    // ---------------------------------------------------------------
    // 7. Property binding works correctly / additional behavior
    // ---------------------------------------------------------------

    [AvaloniaFact]
    public void PageUp_MovesSelectionUp()
    {
        var control = new AddressListControl();
        var items = MakeItems(30);
        control.SetItems(items);

        var listBox = control.FindControl<ListBox>("AddressList");
        Assert.NotNull(listBox);
        listBox!.SelectedIndex = 15;

        control.PageUp();
        Assert.Equal(15 - AddressListControl.PageSize, listBox.SelectedIndex);
    }

    [AvaloniaFact]
    public void PageDown_MovesSelectionDown()
    {
        var control = new AddressListControl();
        var items = MakeItems(30);
        control.SetItems(items);

        var listBox = control.FindControl<ListBox>("AddressList");
        Assert.NotNull(listBox);
        listBox!.SelectedIndex = 5;

        control.PageDown();
        Assert.Equal(5 + AddressListControl.PageSize, listBox.SelectedIndex);
    }

    [AvaloniaFact]
    public void PageUp_ClampsToZero()
    {
        var control = new AddressListControl();
        var items = MakeItems(5);
        control.SetItems(items);

        var listBox = control.FindControl<ListBox>("AddressList");
        Assert.NotNull(listBox);
        listBox!.SelectedIndex = 2;

        control.PageUp();
        Assert.Equal(0, listBox.SelectedIndex);
    }

    [AvaloniaFact]
    public void PageDown_ClampsToLastItem()
    {
        var control = new AddressListControl();
        var items = MakeItems(5);
        control.SetItems(items);

        var listBox = control.FindControl<ListBox>("AddressList");
        Assert.NotNull(listBox);
        listBox!.SelectedIndex = 3;

        control.PageDown();
        Assert.Equal(4, listBox.SelectedIndex);
    }

    [AvaloniaFact]
    public void EnablePickMode_ShowsPickHint()
    {
        var control = new AddressListControl();
        var pickHint = control.FindControl<TextBlock>("PickHint");
        Assert.NotNull(pickHint);
        Assert.False(pickHint!.IsVisible);

        control.EnablePickMode();
        Assert.True(pickHint.IsVisible);
    }

    [AvaloniaFact]
    public void JumpToTypeSearchMatch_SelectsFirstMatch()
    {
        var control = new AddressListControl();
        var items = new List<AddrResult>
        {
            new AddrResult(0x1000, "Apple"),
            new AddrResult(0x1004, "Banana"),
            new AddrResult(0x1008, "Avocado"),
        };
        control.SetItems(items);

        control.JumpToTypeSearchMatch("Ban");

        var listBox = control.FindControl<ListBox>("AddressList");
        Assert.NotNull(listBox);
        Assert.Equal(1, listBox!.SelectedIndex);
    }

    [AvaloniaFact]
    public void JumpToTypeSearchMatch_CaseInsensitive()
    {
        var control = new AddressListControl();
        var items = new List<AddrResult>
        {
            new AddrResult(0x1000, "apple"),
            new AddrResult(0x1004, "BANANA"),
        };
        control.SetItems(items);

        control.JumpToTypeSearchMatch("ban");

        var listBox = control.FindControl<ListBox>("AddressList");
        Assert.NotNull(listBox);
        Assert.Equal(1, listBox!.SelectedIndex);
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(100)]
    public void SetItems_VariousCounts_PopulatesCorrectly(int count)
    {
        var control = new AddressListControl();
        var items = MakeItems(count);
        control.SetItems(items);

        var listBox = control.FindControl<ListBox>("AddressList");
        Assert.NotNull(listBox);
        Assert.Equal(count, listBox!.ItemCount);
    }

    [AvaloniaFact]
    public void TypeSearchBuffer_DefaultIsEmpty()
    {
        var control = new AddressListControl();
        Assert.Equal("", control.TypeSearchBuffer);
    }

    [AvaloniaFact]
    public void ResetTypeSearchBuffer_ClearsBuffer()
    {
        var control = new AddressListControl();
        // The buffer is internal; set items and use JumpToTypeSearchMatch to verify reset works
        control.SetItems(MakeItems(3));
        control.ResetTypeSearchBuffer();
        Assert.Equal("", control.TypeSearchBuffer);
    }

    [AvaloniaFact]
    public void PageSize_IsTen()
    {
        Assert.Equal(10, AddressListControl.PageSize);
    }

    [AvaloniaFact]
    public void TypeSearchTimeoutMs_Is500()
    {
        Assert.Equal(500, AddressListControl.TypeSearchTimeoutMs);
    }

    // ---------------------------------------------------------------
    // Helper methods
    // ---------------------------------------------------------------

    /// <summary>Find the "Find" search button in the control.</summary>
    static Button? FindSearchButton(AddressListControl control)
    {
        // The search button is the second child of the first Grid row
        // Walk the visual tree to find it
        var grid = control.Content as global::Avalonia.Controls.Grid;
        if (grid == null) return null;

        foreach (var child in grid.Children)
        {
            if (child is global::Avalonia.Controls.Grid innerGrid)
            {
                foreach (var innerChild in innerGrid.Children)
                {
                    if (innerChild is Button btn && btn.Content?.ToString() == "Find")
                        return btn;
                }
            }
        }
        return null;
    }
}
