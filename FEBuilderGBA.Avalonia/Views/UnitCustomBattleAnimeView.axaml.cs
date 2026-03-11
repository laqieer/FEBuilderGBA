using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class UnitCustomBattleAnimeView : Window, IEditorView, IDataVerifiableView
    {
        readonly UnitCustomBattleAnimeViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Custom Battle Animation";
        public bool IsLoaded => _vm.IsLoaded;

        public UnitCustomBattleAnimeView()
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
            }
            catch (Exception ex)
            {
                Log.Error("UnitCustomBattleAnimeView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.IsLoading = true;
                _vm.LoadEntry(addr);
                UpdateUI();
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _vm.IsLoading = false;
                Log.Error("UnitCustomBattleAnimeView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            WeaponTypeBox.Value = _vm.WeaponType;
            SpecialBox.Value = _vm.Special;
            AnimeNumberBox.Value = _vm.AnimeNumber;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded) return;

            _vm.WeaponType = (uint)(WeaponTypeBox.Value ?? 0);
            _vm.Special = (uint)(SpecialBox.Value ?? 0);
            _vm.AnimeNumber = (uint)(AnimeNumberBox.Value ?? 0);

            _undoService.Begin("Edit Custom Battle Animation");
            try
            {
                _vm.WriteEntry();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services?.ShowInfo("Custom battle animation data written.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("Write failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
    }
}
