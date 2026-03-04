using System;
using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class StatusParamView : Window, IEditorView, IDataVerifiableView
    {
        readonly StatusParamViewModel _vm = new();

        public string ViewTitle => "Status Parameters";
        public bool IsLoaded => _vm.IsLoaded;
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
            Data0Label.Text = $"0x{_vm.Data0:X08}";
            Data4Label.Text = $"0x{_vm.Data4:X08}";
            ColorTypeLabel.Text = $"0x{_vm.ColorType:X02} ({_vm.ColorType})";
            NamePointerLabel.Text = $"0x{_vm.NamePointer:X08}";
            NameLabel.Text = _vm.NameText;
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
