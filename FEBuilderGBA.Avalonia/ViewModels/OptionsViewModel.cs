using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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
                cfg.Save();
            }

            MarkClean();
        }

        /// <summary>Enumerate language codes from config/translate/*.txt files.</summary>
        static List<string> EnumerateLanguages()
        {
            var langs = new List<string> { "en" };
            try
            {
                string baseDir = CoreState.BaseDirectory;
                if (string.IsNullOrEmpty(baseDir))
                    baseDir = AppDomain.CurrentDomain.BaseDirectory;

                string translateDir = Path.Combine(baseDir, "config", "translate");
                if (Directory.Exists(translateDir))
                {
                    var files = Directory.GetFiles(translateDir, "*.txt");
                    foreach (var f in files)
                    {
                        string name = Path.GetFileNameWithoutExtension(f);
                        // Skip dictionary files like dic_ja_en.txt
                        if (name.StartsWith("dic_", StringComparison.OrdinalIgnoreCase))
                            continue;
                        if (!langs.Contains(name))
                            langs.Add(name);
                    }
                }
            }
            catch
            {
                // Ignore enumeration errors -- at least "en" is available
            }
            return langs.OrderBy(l => l).ToList();
        }
    }
}
