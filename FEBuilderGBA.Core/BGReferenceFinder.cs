using System.Collections.Generic;

namespace FEBuilderGBA
{
    /// <summary>
    /// Thin <see cref="EventScript.ArgType.BG"/> wrapper over
    /// <see cref="EventScriptReferenceScanner.FindAllArgReferences"/>, with a
    /// per-ROM-instance cache so the all-map disassembly runs at most once per
    /// loaded ROM. Restores the Avalonia Background Image editor's References
    /// list (the WinForms <c>InputFormRef.UpdateRef(X_REF, id, BG)</c> path,
    /// for the event-script source).
    ///
    /// CACHE INVALIDATION: by ROM-instance identity (a new ROM load produces a
    /// new <see cref="ROM"/> instance, triggering a rebuild). Within-session
    /// event edits do NOT invalidate the cache — a documented residual that
    /// matches the WinForms cache-staleness model (WF rebuilds
    /// <c>AsmMapFileAsmCache</c> only on specific triggers, not on every edit).
    ///
    /// SCOPE: event-script BG references only. Patch-config (MULTICG/BGICON) and
    /// ASM/MAP symbol references are documented residual gaps — see
    /// <see cref="EventScriptReferenceScanner"/>.
    /// </summary>
    public static class BGReferenceFinder
    {
        static readonly object _lock = new object();
        static ROM _cachedRom;
        static Dictionary<uint, List<AddrResult>> _cache;

        /// <summary>
        /// Return the event-script references to BG slot <paramref name="bgId"/>
        /// (a fresh copy of the cached list, so the caller can't mutate the
        /// cache). Empty when the ROM is null/invalid, the scanner prerequisites
        /// are not yet satisfied, or the BG id is unreferenced.
        ///
        /// CACHE-POISONING GUARD: the scanner's
        /// <see cref="EventScriptReferenceScanner.FindAllArgReferences"/> returns
        /// an EMPTY map both when a BG is genuinely unreferenced AND when its
        /// prerequisites (an active <see cref="EventScript"/>, a wired
        /// <see cref="CoreState.CommentCache"/>, and <paramref name="rom"/> being
        /// the active <see cref="CoreState.ROM"/>) are not ready. If we cached
        /// the latter, the SAME ROM instance would stay "cached empty" forever
        /// even after CoreState finished initializing — so the References list
        /// would never populate. We therefore pre-check the prerequisites here
        /// and, when they are NOT satisfied, return a fresh empty list WITHOUT
        /// caching (so a later fully-initialized call rebuilds for real).
        /// </summary>
        public static List<AddrResult> MakeListByUseBG(ROM rom, uint bgId)
        {
            if (rom?.RomInfo == null) return new List<AddrResult>();

            // Prerequisites for a REAL scan (mirror the gate inside
            // FindAllArgReferences + the CommentCache the disasm path needs).
            bool prereqsReady = CoreState.EventScript != null
                && CoreState.CommentCache != null
                && CoreState.ROM != null
                && ReferenceEquals(CoreState.ROM, rom);

            lock (_lock)
            {
                if (!ReferenceEquals(_cachedRom, rom))
                {
                    if (!prereqsReady)
                    {
                        // Not ready — return empty WITHOUT poisoning the cache,
                        // so the next (initialized) call for the same ROM scans.
                        return new List<AddrResult>();
                    }

                    _cache = EventScriptReferenceScanner.FindAllArgReferences(
                        rom, EventScript.ArgType.BG, keepZeroId: true);
                    _cachedRom = rom;
                }

                if (_cache != null && _cache.TryGetValue(bgId, out var l) && l != null)
                {
                    return new List<AddrResult>(l);
                }
                return new List<AddrResult>();
            }
        }

        /// <summary>Reset the per-ROM cache. For tests only.</summary>
        internal static void ResetCache()
        {
            lock (_lock)
            {
                _cachedRom = null;
                _cache = null;
            }
        }
    }
}
