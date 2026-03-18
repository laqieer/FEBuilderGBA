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

        // External tool paths — keys match WinForms config.xml exactly
        string _emulator = "";
        string _emulator2 = "";
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

        /// <summary>Current UI language code (e.g. "en", "zh").</summary>
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

        // ---- External Tool Paths ----

        public string Emulator { get => _emulator; set => SetField(ref _emulator, value); }
        public string Emulator2 { get => _emulator2; set => SetField(ref _emulator2, value); }
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

                    // Load all tool paths using WinForms-compatible keys
                    Emulator = cfg.at("emulator", "");
                    Emulator2 = cfg.at("emulator2", "");
                    Program1 = cfg.at("program1", "");
                    Program2 = cfg.at("program2", "");
                    Program3 = cfg.at("program3", "");
                    Sappy = cfg.at("sappy", "");
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
                cfg["Language"] = langCode;
                cfg["func_lang"] = langCode; // backward compat with WinForms

                // Save all tool paths using WinForms-compatible keys
                cfg["emulator"] = Emulator ?? "";
                cfg["emulator2"] = Emulator2 ?? "";
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

                cfg.Save();
            }

            // Reload translations with new language
            ReloadTranslations();

            // Clear name cache so names are re-decoded in the new language
            NameResolver.ClearCache();

            // Notify all subscribers (ViewModels) to refresh their localized strings
            CoreState.RaiseLanguageChanged();

            MarkClean();
        }

        internal static void ReloadTranslations()
        {
            string lang = CoreState.Language ?? "auto";
            if (lang == "auto")
            {
                lang = System.Globalization.CultureInfo.CurrentCulture.TwoLetterISOLanguageName;
                // Map culture names to our language codes
                if (lang == "ja") lang = "ja";
                else if (lang == "zh") lang = "zh";
                else lang = "en"; // default to English
            }

            // "ja" is the built-in language (no translation file needed)
            if (lang == "ja")
            {
                // Clear translations to use built-in Japanese strings
                MyTranslateResource.LoadResource("");
                return;
            }

            string baseDir = CoreState.BaseDirectory;
            if (string.IsNullOrEmpty(baseDir))
                baseDir = AppDomain.CurrentDomain.BaseDirectory;

            string translateFile = Path.Combine(baseDir, "config", "translate", lang + ".txt");
            if (File.Exists(translateFile))
            {
                MyTranslateResource.LoadResource(translateFile);
            }
        }

        /// <summary>Enumerate languages with display names matching WinForms behavior.</summary>
        internal static List<string> EnumerateLanguages()
        {
            // Display format: "code — Display Name"
            // "auto" = detect from OS locale, "ja" = built-in Japanese (no ja.txt needed)
            return new List<string>
            {
                "auto \u2014 Auto Detect",
                "ja \u2014 \u65e5\u672c\u8a9e",
                "en \u2014 English",
                "zh \u2014 \u4e2d\u6587",
            };
        }
    }
}
