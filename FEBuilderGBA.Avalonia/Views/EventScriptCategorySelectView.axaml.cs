using global::Avalonia;
using System;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class EventScriptCategorySelectView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly EventScriptCategorySelectViewModel _vm = new();

        public string ViewTitle => "Event Script Category Select";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Event Script Category Select", 1000, 700);
        public event EventHandler? CloseRequested;
        public object? DialogResult { get; private set; }
        public ViewModelBase? DataViewModel => _vm;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        // R._() runs the runtime translation (TranslatedWindow's pass only covers
        // AXAML literals, not strings assigned later in code-behind) — Copilot PR
        // review #1525.
        static string Placeholder => R._("(select a command)");

        public EventScriptCategorySelectView()
        {
            InitializeComponent();
            _vm.Load();

            CategoryList.ItemsSource = _vm.Categories;
            ScriptList.ItemsSource = _vm.ScriptNames;
            InfoLabel.Text = Placeholder;

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

        // The VM rebuilds ScriptNames into a fresh list on each filter change
        // (and resets its own selection/info); rebind the ItemsSource and reset
        // the info panel so the view stays in sync.
        void RebindScriptList()
        {
            ScriptList.ItemsSource = _vm.ScriptNames;
            ScriptList.SelectedIndex = -1;
            _vm.SelectedScriptIndex = -1;
            InfoLabel.Text = Placeholder;
        }

        void ScriptList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            _vm.SelectedScriptIndex = ScriptList.SelectedIndex;
            InfoLabel.Text = string.IsNullOrEmpty(_vm.InfoText) ? Placeholder : _vm.InfoText;
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
            {
                DialogResult = _vm.SelectedScript; RequestClose();
            }
            else
            {
                // No valid command selected — surface an inline hint and stay open
                // so OK doesn't look broken (parity with ScriptCommandPickerView).
                InfoLabel.Text = R._("Select a command from the list first.");
            }
        }

        void Cancel_Click(object? sender, RoutedEventArgs e)
        {
            DialogResult = null; RequestClose();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { if (_vm.Categories.Count > 0) CategoryList.SelectedIndex = 0; }
    }
}
