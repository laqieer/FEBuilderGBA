using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.ViewModels;

using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.Views
{
    /// <summary>
    /// Modal dialog for picking a script command from categories.
    /// Works for Event, Procs, and AI script types.
    /// </summary>
    public partial class ScriptCommandPickerView : TranslatedUserControl, IEmbeddableEditor
    {
        public string ViewTitle => "Select Script Command";
        public new bool IsLoaded => true;
        public EditorDescriptor Descriptor => new("Select Script Command", 800, 600, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight);
        public event EventHandler? CloseRequested;
        public object? DialogResult { get; private set; }
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);
        public void NavigateTo(uint address) { }

        EventScript.EventScriptType _scriptType;
        EventScript.Script? _selectedScript;

        // For AI scripts, use the AI category VM; for Procs, the Procs VM.
        // We use separate VMs to handle category loading correctly.
        AIScriptCategorySelectViewModel? _aiVm;
        ProcsScriptCategorySelectViewModel? _procsVm;

        public ScriptCommandPickerView() : this(EventScript.EventScriptType.AI) { }

        public ScriptCommandPickerView(EventScript.EventScriptType scriptType)
        {
            _scriptType = scriptType;
            InitializeComponent();
            FilterBox.TextChanged += FilterBox_TextChanged;
            Configure(scriptType);
        }

        public void Configure(EventScript.EventScriptType scriptType)
        {
            _scriptType = scriptType;
            _selectedScript = null;
            DialogResult = null;
            _aiVm = null;
            _procsVm = null;
            DataContext = null;
            CategoryList.ItemsSource = null;
            ScriptList.ItemsSource = null;
            CategoryList.SelectedIndex = -1;
            ScriptList.SelectedIndex = -1;
            FilterBox.Text = "";
            InfoLabel.Text = "";

            if (scriptType == EventScript.EventScriptType.AI)
            {
                _aiVm = new AIScriptCategorySelectViewModel();
                DataContext = _aiVm;
                _aiVm.Load();
                CategoryList.ItemsSource = _aiVm.Categories;
                ScriptList.ItemsSource = _aiVm.ScriptNames;
                if (_aiVm.Categories.Count > 0)
                    CategoryList.SelectedIndex = 0;
            }
            else
            {
                _procsVm = new ProcsScriptCategorySelectViewModel();
                DataContext = _procsVm;
                _procsVm.Load();
                CategoryList.ItemsSource = _procsVm.Categories;
                ScriptList.ItemsSource = _procsVm.ScriptNames;
                if (_procsVm.Categories.Count > 0)
                    CategoryList.SelectedIndex = 0;
            }
        }

        /// <summary>The selected script command, or null if cancelled.</summary>
        public EventScript.Script? SelectedScript => _selectedScript;

        void CategoryList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            string cat = CategoryList.SelectedItem as string ?? "";
            if (_aiVm != null)
            {
                _aiVm.SelectedCategory = cat;
                ScriptList.ItemsSource = _aiVm.ScriptNames;
            }
            else if (_procsVm != null)
            {
                _procsVm.SelectedCategory = cat;
                ScriptList.ItemsSource = _procsVm.ScriptNames;
            }
        }

        void FilterBox_TextChanged(object? sender, TextChangedEventArgs e)
        {
            string filter = FilterBox.Text ?? "";
            if (_aiVm != null)
            {
                _aiVm.FilterText = filter;
                ScriptList.ItemsSource = _aiVm.ScriptNames;
            }
            else if (_procsVm != null)
            {
                _procsVm.FilterText = filter;
                ScriptList.ItemsSource = _procsVm.ScriptNames;
            }
        }

        void ScriptList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            int index = ScriptList.SelectedIndex;
            if (_aiVm != null)
            {
                _aiVm.SelectedScriptIndex = index;
                InfoLabel.Text = _aiVm.InfoText;
            }
            else if (_procsVm != null)
            {
                _procsVm.SelectedScriptIndex = index;
                InfoLabel.Text = _procsVm.InfoText;
            }
        }

        void Select_Click(object? sender, RoutedEventArgs e)
        {
            // Clear any prior selection so SelectedScript / the returned result is
            // null unless THIS confirmation succeeds (Copilot review: no stale
            // result observable after a failed Select or Cancel).
            _selectedScript = null;
            bool ok = false;
            if (_aiVm != null)
            {
                ok = _aiVm.ConfirmSelection();
                if (ok) _selectedScript = _aiVm.SelectedScript;
            }
            else if (_procsVm != null)
            {
                ok = _procsVm.ConfirmSelection();
                if (ok) _selectedScript = _procsVm.SelectedScript;
            }

            if (ok)
            {
                // Typed modal return (#766): hand the chosen Script back to the
                // caller's ShowDialog<EventScript.Script?>. The AI Script editor's
                // "Script Change" button copies the result's Data bytes into its
                // Binary Code box.
                { DialogResult = _selectedScript; RequestClose(); }
            }
            else
            {
                // No command selected — show an inline hint and stay open
                // (do NOT return a result). VM is untouched (#766 WU2).
                InfoLabel.Text = R._("Select a command from the list first.");
            }
        }

        void Cancel_Click(object? sender, RoutedEventArgs e)
        {
            { DialogResult = null; RequestClose(); }
        }
    }
}
