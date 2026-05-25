using System;
using System.Collections.Generic;
using System.IO;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// ViewModel for the Avalonia ROM Translation Tool dialog. Mirrors the
    /// WinForms <c>ToolTranslateROMForm</c> state surface so the Avalonia
    /// view reproduces the same panel layout, default-checked controls, and
    /// multibyte-only visibility rules.
    ///
    /// The actual ROM-translation execution paths (ImportAllText / ExportallText
    /// / ImportFont / SimpleFireButton) remain WinForms-coupled until the Core
    /// extraction follow-up #536 lands. Until then the action buttons render
    /// with <c>IsEnabled="False"</c> and a tooltip referencing #536.
    /// </summary>
    public class ToolTranslateROMViewModel : ViewModelBase
    {
        // --- File paths (Simple + Detail tabs share most of these) ---
        string _fromRomPath = string.Empty;
        string _toRomPath = string.Empty;
        string _translateDataPath = string.Empty;
        string _extraFontRomPath = string.Empty;
        string _fontRomPath = string.Empty;
        string _useFontName = string.Empty;

        public string FromRomPath { get => _fromRomPath; set => SetField(ref _fromRomPath, value); }
        public string ToRomPath { get => _toRomPath; set => SetField(ref _toRomPath, value); }
        public string TranslateDataPath { get => _translateDataPath; set => SetField(ref _translateDataPath, value); }
        public string ExtraFontRomPath { get => _extraFontRomPath; set => SetField(ref _extraFontRomPath, value); }
        public string FontRomPath { get => _fontRomPath; set => SetField(ref _fontRomPath, value); }
        public string UseFontName { get => _useFontName; set => SetField(ref _useFontName, value); }

        // --- Checkbox state (defaults mirror WF Designer/CheckedChanged behavior) ---
        bool _simpleOverrideJpFont;           // SIMPLE_OVERRAIDE_JPFONT - WF default unchecked
        bool _overrideJpFont;                  // X_OVERRAIDE_JPFONT - WF default unchecked
        bool _useAutoTranslate = true;         // useAutoTranslateCheckBox - WF default checked
        bool _oneLinerCheck;                   // X_ONELINER_CHECK - WF default unchecked
        bool _modifiedTextOnly = true;         // X_MODIFIED_TEXT_ONLY - WF default checked
        bool _fontAutoGenerate = true;         // FontAutoGenelateCheckBox - WF default checked

        public bool SimpleOverrideJpFont { get => _simpleOverrideJpFont; set => SetField(ref _simpleOverrideJpFont, value); }
        public bool OverrideJpFont { get => _overrideJpFont; set => SetField(ref _overrideJpFont, value); }
        public bool UseAutoTranslate { get => _useAutoTranslate; set => SetField(ref _useAutoTranslate, value); }
        public bool OneLinerCheck { get => _oneLinerCheck; set => SetField(ref _oneLinerCheck, value); }
        public bool ModifiedTextOnly { get => _modifiedTextOnly; set => SetField(ref _modifiedTextOnly, value); }
        public bool FontAutoGenerate
        {
            get => _fontAutoGenerate;
            set
            {
                if (SetField(ref _fontAutoGenerate, value))
                {
                    // IsFontRomPickerEnabled is derived from FontAutoGenerate, so
                    // raise its change notification when the source flips. Avalonia
                    // binding negation (`!FontAutoGenerate`) is unreliable and not
                    // used elsewhere in the repo - prefer a derived property.
                    OnPropertyChanged(nameof(IsFontRomPickerEnabled));
                }
            }
        }

        /// <summary>
        /// True when the user has manually opted out of font auto-generation and is
        /// providing a Font ROM path themselves. Drives `IsEnabled` on the FontRom
        /// TextBox and Browse button so the WF behavior (`FontROMTextBox.Enabled =
        /// !FontAutoGenelateCheckBox.Checked`) is preserved without using a
        /// negation operator inside an Avalonia `{Binding ...}` expression.
        /// </summary>
        public bool IsFontRomPickerEnabled => !FontAutoGenerate;

        // --- Language combo state + items ---
        int _fromLanguageIndex;
        int _toLanguageIndex;

        public int FromLanguageIndex
        {
            get => _fromLanguageIndex;
            set
            {
                if (SetField(ref _fromLanguageIndex, value))
                {
                    AutoPopulateRomPathForFromLanguage();
                }
            }
        }
        public int ToLanguageIndex
        {
            get => _toLanguageIndex;
            set
            {
                if (SetField(ref _toLanguageIndex, value))
                {
                    AutoPopulateRomPathForToLanguage();
                }
            }
        }

        /// <summary>
        /// UndoService that handlers wrap their ROM mutations in. Exposed
        /// (and `virtual` Begin/Commit/Rollback) so tests can install a spy
        /// implementation and assert Begin/Commit/Rollback ordering (#536).
        /// </summary>
        public UndoService UndoService { get; set; } = new UndoService();

        /// <summary>
        /// Re-populate `FromRomPath` from the Core FindOrignalROMByLang
        /// helper when the FROM-language combo changes. Mirrors WF
        /// `Translate_from_SelectedIndexChanged`. No-op when no ROM is loaded.
        /// </summary>
        void AutoPopulateRomPathForFromLanguage()
        {
            var rom = CoreState.ROM;
            if (rom?.RomInfo == null || string.IsNullOrEmpty(rom.Filename)) return;
            if (FromLanguageIndex < 0 || FromLanguageIndex >= FromLanguageItemsRaw.Length) return;

            string fromLang = ToolTranslateROMCore.ParseLanguageKey(FromLanguageItemsRaw[FromLanguageIndex]);
            string dir = Path.GetDirectoryName(rom.Filename) ?? string.Empty;
            string path = ToolTranslateROMCore.FindOrignalROMByLang(
                dir, fromLang, rom.RomInfo.version,
                CoreState.BaseDirectory ?? string.Empty,
                lastROMFilename: rom.Filename ?? string.Empty,
                emulatorDirectory: GetEmulatorDirectory());
            if (!string.IsNullOrEmpty(path))
            {
                FromRomPath = path;
            }
        }

        /// <summary>
        /// Resolve the configured emulator directory for the
        /// FindOrignalROMByLang last-resort recursive scan. Mirrors WF
        /// `Path.GetDirectoryName(Program.Config.at("emulator"))`. Returns
        /// empty when the emulator setting isn't configured - the Core
        /// helper then skips the recursive scan entirely.
        /// </summary>
        static string GetEmulatorDirectory()
        {
            var cfg = CoreState.Config;
            if (cfg == null) return string.Empty;
            string emulator = cfg.at("emulator", string.Empty);
            if (string.IsNullOrEmpty(emulator)) return string.Empty;
            try
            {
                return Path.GetDirectoryName(emulator) ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Re-populate `ToRomPath` (and `FontRomPath`) from the Core
        /// FindOrignalROMByLang helper when the TO-language combo changes.
        /// Mirrors WF `Translate_to_SelectedIndexChanged`.
        /// </summary>
        void AutoPopulateRomPathForToLanguage()
        {
            var rom = CoreState.ROM;
            if (rom?.RomInfo == null || string.IsNullOrEmpty(rom.Filename)) return;
            if (ToLanguageIndex < 0 || ToLanguageIndex >= ToLanguageItemsRaw.Length) return;

            string toLang = ToolTranslateROMCore.ParseLanguageKey(ToLanguageItemsRaw[ToLanguageIndex]);
            string dir = Path.GetDirectoryName(rom.Filename) ?? string.Empty;
            string path = ToolTranslateROMCore.FindOrignalROMByLang(
                dir, toLang, rom.RomInfo.version,
                CoreState.BaseDirectory ?? string.Empty,
                lastROMFilename: rom.Filename ?? string.Empty,
                emulatorDirectory: GetEmulatorDirectory());
            if (!string.IsNullOrEmpty(path))
            {
                ToRomPath = path;
                FontRomPath = path;
            }
        }

        /// <summary>
        /// FROM-language combo items, mirrors WF Designer's Translate_from items
        /// at index order: 0=ja, 1=en, 2=zh-CN. Each item is the literal WF
        /// Japanese-suffixed key (e.g. "ja=日本語") that gets pushed through
        /// `R._(...)` so the visible display follows the active UI language
        /// without breaking the `lang=Name` prefix parsing the WF action code
        /// uses (`U.InnerSplit(text, "=", 0)`).
        /// </summary>
        public string[] FromLanguageItems
        {
            get
            {
                var arr = new string[FromLanguageItemsRaw.Length];
                for (int i = 0; i < FromLanguageItemsRaw.Length; i++)
                {
                    arr[i] = R._(FromLanguageItemsRaw[i]);
                }
                return arr;
            }
        }

        /// <summary>
        /// TO-language combo items - same WF-keyed convention as FromLanguageItems.
        /// </summary>
        public string[] ToLanguageItems
        {
            get
            {
                var arr = new string[ToLanguageItemsRaw.Length];
                for (int i = 0; i < ToLanguageItemsRaw.Length; i++)
                {
                    arr[i] = R._(ToLanguageItemsRaw[i]);
                }
                return arr;
            }
        }

        /// <summary>
        /// Raw FROM language keys - exposed as static so parity tests can reach
        /// the canonical list without instantiating the VM or going through
        /// the translation layer.
        /// </summary>
        public static readonly string[] FromLanguageItemsRaw =
        {
            "ja=日本語",
            "en=英語",
            "zh-CN=中国語",
        };

        /// <summary>Raw TO language keys.</summary>
        public static readonly string[] ToLanguageItemsRaw =
        {
            "ja=日本語",
            "en=英語",
            "zh-CN=中国語 簡体",
            "zh-TW=中国語 繁体",
            "es=スペイン語",
            "hi=ヒンディー語",
            "ar=アラビア語",
            "pt=ポルトガル語",
            "ru=ロシア語",
            "fr=フランス語",
            "eo=エスペランド語",
        };

        // --- ROM-name display labels (mirrors WF MakeROMName) ---
        string _fromRomLabel = string.Empty;
        string _toRomLabel = string.Empty;

        public string FromRomLabel { get => _fromRomLabel; set => SetField(ref _fromRomLabel, value); }
        public string ToRomLabel { get => _toRomLabel; set => SetField(ref _toRomLabel, value); }

        bool _isLoaded;
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        // --- Visibility ---

        /// <summary>
        /// True when the JP-font override checkboxes should be visible.
        /// WF rule (from ToolTranslateROMForm constructor):
        ///   if (!is_multibyte AND version >= 7) hide both checkboxes.
        /// Otherwise show them.
        /// </summary>
        public bool ShowJpFontOverride
        {
            get
            {
                var rom = CoreState.ROM;
                if (rom?.RomInfo == null) return true;
                return CalcShowJpFontOverride(rom.RomInfo.is_multibyte, rom.RomInfo.version);
            }
        }

        /// <summary>Pure helper for ShowJpFontOverride - testable without a live ROM.</summary>
        public static bool CalcShowJpFontOverride(bool isMultibyte, int version)
        {
            // WF: hide when (!is_multibyte AND version >= 7), otherwise show.
            return !(!isMultibyte && version >= 7);
        }

        /// <summary>
        /// Initialize the VM from CoreState.ROM. Safe to call without a ROM
        /// (early-returns). Mirrors WF ToolTranslateROMForm constructor
        /// (MakeROMName + TranslateLanguageAutoSelect).
        /// </summary>
        public void Initialize()
        {
            var rom = CoreState.ROM;
            if (rom?.RomInfo == null)
            {
                IsLoaded = false;
                FromRomLabel = string.Empty;
                ToRomLabel = string.Empty;
                FromLanguageIndex = 0;
                ToLanguageIndex = 0;
                return;
            }

            // --- MakeROMName (now Core-delegated) ---
            int version = rom.RomInfo.version;
            bool isMultibyte = rom.RomInfo.is_multibyte;
            var (fromLabel, toLabel) = ToolTranslateROMCore.MakeROMName(version, isMultibyte);
            FromRomLabel = fromLabel;
            ToRomLabel = toLabel;

            // --- TranslateLanguageAutoSelect (WF TranslateTextUtil) ---
            var (from, to) = CalcDefaultLanguageIndexes(
                isMultibyte, CoreState.TextEncoding, CoreState.Language ?? "en");
            FromLanguageIndex = from;
            ToLanguageIndex = to;

            IsLoaded = true;
        }

        /// <summary>
        /// Pure-function port of WF TranslateTextUtil.TranslateLanguageAutoSelect.
        /// Returns the (fromIndex, toIndex) defaults for the language combos
        /// given the current ROM multibyte flag, text encoding, and UI language.
        ///
        /// FROM rule: multibyte + ZH_TBL =&gt; 2 (zh-CN); multibyte + other =&gt; 0 (ja);
        /// non-multibyte =&gt; 1 (en).
        ///
        /// TO rule: lang "zh" =&gt; 2; lang "en" =&gt; 1; otherwise (defaults ja) =&gt; 0.
        ///
        /// Collision handling: when from == to, the WF code swaps to to its peer
        /// (0 &lt;-&gt; 1) so the combos never start on the same language.
        /// </summary>
        public static (int from, int to) CalcDefaultLanguageIndexes(
            bool isMultibyte, TextEncodingEnum textEncoding, string lang)
        {
            int from;
            if (isMultibyte)
            {
                from = textEncoding == TextEncodingEnum.ZH_TBL ? 2 : 0;
            }
            else
            {
                from = 1;
            }

            int to = lang switch
            {
                "zh" => 2,
                "en" => 1,
                _ => 0,
            };

            if (from == to)
            {
                to = to == 0 ? 1 : 0;
            }

            return (from, to);
        }
    }
}
