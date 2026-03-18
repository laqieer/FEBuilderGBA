using System.Collections.Generic;
using System.IO;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class OptionsLanguageTests
    {
        [Fact]
        public void EnumerateLanguages_ContainsAllExpectedCodes()
        {
            var langs = OptionsViewModel.EnumerateLanguages();
            Assert.True(langs.Count >= 4);
            Assert.Contains(langs, s => s.StartsWith("auto"));
            Assert.Contains(langs, s => s.StartsWith("ja"));
            Assert.Contains(langs, s => s.StartsWith("en"));
            Assert.Contains(langs, s => s.StartsWith("zh"));
        }

        [Fact]
        public void EnumerateLanguages_DisplayNamesIncludeJapanese()
        {
            var langs = OptionsViewModel.EnumerateLanguages();
            // "ja" entry should contain 日本語
            var jaEntry = langs.Find(s => s.StartsWith("ja"));
            Assert.NotNull(jaEntry);
            Assert.Contains("\u65e5\u672c\u8a9e", jaEntry);
        }

        [Fact]
        public void EnumerateLanguages_DisplayNamesUseDashSeparator()
        {
            var langs = OptionsViewModel.EnumerateLanguages();
            foreach (var lang in langs)
            {
                Assert.Contains(" \u2014 ", lang); // em dash separator
            }
        }

        [Theory]
        [InlineData("ja \u2014 \u65e5\u672c\u8a9e", "ja")]
        [InlineData("en \u2014 English", "en")]
        [InlineData("zh \u2014 \u4e2d\u6587", "zh")]
        [InlineData("auto \u2014 Auto Detect", "auto")]
        [InlineData("", "auto")]
        [InlineData("en", "en")] // no separator, returns as-is
        public void ExtractLanguageCode_ReturnsCorrectCode(string display, string expected)
        {
            Assert.Equal(expected, OptionsViewModel.ExtractLanguageCode(display));
        }

        [Fact]
        public void ConfigBackwardCompat_FuncLangFallback()
        {
            // Simulate config with only func_lang (WinForms style)
            var cfg = new Config();
            cfg["func_lang"] = "zh";
            // "Language" key is missing, so fallback reads func_lang
            string lang = cfg.at("Language", cfg.at("func_lang", "auto"));
            Assert.Equal("zh", lang);
        }

        [Fact]
        public void ReloadTranslations_DoesNotThrowWithNullBaseDir()
        {
            // Save and restore state
            string? origLang = CoreState.Language;
            string? origBase = CoreState.BaseDirectory;
            try
            {
                CoreState.Language = "ja";
                CoreState.BaseDirectory = "";
                // Should not throw — "ja" clears translations (built-in)
                OptionsViewModel.ReloadTranslations();
            }
            finally
            {
                CoreState.Language = origLang;
                CoreState.BaseDirectory = origBase;
                OptionsViewModel.ReloadTranslations();
            }
        }

        [Fact]
        public void ReloadTranslations_JapaneseClearsTranslations()
        {
            string? origLang = CoreState.Language;
            string? origBase = CoreState.BaseDirectory;
            try
            {
                CoreState.Language = "ja";
                OptionsViewModel.ReloadTranslations();
                // After loading Japanese, R._() should return the key itself (no translation)
                Assert.Equal("TestKey", R._("TestKey"));
            }
            finally
            {
                CoreState.Language = origLang;
                CoreState.BaseDirectory = origBase;
                OptionsViewModel.ReloadTranslations();
            }
        }
    }
}
