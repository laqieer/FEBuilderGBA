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
            SupportPartner1Nud.Value = _vm.SupportPartner1;
            SupportPartner2Nud.Value = _vm.SupportPartner2;
            TextCNud.Value = _vm.TextC;
            TextBNud.Value = _vm.TextB;
            TextANud.Value = _vm.TextA;
            SongCNud.Value = _vm.SongC;
            SongBNud.Value = _vm.SongB;
            SongANud.Value = _vm.SongA;
            PaddingNud.Value = _vm.Padding;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                _vm.SupportPartner1 = (uint)(SupportPartner1Nud.Value ?? 0);
                _vm.SupportPartner2 = (uint)(SupportPartner2Nud.Value ?? 0);
                _vm.TextC = (uint)(TextCNud.Value ?? 0);
                _vm.TextB = (uint)(TextBNud.Value ?? 0);
                _vm.TextA = (uint)(TextANud.Value ?? 0);
                _vm.SongC = (uint)(SongCNud.Value ?? 0);
                _vm.SongB = (uint)(SongBNud.Value ?? 0);
                _vm.SongA = (uint)(SongANud.Value ?? 0);
                _vm.Padding = (uint)(PaddingNud.Value ?? 0);

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
