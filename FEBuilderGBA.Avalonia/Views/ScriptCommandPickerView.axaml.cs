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
    public partial class ScriptCommandPickerView : TranslatedWindow
    {
        readonly EventScript.EventScriptType _scriptType;
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

            Title = scriptType switch
            {
                EventScript.EventScriptType.AI => "Select AI Script Command",
                EventScript.EventScriptType.Procs => "Select Procs Script Command",
                _ => "Select Script Command"
            };

            if (scriptType == EventScript.EventScriptType.AI)
            {
                _aiVm = new AIScriptCategorySelectViewModel();
                _aiVm.Load();
                CategoryList.ItemsSource = _aiVm.Categories;
                ScriptList.ItemsSource = _aiVm.ScriptNames;
                if (_aiVm.Categories.Count > 0)
                    CategoryList.SelectedIndex = 0;
            }
            else
            {
                _procsVm = new ProcsScriptCategorySelectViewModel();
                _procsVm.Load();
                CategoryList.ItemsSource = _procsVm.Categories;
                ScriptList.ItemsSource = _procsVm.ScriptNames;
                if (_procsVm.Categories.Count > 0)
                    CategoryList.SelectedIndex = 0;
            }

            FilterBox.TextChanged += FilterBox_TextChanged;
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
                Close(_selectedScript);
        }

        void Cancel_Click(object? sender, RoutedEventArgs e)
        {
            Close(null);
        }
    }
}
