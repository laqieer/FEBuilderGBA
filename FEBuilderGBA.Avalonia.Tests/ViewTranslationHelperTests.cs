using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FEBuilderGBA;                       // R (Core type used by R._() lookups)
using FEBuilderGBA.Avalonia.Services;     // ViewTranslationHelper, TranslatedWindow

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// Tests for ViewTranslationHelper — the logical-tree-walking translation engine.
    /// Verifies that TranslateAll() correctly finds and translates text on controls,
    /// and that OnLanguageChanged re-applies translations.
    /// </summary>
    public class ViewTranslationHelperTests
    {
        [AvaloniaFact]
        public void TranslateAll_TranslatesTextBlock()
        {
            var panel = new StackPanel();
            var tb = new TextBlock { Text = "Write" };
            panel.Children.Add(tb);

            var helper = new ViewTranslationHelper(panel);
            helper.TranslateAll();

            // R._("Write") should return translated text (or "Write" if no translation loaded)
            string expected = R._("Write");
            Assert.Equal(expected, tb.Text);
        }

        [AvaloniaFact]
        public void TranslateAll_TranslatesButtonContent()
        {
            var panel = new StackPanel();
            var btn = new Button { Content = "Close" };
            panel.Children.Add(btn);

            var helper = new ViewTranslationHelper(panel);
            helper.TranslateAll();

            string expected = R._("Close");
            Assert.Equal(expected, btn.Content);
        }

        [AvaloniaFact]
        public void TranslateAll_TranslatesCheckBoxContent()
        {
            var panel = new StackPanel();
            var cb = new CheckBox { Content = "Auto Backup" };
            panel.Children.Add(cb);

            var helper = new ViewTranslationHelper(panel);
            helper.TranslateAll();

            string expected = R._("Auto Backup");
            Assert.Equal(expected, cb.Content);
        }

        [AvaloniaFact]
        public void TranslateAll_SkipsWidthAndHeightMarker()
        {
            var panel = new StackPanel();
            var cb = new CheckBox { Content = "WidthAndHeight" };
            panel.Children.Add(cb);

            var helper = new ViewTranslationHelper(panel);
            helper.TranslateAll();

            // "WidthAndHeight" should be skipped
            Assert.Equal("WidthAndHeight", cb.Content);
        }

        [AvaloniaFact]
        public void TranslateAll_SkipsBindingExpressions()
        {
            var panel = new StackPanel();
            var tb = new TextBlock { Text = "{Binding SomeProperty}" };
            panel.Children.Add(tb);

            var helper = new ViewTranslationHelper(panel);
            helper.TranslateAll();

            // Binding expressions should not be translated
            Assert.Equal("{Binding SomeProperty}", tb.Text);
        }

        [AvaloniaFact]
        public void TranslateAll_SkipsEmptyText()
        {
            var panel = new StackPanel();
            var tb = new TextBlock { Text = "" };
            panel.Children.Add(tb);

            var helper = new ViewTranslationHelper(panel);
            helper.TranslateAll();

            Assert.Equal("", tb.Text);
        }

        [AvaloniaFact]
        public void TranslateAll_SkipsNullText()
        {
            var panel = new StackPanel();
            var tb = new TextBlock { Text = null };
            panel.Children.Add(tb);

            var helper = new ViewTranslationHelper(panel);
            helper.TranslateAll();

            Assert.Null(tb.Text);
        }

        [AvaloniaFact]
        public void TranslateAll_SkipsSingleChar()
        {
            var panel = new StackPanel();
            var tb = new TextBlock { Text = "X" };
            panel.Children.Add(tb);

            var helper = new ViewTranslationHelper(panel);
            helper.TranslateAll();

            // Single chars should be skipped
            Assert.Equal("X", tb.Text);
        }

        [AvaloniaFact]
        public void TranslateAll_SkipsHexAddress()
        {
            var panel = new StackPanel();
            var tb = new TextBlock { Text = "0x08000000" };
            panel.Children.Add(tb);

            var helper = new ViewTranslationHelper(panel);
            helper.TranslateAll();

            Assert.Equal("0x08000000", tb.Text);
        }

        [AvaloniaFact]
        public void TranslateAll_TranslatesExpanderHeader()
        {
            var panel = new StackPanel();
            var exp = new Expander { Header = "Identity" };
            panel.Children.Add(exp);

            var helper = new ViewTranslationHelper(panel);
            helper.TranslateAll();

            string expected = R._("Identity");
            Assert.Equal(expected, exp.Header);
        }

        [AvaloniaFact]
        public void TranslateAll_TranslatesTabItemHeader()
        {
            var tc = new TabControl();
            var tab = new TabItem { Header = "General" };
            tc.Items.Add(tab);

            var helper = new ViewTranslationHelper(tc);
            helper.TranslateAll();

            string expected = R._("General");
            Assert.Equal(expected, tab.Header);
        }

        [AvaloniaFact]
        public void TranslateAll_TranslatesNestedControls()
        {
            var panel = new StackPanel();
            var inner = new StackPanel();
            var tb = new TextBlock { Text = "Address:" };
            inner.Children.Add(tb);
            panel.Children.Add(inner);

            var helper = new ViewTranslationHelper(panel);
            helper.TranslateAll();

            string expected = R._("Address:");
            Assert.Equal(expected, tb.Text);
        }

        [AvaloniaFact]
        public void TranslateAll_TranslatesRadioButton()
        {
            var panel = new StackPanel();
            var rb = new RadioButton { Content = "Enabled" };
            panel.Children.Add(rb);

            var helper = new ViewTranslationHelper(panel);
            helper.TranslateAll();

            string expected = R._("Enabled");
            Assert.Equal(expected, rb.Content);
        }

        [AvaloniaFact]
        public void TranslateAll_TranslatesMenuItem()
        {
            var cm = new ContextMenu();
            var mi = new MenuItem { Header = "Copy Address" };
            cm.Items.Add(mi);

            var helper = new ViewTranslationHelper(cm);
            helper.TranslateAll();

            string expected = R._("Copy Address");
            Assert.Equal(expected, mi.Header);
        }

        [AvaloniaFact]
        public void TranslateAll_SkipsPureNumeric()
        {
            var panel = new StackPanel();
            var tb = new TextBlock { Text = "12345" };
            panel.Children.Add(tb);

            var helper = new ViewTranslationHelper(panel);
            helper.TranslateAll();

            Assert.Equal("12345", tb.Text);
        }

        [AvaloniaFact]
        public void TranslateAll_HandlesManyControls()
        {
            var panel = new StackPanel();
            for (int i = 0; i < 50; i++)
            {
                panel.Children.Add(new TextBlock { Text = $"Label {i}" });
            }

            var helper = new ViewTranslationHelper(panel);
            helper.TranslateAll();

            // All should be translated (R._() returns key as-is if no translation)
            for (int i = 0; i < 50; i++)
            {
                var tb = (TextBlock)panel.Children[i];
                Assert.Equal(R._($"Label {i}"), tb.Text);
            }
        }

        [AvaloniaFact]
        public async Task OnLanguageChanged_ReappliesTranslations()
        {
            var panel = new StackPanel();
            var tb = new TextBlock { Text = "Write" };
            panel.Children.Add(tb);

            var helper = new ViewTranslationHelper(panel);
            helper.TranslateAll();

            string initial = tb.Text;

            // Simulate language change - OnLanguageChanged re-applies translations
            // Since we're in test mode without real translation files loaded,
            // it should still work without errors
            helper.OnLanguageChanged();

            // Flush the dispatcher so the async Post has executed
            await Dispatcher.UIThread.InvokeAsync(() => { });

            // After re-apply, text should be the same (same language)
            Assert.Equal(initial, tb.Text);
        }

        [AvaloniaFact]
        public void AllViews_InheritTranslatedBase()
        {
            // Converted editor views inherit from TranslatedUserControl and implement IEmbeddableEditor.
            var viewTypes = new[]
            {
                typeof(FEBuilderGBA.Avalonia.Views.UnitEditorView),
                typeof(FEBuilderGBA.Avalonia.Views.ClassEditorView),
                typeof(FEBuilderGBA.Avalonia.Views.ItemEditorView),
            };

            foreach (var vt in viewTypes)
            {
                Assert.True(typeof(TranslatedUserControl).IsAssignableFrom(vt),
                    $"{vt.Name} should inherit from TranslatedUserControl");
                Assert.True(typeof(FEBuilderGBA.Avalonia.Services.IEmbeddableEditor).IsAssignableFrom(vt),
                    $"{vt.Name} should implement IEmbeddableEditor");
            }
        }

        [AvaloniaFact]
        public void EasyModePanel_InheritsTranslatedUserControl()
        {
            Assert.True(typeof(TranslatedUserControl).IsAssignableFrom(
                typeof(FEBuilderGBA.Avalonia.Views.EasyModePanel)),
                "EasyModePanel should inherit from TranslatedUserControl");
        }

        [AvaloniaFact]
        public void TranslateAll_TranslatesTextBoxWatermark()
        {
            var panel = new StackPanel();
            var tb = new TextBox { Watermark = "Search..." };
            panel.Children.Add(tb);

            var helper = new ViewTranslationHelper(panel);
            helper.TranslateAll();

            string expected = R._("Search...");
            Assert.Equal(expected, tb.Watermark);
        }

        [AvaloniaFact]
        public void TranslateAll_SkipsEmptyWatermark()
        {
            var panel = new StackPanel();
            var tb = new TextBox { Watermark = "" };
            panel.Children.Add(tb);

            var helper = new ViewTranslationHelper(panel);
            helper.TranslateAll();

            Assert.Equal("", tb.Watermark);
        }
    }
}
