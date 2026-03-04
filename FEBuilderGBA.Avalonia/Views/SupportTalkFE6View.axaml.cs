using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SupportTalkFE6View : Window, IEditorView, IDataVerifiableView
    {
        readonly SupportTalkFE6ViewModel _vm = new();

        public string ViewTitle => "Support Talk (FE6)";
        public bool IsLoaded => _vm.IsLoaded;

        public SupportTalkFE6View()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadSupportTalkList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("SupportTalkFE6View.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadSupportTalk(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("SupportTalkFE6View.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            Unit1IdLabel.Text = $"0x{_vm.Unit1Id:X02} ({_vm.Unit1Id})";
            Unit2IdLabel.Text = $"0x{_vm.Unit2Id:X02} ({_vm.Unit2Id})";
            TextCLabel.Text = $"0x{_vm.TextC:X08}";
            TextBLabel.Text = $"0x{_vm.TextB:X08}";
            TextALabel.Text = $"0x{_vm.TextA:X08}";
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
    }
}
