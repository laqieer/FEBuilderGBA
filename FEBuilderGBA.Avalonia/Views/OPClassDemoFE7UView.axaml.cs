using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class OPClassDemoFE7UView : Window, IEditorView, IDataVerifiableView
    {
        readonly OPClassDemoFE7UViewModel _vm = new();

        public string ViewTitle => "OP Class Demo (FE7U) Editor";
        public bool IsLoaded => _vm.CanWrite;
        public ViewModelBase? DataViewModel => _vm;

        public OPClassDemoFE7UView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadList();
                EntryList.SetItems(items);
                if (!string.IsNullOrEmpty(_vm.UnavailableMessage))
                    UnavailableLabel.Text = _vm.UnavailableMessage;
            }
            catch (Exception ex)
            {
                Log.Error("OPClassDemoFE7UView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadEntry(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("OPClassDemoFE7UView.OnSelected failed: {0}", ex.Message);
            }
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
            _vm.B11 = (uint)(ClassIdBox.Value ?? 0);
            _vm.B12 = (uint)(AnimTypeBox.Value ?? 0);
            _vm.B13 = (uint)(BattleAnimeBox.Value ?? 0);
            _vm.WriteEntry();
            CoreState.Services?.ShowInfo("OP Class Demo (FE7U) data written.");
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
