using System;
using global::Avalonia;
using System.Collections.Generic;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using global::Avalonia.Media;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    /// <summary>
    /// Avalonia Custom Battle Animation editor — TWO-level pointer-table parity with WinForms
    /// <c>UnitCustomBattleAnimeForm</c> (#1412). Left: ClassList (the FE7 pointer table, one entry
    /// per class). Middle: EntryList (inner weapon-anime records of the selected class, reached by a
    /// second p32 dereference). Right: per-record editor — writes ONLY inner records, never a table slot.
    ///
    /// Out of scope (documented per #1412 plan review): the WinForms Independence panel + split button
    /// (<c>UnitCustomBattleAnimeForm.IndependenceButton_Click</c>) that detaches a shared inner list when
    /// multiple pointer-table slots reference the same base. That UX is NOT ported here; the fix's sole
    /// concern is the release-blocking corruption (writing weapon-anime fields over pointer-table slots).
    /// Editing a shared inner list mutates only the inner bytes — every pointer-table slot stays intact —
    /// so a shared list is still safe to edit (the edit is just shared across its referencing classes,
    /// exactly as the underlying ROM data is shared).
    /// </summary>
    public partial class UnitCustomBattleAnimeView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly UnitCustomBattleAnimeViewModel _vm = new();
        readonly UndoService _undoService = new();
        bool _hasLoadedList;
        List<AddrResult> _classList = new();
        List<AddrResult> _entryList = new();

        public string ViewTitle => "Custom Battle Animation";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Custom Battle Animation", 1314, 521, SizeToContent: true);
        public event EventHandler? CloseRequested;

        public UnitCustomBattleAnimeView()
        {
            InitializeComponent();
            ClassList.SelectedAddressChanged += OnClassSelected;
            EntryList.SelectedAddressChanged += OnEntrySelected;
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (!_hasLoadedList)
            {
                _hasLoadedList = true;
                LoadClassList();
            }
        }

        // ===================================================================
        // Level 1 — pointer table
        // ===================================================================

        void LoadClassList()
        {
            try
            {
                // Clear the inner list first so the ClassList SetItems->SelectFirst chain
                // (which fires OnClassSelected synchronously) can populate it without it
                // being overwritten afterwards (ItemShopViewerView pattern).
                _entryList = new List<AddrResult>();
                EntryList.SetItems(_entryList);

                _classList = _vm.LoadPointerTable();
                ClassList.SetItems(_classList);
                SetStatus($"{_classList.Count} class(es) with custom battle animations.", success: false);
            }
            catch (Exception ex)
            {
                Log.Error("UnitCustomBattleAnimeView.LoadClassList failed: " + ex.ToString());
                SetStatus($"Failed to load: {ex.Message}", success: false, error: true);
            }
        }

        /// <summary>Set the status text with a colour appropriate to the message kind so a
        /// failure never renders in the success colour (Copilot PR #1470 review).</summary>
        void SetStatus(string text, bool success, bool error = false)
        {
            StatusLabel.Text = text;
            StatusLabel.Foreground = error
                ? Brushes.IndianRed
                : (success ? Brushes.DarkGreen : Brushes.Gray);
        }

        void OnClassSelected(uint slotAddr)
        {
            try
            {
                _vm.IsLoading = true;

                AddrResult? entry = FindClassByAddr(slotAddr);
                ClassNameLabel.Text = entry?.name ?? string.Empty;

                _entryList = _vm.LoadInnerList(slotAddr);
                EntryList.SetItems(_entryList);

                // No inner records: clear the editor and block Write until a record is
                // selected, so a stale CurrentAddr from a previous class can't be written.
                if (_entryList.Count == 0)
                {
                    _vm.CurrentAddr = 0;
                    _vm.IsLoaded = false;
                    AddrLabel.Text = "(no weapon animations for this class)";
                    WeaponTypeBox.Value = 0;
                    SpecialBox.Value = 0;
                    AnimeNumberBox.Value = 0;
                }

                _vm.IsLoading = false;
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _vm.IsLoading = false;
                Log.Error("UnitCustomBattleAnimeView.OnClassSelected failed: " + ex.ToString());
            }
        }

        AddrResult? FindClassByAddr(uint slotAddr)
        {
            for (int i = 0; i < _classList.Count; i++)
            {
                if (_classList[i].addr == slotAddr)
                    return _classList[i];
            }
            return null;
        }

        // ===================================================================
        // Level 2 — inner weapon-anime record
        // ===================================================================

        void OnEntrySelected(uint addr)
        {
            try
            {
                _vm.IsLoading = true;
                _vm.LoadEntry(addr);
                UpdateUI();
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _vm.IsLoading = false;
                Log.Error("UnitCustomBattleAnimeView.OnEntrySelected failed: " + ex.ToString());
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            WeaponTypeBox.Value = _vm.WeaponType;
            SpecialBox.Value = _vm.Special;
            AnimeNumberBox.Value = _vm.AnimeNumber;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded)
            {
                SetStatus("Select a weapon animation record first.", success: false);
                return;
            }

            _vm.WeaponType = (uint)(WeaponTypeBox.Value ?? 0);
            _vm.Special = (uint)(SpecialBox.Value ?? 0);
            _vm.AnimeNumber = (uint)(AnimeNumberBox.Value ?? 0);

            _undoService.Begin("Edit Custom Battle Animation");
            try
            {
                _vm.WriteEntry();
                _undoService.Commit();
                _vm.MarkClean();
                SetStatus("Custom battle animation data written.", success: true);
                CoreState.Services?.ShowInfo("Custom battle animation data written.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("UnitCustomBattleAnimeView.Write failed: " + ex.ToString());
                SetStatus($"Write failed: {ex.Message}", success: false, error: true);
            }
        }

        // ===================================================================
        // IEditorView
        // ===================================================================

        public void NavigateTo(uint address)
        {
            // 1) A pointer-table slot address selects that class directly.
            for (int i = 0; i < _classList.Count; i++)
            {
                if (_classList[i].addr == address)
                {
                    ClassList.SelectAddress(address);
                    return;
                }
            }

            // 2) An inner weapon-anime record address may belong to a class other than the one
            //    currently selected. Resolve the OWNING pointer-table slot, select that class
            //    (which rebuilds EntryList synchronously), then select the inner row.
            uint owningSlot = _vm.FindOwningSlot(address);
            if (owningSlot != 0)
            {
                ClassList.SelectAddress(owningSlot);
                EntryList.SelectAddress(address);
                return;
            }

            // 3) Fallback: try the currently loaded inner list.
            EntryList.SelectAddress(address);
        }

        public void SelectFirstItem()
        {
            ClassList.SelectFirst();
            EntryList.SelectFirst();
        }

        public ViewModelBase? DataViewModel => _vm;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
