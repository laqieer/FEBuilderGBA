using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SupportTalkFE7View : Window, IEditorView, IDataVerifiableView
    {
        readonly SupportTalkFE7ViewModel _vm = new();

        public string ViewTitle => "Support Talk (FE7)";
        public bool IsLoaded => _vm.IsLoaded;

        public SupportTalkFE7View()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            WriteButton.Click += Write_Click;
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
                Log.Error("SupportTalkFE7View.LoadList failed: {0}", ex.Message);
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
                Log.Error("SupportTalkFE7View.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            Unit1IdNud.Value = _vm.Unit1Id;
            Unit2IdNud.Value = _vm.Unit2Id;
            TextCNud.Value = _vm.TextC;
            TextBNud.Value = _vm.TextB;
            TextANud.Value = _vm.TextA;
            B16Nud.Value = _vm.B16;
            B17Nud.Value = _vm.B17;
            B18Nud.Value = _vm.B18;
            B19Nud.Value = _vm.B19;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                _vm.Unit1Id = (uint)(Unit1IdNud.Value ?? 0);
                _vm.Unit2Id = (uint)(Unit2IdNud.Value ?? 0);
                _vm.TextC = (uint)(TextCNud.Value ?? 0);
                _vm.TextB = (uint)(TextBNud.Value ?? 0);
                _vm.TextA = (uint)(TextANud.Value ?? 0);
                _vm.B16 = (uint)(B16Nud.Value ?? 0);
                _vm.B17 = (uint)(B17Nud.Value ?? 0);
                _vm.B18 = (uint)(B18Nud.Value ?? 0);
                _vm.B19 = (uint)(B19Nud.Value ?? 0);

                _vm.WriteSupportTalk();
            }
            catch (Exception ex)
            {
                Log.Error("SupportTalkFE7View.Write_Click failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
    }
}
