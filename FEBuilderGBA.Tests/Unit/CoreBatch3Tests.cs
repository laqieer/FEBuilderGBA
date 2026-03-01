using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Tests.Unit
{
    /// <summary>
    /// Tests for batch 3 Core migration: MultiByteJPUtil (already tested in MultiByteJPUtilTests),
    /// MyTranslateResource, MyTranslateResourceLow, EtcCacheResource,
    /// and new Core U utility methods.
    ///
    /// Note: U.LoadTSV*/SaveTSV*/ConfigDataFilename/ConfigEtcFilename methods exist in both
    /// Core and WinForms. Due to CS0436 shadowing, tests call WinForms' versions which have
    /// different dependencies (R.ShowStopError, OptionForm.lang). These Core methods are
    /// indirectly tested through MyTranslateResourceLow and EtcCacheResource which use
    /// Core's U internally.
    /// </summary>
    public class CoreBatch3Tests : IDisposable
    {
        private readonly string _tempDir;

        public CoreBatch3Tests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "FEBuilderGBA_CoreBatch3_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }

        // ---- MyTranslateResourceLow Tests ----

        [Fact]
        public void MyTranslateResourceLow_LoadResource_LoadsTranslations()
        {
            string resFile = Path.Combine(_tempDir, "test_translate.txt");
            File.WriteAllLines(resFile, new[]
            {
                ":Hello",
                "Hola",
                "",
                ":Goodbye",
                "Adiós",
                ""
            });

            var low = new MyTranslateResourceLow();
            low.LoadResource(resFile);

            Assert.Equal("Hola", low.str("Hello"));
            Assert.Equal("Adiós", low.str("Goodbye"));
        }

        [Fact]
        public void MyTranslateResourceLow_Str_ReturnsOriginalWhenNotFound()
        {
            var low = new MyTranslateResourceLow();
            Assert.Equal("NotTranslated", low.str("NotTranslated"));
        }

        [Fact]
        public void MyTranslateResourceLow_Exist_ReturnsTrueForLoadedKey()
        {
            string resFile = Path.Combine(_tempDir, "test_exist.txt");
            File.WriteAllLines(resFile, new[]
            {
                ":TestKey",
                "TestValue",
                ""
            });

            var low = new MyTranslateResourceLow();
            low.LoadResource(resFile);

            Assert.True(low.Exist("TestKey"));
            Assert.False(low.Exist("MissingKey"));
        }

        [Fact]
        public void MyTranslateResourceLow_ReplaceTranslateString_OverridesTranslation()
        {
            string resFile = Path.Combine(_tempDir, "test_replace.txt");
            File.WriteAllLines(resFile, new[]
            {
                ":Key",
                "Original",
                ""
            });

            var low = new MyTranslateResourceLow();
            low.LoadResource(resFile);

            Assert.Equal("Original", low.str("Key"));
            low.replaceTranslateString("Key", "Replaced");
            Assert.Equal("Replaced", low.str("Key"));
        }

        [Fact]
        public void MyTranslateResourceLow_WriteResource_RoundTrips()
        {
            string resFile = Path.Combine(_tempDir, "test_write_in.txt");
            File.WriteAllLines(resFile, new[]
            {
                ":Alpha",
                "Beta",
                "",
                ":Gamma",
                "Delta",
                ""
            });

            var low = new MyTranslateResourceLow();
            low.LoadResource(resFile);

            string outFile = Path.Combine(_tempDir, "test_write_out.txt");
            low.WriteResource(outFile);

            var low2 = new MyTranslateResourceLow();
            low2.LoadResource(outFile);
            Assert.Equal("Beta", low2.str("Alpha"));
            Assert.Equal("Delta", low2.str("Gamma"));
        }

        [Fact]
        public void MyTranslateResourceLow_LoadResource_HandlesNewlinesInKeys()
        {
            string resFile = Path.Combine(_tempDir, "test_newline.txt");
            File.WriteAllLines(resFile, new[]
            {
                @":Line1\r\nLine2",
                @"Trans1\r\nTrans2",
                ""
            });

            var low = new MyTranslateResourceLow();
            low.LoadResource(resFile);

            Assert.Equal("Trans1\r\nTrans2", low.str("Line1\r\nLine2"));
        }

        [Fact]
        public void MyTranslateResourceLow_LoadResource_MissingFile_ShowsError()
        {
            var mockServices = new MockAppServices();
            CoreState.Services = mockServices;

            var low = new MyTranslateResourceLow();
            low.LoadResource(Path.Combine(_tempDir, "nonexistent_en.txt"));

            Assert.True(mockServices.ErrorShown);
        }

        [Fact]
        public void MyTranslateResourceLow_LoadResource_MissingJaFile_NoError()
        {
            var mockServices = new MockAppServices();
            CoreState.Services = mockServices;

            var low = new MyTranslateResourceLow();
            low.LoadResource(Path.Combine(_tempDir, "ja.txt"));

            Assert.False(mockServices.ErrorShown);
        }

        [Fact]
        public void MyTranslateResourceLow_ConvertOnelineSplitWord_ReturnsDictionary()
        {
            string resFile = Path.Combine(_tempDir, "test_split.txt");
            File.WriteAllLines(resFile, new[]
            {
                ":Word1",
                "Trans1",
                ""
            });

            var low = new MyTranslateResourceLow();
            low.LoadResource(resFile);

            var dic = low.ConvertOnelineSplitWord();
            Assert.NotNull(dic);
            Assert.True(dic.ContainsKey("Word1"));
            Assert.Equal("Trans1", dic["Word1"]);
        }

        // ---- MyTranslateResource static facade ----

        [Fact]
        public void MyTranslateResource_Str_ReturnsOriginalForUntranslated()
        {
            Assert.Equal("UntranslatedString", MyTranslateResource.str("UntranslatedString"));
        }

        [Fact]
        public void MyTranslateResource_StrWithArgs_FormatsCorrectly()
        {
            string result = MyTranslateResource.str("{0} items found", 42);
            Assert.Equal("42 items found", result);
        }

        [Fact]
        public void MyTranslateResource_StrWithNoArgs_ReturnsTranslatedString()
        {
            string result = MyTranslateResource.str("SomeKey");
            Assert.Equal("SomeKey", result); // no translation loaded, returns original
        }

        // ---- EtcCacheResource Tests ----

        [Fact]
        public void EtcCacheResource_IsPublicClass()
        {
            var type = typeof(EtcCacheResource);
            Assert.True(type.IsPublic);
            Assert.True(type.IsClass);
            Assert.False(type.IsAbstract);
        }

        // ---- U.GetFirstPeriodFilename Tests ----

        [Fact]
        public void U_GetFirstPeriodFilename_ExtractsFirstPart()
        {
            Assert.Equal("game", U.GetFirstPeriodFilename("game.ups.gba"));
        }

        [Fact]
        public void U_GetFirstPeriodFilename_NoExtension_ReturnsFullPath()
        {
            Assert.Equal("game", U.GetFirstPeriodFilename("game"));
        }

        [Fact]
        public void U_GetFirstPeriodFilename_SingleDot_ReturnsName()
        {
            Assert.Equal("rom", U.GetFirstPeriodFilename("rom.gba"));
        }

        // ---- U.DicKeys Tests ----

        [Fact]
        public void U_DicKeys_Uint_ReturnsAllKeys()
        {
            var dic = new Dictionary<uint, string>
            {
                { 1, "one" },
                { 2, "two" },
                { 3, "three" }
            };

            uint[] keys = U.DicKeys(dic);
            Assert.Equal(3, keys.Length);
            Assert.Contains(1u, keys);
            Assert.Contains(2u, keys);
            Assert.Contains(3u, keys);
        }

        [Fact]
        public void U_DicKeys_String_ReturnsAllKeys()
        {
            var dic = new Dictionary<string, string>
            {
                { "a", "alpha" },
                { "b", "beta" }
            };

            string[] keys = U.DicKeys(dic);
            Assert.Equal(2, keys.Length);
            Assert.Contains("a", keys);
            Assert.Contains("b", keys);
        }

        // ---- U.CanSecondLanguageEnglish Tests ----

        [Fact]
        public void U_CanSecondLanguageEnglish_ReturnsFalseForEnAndJa()
        {
            Assert.False(U.CanSecondLanguageEnglish("en"));
            Assert.False(U.CanSecondLanguageEnglish("ja"));
        }

        [Fact]
        public void U_CanSecondLanguageEnglish_ReturnsTrueForOtherLanguages()
        {
            Assert.True(U.CanSecondLanguageEnglish("zh"));
            Assert.True(U.CanSecondLanguageEnglish("de"));
            Assert.True(U.CanSecondLanguageEnglish("fr"));
        }

        // ---- U.mkdir Tests ----

        [Fact]
        public void U_Mkdir_CreatesDirectory()
        {
            string newDir = Path.Combine(_tempDir, "newdir");
            Assert.True(U.mkdir(newDir));
            Assert.True(Directory.Exists(newDir));
        }

        [Fact]
        public void U_Mkdir_RecreatesExistingDirectory()
        {
            string existingDir = Path.Combine(_tempDir, "existing");
            Directory.CreateDirectory(existingDir);
            File.WriteAllText(Path.Combine(existingDir, "test.txt"), "data");

            Assert.True(U.mkdir(existingDir));
            Assert.True(Directory.Exists(existingDir));
            Assert.False(File.Exists(Path.Combine(existingDir, "test.txt")));
        }

        // ---- U.LoadTSVResource1 / LoadTSVResourcePair2 (not-required path only) ----

        [Fact]
        public void U_LoadTSVResource1_NotRequired_MissingFile_ReturnsEmptyDic()
        {
            var dic = U.LoadTSVResource1(Path.Combine(_tempDir, "missing.txt"), false);
            Assert.Empty(dic);
        }

        [Fact]
        public void U_LoadTSVResourcePair2_NotRequired_MissingFile_ReturnsEmptyDic()
        {
            var dic = U.LoadTSVResourcePair2(Path.Combine(_tempDir, "missing2.txt"), false);
            Assert.Empty(dic);
        }

        // ---- Assembly-level checks ----

        [Fact]
        public void MultiByteJPUtil_IsPublicStaticClass()
        {
            var type = typeof(MultiByteJPUtil);
            Assert.True(type.IsPublic);
            Assert.True(type.IsAbstract && type.IsSealed); // static class
        }

        [Fact]
        public void MyTranslateResource_IsPublicStaticClass()
        {
            var type = typeof(MyTranslateResource);
            Assert.True(type.IsPublic);
            Assert.True(type.IsAbstract && type.IsSealed); // static class
        }

        [Fact]
        public void MyTranslateResourceLow_IsPublicClass()
        {
            var type = typeof(MyTranslateResourceLow);
            Assert.True(type.IsPublic);
            Assert.True(type.IsClass);
        }

        /// <summary>
        /// Mock IAppServices for testing error/question paths.
        /// </summary>
        private class MockAppServices : IAppServices
        {
            public bool ErrorShown { get; private set; }
            public string LastError { get; private set; }
            public bool QuestionResult { get; set; } = false;

            public void ShowError(string message)
            {
                ErrorShown = true;
                LastError = message;
            }

            public void ShowInfo(string message) { }

            public bool ShowQuestion(string message) => QuestionResult;

            public bool ShowYesNo(string message) => QuestionResult;

            public void RunOnUIThread(Action action) => action();

            public bool IsMainThread() => true;
        }
    }
}
