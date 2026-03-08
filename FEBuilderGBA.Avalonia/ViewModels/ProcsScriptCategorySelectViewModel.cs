using System.Collections.Generic;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>Category picker for Procs scripts.</summary>
    public class ProcsScriptCategorySelectViewModel : ViewModelBase
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
            cats.Add("All Procs");
            cats.Add("Battle Procs");
            cats.Add("Menu Procs");
            cats.Add("Map Procs");
            cats.Add("Animation Procs");
            cats.Add("UI Procs");
            cats.Add("System Procs");
            cats.Add("Misc Procs");
            Categories = cats;
            IsLoaded = true;
        }
    }
}
