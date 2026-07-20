using System;
using System.Collections.Generic;
using System.IO;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// ViewModel for the user preferences (Options) dialog.
    /// </summary>
    public class OptionsViewModel : ViewModelBase
    {
        string _language = "en";
        List<string> _availableLanguages = new();
        string _gitPath = "git";
        bool _autoBackup = true;
        bool _autoUpdateEnabled = true;
        // Preserve the raw func_auto_update interval (0/1/3/7 — off/daily/every-3-days/weekly, per
        // the WinForms OptionForm combo) so toggling the Avalonia checkbox doesn't silently collapse
        // a user's chosen 1/7 interval to 3 in the shared config.xml (#1804).
        string _autoUpdateRaw = "3";
        bool _autoSaveEnabled = false;
        int _autoSaveIntervalMinutes = 5;

        // External tool paths — keys match WinForms config.xml exactly
        string _emulator = "";
        string _emulator2 = "";
        string _binaryEditor = "";
        string _program1 = "";
        string _program2 = "";
        string _program3 = "";
        string _sappy = "";
        string _mid2agb = "";
        string _gbaMusRiper = "";
        string _sox = "";
        string _midfix4agb = "";
        string _eventAssembler = "";
        string _devkitproEabi = "";
        string _goldroadAsm = "";
        string _cflags = "";
        string _retdec = "";
        string _python3 = "";
        string _feclib = "";
        string _srccodeTexteditor = "";
        string _srccodeDirectory = "";

        // Submodule remote URLs
        string _submodulePatch2Url = "";
        string _submoduleFERepoUrl = "";
        string _submoduleFERepoMusicUrl = "";

        // FEMapCreator (external random-map-generation tool) setup — #1978 Slice 2.
        // Optional/empty by default: opening Options never assumes the tool is installed and
        // never probes the filesystem beyond what Validate() below does on demand for display.
        string _femapCreatorPath = "";
        string _femapCreatorAssetsRoot = "";

        // Per-dialog-session (NEVER static/shared) reuse cache for the FEMapCreator executable's
        // SHA-256 hash. A fresh OptionsViewModel — i.e. every time Options is opened — gets a
        // fresh, empty cache, so there is no cross-session/cross-ROM staleness risk; it exists
        // solely so GetFEMapCreatorSetupSnapshot() doesn't re-hash an unchanged executable on
        // every keystroke typed into either FEMapCreator textbox (Slice 2 review follow-up).
        readonly FEMapCreatorExecutableIdentityCache _femapCreatorExecutableIdentityCache = new();

        /// <summary>Current language selection entry (e.g. "en — English").</summary>
        public string Language
        {
            get => _language;
            set => SetField(ref _language, value);
        }

        /// <summary>Available languages enumerated from config/translate/ directory.</summary>
        public List<string> AvailableLanguages
        {
            get => _availableLanguages;
            set => SetField(ref _availableLanguages, value);
        }

        /// <summary>Path to git executable.</summary>
        public string GitPath
        {
            get => _gitPath;
            set => SetField(ref _gitPath, value);
        }

        /// <summary>Whether to auto-backup ROM before saving.</summary>
        public bool AutoBackup
        {
            get => _autoBackup;
            set => SetField(ref _autoBackup, value);
        }

        /// <summary>Whether Avalonia should periodically check GitHub releases for app updates.</summary>
        public bool AutoUpdateEnabled
        {
            get => _autoUpdateEnabled;
            set => SetField(ref _autoUpdateEnabled, value);
        }

        /// <summary>Whether auto-save to sidecar file is enabled.</summary>
        public bool AutoSaveEnabled
        {
            get => _autoSaveEnabled;
            set => SetField(ref _autoSaveEnabled, value);
        }

        /// <summary>Auto-save interval in minutes (1-60).</summary>
        public int AutoSaveIntervalMinutes
        {
            get => _autoSaveIntervalMinutes;
            set => SetField(ref _autoSaveIntervalMinutes, Math.Clamp(value, 1, 60));
        }

        // ---- External Tool Paths ----

        public string Emulator { get => _emulator; set => SetField(ref _emulator, value); }
        public string Emulator2 { get => _emulator2; set => SetField(ref _emulator2, value); }
        public string BinaryEditor { get => _binaryEditor; set => SetField(ref _binaryEditor, value); }
        public string Program1 { get => _program1; set => SetField(ref _program1, value); }
        public string Program2 { get => _program2; set => SetField(ref _program2, value); }
        public string Program3 { get => _program3; set => SetField(ref _program3, value); }
        public string Sappy { get => _sappy; set => SetField(ref _sappy, value); }
        public string Mid2agb { get => _mid2agb; set => SetField(ref _mid2agb, value); }
        public string GbaMusRiper { get => _gbaMusRiper; set => SetField(ref _gbaMusRiper, value); }
        public string Sox { get => _sox; set => SetField(ref _sox, value); }
        public string Midfix4agb { get => _midfix4agb; set => SetField(ref _midfix4agb, value); }
        public string EventAssembler { get => _eventAssembler; set => SetField(ref _eventAssembler, value); }
        public string DevkitproEabi { get => _devkitproEabi; set => SetField(ref _devkitproEabi, value); }
        public string GoldroadAsm { get => _goldroadAsm; set => SetField(ref _goldroadAsm, value); }
        public string Cflags { get => _cflags; set => SetField(ref _cflags, value); }
        public string Retdec { get => _retdec; set => SetField(ref _retdec, value); }
        public string Python3 { get => _python3; set => SetField(ref _python3, value); }
        public string Feclib { get => _feclib; set => SetField(ref _feclib, value); }
        public string SrccodeTexteditor { get => _srccodeTexteditor; set => SetField(ref _srccodeTexteditor, value); }
        public string SrccodeDirectory { get => _srccodeDirectory; set => SetField(ref _srccodeDirectory, value); }

        // ---- Submodule Remote URLs ----

        public string SubmodulePatch2Url { get => _submodulePatch2Url; set => SetField(ref _submodulePatch2Url, value); }
        public string SubmoduleFERepoUrl { get => _submoduleFERepoUrl; set => SetField(ref _submoduleFERepoUrl, value); }
        public string SubmoduleFERepoMusicUrl { get => _submoduleFERepoMusicUrl; set => SetField(ref _submoduleFERepoMusicUrl, value); }

        // ---- FEMapCreator (external random-map-generation tool) setup ----
        // #1978 Slice 2: durable configuration only. No auto-download/install/PATH-search, and
        // no per-tileset mapping editing lives here (that stays in the Map Editor for Slice 3).

        /// <summary>Absolute path to the external FEMapCreator executable (empty = not configured).</summary>
        public string FEMapCreatorPath { get => _femapCreatorPath; set => SetField(ref _femapCreatorPath, value); }

        /// <summary>Optional absolute path to an external FEMapCreator assets root (empty is valid).</summary>
        public string FEMapCreatorAssetsRoot { get => _femapCreatorAssetsRoot; set => SetField(ref _femapCreatorAssetsRoot, value); }

        /// <summary>
        /// Re-validates the current (possibly unsaved) FEMapCreator path/assets-root values on
        /// demand. Pure/read-only — only stats the filesystem, never launches a process or
        /// touches the network. Callers (the Options view) use this to render a live status line
        /// as the user edits the fields, without requiring a ROM to be loaded.
        /// </summary>
        public FEMapCreatorSetupSnapshot GetFEMapCreatorSetupSnapshot()
        {
            return FEMapCreatorProfileCore.Validate(FEMapCreatorPath, FEMapCreatorAssetsRoot, _femapCreatorExecutableIdentityCache);
        }

        /// <summary>
        /// Test-only seam exposing this session's executable-identity cache so tests can assert
        /// hash-reuse behavior (e.g. <c>HashComputeCount</c> stays flat across repeated calls with
        /// an unchanged executable). Never read by production code; internal, not public API.
        /// </summary>
        internal FEMapCreatorExecutableIdentityCache FEMapCreatorExecutableIdentityCacheForTests => _femapCreatorExecutableIdentityCache;

        /// <summary>
        /// Human-readable, localized summary of <see cref="GetFEMapCreatorSetupSnapshot"/> for
        /// display in the Options view.
        /// </summary>
        public string GetFEMapCreatorStatusText()
        {
            FEMapCreatorSetupSnapshot snapshot = GetFEMapCreatorSetupSnapshot();
            switch (snapshot.Status)
            {
                case FEMapCreatorSetupStatus.NotConfigured:
                    return R._("Not configured (optional).");
                case FEMapCreatorSetupStatus.Configured:
                    return R._("Configured.");
                default:
                    return R._("Invalid") + ": " + (snapshot.ErrorMessage ?? R._("unknown error"));
            }
        }

        /// <summary>Load settings from CoreState and Config.</summary>
        public void Load()
        {
            IsLoading = true;
            try
            {
                // Enumerate available languages from config/translate/*.txt
                AvailableLanguages = EnumerateLanguages();

                // Read current values from CoreState / Config
                // Find display string matching current language code
                string code = CoreState.Language ?? "en";
                Language = AvailableLanguages.Find(s => s.StartsWith(code + " ")) ?? code;
                GitPath = CoreState.GitPath ?? "git";

                var cfg = CoreState.Config;
                if (cfg != null)
                {
                    // func_auto_backup: 0=None, 1=SmartBackup, 2=FullBackup (default 2)
                    int backupVal = 2;
                    int.TryParse(cfg.at("func_auto_backup", "2"), out backupVal);
                    AutoBackup = backupVal > 0;
                    _autoUpdateRaw = cfg.at("func_auto_update", "3");
                    AutoUpdateEnabled = _autoUpdateRaw != "0";

                    // Auto-save settings
                    AutoSaveEnabled = cfg.at("autosave_enabled", "false") == "true";
                    int asInterval = 5;
                    int.TryParse(cfg.at("autosave_interval_minutes", "5"), out asInterval);
                    AutoSaveIntervalMinutes = Math.Clamp(asInterval, 1, 60);

                    // Load all tool paths using WinForms-compatible keys
                    Emulator = GetToolPath(cfg, "emulator", "Emulator_Path");
                    Emulator2 = cfg.at("emulator2", "");
                    BinaryEditor = GetToolPath(cfg, "binary_editor", "BinaryEditor_Path");
                    Program1 = GetToolPath(cfg, "program1", "CustomTool_Path");
                    Program2 = cfg.at("program2", "");
                    Program3 = cfg.at("program3", "");
                    Sappy = GetToolPath(cfg, "sappy", "Sappy_Path");
                    Mid2agb = cfg.at("mid2agb", "");
                    GbaMusRiper = cfg.at("gba_mus_riper", "");
                    Sox = cfg.at("sox", "");
                    Midfix4agb = cfg.at("midfix4agb", "");
                    EventAssembler = cfg.at("event_assembler", "");
                    DevkitproEabi = cfg.at("devkitpro_eabi", "");
                    GoldroadAsm = cfg.at("goldroad_asm", "");
                    Cflags = cfg.at("CFLAGS", "");
                    Retdec = cfg.at("retdec", "");
                    Python3 = cfg.at("python3", "");
                    Feclib = cfg.at("FECLIB", "");
                    SrccodeTexteditor = cfg.at("srccode_texteditor", "");
                    SrccodeDirectory = cfg.at("srccode_directory", "");

                    // Submodule remote URLs (empty = use defaults)
                    SubmodulePatch2Url = cfg.at("submodule_patch2_url", "");
                    SubmoduleFERepoUrl = cfg.at("submodule_fe_repo_url", "");
                    SubmoduleFERepoMusicUrl = cfg.at("submodule_fe_repo_music_url", "");

                    // FEMapCreator setup (#1978 Slice 2) — read-only value load, no validation
                    // side effects (no filesystem probing beyond what GetFEMapCreatorSetupSnapshot
                    // does on-demand for display, no process launch, no network).
                    FEMapCreatorPath = cfg.at(FEMapCreatorProfileCore.ExecutablePathConfigKey, "");
                    FEMapCreatorAssetsRoot = cfg.at(FEMapCreatorProfileCore.AssetsRootConfigKey, "");
                }
            }
            finally
            {
                IsLoading = false;
                MarkClean();
            }
        }

        /// <summary>Extract the language code from a display string like "ja \u2014 \u65e5\u672c\u8a9e" → "ja".</summary>
        internal static string ExtractLanguageCode(string displayString)
        {
            if (string.IsNullOrEmpty(displayString)) return "auto";
            int sep = displayString.IndexOf(" \u2014 ");
            return sep > 0 ? displayString.Substring(0, sep) : displayString;
        }

        internal static string GetToolPath(Config? cfg, string key, params string[] fallbackKeys)
        {
            if (cfg == null)
                return "";

            string value = cfg.at(key, "");
            if (!string.IsNullOrEmpty(value))
                return value;

            foreach (string fallbackKey in fallbackKeys)
            {
                value = cfg.at(fallbackKey, "");
                if (!string.IsNullOrEmpty(value))
                    return value;
            }

            return "";
        }

        /// <summary>Save settings to CoreState and Config.</summary>
        public void Save()
        {
            string langCode = ExtractLanguageCode(Language);
            CoreState.Language = langCode;
            CoreState.GitPath = GitPath;

            var cfg = CoreState.Config;
            if (cfg != null)
            {
                cfg["git_path"] = GitPath ?? "git";
                cfg["func_auto_backup"] = AutoBackup ? "2" : "0";
                // Preserve the loaded interval (1/3/7) when still enabled; only default to "3" if it
                // was previously off. Never collapses a WinForms-chosen 1/7 to 3 on an unrelated save.
                cfg["func_auto_update"] = AutoUpdateEnabled ? (_autoUpdateRaw != "0" ? _autoUpdateRaw : "3") : "0";
                cfg["Language"] = langCode;
                cfg["func_lang"] = langCode; // backward compat with WinForms

                // Auto-save settings
                cfg["autosave_enabled"] = AutoSaveEnabled ? "true" : "false";
                cfg["autosave_interval_minutes"] = AutoSaveIntervalMinutes.ToString();

                // Save all tool paths using WinForms-compatible keys
                cfg["emulator"] = Emulator ?? "";
                cfg["emulator2"] = Emulator2 ?? "";
                cfg["binary_editor"] = BinaryEditor ?? "";
                cfg["program1"] = Program1 ?? "";
                cfg["program2"] = Program2 ?? "";
                cfg["program3"] = Program3 ?? "";
                cfg["sappy"] = Sappy ?? "";
                cfg["mid2agb"] = Mid2agb ?? "";
                cfg["gba_mus_riper"] = GbaMusRiper ?? "";
                cfg["sox"] = Sox ?? "";
                cfg["midfix4agb"] = Midfix4agb ?? "";
                cfg["event_assembler"] = EventAssembler ?? "";
                cfg["devkitpro_eabi"] = DevkitproEabi ?? "";
                cfg["goldroad_asm"] = GoldroadAsm ?? "";
                cfg["CFLAGS"] = Cflags ?? "";
                cfg["retdec"] = Retdec ?? "";
                cfg["python3"] = Python3 ?? "";
                cfg["FECLIB"] = Feclib ?? "";
                cfg["srccode_texteditor"] = SrccodeTexteditor ?? "";
                cfg["srccode_directory"] = SrccodeDirectory ?? "";

                // Submodule remote URLs
                cfg["submodule_patch2_url"] = SubmodulePatch2Url ?? "";
                cfg["submodule_fe_repo_url"] = SubmoduleFERepoUrl ?? "";
                cfg["submodule_fe_repo_music_url"] = SubmoduleFERepoMusicUrl ?? "";

                // FEMapCreator setup (#1978 Slice 2) — persisted verbatim; validation happens on
                // read/display only, never blocks or mutates Save().
                cfg[FEMapCreatorProfileCore.ExecutablePathConfigKey] = FEMapCreatorPath ?? "";
                cfg[FEMapCreatorProfileCore.AssetsRootConfigKey] = FEMapCreatorAssetsRoot ?? "";

                cfg.Save();

                // Apply submodule remote URL changes
                ApplySubmoduleRemotes();
            }

            // Apply the language change (reload translations, clear the name cache,
            // raise LanguageChanged). cfg["Language"]/["func_lang"] were already
            // persisted in the batch above, so pass persist:false to avoid a second
            // cfg.Save(). #1895 factored this into ApplyLanguage so the web-app
            // home-page language switcher can reuse it.
            ApplyLanguage(langCode, persist: false);

            MarkClean();
        }

        /// <summary>
        /// Apply a language change from anywhere (e.g. the web-app home-page language
        /// switcher — #1895). Sets <see cref="CoreState.Language"/>, optionally
        /// persists the language keys, then reloads translations, clears the name
        /// cache, and raises <see cref="CoreState.LanguageChanged"/> so open views
        /// relocalize. This is the language-only slice of <see cref="Save"/>: it must
        /// NOT touch GitPath, tool paths, or submodule remotes.
        /// </summary>
        /// <param name="code">Language code, e.g. "en", "ja", "auto".</param>
        /// <param name="persist">When true, write cfg["Language"]/["func_lang"] and save.</param>
        internal static void ApplyLanguage(string code, bool persist = true)
        {
            CoreState.Language = code;

            if (persist)
            {
                var cfg = CoreState.Config;
                if (cfg != null)
                {
                    cfg["Language"] = code;
                    cfg["func_lang"] = code; // backward compat with WinForms
                    cfg.Save();
                }
            }

            // Reload translations with the new language.
            ReloadTranslations();

            // Clear name cache so names are re-decoded in the new language.
            NameResolver.ClearCache();

            // Notify all subscribers (ViewModels) to refresh their localized strings.
            CoreState.RaiseLanguageChanged();
        }

        void ApplySubmoduleRemotes()
        {
            string baseDir = CoreState.BaseDirectory ?? AppDomain.CurrentDomain.BaseDirectory;

            // Apply or restore defaults for each submodule
            ApplyOneRemote(Path.Combine(baseDir, "config", "patch2"),
                SubmodulePatch2Url, GitUtil.GetPatch2RemoteUrl());
            ApplyOneRemote(Path.Combine(baseDir, "resources", "FE-Repo"),
                SubmoduleFERepoUrl, GitUtil.FERepoDefaultUrl);
            ApplyOneRemote(Path.Combine(baseDir, "resources", "FE-Repo-Music-No-Preview"),
                SubmoduleFERepoMusicUrl, GitUtil.FERepoMusicDefaultUrl);
        }

        static void ApplyOneRemote(string submodulePath, string customUrl, string defaultUrl)
        {
            // Use custom URL if set, otherwise restore default
            string effectiveUrl = string.IsNullOrWhiteSpace(customUrl) ? defaultUrl : customUrl;
            if (!GitUtil.SetSubmoduleRemote(submodulePath, effectiveUrl))
                Log.ErrorF("Failed to set remote for {0}", submodulePath);
        }

        internal static void ReloadTranslations()
        {
            string lang = CoreState.Language ?? "auto";
            if (lang == "auto")
            {
                lang = System.Globalization.CultureInfo.CurrentCulture.TwoLetterISOLanguageName;

                // Check if a translation file exists for the system locale
                string baseDir = CoreState.BaseDirectory;
                if (string.IsNullOrEmpty(baseDir))
                    baseDir = AppDomain.CurrentDomain.BaseDirectory;

                if (lang != "ja")
                {
                    string autoFile = Path.Combine(baseDir, "config", "translate", lang + ".txt");
                    if (!File.Exists(autoFile))
                        lang = "en"; // default to English if no matching translation
                }
            }

            string translateBaseDir = CoreState.BaseDirectory;
            if (string.IsNullOrEmpty(translateBaseDir))
                translateBaseDir = AppDomain.CurrentDomain.BaseDirectory;

            string enFilePath = Path.Combine(translateBaseDir, "config", "translate", "en.txt");
            string translateDir = Path.Combine(translateBaseDir, "config", "translate");

            // "ja" is the built-in language — Japanese keys pass through from WinForms.
            // However, Avalonia uses English keys (R._("Characters"), etc.) that need
            // explicit Japanese translations from ja.txt.
            if (lang == "ja")
            {
                string jaFile = Path.Combine(translateDir, "ja.txt");
                if (File.Exists(jaFile))
                {
                    // Load ja.txt so English Avalonia keys map to Japanese translations.
                    // WinForms Japanese keys not in ja.txt will miss and pass through as-is,
                    // which is correct — they ARE the Japanese text.
                    MyTranslateResource.LoadResource(jaFile);
                    // Also load reverse map so any English keys NOT in ja.txt can chain
                    // through en.txt's English→Japanese mapping.
                    MyTranslateResource.LoadReverseEnglishMap(enFilePath);
                }
                else
                {
                    // No ja.txt — fall back to old behavior
                    MyTranslateResource.LoadReverseEnglishMap(enFilePath);
                    MyTranslateResource.Clear();
                }
                return;
            }

            // English mode: en.txt maps Japanese→English for WinForms compat;
            // Avalonia English keys work by pass-through (not found in Dic, returned as-is).
            if (lang == "en")
            {
                string enTranslateFile = Path.Combine(translateBaseDir, "config", "translate", "en.txt");
                if (File.Exists(enTranslateFile))
                    MyTranslateResource.LoadResource(enTranslateFile);
                else
                    MyTranslateResource.Clear();
                return;
            }

            // Non-English, non-Japanese language (zh, etc.):
            // Load target language file, then load reverse English→Japanese map
            // so Avalonia English keys can chain: English → Japanese → target.
            string translateFile = Path.Combine(translateBaseDir, "config", "translate", lang + ".txt");
            if (File.Exists(translateFile))
            {
                MyTranslateResource.LoadResource(translateFile);
                MyTranslateResource.LoadReverseEnglishMap(enFilePath);
            }
            else
            {
                // Requested language file not found — fall back to English
                if (File.Exists(enFilePath))
                    MyTranslateResource.LoadResource(enFilePath);
                else
                    MyTranslateResource.Clear(); // no translation files at all
            }
        }

        /// <summary>
        /// Enumerate languages with display names.
        /// Always includes well-known languages (auto, ja, en, zh) plus any
        /// additional *.txt translation files found in config/translate/.
        /// </summary>
        internal static List<string> EnumerateLanguages()
        {
            // Well-known display names
            var knownNames = new Dictionary<string, string>
            {
                { "auto", "Auto Detect" },
                { "ja", "\u65e5\u672c\u8a9e" },
                { "en", "English" },
                { "zh", "\u4e2d\u6587" },
            };

            // Display format: "code — Display Name"
            var result = new List<string>
            {
                "auto \u2014 Auto Detect",
                "ja \u2014 \u65e5\u672c\u8a9e",
                "en \u2014 English",
                "zh \u2014 \u4e2d\u6587",
            };

            // Scan config/translate/ for additional *.txt files
            string baseDir = CoreState.BaseDirectory;
            if (string.IsNullOrEmpty(baseDir))
                baseDir = AppDomain.CurrentDomain.BaseDirectory;

            string translateDir = Path.Combine(baseDir, "config", "translate");
            if (Directory.Exists(translateDir))
            {
                foreach (string file in Directory.GetFiles(translateDir, "*.txt"))
                {
                    string code = Path.GetFileNameWithoutExtension(file);
                    // Skip non-language files and already-listed languages
                    if (string.IsNullOrEmpty(code) || code.Contains("_") || code.Contains("dic"))
                        continue;
                    if (knownNames.ContainsKey(code))
                        continue;

                    result.Add(code + " \u2014 " + code);
                }
            }

            return result;
        }
    }
}
