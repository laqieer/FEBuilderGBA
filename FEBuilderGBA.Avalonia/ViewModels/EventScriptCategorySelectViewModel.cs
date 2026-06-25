using System;
using System.Collections.Generic;
using System.IO;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// Category picker for event scripts.
    /// Loads real categories from the config TSV (<c>event_category_</c>) and lists
    /// real event-script commands from Core, filtering by category + text search,
    /// returning a chosen <see cref="EventScript.Script"/>.
    /// Mirrors WinForms <c>EventScriptFormCategorySelectForm</c> and the Avalonia
    /// siblings <see cref="ProcsScriptCategorySelectViewModel"/> /
    /// <see cref="AIScriptCategorySelectViewModel"/>.
    ///
    /// Scope note (#1443): the WinForms <c>{TEMPLATE}</c> category appends
    /// <c>EventTemplateImpl</c> entries and returns them via a separate
    /// <c>DialogResult.Retry</c> / <c>EventTemplateCode</c> insertion path consumed by
    /// the write-back <c>EventScriptInnerControl</c>. The Avalonia <c>EventScriptView</c>
    /// is a read-only disassembler with no insertion consumer, so the
    /// <c>{TEMPLATE}</c> category is intentionally SKIPPED here (no dead category, no
    /// template entries in "Show all"). That insertion flow is documented remaining
    /// consumer-side work.
    /// </summary>
    public class EventScriptCategorySelectViewModel : ViewModelBase
    {
        /// <summary>WinForms category token for event templates (intentionally skipped, #1443).</summary>
        internal const string TemplateCategoryToken = "{TEMPLATE}";

        List<string> _categories = new();
        string _selectedCategory = "";
        bool _isLoaded;
        List<string> _scriptNames = new();
        int _selectedScriptIndex = -1;
        string _filterText = "";
        string _infoText = "";
        bool _showLowCommand;

        /// <summary>Selected script (result of the dialog).</summary>
        public EventScript.Script? SelectedScript { get; private set; }

        // Internal: category display-name -> filter token mapping (e.g. "Text" -> "{TEXT}").
        readonly Dictionary<string, string> _categoryDic = new();
        // Internal: parallel list of Script objects matching _scriptNames.
        readonly List<EventScript.Script> _scriptCache = new();

        public List<string> Categories { get => _categories; set => SetField(ref _categories, value); }
        public string SelectedCategory
        {
            get => _selectedCategory;
            set
            {
                if (SetField(ref _selectedCategory, value))
                    RefreshScriptList();
            }
        }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public List<string> ScriptNames { get => _scriptNames; set => SetField(ref _scriptNames, value); }
        public int SelectedScriptIndex
        {
            get => _selectedScriptIndex;
            set
            {
                if (SetField(ref _selectedScriptIndex, value))
                    UpdateInfo(value);
            }
        }
        public string FilterText
        {
            get => _filterText;
            set
            {
                if (SetField(ref _filterText, value))
                    RefreshScriptList();
            }
        }
        public string InfoText { get => _infoText; set => SetField(ref _infoText, value); }

        /// <summary>
        /// When false (default, matching WinForms), dangerous LOW commands
        /// (<see cref="EventScript.Script.IsLowCommand"/>) are hidden from the list.
        /// </summary>
        public bool ShowLowCommand
        {
            get => _showLowCommand;
            set
            {
                if (SetField(ref _showLowCommand, value))
                    RefreshScriptList();
            }
        }

        public void Load()
        {
            LoadCategories();
            EnsureEventScriptLoaded();
            RefreshScriptList();
            IsLoaded = true;
        }

        void LoadCategories()
        {
            _categoryDic.Clear();
            var cats = new List<string>();

            string? fullfilename = null;
            try
            {
                if (!string.IsNullOrEmpty(CoreState.BaseDirectory))
                    fullfilename = U.ConfigDataFilename("event_category_");
            }
            catch { /* BaseDirectory or ROM may be null in test/headless contexts */ }

            if (!string.IsNullOrEmpty(fullfilename) && File.Exists(fullfilename))
            {
                var dic = AIScriptCategorySelectViewModel.LoadTSVResource(fullfilename);
                foreach (var pair in dic)
                {
                    // #1443: skip the WinForms-only {TEMPLATE} category — its
                    // EventTemplateImpl insertion path has no Avalonia consumer
                    // (EventScriptView is a read-only disassembler).
                    if (pair.Value == TemplateCategoryToken)
                        continue;

                    _categoryDic[pair.Key] = pair.Value;
                    cats.Add(pair.Key);
                }
            }

            if (cats.Count == 0)
            {
                // Fallback: provide a default "All" category for headless/test contexts.
                _categoryDic["All Events"] = "{}";
                cats.Add("All Events");
            }

            Categories = cats;
        }

        void EnsureEventScriptLoaded()
        {
            var es = CoreState.EventScript;
            if (es == null || es.Scripts == null || es.Scripts.Length == 0)
            {
                try
                {
                    es = new EventScript();
                    es.Load(EventScript.EventScriptType.Event);
                    CoreState.EventScript = es;
                }
                catch
                {
                    // Script definitions not available (headless/test).
                }
            }
        }

        void RefreshScriptList()
        {
            _scriptCache.Clear();
            var names = new List<string>();

            var es = CoreState.EventScript;
            if (es?.Scripts == null)
            {
                ScriptNames = names;
                return;
            }

            string categoryFilter = GetCategoryFilter();
            bool filtered = !string.IsNullOrEmpty(categoryFilter);
            string filter = (_filterText ?? "").Trim();

            foreach (var script in es.Scripts)
            {
                if (filtered && (script.Category == null || script.Category.IndexOf(categoryFilter, StringComparison.Ordinal) < 0))
                    continue;

                // WinForms hides dangerous LOW commands unless explicitly enabled.
                if (!_showLowCommand && script.IsLowCommand)
                    continue;

                string name = EventScript.makeCommandComboText(script, true);

                if (filter.Length > 0)
                {
                    string searchable = name + " " + (script.PopupHint ?? "");
                    if (searchable.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                }

                names.Add(name);
                _scriptCache.Add(script);
            }

            ScriptNames = names;
        }

        string GetCategoryFilter()
        {
            if (string.IsNullOrEmpty(_selectedCategory))
                return "";
            if (_categoryDic.TryGetValue(_selectedCategory, out string? value))
            {
                if (value == "{}")
                    return "";
                return value ?? "";
            }
            return "";
        }

        void UpdateInfo(int index)
        {
            if (index < 0 || index >= _scriptCache.Count)
            {
                InfoText = "";
                SelectedScript = null;
                return;
            }

            var script = _scriptCache[index];
            SelectedScript = script;

            string name = EventScript.makeCommandComboText(script, true);
            if (!string.IsNullOrEmpty(script.PopupHint))
                InfoText = name + "\n" + script.PopupHint;
            else
                InfoText = name;
        }

        /// <summary>Confirm selection. Returns true if a valid script is selected.</summary>
        public bool ConfirmSelection()
        {
            if (_selectedScriptIndex < 0 || _selectedScriptIndex >= _scriptCache.Count)
                return false;

            SelectedScript = _scriptCache[_selectedScriptIndex];
            return true;
        }
    }
}
