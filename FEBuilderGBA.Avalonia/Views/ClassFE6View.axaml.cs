using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ClassFE6View : Window, IPickableEditor, IDataVerifiableView
    {
        readonly ClassFE6ViewModel _vm = new();

        public string ViewTitle => "Class Editor (FE6)";
        public bool IsLoaded => _vm.IsLoaded;

        public event Action<PickResult>? SelectionConfirmed;

        public ClassFE6View()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            EntryList.SelectionConfirmed += result => SelectionConfirmed?.Invoke(result);
            Opened += (_, _) => LoadList();

            // Auto-recalculate growth sim when SimLevel changes
            SimLevelBox.ValueChanged += OnSimLevelChanged;
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
                Log.Error("ClassFE6View.LoadList failed: {0}", ex.Message);
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
                Log.Error("ClassFE6View.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = string.Format("0x{0:X08}", _vm.CurrentAddr);

            // Auto-calculate growth on entry load
            SimLevelBox.Value = _vm.SimLevel;
            _vm.CalculateGrowth();
            GrowthSimLabel.Text = _vm.GrowthSimText;
        }

        void CalculateGrowth_Click(object? sender, RoutedEventArgs e)
        {
            _vm.SimLevel = (uint)(SimLevelBox.Value ?? 20);
            _vm.CalculateGrowth();
            GrowthSimLabel.Text = _vm.GrowthSimText;
        }

        void OnSimLevelChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            if (_vm.IsLoading || !_vm.CanWrite) return;
            _vm.SimLevel = (uint)(SimLevelBox.Value ?? 20);
            _vm.CalculateGrowth();
            GrowthSimLabel.Text = _vm.GrowthSimText;
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public void EnablePickMode() => EntryList.EnablePickMode();

        public ViewModelBase? DataViewModel => _vm;
    }
}
