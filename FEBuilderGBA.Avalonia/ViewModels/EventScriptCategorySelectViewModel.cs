using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>Category picker for event scripts.</summary>
    public class EventScriptCategorySelectViewModel : ViewModelBase, IDataVerifiable
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
            cats.Add("All Events");
            cats.Add("Chapter Events");
            cats.Add("World Map Events");
            cats.Add("Battle Events");
            cats.Add("Talk Events");
            cats.Add("Turn Events");
            cats.Add("Location Events");
            cats.Add("Misc Events");
            Categories = cats;
            IsLoaded = true;
        }

        public int GetListCount() => Categories.Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["SelectedCategory"] = SelectedCategory,
                ["CategoryCount"] = Categories.Count.ToString(),
            };
        }

        public Dictionary<string, string> GetRawRomReport()
        {
            return new Dictionary<string, string>();
        }
    }
}
