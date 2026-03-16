using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
using global::Avalonia.Media.Imaging;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.Controls
{
    /// <summary>
    /// Represents a single item in the address list, with optional icon thumbnail.
    /// </summary>
    public class AddressListItem
    {
        public string Text { get; set; } = "";
        public Bitmap? Icon { get; set; }
    }

    public partial class AddressListControl : UserControl
    {
        readonly ObservableCollection<AddressListItem> _displayItems = new();
        List<AddrResult> _items = new();
        // Maps each display index to its corresponding index in _items.
        // When no filter is active, _filteredIndices[i] == i.
        List<int> _filteredIndices = new();
        bool _isRefreshing;
        // Optional icon loader function: given an item index, returns a Bitmap or null.
        Func<int, Bitmap?>? _iconLoader;

        /// <summary>Fired when the selected address changes.</summary>
        public event Action<uint>? SelectedAddressChanged;

        /// <summary>Fired when user requests a hex editor for the selected address.</summary>
        public event Action<uint>? HexEditorRequested;

        /// <summary>Fired when user confirms a selection (double-click or Enter in pick mode).</summary>
        public event Action<PickResult>? SelectionConfirmed;

        public AddressListControl()
        {
            InitializeComponent();
            AddressList.ItemsSource = _displayItems;
            AddressList.DoubleTapped += AddressList_DoubleTapped;
            AddressList.KeyDown += AddressList_KeyDown;
            SearchBox.KeyDown += SearchBox_KeyDown;
            KeyDown += Control_KeyDown;
        }

        /// <summary>Load address list from AddrResult items.</summary>
        public void SetItems(List<AddrResult> items)
        {
            _items = items ?? new List<AddrResult>();
            _iconLoader = null;
            RefreshDisplay();
            SelectFirst();
        }

        /// <summary>Load address list with icon thumbnails for each item.</summary>
        /// <param name="items">The address list items.</param>
        /// <param name="iconLoader">Function that takes an item index and returns a Bitmap thumbnail, or null.</param>
        public void SetItemsWithIcons(List<AddrResult> items, Func<int, Bitmap?> iconLoader)
        {
            _items = items ?? new List<AddrResult>();
            _iconLoader = iconLoader;
            RefreshDisplay();
            SelectFirst();
        }

        /// <summary>Get the currently selected AddrResult.</summary>
        public AddrResult? SelectedItem
        {
            get
            {
                int displayIdx = AddressList.SelectedIndex;
                if (displayIdx < 0 || displayIdx >= _filteredIndices.Count)
                    return null;
                int itemIdx = _filteredIndices[displayIdx];
                return itemIdx >= 0 && itemIdx < _items.Count ? _items[itemIdx] : null;
            }
        }

        /// <summary>Get the original (unfiltered) index of the currently selected item.</summary>
        public int SelectedOriginalIndex
        {
            get
            {
                int displayIdx = AddressList.SelectedIndex;
                if (displayIdx < 0 || displayIdx >= _filteredIndices.Count)
                    return -1;
                return _filteredIndices[displayIdx];
            }
        }

        /// <summary>Select the first item in the list.</summary>
        public void SelectFirst()
        {
            if (_displayItems.Count > 0)
                AddressList.SelectedIndex = 0;
        }

        /// <summary>Select the last item in the list.</summary>
        public void SelectLast()
        {
            if (_displayItems.Count > 0)
                AddressList.SelectedIndex = _displayItems.Count - 1;
        }

        /// <summary>Focus the search TextBox.</summary>
        public void FocusSearch()
        {
            SearchBox.Focus();
        }

        /// <summary>Select an item by address.</summary>
        public void SelectAddress(uint address)
        {
            // Find the display index that corresponds to this address.
            for (int displayIdx = 0; displayIdx < _filteredIndices.Count; displayIdx++)
            {
                int itemIdx = _filteredIndices[displayIdx];
                if (itemIdx < _items.Count && _items[itemIdx].addr == address)
                {
                    AddressList.SelectedIndex = displayIdx;
                    AddressList.ScrollIntoView(displayIdx);
                    return;
                }
            }
        }

        /// <summary>Enable pick mode — shows hint and makes double-click/Enter fire SelectionConfirmed.</summary>
        public void EnablePickMode()
        {
            PickHint.IsVisible = true;
        }

        void RefreshDisplay(string? filter = null)
        {
            _isRefreshing = true;
            try
            {
                _displayItems.Clear();
                _filteredIndices.Clear();
                for (int i = 0; i < _items.Count; i++)
                {
                    string label = _items[i].name ?? $"#{i}";
                    string display = label;
                    if (filter != null && !display.Contains(filter, StringComparison.OrdinalIgnoreCase))
                        continue;

                    Bitmap? icon = null;
                    try { icon = _iconLoader?.Invoke(i); } catch { /* ignore icon load failures */ }

                    _displayItems.Add(new AddressListItem { Text = display, Icon = icon });
                    _filteredIndices.Add(i);
                }
                CountLabel.Text = $"{_items.Count} items";
            }
            finally
            {
                _isRefreshing = false;
            }
        }

        void AddressList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_isRefreshing) return;
            var item = SelectedItem;
            if (item != null)
                SelectedAddressChanged?.Invoke(item.addr);
        }

        void AddressList_DoubleTapped(object? sender, TappedEventArgs e)
        {
            FireSelectionConfirmed();
        }

        void AddressList_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                FireSelectionConfirmed();
                e.Handled = true;
            }
            else if (e.Key == Key.Home)
            {
                SelectFirst();
                e.Handled = true;
            }
            else if (e.Key == Key.End)
            {
                SelectLast();
                e.Handled = true;
            }
        }

        /// <summary>Handle Ctrl+F at the UserControl level to focus search from anywhere.</summary>
        void Control_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.F && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                FocusSearch();
                e.Handled = true;
            }
        }

        void FireSelectionConfirmed()
        {
            var item = SelectedItem;
            if (item == null) return;
            int originalIdx = SelectedOriginalIndex;
            SelectionConfirmed?.Invoke(new PickResult(originalIdx, item.addr, item.name ?? $"#{originalIdx}"));
        }

        void Previous_Click(object? sender, RoutedEventArgs e)
        {
            if (AddressList.SelectedIndex > 0)
                AddressList.SelectedIndex--;
        }

        void Next_Click(object? sender, RoutedEventArgs e)
        {
            if (AddressList.SelectedIndex < _displayItems.Count - 1)
                AddressList.SelectedIndex++;
        }

        void SearchBox_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ApplySearchFilter();
                e.Handled = true;
            }
        }

        void Search_Click(object? sender, RoutedEventArgs e)
        {
            ApplySearchFilter();
        }

        void ApplySearchFilter()
        {
            string? filter = SearchBox.Text;
            if (string.IsNullOrWhiteSpace(filter))
                RefreshDisplay();
            else
                RefreshDisplay(filter);
        }

        async void CopyAddress_Click(object? sender, RoutedEventArgs e)
        {
            var item = SelectedItem;
            if (item == null) return;
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard != null)
                await clipboard.SetTextAsync($"0x{item.addr:X08}");
        }

        async void CopyName_Click(object? sender, RoutedEventArgs e)
        {
            var item = SelectedItem;
            if (item == null) return;
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard != null)
                await clipboard.SetTextAsync(item.name ?? "");
        }

        async void CopyHexData_Click(object? sender, RoutedEventArgs e)
        {
            var item = SelectedItem;
            if (item == null) return;
            var rom = CoreState.ROM;
            if (rom == null) return;

            // Copy 16 bytes from the address as hex string
            uint length = Math.Min(16, (uint)(rom.Data.Length - item.addr));
            var sb = new StringBuilder();
            for (uint i = 0; i < length; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(rom.u8(item.addr + i).ToString("X02"));
            }

            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard != null)
                await clipboard.SetTextAsync(sb.ToString());
        }
    }
}
