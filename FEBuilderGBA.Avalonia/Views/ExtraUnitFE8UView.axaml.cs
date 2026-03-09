using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ExtraUnitFE8UView : Window, IEditorView, IDataVerifiableView
    {
        readonly ExtraUnitFE8UViewModel _vm = new();

        public string ViewTitle => "Extra Unit (FE8U)";
        public bool IsLoaded => _vm.IsLoaded;

        public ExtraUnitFE8UView()
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
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("ExtraUnitFE8UView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadEntry(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("ExtraUnitFE8UView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            FlagIdBox.Text = $"0x{_vm.FlagId:X08}";
            UnitInfoPtrBox.Text = $"0x{_vm.UnitInfoPtr:X08}";
        }

        void ReadFromUI()
        {
            _vm.FlagId = U.atoh(FlagIdBox.Text ?? "");
            _vm.UnitInfoPtr = U.atoh(UnitInfoPtrBox.Text ?? "");
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                ReadFromUI();
                _vm.WriteEntry();
                CoreState.Services?.ShowInfo("Extra unit data written.");
            }
            catch (Exception ex)
            {
                Log.Error("ExtraUnitFE8UView.Write failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
    }
}
