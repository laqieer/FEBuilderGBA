using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MapSettingDifficultyDialogView : Window, IEditorView, IDataVerifiableView
    {
        readonly MapSettingDifficultyDialogViewModel _vm = new();

        public string ViewTitle => "Difficulty Settings";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public MapSettingDifficultyDialogView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.Initialize();
            HardBoostInput.ValueChanged += OnBoostChanged;
            NormalPenaltyInput.ValueChanged += OnBoostChanged;
            EasyPenaltyInput.ValueChanged += OnBoostChanged;
        }

        public void SetDifficultyValue(uint u16Difficulty)
        {
            _vm.LoadFromValue(u16Difficulty);
            DifficultyValueInput.Value = u16Difficulty & 0xffff;
            HardBoostInput.Value = _vm.HardBoost;
            NormalPenaltyInput.Value = _vm.NormalPenalty;
            EasyPenaltyInput.Value = _vm.EasyPenalty;
        }

        public uint GetDifficultyValue()
        {
            return _vm.DifficultyValue;
        }

        void OnBoostChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            _vm.HardBoost = (int)(HardBoostInput.Value ?? 0);
            _vm.NormalPenalty = (int)(NormalPenaltyInput.Value ?? 0);
            _vm.EasyPenalty = (int)(EasyPenaltyInput.Value ?? 0);
            DifficultyValueInput.Value = _vm.DifficultyValue;
        }

        void OK_Click(object? sender, RoutedEventArgs e)
        {
            _vm.DifficultyValue = (uint)(DifficultyValueInput.Value ?? 0);
            _vm.DialogResult = "OK";
            Close(_vm.DifficultyValue);
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
