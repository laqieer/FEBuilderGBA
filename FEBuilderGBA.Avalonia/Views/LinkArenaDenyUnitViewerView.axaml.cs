using System;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Core;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class LinkArenaDenyUnitViewerView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly LinkArenaDenyUnitViewerViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Link Arena Deny Unit";
        public bool IsLoaded => _vm.CanWrite;

        public LinkArenaDenyUnitViewerView()
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
                var items = _vm.LoadLinkArenaDenyUnitList();
                EntryList.SetItemsWithIcons(items, i => ListIconLoaders.UnitPortraitByIdLoader(items, i));
            }
            catch (Exception ex)
            {
                Log.Error("LinkArenaDenyUnitViewerView.LoadList failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void OnSelected(uint addr)
        {
            _vm.IsLoading = true;
            try
            {
                _vm.LoadLinkArenaDenyUnit(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("LinkArenaDenyUnitViewerView.OnSelected failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            UnitIdBox.Value = _vm.UnitId;
            try { UnitIdBox.NameText = SupportUnitNavigation.ResolveUnitTableName(CoreState.ROM, _vm.UnitId); }
            catch { /* SupportUnitNavigation may fail without ROM — leave prior text */ }
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;
            _undoService.Begin("Edit Link Arena Deny Unit");
            try
            {
                _vm.UnitId = UnitIdBox.Value;
                _vm.WriteLinkArenaDenyUnit();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services?.ShowInfo("Link Arena Deny Unit data written.");
            }
            catch (Exception ex) { _undoService.Rollback(); Log.Error("LinkArenaDenyUnitViewerView.Write: {0}", ex.Message); }
        }

        // -- IdFieldControl handlers (#360) ----------------------------------

        /// <summary>
        /// Compute the UnitEditor view's ROM entry address for a given unit id.
        /// Preserves the FE6 dummy-entry skip (`baseAddr += dataSize`) that
        /// the original OnUnitIdLinkClick used. Returns 0 when ROM is
        /// unavailable or the entry would fall outside ROM bounds.
        /// </summary>
        static uint UnitAddrFor(uint unitId)
        {
            var rom = CoreState.ROM;
            if (rom?.RomInfo == null) return 0;
            uint unitPtr = rom.RomInfo.unit_pointer;
            if (unitPtr == 0) return 0;
            uint baseAddr = rom.p32(unitPtr);
            if (!U.isSafetyOffset(baseAddr, rom)) return 0;
            uint dataSize = rom.RomInfo.unit_datasize;
            if (dataSize == 0) return 0;
            if (rom.RomInfo.version == 6) baseAddr += dataSize;
            uint entryAddr = baseAddr + unitId * dataSize;
            if (!U.isSafetyOffset(entryAddr, rom)) return 0;
            if (!U.isSafetyOffset(entryAddr + dataSize - 1, rom)) return 0;
            return entryAddr;
        }

        void UnitId_Jump(object? sender, RoutedEventArgs e)
        {
            try
            {
                uint addr = UnitAddrFor(UnitIdBox.Value);
                if (addr == 0) return;
                WindowManager.Instance.Navigate<UnitEditorView>(addr);
            }
            catch (Exception ex) { Log.Error("LinkArenaDenyUnitViewerView.UnitId_Jump failed: {0}", ex.Message); }
        }

        async void UnitId_Pick(object? sender, RoutedEventArgs e)
        {
            try
            {
                uint addr = UnitAddrFor(UnitIdBox.Value);
                var result = await WindowManager.Instance.PickFromEditor<UnitEditorView>(addr, this);
                if (result != null)
                {
                    UnitIdBox.Value = (uint)result.Index;
                }
            }
            catch (Exception ex) { Log.Error("LinkArenaDenyUnitViewerView.UnitId_Pick failed: {0}", ex.Message); }
        }

        void UnitId_ValueChanged(object? sender, IdFieldValueChangedEventArgs e)
        {
            try { UnitIdBox.NameText = SupportUnitNavigation.ResolveUnitTableName(CoreState.ROM, e.NewValue); }
            catch { /* SupportUnitNavigation may fail without ROM — leave prior text */ }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
    }
}
