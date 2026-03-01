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
        void Update(uint addr, string comment);
        void Remove(uint addr);
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
    /// Text encoding mode, extracted from OptionForm.textencoding_enum.
    /// Values must match the WinForms OptionForm enum.
    /// </summary>
    public enum TextEncodingEnum
    {
        Auto = 0,
        LAT1 = 1,
        Shift_JIS = 2,
        UTF8 = 3,
        ZH_TBL = 4,
        EN_TBL = 5,
        AR_TBL = 6,
        KR_TBL = 7,
        KO_TBL = 8,
        NoCache = 99,
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

        // ---- Types now in Core (concrete) ----
        public static Config Config { get; set; }
        public static EventScript EventScript { get; set; }
        public static EventScript ProcsScript { get; set; }
        public static EventScript AIScript { get; set; }
        public static FETextEncode FETextEncoder { get; set; }
        public static TextEscape TextEscape { get; set; }
        public static object UseTextIDCache { get; set; }
        public static EtcCacheFLag FlagCache { get; set; }
        public static object ResourceCache { get; set; }
        public static ExportFunction ExportFunction { get; set; }
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

        // ---- Text encoding ----
        /// <summary>
        /// Current text encoding mode. Set by WinForms from OptionForm.textencoding().
        /// </summary>
        public static TextEncodingEnum TextEncoding { get; set; } = TextEncodingEnum.Auto;

        // ---- Callbacks for WinForms-dependent logic ----

        /// <summary>
        /// Callback to append binary data to ROM free space (wraps InputFormRef.AppendBinaryData).
        /// Used by RecycleAddress when no recycled region fits.
        /// </summary>
        public static Func<byte[], Undo.UndoData, uint> AppendBinaryData { get; set; }

        /// <summary>
        /// Callback to load patch-provided event scripts (wraps PatchForm.MakeEventScript).
        /// Called during EventScript.Load() for Event type scripts.
        /// </summary>
        public static Action<List<EventScript.Script>, Dictionary<uint, string>, TextEscape, ExportFunction> EventScriptPatchLoader { get; set; }

        /// <summary>
        /// Returns the maximum level cap (wraps PatchUtil.GetLevelMaxCaps).
        /// Used by GrowSimulator.ClassMaxLevel/CalcMaxLevel.
        /// Default: returns ROM value or 20.
        /// </summary>
        public static Func<uint> GetLevelMaxCaps { get; set; }

        /// <summary>
        /// Returns whether a class is a high/promoted class (wraps ClassForm.isHighClass).
        /// Used by GrowSimulator.ClassMaxLevel.
        /// </summary>
        public static Func<uint, bool> IsHighClass { get; set; }

        /// <summary>
        /// Returns the class-change count for a class (wraps CCBranchForm.GetCCCount).
        /// Used by GrowSimulator.CalcMaxLevel.
        /// </summary>
        public static Func<uint, int> GetCCCount { get; set; }
    }
}
