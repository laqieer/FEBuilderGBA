using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SupportTalkView : Window, IEditorView, IDataVerifiableView
    {
        readonly SupportTalkViewModel _vm = new();

        public string ViewTitle => "Support Talk";
        public bool IsLoaded => _vm.IsLoaded;

        public SupportTalkView()
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
                Log.Error("SupportTalkView.LoadList failed: {0}", ex.Message);
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
                Log.Error("SupportTalkView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            UnitId1Nud.Value = _vm.UnitId1;
            UnitId2Nud.Value = _vm.UnitId2;
            TextIdCNud.Value = _vm.TextIdC;
            TextIdBNud.Value = _vm.TextIdB;
            TextIdANud.Value = _vm.TextIdA;
            W10Nud.Value = _vm.W10;
            W12Nud.Value = _vm.W12;
            W14Nud.Value = _vm.W14;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                _vm.UnitId1 = (uint)(UnitId1Nud.Value ?? 0);
                _vm.UnitId2 = (uint)(UnitId2Nud.Value ?? 0);
                _vm.TextIdC = (uint)(TextIdCNud.Value ?? 0);
                _vm.TextIdB = (uint)(TextIdBNud.Value ?? 0);
                _vm.TextIdA = (uint)(TextIdANud.Value ?? 0);
                _vm.W10 = (uint)(W10Nud.Value ?? 0);
                _vm.W12 = (uint)(W12Nud.Value ?? 0);
                _vm.W14 = (uint)(W14Nud.Value ?? 0);

                _vm.WriteSupportTalk();
            }
            catch (Exception ex)
            {
                Log.Error("SupportTalkView.Write_Click failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address)
        {
            EntryList.SelectAddress(address);
        }

        public void SelectFirstItem()
        {
            EntryList.SelectFirst();
        }

        public ViewModelBase? DataViewModel => _vm;
    }
}
