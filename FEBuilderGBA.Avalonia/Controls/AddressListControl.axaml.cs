using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;

namespace FEBuilderGBA.Avalonia.Controls
{
    public partial class AddressListControl : UserControl
    {
        readonly ObservableCollection<string> _displayItems = new();
        List<AddrResult> _items = new();

        /// <summary>Fired when the selected address changes.</summary>
        public event Action<uint>? SelectedAddressChanged;

        public AddressListControl()
        {
            InitializeComponent();
            AddressList.ItemsSource = _displayItems;
        }

        /// <summary>Load address list from AddrResult items.</summary>
        public void SetItems(List<AddrResult> items)
        {
            _items = items ?? new List<AddrResult>();
            RefreshDisplay();
        }

        /// <summary>Get the currently selected AddrResult.</summary>
        public AddrResult? SelectedItem
        {
            get
            {
                int idx = AddressList.SelectedIndex;
                return idx >= 0 && idx < _items.Count ? _items[idx] : null;
            }
        }

        /// <summary>Select an item by address.</summary>
        public void SelectAddress(uint address)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i].addr == address)
                {
                    AddressList.SelectedIndex = i;
                    AddressList.ScrollIntoView(i);
                    return;
                }
            }
        }

        void RefreshDisplay(string? filter = null)
        {
            _displayItems.Clear();
            for (int i = 0; i < _items.Count; i++)
            {
                string display = _items[i].name ?? $"0x{_items[i].addr:X08}";
                if (filter != null && !display.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    continue;
                _displayItems.Add(display);
            }
            CountLabel.Text = $"{_items.Count} items";
        }

        void AddressList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            var item = SelectedItem;
            if (item != null)
                SelectedAddressChanged?.Invoke(item.addr);
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

        void Search_Click(object? sender, RoutedEventArgs e)
        {
            string? filter = SearchBox.Text;
            if (string.IsNullOrWhiteSpace(filter))
                RefreshDisplay();
            else
                RefreshDisplay(filter);
        }
    }
}
