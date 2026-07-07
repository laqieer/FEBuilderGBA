// SPDX-License-Identifier: GPL-3.0-or-later
// Avalonia counterpart of WinForms FE8SpellMenuExtendsForm. Issue #1167 raises
// the AXAML control surface from a single-placeholder stub to a functional
// master/detail editor over the FE8 SkillSystems spell-menu patch.
//
// Master row (unit) -> the unit's spell-list pointer slot. The N1 sub-list is
// the unit's 0x0000-terminated u16 [B0|B1] spell array (B0 = level|promoted,
// B1 = item/spell id). Master + N1 ROM writes and the N1 list-expand are
// wrapped in View-owned UndoService scopes.
//
// The N1 spell-id field is a HEX TextBox (parsed via U.atoh), NOT a hex
// FormatString NumericUpDown — that combination throws a FormatException the
// AvaloniaEditorTests gate catches. The B1 item icon is loaded via
// PreviewIconHelper.LoadItemIconByItemId (a detail-panel preview, NOT a per-row
// list-icon loader), so the master-list rows stay icon-free per the #939
// Category-B contract.
using global::Avalonia;
using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using global::Avalonia.Media.Imaging;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class FE8SpellMenuExtendsView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly FE8SpellMenuExtendsViewModel _vm = new();
        readonly UndoService _undoService = new();
        bool _hasLoadedList;
        Bitmap _n1IconBitmap;

        public string ViewTitle => "Spell Menu Extensions";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Spell Menu Extensions", 1189, 849);
        public event EventHandler? CloseRequested;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);
        public ViewModelBase DataViewModel => _vm;

        public FE8SpellMenuExtendsView()
        {
            InitializeComponent();
            // Bind DataContext so AXAML {Binding SpellEntries} resolves.
            DataContext = _vm;
            EntryList.SelectedAddressChanged += OnSelected;
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            DisposeBitmap(ref _n1IconBitmap);
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (!_hasLoadedList)
            {
                _hasLoadedList = true;
                LoadList();
            }
        }

        static void DisposeBitmap(ref Bitmap bmp)
        {
            if (bmp == null) return;
            try { bmp.Dispose(); } catch (System.Exception ex) { Log.Debug("FE8SpellMenuExtendsView dispose bmp: " + ex.ToString()); }
            bmp = null;
        }

        void LoadList()
        {
            _vm.IsLoading = true;
            try
            {
                var items = _vm.LoadList();
                // #939 Category-B: master rows are units, NOT an item table —
                // no per-row icon loader (the B1 item icon lives in the detail
                // panel, loaded via PreviewIconHelper, not a per-row loader).
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("FE8SpellMenuExtendsView.LoadList failed: " + ex.ToString());
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void OnSelected(uint addr)
        {
            _vm.IsLoading = true;
            try
            {
                _vm.LoadEntry(addr);
                UpdateUI();
                if (_vm.SpellEntries.Count > 0)
                {
                    N1EntryList.SelectedIndex = 0;
                }
            }
            catch (Exception ex)
            {
                Log.Error("FE8SpellMenuExtendsView.OnSelected failed: " + ex.ToString());
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void UpdateUI()
        {
            AddrLabel.Content = string.Format("0x{0:X08}", _vm.CurrentAddr);
            SelectedAddressLabel.Content = string.Format("0x{0:X08}", _vm.CurrentAddr);
            // Editable per-unit list-base OFFSET (mirrors WF N1_ReadStartAddress).
            // master Write repoints the unit slot at whatever the user types here.
            ListPointerBox.Text = "0x" + U.toOffset(_vm.UnitListPointer).ToString("X08");
            UpdateZeroPointerPanel();
        }

        void UpdateZeroPointerPanel()
        {
            ZeroPointerPanel.IsVisible = _vm.IsZeroPointer;
            if (string.IsNullOrEmpty(ZeroPointerText.Text))
            {
                ZeroPointerText.Text = R._("No spell list is allocated for this unit.\nUse Expand List to allocate one.");
            }
        }

        void N1EntryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (N1EntryList.SelectedItem is not FE8SpellMenuExtendsViewModel.SpellEntry entry) return;
            try
            {
                _vm.IsLoading = true;
                _vm.LoadN1Entry(entry.Addr);
                UpdateN1UI();
            }
            catch (Exception ex)
            {
                Log.Error("FE8SpellMenuExtendsView.N1EntryList_SelectionChanged failed: " + ex.ToString());
            }
            finally { _vm.IsLoading = false; }
        }

        void UpdateN1UI()
        {
            N1SelectedAddressLabel.Content = string.Format("0x{0:X08}", _vm.SelectedN1Addr);
            N1BlockSizeLabel.Content = _vm.N1BlockSize.ToString();
            N1LevelBox.Value = _vm.N1Level;
            N1PromotedCheck.IsChecked = _vm.N1Promoted;
            N1SpellIdBox.Text = "0x" + _vm.N1SpellId.ToString("X02");
            N1SpellNameBox.Text = NameResolver.GetItemName(_vm.N1SpellId);
            RefreshN1Icon();
        }

        void RefreshN1Icon()
        {
            try
            {
                ROM rom = CoreState.ROM;
                if (rom != null && _vm.N1SpellId > 0)
                {
                    using var img = PreviewIconHelper.LoadItemIconByItemId(_vm.N1SpellId);
                    Bitmap bmp = img != null ? ImageConversionHelper.ToAvaloniaBitmap(img) : null;
                    SetN1Icon(bmp);
                }
                else
                {
                    SetN1Icon(null);
                }
            }
            catch { SetN1Icon(null); }
        }

        void SetN1Icon(Bitmap bmp)
        {
            if (_n1IconBitmap != null && !ReferenceEquals(_n1IconBitmap, bmp))
            {
                try { _n1IconBitmap.Dispose(); } catch (System.Exception ex) { Log.Debug("FE8SpellMenuExtendsView dispose n1 icon: " + ex.ToString()); }
            }
            _n1IconBitmap = bmp;
            N1SpellIconImage.Source = bmp;
        }

        void MasterWriteButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded || _vm.CurrentAddr == 0) return;
            _undoService.Begin("Edit Spell Menu Extensions Unit Pointer");
            try
            {
                // Repoint the unit's spell-list base at the user-edited offset
                // (mirrors WF WriteButton_Click reading N1_ReadStartAddress.Value).
                _vm.UnitListPointer = U.atoh(ListPointerBox.Text ?? "0");
                _vm.WriteMaster();
                _undoService.Commit();
                _vm.MarkClean();
                // Re-read the slot so UnitListPointer holds the canonical GBA
                // pointer write_p32 stored (a bare offset fails U.isSafetyPointer
                // in LoadSpellList), then refresh the N1 list + zero-pointer panel.
                ROM rom = CoreState.ROM;
                if (rom != null && U.isSafetyOffset(_vm.CurrentAddr + 3, rom))
                {
                    _vm.UnitListPointer = rom.u32(_vm.CurrentAddr);
                    _vm.IsZeroPointer = !U.isSafetyPointer(_vm.UnitListPointer);
                    _vm.LoadSpellList(rom);
                }
                UpdateUI();
                if (_vm.SpellEntries.Count > 0) N1EntryList.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("FE8SpellMenuExtendsView.MasterWrite failed: " + ex.ToString());
            }
        }

        void N1WriteButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded || _vm.SelectedN1Addr == 0) return;
            _undoService.Begin("Edit Spell Menu Extensions Entry");
            try
            {
                _vm.N1Level = (uint)(N1LevelBox.Value ?? 0);
                _vm.N1Promoted = N1PromotedCheck.IsChecked == true;
                _vm.N1SpellId = U.atoh(N1SpellIdBox.Text ?? "0");
                _vm.WriteN1();
                _undoService.Commit();
                _vm.MarkClean();
                // Reload the N1 list label so the row reflects the new B1, then
                // re-select the same entry.
                uint keepAddr = _vm.SelectedN1Addr;
                ROM rom = CoreState.ROM;
                if (rom != null) _vm.LoadSpellList(rom);
                SelectN1ByAddr(keepAddr);
                RefreshN1Icon();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("FE8SpellMenuExtendsView.N1Write failed: " + ex.ToString());
            }
        }

        async void N1ListExpand_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!_vm.IsLoaded || _vm.CurrentAddr == 0)
                {
                    CoreState.Services?.ShowInfo(R._("Select a unit first."));
                    return;
                }
                uint currentCount = (uint)_vm.SpellEntries.Count;
                uint maxCount = System.Math.Max(64u, currentCount + 1);
                uint defaultCount = currentCount + 1;
                if (defaultCount > maxCount) defaultCount = maxCount;
                uint? chosen = await NumberInputDialog.Show(
                    TopLevel.GetTopLevel(this) as Window,
                    R._("Enter the new spell-list entry count for this unit (current: {0}, max: {1}).",
                        currentCount, maxCount),
                    R._("List Expansion"),
                    defaultCount,
                    1,
                    maxCount);
                if (chosen == null) return; // user cancelled
                uint newCount = chosen.Value;

                _undoService.Begin("Expand Spell Menu Extensions List");
                try
                {
                    bool ok = _vm.ExpandN1List(newCount, _undoService.GetActiveUndoData());
                    if (!ok)
                    {
                        _undoService.Rollback();
                        CoreState.Services?.ShowError(R._("List expansion failed (out of free space?)."));
                        return;
                    }
                    _undoService.Commit();
                    _vm.MarkClean();
                    UpdateUI();
                    if (_vm.SpellEntries.Count > 0) N1EntryList.SelectedIndex = 0;
                    CoreState.Services?.ShowInfo(
                        R._("Expanded the spell list to {0} entries.", newCount));
                }
                catch (Exception inner)
                {
                    _undoService.Rollback();
                    Log.Error("FE8SpellMenuExtendsView.N1ListExpand inner failed: " + inner.ToString());
                    CoreState.Services?.ShowError(R._("List expansion failed: {0}", inner.Message));
                }
            }
            catch (Exception ex)
            {
                Log.Error("FE8SpellMenuExtendsView.N1ListExpand failed: " + ex.ToString());
            }
        }

        void ReloadList_Click(object sender, RoutedEventArgs e) => LoadList();

        void SelectN1ByAddr(uint addr)
        {
            for (int i = 0; i < _vm.SpellEntries.Count; i++)
            {
                if (_vm.SpellEntries[i].Addr == addr)
                {
                    N1EntryList.SelectedIndex = i;
                    return;
                }
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
