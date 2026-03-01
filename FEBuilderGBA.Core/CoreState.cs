using System;
using System.Collections.Generic;

namespace FEBuilderGBA
{
    /// <summary>
    /// Interface for cache classes (EtcCache) needed by Core.
    /// Write methods used by Undo; read methods used by DisASMTrumb, etc.
    /// </summary>
    public interface IEtcCache
    {
        void RemoveOverRange(uint range);
        void RemoveRange(uint start, uint end);
        bool CheckFast(uint num);
        string At(uint num, string def = "");
        string S_At(uint num);
        bool TryGetValue(uint num, out string out_data);
    }

    /// <summary>
    /// Interface for text encoder needed by Core (Rom.getString, FETextEncode, FETextDecode).
    /// </summary>
    public interface ISystemTextEncoder
    {
        string Decode(byte[] str);
        string Decode(byte[] str, int start, int len);
        byte[] Encode(string str);
        Dictionary<string, uint> GetTBLEncodeDicLow();
    }

    /// <summary>
    /// Minimal interface for ASM/MAP cache needed by Core (Undo).
    /// </summary>
    public interface IAsmMapCache
    {
        void ClearCache();
    }

    /// <summary>
    /// Central holder for all non-UI application state.
    /// WinForms Program.cs static properties redirect here.
    /// Properties for types not yet in Core use object or interfaces.
    /// These are progressively replaced with concrete types as files move to Core.
    /// </summary>
    public static class CoreState
    {
        // ---- Types already in Core (concrete) ----
        public static ROM ROM { get; set; }
        public static Undo Undo { get; set; }

        // ---- Types used by Core code via interfaces ----
        public static IEtcCache CommentCache { get; set; }
        public static IEtcCache LintCache { get; set; }
        public static IEtcCache WorkSupportCache { get; set; }
        public static ISystemTextEncoder SystemTextEncoder { get; set; }
        public static IAsmMapCache AsmMapFileAsmCache { get; set; }

        // ---- Types not yet in Core (stored as object, cast by WinForms) ----
        public static object Config { get; set; }
        public static object EventScript { get; set; }
        public static object ProcsScript { get; set; }
        public static object AIScript { get; set; }
        public static FETextEncode FETextEncoder { get; set; }
        public static TextEscape TextEscape { get; set; }
        public static object UseTextIDCache { get; set; }
        public static object FlagCache { get; set; }
        public static object ResourceCache { get; set; }
        public static object ExportFunction { get; set; }
        public static object Mod { get; set; }

        // ---- Simple value types ----
        public static string BaseDirectory { get; set; }
        public static bool IsCommandLine { get; set; }
        public static Dictionary<string, string> ArgsDic { get; set; }

        /// <summary>
        /// Current UI language code (e.g. "en", "ja", "zh").
        /// Set by OptionForm at startup. Used by ConfigDataFilename for lang-specific resources.
        /// </summary>
        public static string Language { get; set; } = "en";

        /// <summary>
        /// Path to the git executable (e.g. "git" or "C:\Program Files\Git\cmd\git.exe").
        /// Set from OptionForm.git_path() at startup and when options are saved.
        /// </summary>
        public static string GitPath { get; set; } = "git";

        /// <summary>
        /// Release source preference: 0 = auto, 1 = GitHub, 2 = Gitee.
        /// Set from OptionForm.release_source() at startup and when options are saved.
        /// </summary>
        public static int ReleaseSource { get; set; } = 0;

        /// <summary>
        /// Platform-specific service provider (dialogs, clipboard, etc.).
        /// Must be set before any Core code that shows messages.
        /// </summary>
        public static IAppServices Services { get; set; } = new HeadlessAppServices();
    }
}
