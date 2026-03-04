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

        public string ViewTitle => "Map Setting - Difficulty";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public MapSettingDifficultyDialogView()
        {
            InitializeComponent();
            _vm.Initialize();
        }

        void OK_Click(object? sender, RoutedEventArgs e)
        {
            _vm.DifficultyLevel = (uint)(DifficultyLevelInput.Value ?? 0);
            _vm.EnemyLevelBonus = (uint)(EnemyLevelBonusInput.Value ?? 0);
            _vm.HardModeEnabled = HardModeCheckBox.IsChecked == true;
            Close(new { _vm.DifficultyLevel, _vm.EnemyLevelBonus, _vm.HardModeEnabled });
        }

        void Cancel_Click(object? sender, RoutedEventArgs e) => Close(null);

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
