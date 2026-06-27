using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ExtraUnitView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly ExtraUnitViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Extra Unit Editor";
        public bool IsLoaded => _vm.IsLoaded;

        public ExtraUnitView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadList();
                EntryList.SetItemsWithIcons(items, i => ListIconLoaders.UnitPortraitLoader(items, i));
            }
            catch (Exception ex)
            {
                Log.ErrorF("ExtraUnitView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
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
                Log.ErrorF("ExtraUnitView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = string.Format("0x{0:X08}", _vm.CurrentAddr);
            FlagIdBox.Text = string.Format("0x{0:X02}", _vm.FlagId);
            P0Box.Text = string.Format("0x{0:X08}", _vm.P0);
        }

        void ReadFromUI()
        {
            // The FLAG byte is the only editable field; P0 is read-only display.
            // Clamp to a single byte at parse time so the VM state matches what
            // WriteEntry actually persists (WriteEntry masks with & 0xFF).
            _vm.FlagId = U.atoh(FlagIdBox.Text ?? "") & 0xFF;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            ReadFromUI();
            _undoService.Begin("Edit Extra Unit");
            try
            {
                _vm.WriteEntry();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services?.ShowInfo("Extra unit data written.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("ExtraUnitView.Write failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
    }
}
