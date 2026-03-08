using System.Collections.Generic;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>Category picker for AI scripts.</summary>
    public class AIScriptCategorySelectViewModel : ViewModelBase
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
            cats.Add("All AI Scripts");
            cats.Add("Movement AI");
            cats.Add("Targeting AI");
            cats.Add("Item Usage AI");
            cats.Add("Staff Usage AI");
            cats.Add("Steal AI");
            cats.Add("Special AI");
            cats.Add("Misc AI");
            Categories = cats;
            IsLoaded = true;
        }
    }
}
