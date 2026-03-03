using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class OPClassDemoViewerView : Window, IEditorView
    {
        readonly OPClassDemoViewerViewModel _vm = new();

        public string ViewTitle => "OP Class Demo Viewer";
        public bool IsLoaded => _vm.IsLoaded;

        public OPClassDemoViewerView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try { var items = _vm.LoadOPClassDemoList(); EntryList.SetItems(items); }
            catch (Exception ex) { Log.Error("OPClassDemoViewerView.LoadList: {0}", ex.Message); }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadOPClassDemo(addr);
                UpdateUI();
            }
            catch (Exception ex) { Log.Error("OPClassDemoViewerView.OnSelected: {0}", ex.Message); }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            ClassIdLabel.Text = $"0x{_vm.ClassId:X02} ({_vm.ClassId})";
            AnimTypeLabel.Text = $"0x{_vm.AnimationType:X02} ({_vm.AnimationType})";
            BattleAnimeLabel.Text = $"0x{_vm.BattleAnime:X02} ({_vm.BattleAnime})";
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
