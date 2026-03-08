using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MonsterWMapProbabilityViewerView : Window, IEditorView, IDataVerifiableView
    {
        readonly MonsterWMapProbabilityViewerViewModel _vm = new();

        public string ViewTitle => "World Map Monster";
        public bool IsLoaded => _vm.CanWrite;

        public MonsterWMapProbabilityViewerView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadMonsterWMapProbabilityList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("MonsterWMapProbabilityViewerView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadMonsterWMapProbability(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("MonsterWMapProbabilityViewerView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            BasePointIdBox.Value = _vm.BasePointId;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;

            _vm.BasePointId = (uint)(BasePointIdBox.Value ?? 0);
            _vm.WriteMonsterWMapProbability();
            CoreState.Services.ShowInfo("World map monster data written.");
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
    }
}
