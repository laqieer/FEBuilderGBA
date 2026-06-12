using System.Collections.Generic;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// Category picker for text escape codes (#1108). Loads the REAL shipped
    /// text-escape + text-category tables from Core
    /// (<see cref="FEBuilderGBA.TextRichControlDecode"/>) instead of the previous
    /// hardcoded stub categories. The view shows a two-pane select (category →
    /// escapes within it); OK returns the chosen escape <c>@XXXX</c> Code.
    ///
    /// Faithful to WF <c>TextScriptFormCategorySelectForm</c>: the "{}" / "show
    /// all" category returns every escape; the move/load + position categories are
    /// hidden unless detail mode is on.
    /// </summary>
    public class TextScriptCategorySelectViewModel : ViewModelBase
    {
        // Category labels shown in the left pane (what the View binds to). Index
        // matches _categoryKeys 1:1.
        List<string> _categories = new();
        // The category KEY (e.g. "{DISPLAY}", "{}") for each entry in _categories.
        List<string> _categoryKeys = new();
        // Every escape entry loaded from config (unfiltered by category).
        List<FEBuilderGBA.TextEscapeEntry> _escapeEntries = new();
        string _selectedCategory = "";
        string? _selectedCode;
        bool _isLoaded;

        public List<string> Categories { get => _categories; set => SetField(ref _categories, value); }
        public string SelectedCategory { get => _selectedCategory; set => SetField(ref _selectedCategory, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        /// <summary>The escape Code string (e.g. <c>@0010</c>) the user chose, or null.</summary>
        public string? SelectedCode { get => _selectedCode; set => SetField(ref _selectedCode, value); }

        /// <summary>Read-only view of every loaded escape entry (unfiltered).</summary>
        public IReadOnlyList<FEBuilderGBA.TextEscapeEntry> EscapeEntries => _escapeEntries;

        /// <summary>
        /// Back-compat entry point — loads in detail mode (shows every category).
        /// Existing callers / the View's parameterless ctor path use this.
        /// </summary>
        public void Load() => Init(true);

        /// <summary>
        /// Load the category labels + all escape entries from Core. When
        /// <paramref name="isDetail"/> is false the move/load + position
        /// categories/escapes are filtered out (WF detail-mode parity).
        /// </summary>
        public void Init(bool isDetail = true)
        {
            var cats = new List<string>();
            var keys = new List<string>();
            foreach (var (category, label) in FEBuilderGBA.TextRichControlDecode.LoadEscapeCategories(isDetail))
            {
                keys.Add(category);
                cats.Add(label);
            }

            _categoryKeys = keys;
            Categories = cats;
            _escapeEntries = FEBuilderGBA.TextRichControlDecode.LoadEscapeEntries(isDetail);
            IsLoaded = true;
        }

        /// <summary>
        /// Resolve the category KEY for a label index (the View binds to labels).
        /// Returns "" when out of range.
        /// </summary>
        public string GetCategoryKeyForIndex(int index)
        {
            if (index < 0 || index >= _categoryKeys.Count)
            {
                return "";
            }
            return _categoryKeys[index];
        }

        /// <summary>
        /// Return the escape entries belonging to a category key. The "{}" /
        /// "show all" category returns every loaded escape; otherwise the list is
        /// filtered to entries whose Category matches the key. An empty/unknown
        /// key also returns all entries (defensive — never an empty pane).
        /// </summary>
        public List<FEBuilderGBA.TextEscapeEntry> GetEntriesForCategory(string categoryKey)
        {
            if (string.IsNullOrEmpty(categoryKey) || categoryKey == "{}")
            {
                return new List<FEBuilderGBA.TextEscapeEntry>(_escapeEntries);
            }

            var matched = new List<FEBuilderGBA.TextEscapeEntry>();
            foreach (var e in _escapeEntries)
            {
                if (e.Category == categoryKey)
                {
                    matched.Add(e);
                }
            }
            // If the key matched nothing (e.g. a patch-added "" category that has
            // no dedicated category row), fall back to showing all so the user is
            // never stuck with an empty escape pane.
            return matched.Count > 0 ? matched : new List<FEBuilderGBA.TextEscapeEntry>(_escapeEntries);
        }
    }
}
