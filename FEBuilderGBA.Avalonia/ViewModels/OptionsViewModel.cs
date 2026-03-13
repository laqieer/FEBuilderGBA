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
        string _emulatorPath = "";
        bool _autoBackup = true;

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

        /// <summary>Path to GBA emulator executable.</summary>
        public string EmulatorPath
        {
            get => _emulatorPath;
            set => SetField(ref _emulatorPath, value);
        }

        /// <summary>Whether to auto-backup ROM before saving.</summary>
        public bool AutoBackup
        {
            get => _autoBackup;
            set => SetField(ref _autoBackup, value);
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
                Language = CoreState.Language ?? "en";
                GitPath = CoreState.GitPath ?? "git";

                var cfg = CoreState.Config;
                if (cfg != null)
                {
                    EmulatorPath = cfg.at("Emulator_Path", "");
                    // func_auto_backup: 0=None, 1=SmartBackup, 2=FullBackup (default 2)
                    int backupVal = 2;
                    int.TryParse(cfg.at("func_auto_backup", "2"), out backupVal);
                    AutoBackup = backupVal > 0;
                }
            }
            finally
            {
                IsLoading = false;
                MarkClean();
            }
        }

        /// <summary>Save settings to CoreState and Config.</summary>
        public void Save()
        {
            CoreState.Language = Language;
            CoreState.GitPath = GitPath;

            var cfg = CoreState.Config;
            if (cfg != null)
            {
                cfg["Emulator_Path"] = EmulatorPath ?? "";
                cfg["git_path"] = GitPath ?? "git";
                cfg["func_auto_backup"] = AutoBackup ? "2" : "0";
                cfg["Language"] = Language ?? "auto";
                cfg.Save();
            }

            // Reload translations with new language
            ReloadTranslations();

            MarkClean();
        }

        static void ReloadTranslations()
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

        /// <summary>Enumerate language codes matching WinForms behavior.</summary>
        static List<string> EnumerateLanguages()
        {
            // Hard-code language options matching WinForms behavior.
            // "auto" = detect from OS locale, "ja" = built-in Japanese (no ja.txt needed)
            return new List<string> { "auto", "ja", "en", "zh" };
        }
    }
}
