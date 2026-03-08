using System.Collections.Generic;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>Category picker for text scripts.</summary>
    public class TextScriptCategorySelectViewModel : ViewModelBase
    {
        List<string> _categories = new();
        string _selectedCategory = "";
        bool _isLoaded;

        public List<string> Categories { get => _categories; set => SetField(ref _categories, value); }
        public string SelectedCategory { get => _selectedCategory; set => SetField(ref _selectedCategory, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        public void Load()
        {
            var cats = new List<string>();
            cats.Add("All Text");
            cats.Add("Dialogue Text");
            cats.Add("System Text");
            cats.Add("Menu Text");
            cats.Add("Item Names");
            cats.Add("Class Names");
            cats.Add("Unit Names");
            cats.Add("Misc Text");
            Categories = cats;
            IsLoaded = true;
        }
    }
}
