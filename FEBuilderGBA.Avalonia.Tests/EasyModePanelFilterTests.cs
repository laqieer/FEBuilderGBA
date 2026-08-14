using System.Collections.Generic;
using System.Linq;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    public class EasyModePanelFilterTests
    {
        [AvaloniaTheory]
        [InlineData("Visual")]
        [InlineData("vIsUaL")]
        public void ApplyFilter_EditorTitle_ShowsMatchingCategory(string query)
        {
            var panel = new EasyModePanel();

            panel.ApplyFilter(query);

            Assert.True(Category(panel, "CategoryMaps").IsVisible);
            Assert.All(Categories(panel).Where(c => c.Name != "CategoryMaps"),
                category => Assert.False(category.IsVisible));
        }

        [AvaloniaTheory]
        [InlineData("Unit Editor", "CategoryCharacters")]
        [InlineData("Item Shop Editor", "CategoryItems")]
        [InlineData("Visual Map Editor", "CategoryMaps")]
        [InlineData("Event Condition Editor", "CategoryEvents")]
        [InlineData("Battle Animation Editor", "CategoryGraphics")]
        [InlineData("Sound Room Editor", "CategoryMusic")]
        [InlineData("Text Editor", "CategoryText")]
        [InlineData("Pointer Tool", "CategoryTools")]
        public void ApplyFilter_EditorTitles_CoverEveryCategory(
            string query,
            string expectedCategory)
        {
            var panel = new EasyModePanel();

            panel.ApplyFilter(query);

            Assert.True(Category(panel, expectedCategory).IsVisible);
        }

        [AvaloniaTheory]
        [InlineData("shops", "CategoryItems")]
        [InlineData("maps", "CategoryMaps")]
        [InlineData("lint", "CategoryTools")]
        public void ApplyFilter_LegacyKeyword_StillMatches(
            string query,
            string expectedCategory)
        {
            var panel = new EasyModePanel();

            panel.ApplyFilter(query);

            Assert.True(Category(panel, expectedCategory).IsVisible);
        }

        [AvaloniaFact]
        public void ApplyFilter_NoMatch_HidesAllCategories()
        {
            var panel = new EasyModePanel();

            panel.ApplyFilter("definitely-not-an-editor");

            Assert.All(Categories(panel),
                category => Assert.False(category.IsVisible));
        }

        [AvaloniaFact]
        public void ApplyFilter_UnrelatedEditorAlias_DoesNotMatchUnmappedAction()
        {
            var panel = new EasyModePanel();

            panel.ApplyFilter("Special OAM");

            Assert.All(Categories(panel),
                category => Assert.False(category.IsVisible));
        }

        [AvaloniaFact]
        public void ApplyFilter_EmptyQuery_RestoresAllCategories()
        {
            var panel = new EasyModePanel();
            panel.ApplyFilter("Visual");

            panel.ApplyFilter("");

            Assert.All(Categories(panel),
                category => Assert.True(category.IsVisible));
        }

        [Fact]
        public void CatalogSearchKeys_AllExistInSharedIndex()
        {
            foreach (string key in EasyModePanel.CatalogSearchKeys)
            {
                Assert.True(EditorSearchIndex.CatalogAliases.ContainsKey(key),
                    $"Unknown Easy Mode catalog search key: {key}");
            }
        }

        static IReadOnlyList<Border> Categories(EasyModePanel panel)
        {
            return new[]
            {
                Category(panel, "CategoryCharacters"),
                Category(panel, "CategoryItems"),
                Category(panel, "CategoryMaps"),
                Category(panel, "CategoryEvents"),
                Category(panel, "CategoryGraphics"),
                Category(panel, "CategoryMusic"),
                Category(panel, "CategoryText"),
                Category(panel, "CategoryTools"),
            };
        }

        static Border Category(EasyModePanel panel, string name)
        {
            Border? category = panel.FindControl<Border>(name);
            Assert.NotNull(category);
            return category!;
        }
    }
}
