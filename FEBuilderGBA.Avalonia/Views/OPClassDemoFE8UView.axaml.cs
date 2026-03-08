using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class OPClassDemoFE8UView : Window, IEditorView, IDataVerifiableView
    {
        readonly OPClassDemoFE8UViewModel _vm = new();

        public string ViewTitle => "OP Class Demo (FE8U) Editor";
        public bool IsLoaded => _vm.CanWrite;
        public ViewModelBase? DataViewModel => _vm;

        public OPClassDemoFE8UView()
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
                Log.Error("OPClassDemoFE8UView.LoadList failed: {0}", ex.Message);
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
                Log.Error("OPClassDemoFE8UView.OnSelected failed: {0}", ex.Message);
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
            _vm.B5 = (uint)(ClassIdBox.Value ?? 0);
            _vm.B6 = (uint)(AnimTypeBox.Value ?? 0);
            _vm.B7 = (uint)(BattleAnimeBox.Value ?? 0);
            _vm.WriteEntry();
            CoreState.Services?.ShowInfo("OP Class Demo (FE8U) data written.");
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
