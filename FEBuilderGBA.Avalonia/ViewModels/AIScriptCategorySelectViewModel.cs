using System;
using System.Collections.Generic;
using System.IO;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// Category picker for AI scripts.
    /// Loads categories from config TSV and lists AI script commands from Core,
    /// filtering by category and text search.
    /// </summary>
    public class AIScriptCategorySelectViewModel : ViewModelBase
    {
        List<string> _categories = new();
        string _selectedCategory = "";
        bool _isLoaded;
        List<string> _scriptNames = new();
        int _selectedScriptIndex = -1;
        string _filterText = "";
        string _infoText = "";

        /// <summary>Selected script (result of the dialog).</summary>
        public EventScript.Script? SelectedScript { get; private set; }

        // Internal: category key -> filter value mapping
        readonly Dictionary<string, string> _categoryDic = new();
        // Internal: parallel list of Script objects matching _scriptNames
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

        public void Load()
        {
            LoadCategories();
            EnsureAIScriptLoaded();
            RefreshScriptList();
            IsLoaded = true;
        }

        void LoadCategories()
        {
            _categoryDic.Clear();
            var cats = new List<string>();

            string fullfilename = null;
            try
            {
                if (!string.IsNullOrEmpty(CoreState.BaseDirectory))
                    fullfilename = U.ConfigDataFilename("aiscript_category_");
            }
            catch { /* BaseDirectory or ROM may be null in test/headless contexts */ }

            if (!string.IsNullOrEmpty(fullfilename) && File.Exists(fullfilename))
            {
                var dic = LoadTSVResource(fullfilename);
                foreach (var pair in dic)
                {
                    _categoryDic[pair.Key] = pair.Value;
                    cats.Add(pair.Key);
                }
            }

            if (cats.Count == 0)
            {
                // Fallback: provide a default "All" category
                _categoryDic["All AI Scripts"] = "{}";
                cats.Add("All AI Scripts");
            }

            Categories = cats;
        }

        void EnsureAIScriptLoaded()
        {
            var es = CoreState.AIScript;
            if (es == null || es.Scripts == null || es.Scripts.Length == 0)
            {
                try
                {
                    // AI scripts use a FIXED 16-byte instruction grid — the
                    // unknown-opcode width must be 16 (parity with WinForms
                    // Program.cs `new EventScript(16)`), otherwise an
                    // unrecognized opcode decodes as four 4-byte rows (#757).
                    es = new EventScript(16);
                    es.Load(EventScript.EventScriptType.AI);
                    CoreState.AIScript = es;
                }
                catch
                {
                    // Script definitions not available
                }
            }
        }

        void RefreshScriptList()
        {
            _scriptCache.Clear();
            var names = new List<string>();

            var es = CoreState.AIScript;
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

        /// <summary>Load a TSV resource file into a dictionary (display name -> category key).</summary>
        internal static Dictionary<string, string> LoadTSVResource(string fullfilename)
        {
            var dic = new Dictionary<string, string>();
            if (!File.Exists(fullfilename))
                return dic;

            using var reader = File.OpenText(fullfilename);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (U.IsComment(line))
                    continue;
                try
                {
                    if (U.OtherLangLine(line))
                        continue;
                }
                catch
                {
                    // OtherLangLine may throw if CoreState.ROM is null (headless/test context)
                }
                line = U.ClipComment(line);
                if (string.IsNullOrEmpty(line))
                    continue;
                string[] sp = line.Split('\t');
                if (sp.Length < 2)
                    continue;
                dic[sp[1]] = sp[0];
            }
            return dic;
        }
    }
}
