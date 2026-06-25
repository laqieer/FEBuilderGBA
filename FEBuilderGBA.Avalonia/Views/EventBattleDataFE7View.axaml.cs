using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class EventBattleDataFE7View : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly EventBattleDataFE7ViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Battle Data (FE7)";
        public bool IsLoaded => _vm.IsLoaded;

        public EventBattleDataFE7View()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            WriteButton.Click += OnWrite;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("EventBattleDataFE7View.LoadList failed: {0}", ex.Message);
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
                Log.Error("EventBattleDataFE7View.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = string.Format("0x{0:X08}", _vm.CurrentAddr);
            AttackTypeUpDown.Value = _vm.AttackType;
            AttackerUpDown.Value = _vm.Attacker;
            DamageUpDown.Value = _vm.Damage;
        }

        void OnWrite(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded) return;

            _undoService.Begin(R._("Edit Battle Data (FE7)"));
            try
            {
                _vm.AttackType = (uint)(AttackTypeUpDown.Value ?? 0);
                _vm.Attacker = (uint)(AttackerUpDown.Value ?? 0);
                _vm.Damage = (uint)(DamageUpDown.Value ?? 0);
                _vm.Write();
                _undoService.Commit();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("EventBattleDataFE7View.OnWrite failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
    }
}
