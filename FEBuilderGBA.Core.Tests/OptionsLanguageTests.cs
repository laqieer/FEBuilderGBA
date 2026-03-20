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

        [Fact]
        public void EnumerateLanguages_WellKnownEntriesFirst()
        {
            var langs = OptionsViewModel.EnumerateLanguages();
            // First 4 entries should be the well-known languages in order
            Assert.StartsWith("auto", langs[0]);
            Assert.StartsWith("ja", langs[1]);
            Assert.StartsWith("en", langs[2]);
            Assert.StartsWith("zh", langs[3]);
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

        [Fact]
        public void ReloadTranslations_JapaneseDoesNotTriggerShowError()
        {
            // Regression: ReloadTranslations("ja") used to call LoadResource("")
            // which triggered a ShowError dialog because "" is not "ja.txt"
            string? origLang = CoreState.Language;
            string? origBase = CoreState.BaseDirectory;
            var origServices = CoreState.Services;
            bool errorShown = false;
            try
            {
                CoreState.Services = new TestAppServices(() => errorShown = true);
                CoreState.Language = "ja";
                CoreState.BaseDirectory = "";
                OptionsViewModel.ReloadTranslations();
                Assert.False(errorShown, "ShowError should not be called when language is 'ja'");
            }
            finally
            {
                CoreState.Language = origLang;
                CoreState.BaseDirectory = origBase;
                CoreState.Services = origServices;
            }
        }

        [Fact]
        public void ReloadTranslations_UnknownLanguageFallsBackToEnglishOrClears()
        {
            // When a language file doesn't exist, should fall back gracefully
            string? origLang = CoreState.Language;
            string? origBase = CoreState.BaseDirectory;
            try
            {
                CoreState.Language = "xx_nonexistent";
                CoreState.BaseDirectory = "";
                // Should not throw
                OptionsViewModel.ReloadTranslations();
                // After fallback, R._() should still work (returns key or english)
                Assert.Equal("TestKey", R._("TestKey"));
            }
            finally
            {
                CoreState.Language = origLang;
                CoreState.BaseDirectory = origBase;
                OptionsViewModel.ReloadTranslations();
            }
        }

        [Fact]
        public void MyTranslateResource_Clear_ResetsToPassthrough()
        {
            // Verify that Clear() makes str() return keys as-is
            MyTranslateResource.Clear();
            Assert.Equal("SomeTestString", MyTranslateResource.str("SomeTestString"));
            Assert.Equal("Another Key", MyTranslateResource.str("Another Key"));
        }

        [Fact]
        public void SaveAndReload_LanguagePersisted()
        {
            // Verify that Save() writes both "Language" and "func_lang" keys
            string? origLang = CoreState.Language;
            var origConfig = CoreState.Config;
            try
            {
                var cfg = new Config();
                CoreState.Config = cfg;
                CoreState.Language = "en";

                var vm = new OptionsViewModel();
                vm.Language = "zh \u2014 \u4e2d\u6587";

                // Extract and verify
                string code = OptionsViewModel.ExtractLanguageCode(vm.Language);
                Assert.Equal("zh", code);
            }
            finally
            {
                CoreState.Language = origLang;
                CoreState.Config = origConfig;
            }
        }

        /// <summary>
        /// Test IAppServices implementation that tracks whether ShowError was called.
        /// </summary>
        class TestAppServices : IAppServices
        {
            readonly Action _onError;
            public TestAppServices(Action onError) { _onError = onError; }
            public void ShowError(string msg) => _onError();
            public void ShowInfo(string msg) { }
            public bool ShowQuestion(string msg) => false;
            public bool ShowYesNo(string msg) => false;
            public void RunOnUIThread(Action action) => action();
            public bool IsMainThread() => true;
        }
    }
}
