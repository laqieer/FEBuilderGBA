using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ExtraUnitFE8UView : Window, IEditorView
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
            AddrLabel.Text = string.Format("0x{0:X08}", _vm.CurrentAddr);
            D0Box.Text = string.Format("0x{0:X08}", _vm.D0);
            P4Box.Text = string.Format("0x{0:X08}", _vm.P4);
        }

        void ReadFromUI()
        {
            _vm.D0 = U.atoh(D0Box.Text ?? "");
            _vm.P4 = U.atoh(P4Box.Text ?? "");
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                ReadFromUI();
                _vm.WriteEntry();
            }
            catch (Exception ex)
            {
                Log.Error("ExtraUnitFE8UView.Write failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
