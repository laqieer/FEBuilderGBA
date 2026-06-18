using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
using global::Avalonia.Media.Imaging;
using global::Avalonia.Threading;
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

    public partial class AddressListControl : TranslatedUserControl
    {
        readonly ObservableCollection<AddressListItem> _displayItems = new();
        List<AddrResult> _items = new();
        // Maps each display index to its corresponding index in _items.
        // When no filter is active, _filteredIndices[i] == i.
        List<int> _filteredIndices = new();
        bool _isRefreshing;
        // Optional icon loader function: given an item index, returns a Bitmap or null.
        Func<int, Bitmap?>? _iconLoader;

        /// <summary>Number of items to jump for PageUp/PageDown.</summary>
        internal const int PageSize = 10;

        /// <summary>Timeout in milliseconds before the type-to-search buffer resets.</summary>
        internal const int TypeSearchTimeoutMs = 500;

        // Type-to-search state
        string _typeSearchBuffer = "";
        DispatcherTimer? _typeSearchTimer;

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

        /// <summary>
        /// Load address list and re-select the row matching
        /// <paramref name="preserveAddress"/>. If no row matches, falls back
        /// to <see cref="SelectFirst"/>. Used by editor refresh handlers that
        /// reload the list after a successful Write so the user does not lose
        /// their selection (Copilot CLI PR #596 round-4 review threads).
        /// </summary>
        public void SetItemsPreserveSelection(List<AddrResult> items, uint preserveAddress)
        {
            _items = items ?? new List<AddrResult>();
            _iconLoader = null;
            RefreshDisplay();
            // Clear before attempting to select so the fallback path can
            // reliably detect "preserveAddress not found" by checking
            // SelectedIndex == -1 after the SelectAddress call.
            AddressList.SelectedIndex = -1;
            if (preserveAddress != 0)
                SelectAddress(preserveAddress);
            if (AddressList.SelectedIndex < 0)
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

        /// <summary>Total number of items loaded into the list (regardless of filter state).</summary>
        public int ItemCount => _items.Count;

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

        /// <summary>Select an item by its original (unfiltered) index.</summary>
        /// <returns>True if the index was found and selected; false otherwise.</returns>
        public bool SelectByIndex(int originalIndex)
        {
            for (int displayIdx = 0; displayIdx < _filteredIndices.Count; displayIdx++)
            {
                if (_filteredIndices[displayIdx] == originalIndex)
                {
                    AddressList.SelectedIndex = displayIdx;
                    AddressList.ScrollIntoView(displayIdx);
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Select the item whose <see cref="AddrResult.tag"/> equals
        /// <paramref name="tag"/>. Unlike <see cref="SelectByIndex"/> (original
        /// index) this matches the per-row tag — e.g. an item ID — so callers can
        /// disambiguate multiple rows that share the same <c>addr</c>.
        /// </summary>
        /// <returns>True if a row with that tag was found and selected.</returns>
        public bool SelectByTag(uint tag)
        {
            for (int displayIdx = 0; displayIdx < _filteredIndices.Count; displayIdx++)
            {
                int itemIdx = _filteredIndices[displayIdx];
                if (itemIdx >= 0 && itemIdx < _items.Count && _items[itemIdx].tag == tag)
                {
                    AddressList.SelectedIndex = displayIdx;
                    AddressList.ScrollIntoView(displayIdx);
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Clear the current selection. After this call <see cref="SelectedItem"/>
        /// returns null and the inner ListBox shows nothing highlighted.
        /// Mirrors WinForms `ListBox.SelectedIndex = -1` and is used by
        /// MonsterItemViewerView's cross-tab Jump handlers to honour the
        /// WF "value 0 means no selection" contract.
        /// </summary>
        public void Deselect()
        {
            AddressList.SelectedIndex = -1;
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

        /// <summary>
        /// Programmatically set the search filter text and immediately apply
        /// it. Used by host editors that surface a parallel "Filter" textbox
        /// in their own toolbar (e.g. SongInstrumentView mirrors WF
        /// `SongInstrumentForm.panel1.Filter`) to drive the underlying search
        /// without coupling the host to the SearchBox child control.
        /// </summary>
        public void ApplySearchFilter(string? filter)
        {
            SearchBox.Text = filter ?? string.Empty;
            ApplySearchFilter();
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

        /// <summary>Get a defensive copy of the current (unfiltered) item list.</summary>
        public IReadOnlyList<AddrResult> GetItems() => new List<AddrResult>(_items).AsReadOnly();

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
            else if (e.Key == Key.PageUp)
            {
                PageUp();
                e.Handled = true;
            }
            else if (e.Key == Key.PageDown)
            {
                PageDown();
                e.Handled = true;
            }
            else
            {
                // Type-to-search: handle printable characters
                HandleTypeToSearch(e);
            }
        }

        /// <summary>Move selection up by PageSize items.</summary>
        public void PageUp()
        {
            if (_displayItems.Count == 0) return;
            int current = AddressList.SelectedIndex;
            if (current < 0) current = 0;
            int target = Math.Max(0, current - PageSize);
            AddressList.SelectedIndex = target;
            AddressList.ScrollIntoView(target);
        }

        /// <summary>Move selection down by PageSize items.</summary>
        public void PageDown()
        {
            if (_displayItems.Count == 0) return;
            int current = AddressList.SelectedIndex;
            if (current < 0) current = 0;
            int target = Math.Min(_displayItems.Count - 1, current + PageSize);
            AddressList.SelectedIndex = target;
            AddressList.ScrollIntoView(target);
        }

        /// <summary>Handle type-to-search: accumulate typed characters and jump to matching item.</summary>
        void HandleTypeToSearch(KeyEventArgs e)
        {
            // Only handle letter/digit keys without modifier keys (except Shift for uppercase)
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control) ||
                e.KeyModifiers.HasFlag(KeyModifiers.Alt))
                return;

            char? ch = KeyToChar(e.Key, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
            if (ch == null) return;

            _typeSearchBuffer += ch.Value;
            e.Handled = true;

            // Reset or restart the timer
            if (_typeSearchTimer == null)
            {
                _typeSearchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(TypeSearchTimeoutMs) };
                _typeSearchTimer.Tick += (_, _) =>
                {
                    _typeSearchBuffer = "";
                    _typeSearchTimer.Stop();
                };
            }
            _typeSearchTimer.Stop();
            _typeSearchTimer.Start();

            // Search for the first item whose text starts with the buffer
            JumpToTypeSearchMatch(_typeSearchBuffer);
        }

        /// <summary>Jump to the first display item whose text starts with the given prefix (case-insensitive).</summary>
        internal void JumpToTypeSearchMatch(string prefix)
        {
            for (int i = 0; i < _displayItems.Count; i++)
            {
                if (_displayItems[i].Text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    AddressList.SelectedIndex = i;
                    AddressList.ScrollIntoView(i);
                    return;
                }
            }
        }

        /// <summary>Convert a Key enum to a printable character, or null if not printable.</summary>
        static char? KeyToChar(Key key, bool shift)
        {
            if (key >= Key.A && key <= Key.Z)
            {
                char c = (char)('a' + (key - Key.A));
                return shift ? char.ToUpper(c) : c;
            }
            if (key >= Key.D0 && key <= Key.D9)
                return (char)('0' + (key - Key.D0));
            if (key >= Key.NumPad0 && key <= Key.NumPad9)
                return (char)('0' + (key - Key.NumPad0));
            if (key == Key.Space) return ' ';
            return null;
        }

        /// <summary>Get the current type-to-search buffer (for testing).</summary>
        internal string TypeSearchBuffer => _typeSearchBuffer;

        /// <summary>Reset the type-to-search buffer (for testing).</summary>
        internal void ResetTypeSearchBuffer()
        {
            _typeSearchBuffer = "";
            _typeSearchTimer?.Stop();
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

        /// <summary>
        /// Internal: apply the SearchBox.Text as the filter. External
        /// callers should use the string-arg overload above; this overload
        /// is kept `internal` so xUnit-style external tests don't need
        /// reflection but the API surface stays small (Copilot PR #626
        /// round 2 finding — keep the parameterless helper non-public).
        /// </summary>
        internal void ApplySearchFilter()
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
