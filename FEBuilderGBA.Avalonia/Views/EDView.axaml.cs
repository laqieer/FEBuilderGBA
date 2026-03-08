using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class EDView : Window, IEditorView, IDataVerifiableView
    {
        readonly EDViewModel _vm = new();

        public string ViewTitle => "Ending Event Editor";
        public bool IsLoaded => _vm.CanWrite;
        public ViewModelBase? DataViewModel => _vm;

        public EDView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadEDList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("EDView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadED(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("EDView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            UnitIdBox.Value = _vm.UnitId;
            FlagBox.Value = _vm.Flag;
            B2Box.Value = _vm.B2;
            B3Box.Value = _vm.B3;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;

            _vm.UnitId = (uint)(UnitIdBox.Value ?? 0);
            _vm.Flag = (uint)(FlagBox.Value ?? 0);
            _vm.B2 = (uint)(B2Box.Value ?? 0);
            _vm.B3 = (uint)(B3Box.Value ?? 0);
            _vm.WriteED();
            CoreState.Services?.ShowInfo("Ending event data written.");
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
