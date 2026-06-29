// SPDX-License-Identifier: GPL-3.0-or-later
// #1687 — name lists truncated / overflowing across several editors.
//
// The shared AddressListControl hosts the name list in every list-driven
// editor. Its inner ListBox row template is an icon + an unconstrained Text
// TextBlock, and a ListBox defaults to NO horizontal scrollbar — so when a
// row's name was wider than the host column (every consuming view gives the
// control a 250px column) the name was SILENTLY clipped, and long rows could
// paint over the vertical scrollbar (the Map Tile Animation Type 1 report).
//
// The fix sets ScrollViewer.HorizontalScrollBarVisibility="Auto" on that inner
// ListBox so overflowing names scroll instead of clipping and the vertical
// scrollbar stays visible. Because that control backs ~200 views, the change
// also fixes the TSA Animation Editor (both versions) and Status Screen Options
// reports.
//
// SongTrackView's master address bar is NOT in AddressListControl, so its
// Address (NumericUpDown over the full 0-4294967295 range) and SelectedAddress
// fields are widened locally.
//
// These are headless structural assertions — they need no ROM.
using System.Linq;
using global::Avalonia.Controls;
using global::Avalonia.Controls.Primitives;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.LogicalTree;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests;

public class NameListTruncationTests
{
    // ---- shared control: long names scroll instead of clip -----------

    [AvaloniaFact]
    public void AddressListControl_ListBox_HasAutoHorizontalScrollBar()
    {
        var control = new AddressListControl();
        var listBox = control.FindControl<ListBox>("AddressList");
        Assert.NotNull(listBox);
        // #1687: Auto so overflowing names scroll (never silently clipped) and
        // the vertical scrollbar is never obscured.
        Assert.Equal(
            ScrollBarVisibility.Auto,
            ScrollViewer.GetHorizontalScrollBarVisibility(listBox!));
    }

    // ---- the four reported editors instantiate (smoke) ---------------

    [AvaloniaFact]
    public void ImageTSAAnimeView_HasAddressList()
    {
        var view = new ImageTSAAnimeView();
        var list = view.FindControl<AddressListControl>("EntryList");
        Assert.NotNull(list);
    }

    [AvaloniaFact]
    public void ImageTSAAnime2View_HasAddressList()
    {
        var view = new ImageTSAAnime2View();
        var list = view.FindControl<AddressListControl>("EntryList");
        Assert.NotNull(list);
    }

    [AvaloniaFact]
    public void MapTileAnimation1View_HasAddressList()
    {
        var view = new MapTileAnimation1View();
        var list = view.FindControl<AddressListControl>("EntryList");
        Assert.NotNull(list);
    }

    [AvaloniaFact]
    public void StatusOptionView_HasAddressList()
    {
        var view = new StatusOptionView();
        var list = view.FindControl<AddressListControl>("EntryList");
        Assert.NotNull(list);
    }

    [AvaloniaFact]
    public void StatusOptionOrderView_HasAddressList()
    {
        var view = new StatusOptionOrderView();
        var list = view.FindControl<AddressListControl>("EntryList");
        Assert.NotNull(list);
    }

    // ---- SongTrack address fields are wide enough for their content --

    [AvaloniaFact]
    public void SongTrackView_AddressField_IsWideEnough()
    {
        var view = new SongTrackView();
        var addressBox = view.FindControl<NumericUpDown>("AddressBox");
        Assert.NotNull(addressBox);
        // A 0x0800xxxx-range address renders as a 9-10 digit decimal plus the
        // NumericUpDown spinner; 120px clipped it. Require the widened value.
        Assert.True(addressBox!.Width >= 160,
            $"AddressBox.Width was {addressBox.Width}; expected >= 160 so the full decimal address + spinner is visible (#1687).");
    }

    [AvaloniaFact]
    public void SongTrackView_SelectedAddressLabel_IsWideEnough()
    {
        var view = new SongTrackView();
        var label = view.FindControl<TextBlock>("SelectedAddressLabel");
        Assert.NotNull(label);
        // Shows a 0xXXXXXXXX track pointer (10 chars); was undersized at 120px.
        Assert.True(label!.Width >= 150,
            $"SelectedAddressLabel.Width was {label.Width}; expected >= 150 so the full 0xXXXXXXXX pointer is visible (#1687).");
    }
}
