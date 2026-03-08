using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class OPClassDemoViewerView : Window, IEditorView, IDataVerifiableView
    {
        readonly OPClassDemoViewerViewModel _vm = new();

        public string ViewTitle => "OP Class Demo Editor";
        public bool IsLoaded => _vm.CanWrite;
        public ViewModelBase? DataViewModel => _vm;

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
            ClassIdBox.Value = _vm.ClassId;
            AnimTypeBox.Value = _vm.AnimationType;
            BattleAnimeBox.Value = _vm.BattleAnime;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;
            _vm.B14 = (uint)(ClassIdBox.Value ?? 0);
            _vm.B15 = (uint)(AnimTypeBox.Value ?? 0);
            _vm.B16 = (uint)(BattleAnimeBox.Value ?? 0);
            _vm.WriteOPClassDemo();
            CoreState.Services?.ShowInfo("OP Class Demo data written.");
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
