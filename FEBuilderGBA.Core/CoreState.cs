using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

namespace FEBuilderGBA
{
    public readonly struct EtcCacheRange
    {
        public const ulong AddressSpaceEnd = 1UL << 32;

        public EtcCacheRange(uint start, ulong endExclusive)
        {
            if (endExclusive < start || endExclusive > AddressSpaceEnd)
                throw new ArgumentOutOfRangeException(nameof(endExclusive));
            Start = start;
            EndExclusive = endExclusive;
        }

        public EtcCacheRange(uint start, uint size)
            : this(start, (ulong)start + size)
        {
        }

        public uint Start { get; }
        public ulong EndExclusive { get; }
        public bool Contains(uint address) =>
            address >= Start && (ulong)address < EndExclusive;

        public static EtcCacheRange All { get; } =
            new EtcCacheRange(0, AddressSpaceEnd);
    }

    public sealed class EtcCacheSnapshot
    {
        readonly EtcCacheRange[] _ranges;
        readonly Dictionary<uint, string> _entries;
        readonly ReadOnlyCollection<EtcCacheRange> _readOnlyRanges;
        readonly ReadOnlyDictionary<uint, string> _readOnlyEntries;

        internal EtcCacheSnapshot(
            IReadOnlyList<EtcCacheRange> ranges,
            IReadOnlyDictionary<uint, string> entries)
        {
            _ranges = new EtcCacheRange[ranges.Count];
            for (int i = 0; i < ranges.Count; i++)
                _ranges[i] = ranges[i];
            _entries = new Dictionary<uint, string>(entries);
            _readOnlyRanges = Array.AsReadOnly(_ranges);
            _readOnlyEntries =
                new ReadOnlyDictionary<uint, string>(_entries);
        }

        public IReadOnlyList<EtcCacheRange> Ranges => _readOnlyRanges;
        public IReadOnlyDictionary<uint, string> Entries => _readOnlyEntries;
        internal bool CoversAll =>
            _ranges.Length == 1
            && _ranges[0].Start == 0
            && _ranges[0].EndExclusive == EtcCacheRange.AddressSpaceEnd;

        internal static EtcCacheSnapshot Capture(
            IReadOnlyDictionary<uint, string> source,
            IReadOnlyList<EtcCacheRange> ranges)
        {
            var entries = new Dictionary<uint, string>();
            foreach (var pair in source)
            {
                if (Contains(ranges, pair.Key))
                    entries[pair.Key] = pair.Value;
            }
            return new EtcCacheSnapshot(ranges, entries);
        }

        internal void RestoreRanges(Dictionary<uint, string> target)
        {
            var remove = new List<uint>();
            foreach (uint key in target.Keys)
            {
                if (Contains(_ranges, key))
                    remove.Add(key);
            }
            foreach (uint key in remove)
                target.Remove(key);
            foreach (var pair in _entries)
                target[pair.Key] = pair.Value;
        }

        internal void RestoreAll(Dictionary<uint, string> target)
        {
            target.Clear();
            foreach (var pair in _entries)
                target[pair.Key] = pair.Value;
        }

        static bool Contains(
            IReadOnlyList<EtcCacheRange> ranges,
            uint address)
        {
            for (int i = 0; i < ranges.Count; i++)
            {
                if (ranges[i].Contains(address))
                    return true;
            }
            return false;
        }
    }

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

        /// <summary>
        /// Relocate every cache key in <c>[oldAddr, oldAddr + oldSize)</c> by
        /// <c>(newAddr - oldAddr)</c>. Used by table-expansion helpers (e.g.
        /// <see cref="DataExpansionCore.ExpandTableTo"/>) so per-row comments
        /// and lint metadata follow the moved table to its new ROM offset.
        /// Mirrors WinForms <c>EtcCache.RepointEtcData</c>.
        ///
        /// Default body is a no-op so external <see cref="IEtcCache"/> implementors
        /// continue to compile without breaking.
        /// </summary>
        void RepointEtcData(uint oldAddr, uint oldSize, uint newAddr) { }

        bool TryCaptureAll(out EtcCacheSnapshot snapshot)
        {
            snapshot = null!;
            return false;
        }

        bool TryCaptureRanges(
            IReadOnlyList<EtcCacheRange> ranges,
            out EtcCacheSnapshot snapshot)
        {
            snapshot = null!;
            return false;
        }

        bool TryRestoreAll(EtcCacheSnapshot snapshot) => false;
        bool TryRestoreRanges(EtcCacheSnapshot snapshot) => false;
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

        /// <summary>
        /// Returns true when the supplied unit id (1-based) is referenced
        /// by hardcoded ASM in the loaded ROM. Used by the unit-editor
        /// HardCoding warning label (#428). Implementations that do not
        /// have ASM-map data available should return false.
        /// </summary>
        bool IsHardCodeUnit(uint unitId);

        /// <summary>
        /// Returns true when the supplied class id (0-based, same key the
        /// AddressList exposes via <c>SelectedIndex</c>) is referenced by
        /// hardcoded ASM in the loaded ROM. Used by the class-editor
        /// HardCoding warning label (#406, parity with WF
        /// <c>ClassForm.CheckHardCodingWarning</c>). Implementations that
        /// do not have ASM-map data available should return false. Default
        /// implementation returns false so legacy implementors keep
        /// compiling.
        /// </summary>
        bool IsHardCodeClass(uint classId) => false;

        /// <summary>
        /// Returns true when the supplied item id is referenced by
        /// hardcoded ASM in the loaded ROM. Used by the item-editor
        /// HardCoding warning label (#409). Implementations that do not
        /// have ASM-map data available should return false (default
        /// implementation returns false so legacy implementors keep
        /// compiling).
        /// </summary>
        bool IsHardCodeItem(uint itemId) => false;

        /// <summary>
        /// Return the loaded ASM/MAP symbol table for the Pointer Tool
        /// "What is this address?" lookup (#1026). Implementations that have
        /// no symbol data available return <c>null</c>.
        ///
        /// <para>WinForms <c>AsmMapFileAsmCache</c> returns its full WF
        /// <c>AsmMapFile</c>; the cross-platform <see cref="CoreAsmMapCache"/>
        /// returns a lazily-built <see cref="AsmMapSymbolFile"/>. The default
        /// body returns <c>null</c> so <see cref="HeadlessAsmMapCache"/> and any
        /// external implementor keep compiling — callers null-check before use,
        /// falling back to a region-class hint only.</para>
        /// </summary>
        IAsmMapFile? GetAsmMapFile() => null;
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
        [AllowNull]
        public static ROM ROM { get; set; }
        [AllowNull]
        public static Undo Undo { get; set; }

        // ---- Decomp project open-mode (#1129 slice 1) ----
        /// <summary>
        /// Active decomp project, or null in classic ROM mode. When set, the loaded
        /// ROM is a build PREVIEW of a source tree and is treated as read-only.
        /// </summary>
        [AllowNull]
        public static DecompProject DecompProject { get; set; }

        /// <summary>
        /// Computed: true when a decomp project is open. Cannot go stale because it
        /// reads <see cref="DecompProject"/> directly.
        /// </summary>
        public static bool IsDecompMode => DecompProject != null;

        // ---- Types used by Core code via interfaces ----
        [AllowNull]
        public static IEtcCache CommentCache { get; set; }
        [AllowNull]
        public static IEtcCache LintCache { get; set; }
        [AllowNull]
        public static IEtcCache WorkSupportCache { get; set; }
        [AllowNull]
        public static ISystemTextEncoder SystemTextEncoder { get; set; }
        public static IAsmMapCache? AsmMapFileAsmCache { get; set; }

        // ---- Types now in Core (concrete) ----
        [AllowNull]
        public static Config Config { get; set; }
        [AllowNull]
        public static EventScript EventScript { get; set; }
        [AllowNull]
        public static EventScript ProcsScript { get; set; }
        [AllowNull]
        public static EventScript AIScript { get; set; }
        [AllowNull]
        public static FETextEncode FETextEncoder { get; set; }
        [AllowNull]
        public static TextEscape TextEscape { get; set; }
        [AllowNull]
        public static ITextIDCache UseTextIDCache { get; set; }
        [AllowNull]
        public static EtcCacheFLag FlagCache { get; set; }
        [AllowNull]
        public static object ResourceCache { get; set; }
        [AllowNull]
        public static ExportFunction ExportFunction { get; set; }
        public static Mod Mod { get; set; }

        // ---- Simple value types ----
        [AllowNull]
        public static string BaseDirectory { get; set; }
        public static bool IsCommandLine { get; set; }
        public static Dictionary<string, string> ArgsDic { get; set; }

        /// <summary>
        /// Raised after the UI language is changed and translations reloaded.
        /// Subscribers (ViewModels, etc.) should refresh their localized strings.
        /// </summary>
        public static event Action LanguageChanged;

        /// <summary>Raise the LanguageChanged event.</summary>
        public static void RaiseLanguageChanged() => LanguageChanged?.Invoke();

        /// <summary>
        /// Current UI language code (e.g. "en", "ja", "zh").
        /// Set by OptionForm at startup. Used by ConfigDataFilename for lang-specific resources.
        /// </summary>
        [AllowNull]
        public static string Language { get; set; } = "en";

        /// <summary>
        /// Path to the git executable (e.g. "git" or "C:\Program Files\Git\cmd\git.exe").
        /// Set from OptionForm.git_path() at startup and when options are saved.
        /// </summary>
        public static string GitPath { get; set; } = "git";

        /// <summary>
        /// Platform-specific service provider (dialogs, clipboard, etc.).
        /// Must be set before any Core code that shows messages.
        /// </summary>
        [AllowNull]
        public static IAppServices Services { get; set; } = new HeadlessAppServices();

        /// <summary>
        /// Cross-platform image service for GBA graphics operations.
        /// Set by the host application (WinForms, Avalonia, CLI).
        /// </summary>
        [AllowNull]
        public static IImageService ImageService { get; set; }

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
        [AllowNull]
        public static Func<byte[], Undo.UndoData, uint> AppendBinaryData { get; set; }

        /// <summary>
        /// Wire <see cref="AppendBinaryData"/> to a headless free-space allocator
        /// (only if not already set) so RecycleAddress's "no recycled region fits"
        /// fallback works outside WinForms. WinForms sets AppendBinaryData to
        /// InputFormRef.AppendBinaryData; the Avalonia app (App.axaml.cs) and the
        /// CLI (RomLoader.InitFull) call this instead, and tests call it to
        /// exercise the EXACT production allocator (#796).
        ///
        /// Mirrors the proven per-feature headless pattern
        /// (MapExitPointCore.NewAlloc / MapEventUnitCore.ExpandUnitList):
        /// FindFreeSpace in the upper half, fall back to the lower half, then
        /// write the payload (recording into the passed undo when non-null).
        /// Operates on <see cref="ROM"/>; returns the write offset or
        /// <c>U.NOT_FOUND</c> on failure (never throws).
        /// </summary>
        public static void WireHeadlessAppendBinaryData()
        {
            if (AppendBinaryData != null) return;
            AppendBinaryData = (data, undo) =>
            {
                var rom = ROM;
                if (rom?.RomInfo == null || data == null) return U.NOT_FOUND;

                uint needsize = (uint)data.Length;
                uint addr = rom.FindFreeSpace((uint)(rom.Data.Length / 2), needsize);
                if (addr == U.NOT_FOUND)
                {
                    addr = rom.FindFreeSpace(0x100u, needsize);
                }
                if (addr == U.NOT_FOUND) return U.NOT_FOUND;

                if (undo != null) rom.write_range(addr, data, undo);
                else rom.write_range(addr, data);
                return addr;
            };
        }

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

        /// <summary>
        /// Resolves a skill ID to a human-readable name.
        /// Set by the UI layer (Avalonia/WinForms) which knows the installed skill system.
        /// Returns null if the name cannot be resolved (NameResolver will use hex fallback).
        /// </summary>
        public static Func<uint, string?>? SkillNameResolver { get; set; }
    }
}
