using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class StatusParamView : Window, IEditorView, IDataVerifiableView
    {
        readonly StatusParamViewModel _vm = new();

        public string ViewTitle => "Status Parameters";
        public bool IsLoaded => _vm.CanWrite;
        public ViewModelBase? DataViewModel => _vm;

        public StatusParamView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadStatusParamList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("StatusParamView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadStatusParam(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("StatusParamView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            Data0Box.Value = _vm.Data0;
            Data4Box.Value = _vm.Data4;
            ColorTypeBox.Value = _vm.ColorType;
            B9Box.Value = _vm.B9;
            B10Box.Value = _vm.B10;
            B11Box.Value = _vm.B11;
            NamePointerBox.Value = _vm.NamePointer;
            NameLabel.Text = _vm.NameText;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;

            _vm.Data0 = (uint)(Data0Box.Value ?? 0);
            _vm.Data4 = (uint)(Data4Box.Value ?? 0);
            _vm.ColorType = (uint)(ColorTypeBox.Value ?? 0);
            _vm.B9 = (uint)(B9Box.Value ?? 0);
            _vm.B10 = (uint)(B10Box.Value ?? 0);
            _vm.B11 = (uint)(B11Box.Value ?? 0);
            _vm.NamePointer = (uint)(NamePointerBox.Value ?? 0);
            _vm.WriteStatusParam();
            CoreState.Services?.ShowInfo("Status parameter data written.");
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
