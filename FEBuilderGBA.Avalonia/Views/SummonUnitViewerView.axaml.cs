using System;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Core;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SummonUnitViewerView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly SummonUnitViewerViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Summon Unit";
        public bool IsLoaded => _vm.CanWrite;

        public SummonUnitViewerView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            _vm.IsLoading = true;
            try
            {
                var items = _vm.LoadSummonUnitList();
                EntryList.SetItemsWithIcons(items, i => ListIconLoaders.UnitPortraitByIdLoader(items, i));
            }
            catch (Exception ex)
            {
                Log.Error("SummonUnitViewerView.LoadList failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }

            // #1605: the summon table only exists on FE8 (FE6/FE7 have
            // summon_unit_pointer == 0). Gate the Expand affordance to the
            // active ROM so it disables on a non-FE8 / no ROM load. Wrapped
            // defensively since ExpandListButton is a generated AXAML field.
            try
            {
                if (ExpandListButton != null)
                    ExpandListButton.IsEnabled =
                        CoreState.ROM?.RomInfo?.version == 8 &&
                        CoreState.ROM?.RomInfo?.summon_unit_pointer != 0;
            }
            catch (Exception ex)
            {
                Log.Error($"SummonUnitViewerView.LoadList expand-gate failed: {ex.Message}");
            }
        }

        void OnSelected(uint addr)
        {
            _vm.IsLoading = true;
            try
            {
                _vm.LoadSummonUnit(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("SummonUnitViewerView.OnSelected failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            UnitIdBox.Value = _vm.UnitId;
            // UnitId is a 1-based ROM unit ID; GetUnitNameByOneBasedId handles
            // the 0/bounds cases and the 1-based → 0-based conversion (#937).
            try { UnitIdBox.NameText = NameResolver.GetUnitNameByOneBasedId(_vm.UnitId); }
            catch { /* GetUnitNameByOneBasedId returns a fallback and never throws; defensive only */ }
            // #950 T4: Summoned Unit (B1) is also a 1-based unit ID; populate the
            // IdFieldControl value + inline unit-name preview like Summoner (B0).
            UnknownBox.Value = _vm.Unknown;
            try { UnknownBox.NameText = NameResolver.GetUnitNameByOneBasedId(_vm.Unknown); }
            catch { /* GetUnitNameByOneBasedId returns a fallback and never throws; defensive only */ }
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;
            _undoService.Begin("Edit Summon Unit");
            try
            {
                _vm.UnitId = UnitIdBox.Value;
                // #950 T4: IdFieldControl.Value is a non-nullable uint.
                _vm.Unknown = UnknownBox.Value;
                _vm.WriteSummonUnit();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services?.ShowInfo("Summon unit data written.");
            }
            catch (Exception ex) { _undoService.Rollback(); Log.Error("SummonUnitViewerView.Write: {0}", ex.Message); }
        }

        // リストの拡張 — expand the 2-byte summon_unit_pointer table by a prompted
        // count, mirroring WinForms SummonUnitForm.AddressListExpandsEvent (#1605).
        // FE8-only (FE6/FE7 have summon_unit_pointer == 0). The whole expand runs
        // under one UndoService scope with a byte-identical fault restore inside
        // SummonUnitExpandCore.ExpandSummonUnitTable. On a cancel / malformed /
        // zero / same count this is a no-op.
        async void OnExpandListClick(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (CoreState.ROM == null)
                {
                    CoreState.Services?.ShowInfo(R._("Load a ROM first."));
                    return;
                }
                if (CoreState.ROM.RomInfo?.version != 8)
                {
                    CoreState.Services?.ShowInfo(R._("Summon unit table is only available on FE8."));
                    return;
                }

                uint current = (uint)_vm.LoadSummonUnitList().Count;
                if (current == 0)
                {
                    CoreState.Services?.ShowInfo(R._("Cannot expand: the summon list is empty."));
                    return;
                }

                // Max = the Core/editor enumeration cap (SummonUnitExpandCore.MaxRows
                // == 0x100 == 256). Refuse when already at the cap so the dialog
                // never gets an invalid min>max range.
                uint maxCount = SummonUnitExpandCore.MaxRows;
                if (current >= maxCount)
                {
                    CoreState.Services?.ShowInfo(R._("Already at the maximum of {0} entries.", maxCount));
                    return;
                }

                uint defaultCount = current + 1;
                if (defaultCount > maxCount) defaultCount = maxCount;
                uint? chosen = await NumberInputDialog.Show(
                    this,
                    R._("Enter the new summon entry count (current: {0}, max: {1}).", current, maxCount),
                    R._("List Expansion"),
                    defaultCount,
                    current,
                    maxCount);
                if (chosen == null) return; // user cancelled

                uint newCount = chosen.Value;
                if (newCount <= current)
                {
                    CoreState.Services?.ShowInfo(R._("No change: new count must be greater than the current count."));
                    return;
                }
                uint addCount = newCount - current;

                _undoService.Begin("Expand Summon Unit");
                try
                {
                    var result = SummonUnitExpandCore.ExpandSummonUnitTable(
                        CoreState.ROM, addCount, _undoService.GetActiveUndoData(), out string err);
                    if (!result.Success)
                    {
                        _undoService.Rollback();
                        CoreState.Services?.ShowError(string.IsNullOrEmpty(err)
                            ? R._("Summon list expansion failed.") : err);
                        return;
                    }
                    _undoService.Commit();
                    _vm.MarkClean();
                    LoadList();
                    CoreState.Services?.ShowInfo(
                        R._("Expanded summon list to {0} entries.", newCount));
                }
                catch (Exception inner)
                {
                    _undoService.Rollback();
                    Log.Error($"SummonUnitViewerView.OnExpandListClick inner failed: {inner}");
                    CoreState.Services?.ShowError(R._("Summon list expansion failed: {0}", inner.Message));
                }
            }
            catch (Exception ex)
            {
                Log.Error($"SummonUnitViewerView.OnExpandListClick failed: {ex}");
            }
        }

        // -- IdFieldControl handlers (#360) ----------------------------------

        void UnitId_Jump(object? sender, RoutedEventArgs e)
        {
            try
            {
                // UnitId is 1-based; UnitAddrForOneBased applies the (id-1)
                // index + FE6 dummy-entry skip so Jump lands on the right unit (#937).
                uint addr = SupportUnitNavigation.UnitAddrForOneBased(CoreState.ROM, UnitIdBox.Value);
                if (addr == 0) return;
                WindowManager.Instance.Navigate<UnitEditorView>(addr);
            }
            catch (Exception ex) { Log.Error("SummonUnitViewerView.UnitId_Jump failed: {0}", ex.Message); }
        }

        async void UnitId_Pick(object? sender, RoutedEventArgs e)
        {
            try
            {
                uint addr = SupportUnitNavigation.UnitAddrForOneBased(CoreState.ROM, UnitIdBox.Value);
                var result = await WindowManager.Instance.PickFromEditor<UnitEditorView>(addr, this);
                if (result != null)
                {
                    // PickResult.Index is 0-based; UnitId is 1-based (#937).
                    UnitIdBox.Value = SupportUnitNavigation.OneBasedIdFromPickIndex(result.Index);
                }
            }
            catch (Exception ex) { Log.Error("SummonUnitViewerView.UnitId_Pick failed: {0}", ex.Message); }
        }

        void UnitId_ValueChanged(object? sender, IdFieldValueChangedEventArgs e)
        {
            // 1-based Unit ID → name via GetUnitNameByOneBasedId (#937).
            try { UnitIdBox.NameText = NameResolver.GetUnitNameByOneBasedId(e.NewValue); }
            catch { /* GetUnitNameByOneBasedId returns a fallback and never throws; defensive only */ }
        }

        // -- Summoned Unit IdFieldControl handlers (#950 T4) -----------------
        // B1 is a 1-based unit ID, identical handling to Summoner (B0): the
        // 1-based (id-1)+FE6-dummy-skip nav helper for Jump, and the
        // OneBasedIdFromPickIndex conversion on the Pick result.

        void SummonedUnit_Jump(object? sender, RoutedEventArgs e)
        {
            try
            {
                uint addr = SupportUnitNavigation.UnitAddrForOneBased(CoreState.ROM, UnknownBox.Value);
                if (addr == 0) return;
                WindowManager.Instance.Navigate<UnitEditorView>(addr);
            }
            catch (Exception ex) { Log.Error("SummonUnitViewerView.SummonedUnit_Jump failed: {0}", ex.Message); }
        }

        async void SummonedUnit_Pick(object? sender, RoutedEventArgs e)
        {
            try
            {
                uint addr = SupportUnitNavigation.UnitAddrForOneBased(CoreState.ROM, UnknownBox.Value);
                var result = await WindowManager.Instance.PickFromEditor<UnitEditorView>(addr, this);
                if (result != null)
                {
                    UnknownBox.Value = SupportUnitNavigation.OneBasedIdFromPickIndex(result.Index);
                }
            }
            catch (Exception ex) { Log.Error("SummonUnitViewerView.SummonedUnit_Pick failed: {0}", ex.Message); }
        }

        void SummonedUnit_ValueChanged(object? sender, IdFieldValueChangedEventArgs e)
        {
            try { UnknownBox.NameText = NameResolver.GetUnitNameByOneBasedId(e.NewValue); }
            catch { /* GetUnitNameByOneBasedId returns a fallback and never throws; defensive only */ }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
    }
}
