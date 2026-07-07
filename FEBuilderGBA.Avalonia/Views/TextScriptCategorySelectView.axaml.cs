using System;
using global::Avalonia;
using System.Collections.Generic;
using System.Linq;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class TextScriptCategorySelectView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly TextScriptCategorySelectViewModel _vm = new();

        // The escape entries currently shown in the right pane, parallel to the
        // EscapeList items (so the selected index maps back to a TextEscapeEntry).
        List<FEBuilderGBA.TextEscapeEntry> _shownEntries = new();

        public string ViewTitle => "Text Script Category Select";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Text Script Category Select", 700, 500);
        public event EventHandler? CloseRequested;
        public object? DialogResult { get; private set; }
        public ViewModelBase? DataViewModel => _vm;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public TextScriptCategorySelectView()
        {
            InitializeComponent();
            // Default to detail mode so every category is shown. Hosts that want
            // the non-detail subset call Init(false) BEFORE ShowDialog.
            _vm.Init(true);
            CategoryList.ItemsSource = _vm.Categories;
            if (_vm.Categories.Count > 0)
                CategoryList.SelectedIndex = 0;
        }

        /// <summary>
        /// Re-load the category/escape tables in the requested detail mode. When
        /// <paramref name="isDetail"/> is false the move/load + position
        /// categories/escapes are filtered out (WF parity).
        /// </summary>
        public void Init(bool isDetail)
        {
            _vm.Init(isDetail);
            CategoryList.ItemsSource = _vm.Categories;
            if (_vm.Categories.Count > 0)
                CategoryList.SelectedIndex = 0;
        }

        void CategoryList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            string key = _vm.GetCategoryKeyForIndex(CategoryList.SelectedIndex);
            _vm.SelectedCategory = CategoryList.SelectedItem as string ?? "";
            _shownEntries = _vm.GetEntriesForCategory(key);
            // Show "Code  Info" so the user sees both the @XXXX code and its
            // description; OK returns only the Code.
            EscapeList.ItemsSource = _shownEntries
                .Select(en => string.IsNullOrEmpty(en.Info) ? en.Code : en.Code + "  " + en.Info)
                .ToList();
            if (_shownEntries.Count > 0)
                EscapeList.SelectedIndex = 0;
        }

        string? ResolveSelectedCode()
        {
            int idx = EscapeList.SelectedIndex;
            if (idx < 0 || idx >= _shownEntries.Count)
                return null;
            return _shownEntries[idx].Code;
        }

        void EscapeList_DoubleTapped(object? sender, TappedEventArgs e)
        {
            string? code = ResolveSelectedCode();
            if (code == null) return;
            _vm.SelectedCode = code;
            DialogResult = code; RequestClose();
        }

        void OK_Click(object? sender, RoutedEventArgs e)
        {
            string? code = ResolveSelectedCode();
            _vm.SelectedCode = code;
            DialogResult = code; RequestClose();
        }

        void Cancel_Click(object? sender, RoutedEventArgs e) { DialogResult = null; RequestClose(); }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { if (_vm.Categories.Count > 0) CategoryList.SelectedIndex = 0; }
    }
}
