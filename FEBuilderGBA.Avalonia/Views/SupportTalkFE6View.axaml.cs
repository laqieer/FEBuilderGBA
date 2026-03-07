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
            Unit1IdNud.Value = _vm.Unit1Id;
            Unit2IdNud.Value = _vm.Unit2Id;
            TextCNud.Value = _vm.TextC;
            TextBNud.Value = _vm.TextB;
            TextANud.Value = _vm.TextA;
            B14Nud.Value = _vm.B14;
            B15Nud.Value = _vm.B15;
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
                _vm.B14 = (uint)(B14Nud.Value ?? 0);
                _vm.B15 = (uint)(B15Nud.Value ?? 0);

                _vm.WriteSupportTalk();
            }
            catch (Exception ex)
            {
                Log.Error("SupportTalkFE6View.Write_Click failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
    }
}
