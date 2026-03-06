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
            DifficultyValueInput.Value = u16Difficulty & 0xffff;
            HardBoostInput.Value = (u16Difficulty & 0xf0) >> 4;
            EasyPenaltyInput.Value = u16Difficulty & 0x0f;
            NormalPenaltyInput.Value = (u16Difficulty & 0x0f00) >> 8;
        }

        public uint GetDifficultyValue()
        {
            return (uint)(DifficultyValueInput.Value ?? 0);
        }

        void OnBoostChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            uint hard = (uint)(HardBoostInput.Value ?? 0);
            uint easy = (uint)(EasyPenaltyInput.Value ?? 0);
            uint normal = (uint)(NormalPenaltyInput.Value ?? 0);
            DifficultyValueInput.Value = (hard << 4) | easy | (normal << 8);
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
