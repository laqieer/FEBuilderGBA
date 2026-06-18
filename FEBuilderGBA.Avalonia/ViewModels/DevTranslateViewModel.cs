using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// Developer Translation tool (WF <c>DevTranslateForm</c> / Core
    /// <c>MyTranslateBuild</c>). Builds the app's own translation resources from
    /// source: scans C# source, patches, MODs and config data and seeds
    /// <c>config/translate/&lt;lang&gt;.txt</c> (Translate), plus the Designer.cs
    /// convert/reverse helpers. This is a DEVELOPER workflow — it reads source
    /// files and writes translation .txt files, it does NOT touch any ROM bytes,
    /// so it is a read-no-ROM-bytes orphan and does not participate in the
    /// data-verification contract.
    /// </summary>
    public class DevTranslateViewModel : ViewModelBase
    {
        // ---- Language combos (func_lang_ resource) ----
        // Each entry is (code, label) e.g. ("en", "English"). "auto" and "ja"
        // are filtered out for the build/convert targets — you cannot build the
        // Japanese source from itself, and "auto" is not a concrete language.
        public ObservableCollection<LangItem> BuildLanguages { get; } = new();
        public ObservableCollection<LangItem> DesignLanguages { get; } = new();

        LangItem _selectedBuildLanguage;
        public LangItem SelectedBuildLanguage
        {
            get => _selectedBuildLanguage;
            set => SetField(ref _selectedBuildLanguage, value);
        }

        LangItem _selectedDesignLanguage;
        public LangItem SelectedDesignLanguage
        {
            get => _selectedDesignLanguage;
            set => SetField(ref _selectedDesignLanguage, value);
        }

        // ---- Source directory + options ----
        string _buildSourcePath = "";
        public string BuildSourcePath
        {
            get => _buildSourcePath;
            set => SetField(ref _buildSourcePath, value);
        }

        string _designSourcePath = "";
        public string DesignSourcePath
        {
            get => _designSourcePath;
            set => SetField(ref _designSourcePath, value);
        }

        bool _englishBase;
        public bool EnglishBase
        {
            get => _englishBase;
            set => SetField(ref _englishBase, value);
        }

        // ---- Status / progress ----
        string _statusMessage = "";
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetField(ref _statusMessage, value);
        }

        public DevTranslateViewModel()
        {
            LoadLanguages();
        }

        /// <summary>
        /// Populate both language combos from the <c>func_lang_</c> resource,
        /// excluding "auto" and "ja" (parity with WF DevTranslateForm's guard).
        /// Falls back to the built-in en/zh set when the resource file is absent
        /// (e.g. headless/test context with no config tree).
        /// </summary>
        void LoadLanguages()
        {
            BuildLanguages.Clear();
            DesignLanguages.Clear();

            foreach (var item in LoadFuncLangResource())
            {
                BuildLanguages.Add(item);
                DesignLanguages.Add(item);
            }

            if (BuildLanguages.Count > 0)
                SelectedBuildLanguage = BuildLanguages[0];
            if (DesignLanguages.Count > 0)
                SelectedDesignLanguage = DesignLanguages[0];
        }

        static List<LangItem> LoadFuncLangResource()
        {
            var result = new List<LangItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string fullfilename = null;
            try
            {
                if (!string.IsNullOrEmpty(CoreState.BaseDirectory))
                    fullfilename = U.ConfigDataFilename("func_lang_");
            }
            catch
            {
                // BaseDirectory or ROM may be null in test/headless contexts.
            }

            if (!string.IsNullOrEmpty(fullfilename) && File.Exists(fullfilename))
            {
                foreach (string raw in File.ReadAllLines(fullfilename))
                {
                    string line = raw;
                    try
                    {
                        if (U.IsComment(line) || U.OtherLangLine(line))
                            continue;
                    }
                    catch
                    {
                        // OtherLangLine may throw when CoreState.ROM is null.
                    }
                    line = U.ClipComment(line).Trim();
                    if (string.IsNullOrEmpty(line))
                        continue;

                    string[] sp = line.Split('\t');
                    if (sp.Length < 2)
                        continue;
                    string code = sp[0].Trim();
                    string label = sp[1].Trim();
                    if (!IsSelectableTarget(code))
                        continue;
                    if (seen.Add(code))
                        result.Add(new LangItem(code, label));
                }
            }

            if (result.Count == 0)
            {
                // Minimal fallback so the tool is usable even without the config
                // tree. These are the languages the project actually ships.
                if (seen.Add("en")) result.Add(new LangItem("en", "English"));
                if (seen.Add("zh")) result.Add(new LangItem("zh", "中文"));
            }

            return result;
        }

        // "auto", "ja" and the empty value are not valid build/convert targets
        // (WF DevTranslateForm shows a stop-error for them).
        static bool IsSelectableTarget(string code)
        {
            if (string.IsNullOrEmpty(code)) return false;
            if (code == "auto") return false;
            if (code == "ja") return false;
            return true;
        }

        /// <summary>
        /// Run the full build scan (ScanPatch + ScanMOD + ScanData + ScanCS) for
        /// the selected build language. Returns true on success. Designed to run
        /// on a background thread; <paramref name="onProgress"/> reports the file
        /// currently being scanned. Throws on I/O / translate failures so the
        /// caller can surface a localized status.
        /// </summary>
        public bool RunBuild(string lang, string sourcePath, bool englishBase, Action<string> onProgress)
        {
            if (string.IsNullOrEmpty(lang) || !IsSelectableTarget(lang))
                return false;
            if (string.IsNullOrEmpty(sourcePath) || !Directory.Exists(sourcePath))
                return false;

            var t = new MyTranslateBuild(lang, englishBase, onProgress);
            t.ScanPatch();
            t.ScanMOD();
            t.ScanData();
            t.ScanCS(sourcePath);
            return true;
        }

        /// <summary>
        /// Run DesignStringConvert: replace Japanese string literals in
        /// *.Designer.cs files with translations from the selected language
        /// (writes a .tlink.txt sidecar so the change is reversible).
        /// </summary>
        public bool RunDesignConvert(string lang, string sourcePath, Action<string> onProgress)
        {
            if (string.IsNullOrEmpty(lang) || !IsSelectableTarget(lang))
                return false;
            if (string.IsNullOrEmpty(sourcePath) || !Directory.Exists(sourcePath))
                return false;

            var t = new MyTranslateBuild(lang, false, onProgress);
            t.DesignStringConvert(sourcePath);
            return true;
        }

        /// <summary>
        /// Run DesignStringReverse: restore the Japanese literals in
        /// *.Designer.cs files from the .tlink.txt sidecar and back-fill the
        /// translate resource with the current (translated) strings.
        /// </summary>
        public bool RunDesignReverse(string lang, string sourcePath, Action<string> onProgress)
        {
            if (string.IsNullOrEmpty(lang) || !IsSelectableTarget(lang))
                return false;
            if (string.IsNullOrEmpty(sourcePath) || !Directory.Exists(sourcePath))
                return false;

            var t = new MyTranslateBuild(lang, false, onProgress);
            t.DesignStringReverse(sourcePath);
            return true;
        }
    }

    /// <summary>A target language entry: code (e.g. "en") + display label.</summary>
    public sealed class LangItem
    {
        public string Code { get; }
        public string Label { get; }

        public LangItem(string code, string label)
        {
            Code = code;
            Label = label;
        }

        // Shown in the ComboBox. Prefer "label (code)" so the dev sees both.
        public override string ToString()
            => string.IsNullOrEmpty(Label) || Label == Code ? Code : $"{Label} ({Code})";
    }
}
