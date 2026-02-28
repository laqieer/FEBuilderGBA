using System;
using System.Collections.Generic;

namespace FEBuilderGBA
{
    /// <summary>
    /// Minimal interface for cache classes (EtcCache) needed by Core.
    /// </summary>
    public interface IEtcCache
    {
        void RemoveOverRange(uint range);
        void RemoveRange(uint start, uint end);
    }

    /// <summary>
    /// Minimal interface for text encoder needed by Core (Rom.getString).
    /// </summary>
    public interface ISystemTextEncoder
    {
        string Decode(byte[] str, int start, int len);
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
        public static object FETextEncoder { get; set; }
        public static object TextEscape { get; set; }
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
        /// Platform-specific service provider (dialogs, clipboard, etc.).
        /// Must be set before any Core code that shows messages.
        /// </summary>
        public static IAppServices Services { get; set; } = new HeadlessAppServices();
    }
}
