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

        /// <summary>
        /// Select the item whose <see cref="AddrResult.addr"/> equals
        /// <paramref name="address"/>.
        /// </summary>
        /// <returns><c>true</c> if a matching row was found and selected;
        /// <c>false</c> if no row matched (the previous selection is left
        /// untouched). The return value lets callers distinguish a real hit from
        /// a miss — e.g. the Battle Animation Editor's Class-Editor Jump
        /// (#1377) falls back to a direct load when the jumped pointer is a
        /// class setting pointer that is NOT one of the list rows. Existing
        /// callers that ignore the return value are unaffected.</returns>
        public bool SelectAddress(uint address)
        {
            // Find the display index that corresponds to this address.
            for (int displayIdx = 0; displayIdx < _filteredIndices.Count; displayIdx++)
            {
                int itemIdx = _filteredIndices[displayIdx];
                if (itemIdx < _items.Count && _items[itemIdx].addr == address)
                {
                    AddressList.SelectedIndex = displayIdx;
                    AddressList.ScrollIntoView(displayIdx);
                    return true;
                }
            }
            return false;
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

        // =================================================================
        // #1539 — opt-in structural-edit context menu (Paste / Swap / Clear)
        //
        // Ports the WinForms InputFormRef.MakeGeneralAddressListContextMenu
        // Copy / Paste / Swap-up / Swap-down / Invalidate set onto the shared
        // AddressListControl. OPT-IN: hosts that never call EnableStructuralEdit
        // keep the copy-only menu, so the 100+ copy-only editors are unaffected.
        // The 'Copy(&C)' item added here writes the WinForms block-copy clipboard
        // format (AddressListClipboardCore.Serialize) so Copy/Paste interoperate
        // byte-for-byte with a WinForms session.
        // =================================================================

        bool _structuralEditEnabled;
        int _blockSize;
        Func<List<AddrResult>>? _reload;
        Func<uint, bool>? _writeProtectId00;
        bool _useSwap;
        bool _useClear;
        string _clipboardListName = "AddressList";
        string _clipboardFormName = "";
        readonly UndoService _structuralUndo = new();

        /// <summary>True once <see cref="EnableStructuralEdit"/> has wired the menu (for tests).</summary>
        internal bool StructuralEditEnabled => _structuralEditEnabled;

        /// <summary>
        /// Enable the WinForms-parity structural-edit context menu (Copy block /
        /// Paste / Swap Up / Swap Down / Invalidate) on this list. OPT-IN — call
        /// once from a host editor that should allow per-row structural edits;
        /// editors that never call this stay copy-only. Idempotent: a second call
        /// is a no-op (so a reload path can't duplicate menu items / key handlers —
        /// Copilot plan review #3).
        /// </summary>
        /// <param name="blockSize">Per-row byte size (the WinForms
        /// <c>InputFormRef.BlockSize</c>). Copy/Paste/Swap/Clear operate on this many
        /// bytes at the selected row's address.</param>
        /// <param name="reload">Delegate that re-scans the underlying list after a
        /// mutation (the host's list loader). The list is reloaded preserving the
        /// selected address.</param>
        /// <param name="writeProtectId00">Optional row-0 guard. When non-null and the
        /// selected row is index 0, it is called with the row's id (first u16); if it
        /// returns <c>false</c> the mutation is blocked. NOTE: this is a *safer-than-WF*
        /// addition — WinForms Paste/Clear/Swap do NOT call <c>CheckWriteProtectionID00</c>
        /// (only drag-drop does), so leaving this null reproduces exact WF SoundRoom
        /// behaviour (Copilot plan review #2).</param>
        /// <param name="useSwap">Add the Ctrl+Up / Ctrl+Down swap items (WF
        /// <c>useUpDown</c>).</param>
        /// <param name="useClear">Add the DEL Invalidate item (WF <c>useClear</c>).</param>
        /// <param name="clipboardListName">WinForms inner-ListBox name for the clipboard
        /// identity header (default <c>"AddressList"</c> — the WF control name).</param>
        /// <param name="clipboardFormName">WinForms form name for the clipboard identity
        /// header (e.g. <c>"SoundRoomForm"</c> / <c>"SoundRoomFE6Form"</c>) so paste
        /// interoperates with a WinForms session.</param>
        public void EnableStructuralEdit(
            int blockSize,
            Func<List<AddrResult>> reload,
            Func<uint, bool>? writeProtectId00 = null,
            bool useSwap = true,
            bool useClear = false,
            string? clipboardListName = null,
            string? clipboardFormName = null)
        {
            if (blockSize <= 0 || reload == null) return;

            // ALWAYS refresh the stored mutation parameters: a host window can be reused
            // across a ROM reload (desktop navigation reuses visible windows), so a later
            // call must be able to update the block size / reload delegate / clipboard
            // identity — otherwise a stale header or wrong block size could drive an
            // incorrect write (Copilot review). Only the WIRING (menu items + KeyDown
            // handler, and the useSwap/useClear menu shape) is one-shot.
            _blockSize = blockSize;
            _reload = reload;
            _writeProtectId00 = writeProtectId00;
            if (!string.IsNullOrEmpty(clipboardListName)) _clipboardListName = clipboardListName!;
            if (!string.IsNullOrEmpty(clipboardFormName)) _clipboardFormName = clipboardFormName!;

            if (_structuralEditEnabled) return; // wiring already done — params refreshed above

            _structuralEditEnabled = true;
            _useSwap = useSwap;
            _useClear = useClear;

            var menu = AddressList.ContextMenu;
            if (menu != null)
            {
                menu.Items.Add(new Separator());

                var copyBlock = new MenuItem { Header = R._("コピー(&C)") };
                copyBlock.Click += async (_, _) => await CopyBlock_ClickAsync();
                menu.Items.Add(copyBlock);

                var paste = new MenuItem { Header = R._("貼り付け(&V)") };
                paste.Click += async (_, _) => await Paste_ClickAsync();
                menu.Items.Add(paste);

                if (_useSwap)
                {
                    menu.Items.Add(new Separator());
                    var up = new MenuItem { Header = R._("↑データ入れ替え(Ctrl + Up)") };
                    up.Click += (_, _) => SwapData(false);
                    menu.Items.Add(up);
                    var down = new MenuItem { Header = R._("↓データ入れ替え(Ctrl + Down)") };
                    down.Click += (_, _) => SwapData(true);
                    menu.Items.Add(down);
                }

                if (_useClear)
                {
                    menu.Items.Add(new Separator());
                    var clear = new MenuItem { Header = R._("無効化する(DEL)") };
                    clear.Click += (_, _) => ClearData();
                    menu.Items.Add(clear);
                }
            }

            // Key handlers (WF GeneralAddressList_KeyDown). Registered once.
            AddressList.KeyDown += StructuralEdit_KeyDown;
        }

        void StructuralEdit_KeyDown(object? sender, KeyEventArgs e)
        {
            if (!_structuralEditEnabled) return;
            bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
            if (ctrl && e.Key == Key.C)
            {
                _ = CopyBlock_ClickAsync();
                e.Handled = true;
            }
            else if (ctrl && e.Key == Key.V)
            {
                _ = Paste_ClickAsync();
                e.Handled = true;
            }
            else if (_useSwap && ctrl && e.Key == Key.Up)
            {
                SwapData(false);
                e.Handled = true;
            }
            else if (_useSwap && ctrl && e.Key == Key.Down)
            {
                SwapData(true);
                e.Handled = true;
            }
            else if (_useClear && e.Key == Key.Delete)
            {
                ClearData();
                e.Handled = true;
            }
        }

        /// <summary>
        /// The selected row's address, or <see cref="U.NOT_FOUND"/> when nothing is
        /// selected. WinForms <c>InputFormRef.SelectToAddr</c> equivalent.
        /// </summary>
        uint SelectToAddr()
        {
            var item = SelectedItem;
            return item != null ? item.addr : U.NOT_FOUND;
        }

        /// <summary>
        /// Row-0 write guard for the currently selected row (WF
        /// <c>CheckWriteProtectionID00</c>).
        /// </summary>
        bool CheckWriteProtectionId00() => CheckWriteProtectionId00(SelectedOriginalIndex);

        /// <summary>
        /// Row-0 write guard for an arbitrary original (unfiltered) row index. Returns
        /// true (allowed) unless a guard predicate is set AND the row is original-index 0
        /// AND the predicate — tested against the row's ACTUAL id (first u16), not merely
        /// the index (Copilot plan review #2) — denies it. Swap passes BOTH participating
        /// rows so a guarded id-0 neighbour can't be clobbered by a swap from row 1
        /// (Copilot PR review #2).
        /// </summary>
        bool CheckWriteProtectionId00(int originalIndex)
        {
            if (_writeProtectId00 == null) return true;
            if (originalIndex != 0) return true;
            if (originalIndex < 0 || originalIndex >= _items.Count) return true;
            var item = _items[originalIndex];
            var rom = CoreState.ROM;
            // Validate BOTH bytes of the u16 read: at addr==0x1FF, addr+1==0x200 passes
            // the >=0x200 check but the read still touches the unsafe 0x1FF byte, so guard
            // the low byte too (Copilot review).
            uint id = rom != null && U.isSafetyOffset(item.addr) && U.isSafetyOffset(item.addr + 1)
                ? rom.u16(item.addr) : 0;
            if (_writeProtectId00(id)) return true;
            CoreState.Services?.ShowError(R._("00のデータの変更は許可されていません。"));
            return false;
        }

        async System.Threading.Tasks.Task CopyBlock_ClickAsync()
        {
            if (!_structuralEditEnabled) return;
            uint src = SelectToAddr();
            if (src == U.NOT_FOUND) return;
            var rom = CoreState.ROM;
            if (rom == null || !U.isSafetyOffset(src) || !U.isSafetyOffset((uint)(src + _blockSize - 1))) return;

            byte[] data = rom.getBinaryData(src, _blockSize);
            string text = AddressListClipboardCore.Serialize(_clipboardListName, _clipboardFormName, data);
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard != null)
                await clipboard.SetTextAsync(text);
        }

        async System.Threading.Tasks.Task Paste_ClickAsync()
        {
            if (!_structuralEditEnabled) return;
            uint dest = SelectToAddr();
            if (dest == U.NOT_FOUND) return;
            var rom = CoreState.ROM;
            if (rom == null) return;
            // Validate the full destination range before doing anything.
            if (!U.isSafetyOffset(dest) || !U.isSafetyOffset((uint)(dest + _blockSize - 1))) return;
            if (!CheckWriteProtectionId00()) return;

            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard == null) return;
            string? text = await clipboard.GetTextAsync();
            if (text == null) return;
            PasteFromText(text);
        }

        /// <summary>
        /// Apply a clipboard <paramref name="text"/> payload to the selected row
        /// (the validation + confirm + write + reload half of Paste, factored out so
        /// it is unit-testable without a live clipboard). Returns true when a write
        /// committed. Mirrors WinForms <c>ClipbordToPaste</c>.
        /// </summary>
        internal bool PasteFromText(string text)
        {
            if (!_structuralEditEnabled) return false;
            uint dest = SelectToAddr();
            if (dest == U.NOT_FOUND) return false;
            var rom = CoreState.ROM;
            if (rom == null) return false;
            if (!U.isSafetyOffset(dest) || !U.isSafetyOffset((uint)(dest + _blockSize - 1))) return false;
            if (!CheckWriteProtectionId00()) return false;

            if (!AddressListClipboardCore.TryParse(text, _clipboardListName, _clipboardFormName, _blockSize, out byte[] block))
                return false; // header / count / hex mismatch — silently ignore, no mutation (WF parity)

            if (CoreState.Services?.ShowYesNo(R._("クリップボードのデータで上書きしてもよろしいですか？")) != true)
                return false;

            uint preserve = dest;
            _structuralUndo.Begin("Paste");
            try
            {
                rom.write_range(dest, block);
                _structuralUndo.Commit();
                ReloadPreserving(preserve);
                return true;
            }
            catch (Exception ex)
            {
                _structuralUndo.Rollback();
                Log.Error("AddressListControl.Paste failed: " + ex.ToString());
                return false;
            }
        }

        /// <summary>
        /// Swap the selected row with its adjacent UNDERLYING row (selected
        /// original-index ±1). Operating on original indices (not display indices)
        /// keeps the swap correct under an active search filter and never swaps
        /// non-contiguous visible rows (Copilot plan review #4). WinForms
        /// <c>SwapData(bool isDown)</c>.
        /// </summary>
        internal void SwapData(bool isDown)
        {
            if (!_structuralEditEnabled || !_useSwap) return;
            int origIdx = SelectedOriginalIndex;
            if (origIdx < 0) return;
            var rom = CoreState.ROM;
            if (rom == null) return;

            int neighborIdx = isDown ? origIdx + 1 : origIdx - 1;
            if (neighborIdx < 0 || neighborIdx >= _items.Count) return;

            // The two underlying rows must be adjacent and the neighbour must be
            // currently visible (display) so we never silently reorder rows the user
            // can't see under a filter.
            if (!IsOriginalIndexVisible(neighborIdx)) return;
            // Guard BOTH participating rows: a swap WRITES into both the selected row
            // and its neighbour, so a guarded id-0 row must block the swap whether it is
            // the selection (row 0, swap down) or the neighbour (row 1, swap up)
            // (Copilot PR review #2).
            if (!CheckWriteProtectionId00(origIdx)) return;
            if (!CheckWriteProtectionId00(neighborIdx)) return;

            AddrResult a = _items[origIdx];
            AddrResult b = _items[neighborIdx];

            // Validate both full ranges before any write.
            if (!U.isSafetyOffset(a.addr) || !U.isSafetyOffset((uint)(a.addr + _blockSize - 1))) return;
            if (!U.isSafetyOffset(b.addr) || !U.isSafetyOffset((uint)(b.addr + _blockSize - 1))) return;

            if (CoreState.Services?.ShowYesNo(R._("{0}と{1}を入れ替えもよろしいですか？", a.name ?? "", b.name ?? "")) != true)
                return;

            byte[] abin = rom.getBinaryData(a.addr, _blockSize);
            byte[] bbin = rom.getBinaryData(b.addr, _blockSize);
            if (!AddressListClipboardCore.BuildSwap(abin, bbin, out byte[] newAtA, out byte[] newAtB))
                return;

            uint preserve = a.addr;
            _structuralUndo.Begin("Swap");
            try
            {
                rom.write_range(a.addr, newAtA);
                rom.write_range(b.addr, newAtB);
                _structuralUndo.Commit();
                ReloadPreserving(preserve);
            }
            catch (Exception ex)
            {
                _structuralUndo.Rollback();
                Log.Error("AddressListControl.SwapData failed: " + ex.ToString());
            }
        }

        /// <summary>Zero-fill the selected row's block (WF <c>ClearData</c>).</summary>
        internal void ClearData()
        {
            if (!_structuralEditEnabled || !_useClear) return;
            uint dest = SelectToAddr();
            if (dest == U.NOT_FOUND) return;
            var rom = CoreState.ROM;
            if (rom == null) return;
            if (!U.isSafetyOffset(dest) || !U.isSafetyOffset((uint)(dest + _blockSize - 1))) return;
            if (!CheckWriteProtectionId00()) return;

            // Use the FULL WinForms ClearData prompt (WF InputFormRef.ClearData) so the
            // user gets the "this row becomes the list terminator" warning AND the
            // existing ja→en/zh translation for the complete key applies (Copilot review).
            if (CoreState.Services?.ShowYesNo(R._("このデータを消去してもよろしいですか？\r\nこのデータが終端になり、このデータまでが有効なデータとなります。")) != true)
                return;

            byte[] data = AddressListClipboardCore.BuildCleared(_blockSize);
            uint preserve = dest;
            _structuralUndo.Begin("Clear");
            try
            {
                rom.write_range(dest, data);
                _structuralUndo.Commit();
                ReloadPreserving(preserve);
            }
            catch (Exception ex)
            {
                _structuralUndo.Rollback();
                Log.Error("AddressListControl.ClearData failed: " + ex.ToString());
            }
        }

        /// <summary>True when the given original (unfiltered) index is currently
        /// visible in the filtered display.</summary>
        bool IsOriginalIndexVisible(int originalIndex)
        {
            for (int i = 0; i < _filteredIndices.Count; i++)
                if (_filteredIndices[i] == originalIndex) return true;
            return false;
        }

        /// <summary>Reload the underlying list via the host delegate, re-selecting the
        /// row whose addr equals <paramref name="preserveAddress"/>.</summary>
        void ReloadPreserving(uint preserveAddress)
        {
            if (_reload == null) return;
            List<AddrResult> items;
            try { items = _reload() ?? new List<AddrResult>(); }
            catch (Exception ex)
            {
                Log.Error("AddressListControl.ReloadPreserving reload failed: " + ex.ToString());
                return;
            }
            SetItemsPreserveSelection(items, preserveAddress);
        }
    }
}
