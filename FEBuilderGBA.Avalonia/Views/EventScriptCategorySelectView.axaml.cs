using System;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class EventScriptCategorySelectView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly EventScriptCategorySelectViewModel _vm = new();

        public string ViewTitle => "Event Script Category Select";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public EventScriptCategorySelectView()
        {
            InitializeComponent();
            _vm.Load();

            CategoryList.ItemsSource = _vm.Categories;
            ScriptList.ItemsSource = _vm.ScriptNames;

            FilterBox.Text = _vm.FilterText;
            FilterBox.TextChanged += FilterBox_TextChanged;
            ShowLowCheck.IsChecked = _vm.ShowLowCommand;
            ShowLowCheck.IsCheckedChanged += ShowLowCheck_Changed;

            if (_vm.Categories.Count > 0)
                CategoryList.SelectedIndex = 0;
        }

        void CategoryList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            _vm.SelectedCategory = CategoryList.SelectedItem as string ?? "";
            RebindScriptList();
        }

        void FilterBox_TextChanged(object? sender, TextChangedEventArgs e)
        {
            _vm.FilterText = FilterBox.Text ?? "";
            RebindScriptList();
        }

        void ShowLowCheck_Changed(object? sender, RoutedEventArgs e)
        {
            _vm.ShowLowCommand = ShowLowCheck.IsChecked == true;
            RebindScriptList();
        }

        // The VM rebuilds ScriptNames into a fresh list on each filter change;
        // rebind the ItemsSource and clear selection so the info panel resets.
        void RebindScriptList()
        {
            ScriptList.ItemsSource = _vm.ScriptNames;
            ScriptList.SelectedIndex = -1;
            _vm.SelectedScriptIndex = -1;
            InfoLabel.Text = "(select a command)";
        }

        void ScriptList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            _vm.SelectedScriptIndex = ScriptList.SelectedIndex;
            InfoLabel.Text = string.IsNullOrEmpty(_vm.InfoText) ? "(select a command)" : _vm.InfoText;
        }

        void ScriptList_DoubleTapped(object? sender, TappedEventArgs e)
        {
            ConfirmAndClose();
        }

        void OK_Click(object? sender, RoutedEventArgs e)
        {
            ConfirmAndClose();
        }

        void ConfirmAndClose()
        {
            if (_vm.ConfirmSelection())
                Close(_vm.SelectedScript);
            // No valid command selected — keep the dialog open.
        }

        void Cancel_Click(object? sender, RoutedEventArgs e)
        {
            Close(null);
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { if (_vm.Categories.Count > 0) CategoryList.SelectedIndex = 0; }
    }
}
