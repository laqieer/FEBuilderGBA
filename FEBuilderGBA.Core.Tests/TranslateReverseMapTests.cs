using System;
using System.IO;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Tests for the reverse English→Japanese lookup map in MyTranslateResource,
    /// which enables Avalonia (using English keys) to find translations in
    /// non-English target language files that use Japanese keys.
    /// </summary>
    [Collection("SharedState")]
    public class TranslateReverseMapTests : IDisposable
    {
        // Save original state
        readonly IAppServices _origServices;

        public TranslateReverseMapTests()
        {
            _origServices = CoreState.Services;
            // Provide a headless IAppServices so LoadResource won't crash on missing services
            if (CoreState.Services == null)
                CoreState.Services = new HeadlessAppServices();
        }

        public void Dispose()
        {
            // Restore original state
            MyTranslateResource.Clear();
            CoreState.Services = _origServices;
        }

        [Fact]
        public void ChineseMode_EnglishKey_ReturnsChineseTranslation()
        {
            // Arrange: en.txt maps Japanese→English, zh.txt maps Japanese→Chinese
            string enFile = Path.GetTempFileName();
            string zhFile = Path.GetTempFileName();
            try
            {
                File.WriteAllText(enFile,
                    ":書き込み\n" +
                    "Write to ROM\n" +
                    "\n" +
                    ":先頭アドレス\n" +
                    "Hex Address\n");

                File.WriteAllText(zhFile,
                    ":書き込み\n" +
                    "保存\n" +
                    "\n" +
                    ":先頭アドレス\n" +
                    "十六进制地址\n");

                // Act: Load Chinese translations, then build reverse English map
                MyTranslateResource.LoadResource(zhFile);
                MyTranslateResource.LoadReverseEnglishMap(enFile);

                // Assert: English key → Japanese key → Chinese translation
                Assert.Equal("保存", MyTranslateResource.str("Write to ROM"));
                Assert.Equal("十六进制地址", MyTranslateResource.str("Hex Address"));
            }
            finally
            {
                File.Delete(enFile);
                File.Delete(zhFile);
            }
        }

        [Fact]
        public void JapaneseMode_EnglishKey_ReturnsJapaneseKey()
        {
            // Arrange: Load reverse map, then clear Dic (simulating Japanese mode)
            string enFile = Path.GetTempFileName();
            try
            {
                File.WriteAllText(enFile,
                    ":書き込み\n" +
                    "Write to ROM\n" +
                    "\n" +
                    ":先頭アドレス\n" +
                    "Hex Address\n");

                // Act: Load reverse map, then clear (Japanese mode)
                MyTranslateResource.LoadReverseEnglishMap(enFile);
                MyTranslateResource.Clear();

                // Assert: English key → Japanese key (Dic is empty, so return Japanese key)
                Assert.Equal("書き込み", MyTranslateResource.str("Write to ROM"));
                Assert.Equal("先頭アドレス", MyTranslateResource.str("Hex Address"));
            }
            finally
            {
                File.Delete(enFile);
            }
        }

        [Fact]
        public void EnglishMode_EnglishKey_PassesThrough()
        {
            // Arrange: Load en.txt only (no reverse map needed for English mode)
            string enFile = Path.GetTempFileName();
            try
            {
                File.WriteAllText(enFile,
                    ":書き込み\n" +
                    "Write to ROM\n");

                // Act: Load English translations (Japanese key → English value)
                MyTranslateResource.LoadResource(enFile);
                // Do NOT load reverse map — English mode doesn't need it

                // Assert: "Write to ROM" is not a key in Dic (keys are Japanese),
                // so it passes through as-is
                Assert.Equal("Write to ROM", MyTranslateResource.str("Write to ROM"));
            }
            finally
            {
                File.Delete(enFile);
            }
        }

        [Fact]
        public void BackwardCompat_JapaneseKey_StillWorks()
        {
            // Arrange: Chinese mode with reverse map — direct Japanese key lookup still works
            string enFile = Path.GetTempFileName();
            string zhFile = Path.GetTempFileName();
            try
            {
                File.WriteAllText(enFile,
                    ":書き込み\n" +
                    "Write to ROM\n");

                File.WriteAllText(zhFile,
                    ":書き込み\n" +
                    "保存\n");

                // Act
                MyTranslateResource.LoadResource(zhFile);
                MyTranslateResource.LoadReverseEnglishMap(enFile);

                // Assert: Direct Japanese key lookup (WinForms path) still works
                Assert.Equal("保存", MyTranslateResource.str("書き込み"));
            }
            finally
            {
                File.Delete(enFile);
                File.Delete(zhFile);
            }
        }

        [Fact]
        public void MissingEnFile_GracefulFallback()
        {
            // Arrange: Load zh.txt, call LoadReverseEnglishMap on nonexistent path
            string zhFile = Path.GetTempFileName();
            try
            {
                File.WriteAllText(zhFile,
                    ":書き込み\n" +
                    "保存\n");

                MyTranslateResource.LoadResource(zhFile);

                // Act: Load reverse map from nonexistent file — should not throw
                string nonexistent = Path.Combine(Path.GetTempPath(), "nonexistent_" + Guid.NewGuid() + ".txt");
                MyTranslateResource.LoadReverseEnglishMap(nonexistent);

                // Assert: Direct Japanese lookup still works, English key passes through
                Assert.Equal("保存", MyTranslateResource.str("書き込み"));
                Assert.Equal("Write to ROM", MyTranslateResource.str("Write to ROM"));
            }
            finally
            {
                File.Delete(zhFile);
            }
        }

        [Fact]
        public void DirectLookup_ChineseMode_ReturnsChineseForAvaloniaKey()
        {
            // Arrange: zh.txt has direct English key → Chinese value entries
            // (no reverse map needed for these keys)
            string enFile = Path.GetTempFileName();
            string zhFile = Path.GetTempFileName();
            try
            {
                // en.txt has English identity entries for Avalonia keys
                File.WriteAllText(enFile,
                    ":Characters\n" +
                    "Characters\n" +
                    "\n" +
                    ":Items\n" +
                    "Items\n");

                // zh.txt has direct English key → Chinese translation
                File.WriteAllText(zhFile,
                    ":Characters\n" +
                    "角色\n" +
                    "\n" +
                    ":Items\n" +
                    "道具\n");

                // Act: Load Chinese translations (direct key = English Avalonia key)
                MyTranslateResource.LoadResource(zhFile);
                MyTranslateResource.LoadReverseEnglishMap(enFile);

                // Assert: Direct lookup of Avalonia English key → Chinese
                Assert.Equal("角色", MyTranslateResource.str("Characters"));
                Assert.Equal("道具", MyTranslateResource.str("Items"));
            }
            finally
            {
                File.Delete(enFile);
                File.Delete(zhFile);
            }
        }

        [Fact]
        public void DirectLookup_JapaneseMode_ReturnsJapaneseForAvaloniaKey()
        {
            // Arrange: ja.txt has direct English key → Japanese value entries
            string enFile = Path.GetTempFileName();
            string jaFile = Path.GetTempFileName();
            try
            {
                File.WriteAllText(enFile,
                    ":Characters\n" +
                    "Characters\n" +
                    "\n" +
                    ":Items\n" +
                    "Items\n");

                File.WriteAllText(jaFile,
                    ":Characters\n" +
                    "キャラクター\n" +
                    "\n" +
                    ":Items\n" +
                    "アイテム\n");

                // Act: Load ja.txt (simulating Japanese mode with ja.txt)
                MyTranslateResource.LoadResource(jaFile);
                MyTranslateResource.LoadReverseEnglishMap(enFile);

                // Assert: Direct lookup of Avalonia English key → Japanese
                Assert.Equal("キャラクター", MyTranslateResource.str("Characters"));
                Assert.Equal("アイテム", MyTranslateResource.str("Items"));
            }
            finally
            {
                File.Delete(enFile);
                File.Delete(jaFile);
            }
        }

        [Fact]
        public void DirectLookup_ChineseMode_BothJapaneseAndEnglishKeysWork()
        {
            // Arrange: zh.txt has both Japanese keys (WinForms compat) and English keys (Avalonia)
            string enFile = Path.GetTempFileName();
            string zhFile = Path.GetTempFileName();
            try
            {
                File.WriteAllText(enFile,
                    ":書き込み\n" +
                    "Write to ROM\n" +
                    "\n" +
                    ":Characters\n" +
                    "Characters\n");

                File.WriteAllText(zhFile,
                    ":書き込み\n" +
                    "保存\n" +
                    "\n" +
                    ":Characters\n" +
                    "角色\n");

                // Act
                MyTranslateResource.LoadResource(zhFile);
                MyTranslateResource.LoadReverseEnglishMap(enFile);

                // Assert: Both lookup paths work simultaneously
                Assert.Equal("保存", MyTranslateResource.str("書き込み"));       // WinForms Japanese key
                Assert.Equal("保存", MyTranslateResource.str("Write to ROM"));  // Reverse English→Japanese chain
                Assert.Equal("角色", MyTranslateResource.str("Characters"));    // Direct Avalonia English key
            }
            finally
            {
                File.Delete(enFile);
                File.Delete(zhFile);
            }
        }

        [Fact]
        public void EnglishMode_AvaloniaKeysInEnFile_ReturnsEnglish()
        {
            // Arrange: en.txt has identity mapping for Avalonia keys
            string enFile = Path.GetTempFileName();
            try
            {
                File.WriteAllText(enFile,
                    ":Characters\n" +
                    "Characters\n" +
                    "\n" +
                    ":Items\n" +
                    "Items\n");

                // Act: English mode — load en.txt
                MyTranslateResource.LoadResource(enFile);

                // Assert: Avalonia English key → same English value via Dic lookup
                Assert.Equal("Characters", MyTranslateResource.str("Characters"));
                Assert.Equal("Items", MyTranslateResource.str("Items"));
            }
            finally
            {
                File.Delete(enFile);
            }
        }

        [Fact]
        public void AvaloniaViewStrings_ChineseMode_TranslatesCommonLabels()
        {
            // Arrange: Simulate the Avalonia translation entries that were added to en.txt and zh.txt
            string enFile = Path.GetTempFileName();
            string zhFile = Path.GetTempFileName();
            try
            {
                // en.txt: self-referencing entries for Avalonia-only strings
                File.WriteAllText(enFile,
                    ":Address:\n" +
                    "Address:\n" +
                    "\n" +
                    ":Write\n" +
                    "Write\n" +
                    "\n" +
                    ":Close\n" +
                    "Close\n" +
                    "\n" +
                    ":Cancel\n" +
                    "Cancel\n" +
                    "\n" +
                    ":Unit Editor\n" +
                    "Unit Editor\n");

                // zh.txt: Chinese translations for the same keys
                File.WriteAllText(zhFile,
                    ":Address:\n" +
                    "\u5730\u5740:\n" +
                    "\n" +
                    ":Write\n" +
                    "\u5199\u5165\n" +
                    "\n" +
                    ":Close\n" +
                    "\u5173\u95ed\n" +
                    "\n" +
                    ":Cancel\n" +
                    "\u53d6\u6d88\n" +
                    "\n" +
                    ":Unit Editor\n" +
                    "\u5355\u4f4d\u7f16\u8f91\u5668\n");

                // Act: Load Chinese mode
                MyTranslateResource.LoadResource(zhFile);
                MyTranslateResource.LoadReverseEnglishMap(enFile);

                // Assert: Avalonia English keys translate to Chinese
                Assert.Equal("\u5730\u5740:", MyTranslateResource.str("Address:"));
                Assert.Equal("\u5199\u5165", MyTranslateResource.str("Write"));
                Assert.Equal("\u5173\u95ed", MyTranslateResource.str("Close"));
                Assert.Equal("\u53d6\u6d88", MyTranslateResource.str("Cancel"));
                Assert.Equal("\u5355\u4f4d\u7f16\u8f91\u5668", MyTranslateResource.str("Unit Editor"));
            }
            finally
            {
                File.Delete(enFile);
                File.Delete(zhFile);
            }
        }

        // -------------------------------------------------------------------
        // Issue #356 \u2014 LF/CRLF newline normalization in MyTranslateResourceLow.str()
        //
        // AXAML literals decoded from `&#x0a;` XML entities use bare LF, while
        // the translation files store the literal `\r\n` escape that the parser
        // decodes to CRLF (or sometimes plain LF after manual editing). The
        // runtime must succeed when the runtime string and the stored key
        // differ only in newline form. Tests below cover each lookup path:
        //   - direct lookup (Japanese mode against a Japanese-keyed file)
        //   - reverse-English chain (Avalonia English literal -> Japanese key)
        //   - explicit LF\u2194CRLF mismatch in both directions
        //
        // These tests address Copilot CLI PR #458 round-2 review: the LF/CRLF
        // fallback added by commit 58c65ef64 had no targeted Core-side test.
        // -------------------------------------------------------------------

        [Fact]
        public void LfInput_MatchesCrlfStoredKey_DirectLookup()
        {
            // Translation file stores key with literal `\r\n` (parser decodes
            // to CRLF). Lookup with bare-LF runtime string should still hit
            // via the 2c LF\u2192CRLF normalization fallback.
            string langFile = Path.GetTempFileName();
            try
            {
                // After parser decoding, the key is "Help\r\nLine2"
                File.WriteAllText(langFile,
                    ":Help\\r\\nLine2\n" +
                    "Help-translated\n" +
                    "\n");
                MyTranslateResource.LoadResource(langFile);

                // Runtime string uses bare LF (as Avalonia produces from &#x0a;)
                string lfInput = "Help\nLine2";
                Assert.Equal("Help-translated", MyTranslateResource.str(lfInput));
            }
            finally
            {
                File.Delete(langFile);
            }
        }

        [Fact]
        public void LfInput_MatchesCrlfReverseEnMapKey()
        {
            // en.txt reverse map has a CRLF key (Avalonia English literal
            // stored as `Other ROM\r\nData Address` after \\r\\n decode).
            // The runtime hits the reverse chain with a bare-LF input and
            // should still resolve via the LF\u2192CRLF normalization branch
            // inside the reverse-map lookup.
            string enFile = Path.GetTempFileName();
            string zhFile = Path.GetTempFileName();
            try
            {
                // en.txt maps a Japanese key to a CRLF-keyed English value
                File.WriteAllText(enFile,
                    ":\u4ed6\u306eROM\\r\\n\u30c7\u30fc\u30bf\u30a2\u30c9\u30ec\u30b9\n" +
                    "Other ROM\\r\\nData Address\n" +
                    "\n");
                File.WriteAllText(zhFile,
                    ":\u4ed6\u306eROM\\r\\n\u30c7\u30fc\u30bf\u30a2\u30c9\u30ec\u30b9\n" +
                    "\u5176\u4ed6ROM\\r\\n\u6570\u636e\u5730\u5740\n" +
                    "\n");

                MyTranslateResource.LoadResource(zhFile);
                MyTranslateResource.LoadReverseEnglishMap(enFile);

                // Runtime input uses LF (as Avalonia produces from &#x0a;)
                string lfInput = "Other ROM\nData Address";
                Assert.Equal("\u5176\u4ed6ROM\r\n\u6570\u636e\u5730\u5740",
                    MyTranslateResource.str(lfInput));
            }
            finally
            {
                File.Delete(enFile);
                File.Delete(zhFile);
            }
        }

        [Fact]
        public void NewlineNormalization_PassesThroughWhenNoMatch()
        {
            // If neither LF nor CRLF variants match, the fallback chain
            // should pass through cleanly (return the source string) \u2014 i.e.
            // not crash, not return an empty string. Regression guard for
            // multi-line literals not yet translated.
            string langFile = Path.GetTempFileName();
            try
            {
                File.WriteAllText(langFile,
                    ":SomeOtherKey\n" +
                    "value\n" +
                    "\n");
                MyTranslateResource.LoadResource(langFile);

                string lfInput = "Unmatched\nliteral";
                Assert.Equal(lfInput, MyTranslateResource.str(lfInput));
            }
            finally
            {
                File.Delete(langFile);
            }
        }

    }
}
