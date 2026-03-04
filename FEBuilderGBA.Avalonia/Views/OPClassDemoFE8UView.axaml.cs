using System;
using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class OPClassDemoFE8UView : Window, IEditorView, IDataVerifiableView
    {
        readonly OPClassDemoFE8UViewModel _vm = new();

        public string ViewTitle => "OP Class Demo (FE8U)";
        public bool IsLoaded => _vm.IsLoaded;
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
            ClassIdLabel.Text = $"0x{_vm.ClassId:X02} ({_vm.ClassId})";
            AnimTypeLabel.Text = $"0x{_vm.AnimationType:X02} ({_vm.AnimationType})";
            BattleAnimeLabel.Text = $"0x{_vm.BattleAnime:X02} ({_vm.BattleAnime})";
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
