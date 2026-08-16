using System;
using System.Collections.Generic;

namespace FEBuilderGBA
{
    public static class SongTableExpansionCore
    {
        public const uint MaxSongCount = 32766;

        const uint EntrySize = 8;
        const int MaxPlausibleRepointSlots = 64;

        public static bool ShouldShowExpansion(
            ROM rom,
            bool configuredShow)
        {
            if (configuredShow) return true;
            if (rom?.RomInfo == null) return false;

            uint ptr = rom.RomInfo.sound_table_pointer;
            if (ptr == 0 || ptr + 3 >= (uint)rom.Data.Length)
                return false;
            return rom.u32(ptr) >= rom.RomInfo.extends_address;
        }

        public static DataExpansionCore.ExpandResult Expand(
            ROM rom,
            uint newCount)
        {
            if (CoreState.IsDecompMode)
                return Failure("Song Table expansion is unavailable in decomp mode.");
            if (rom?.RomInfo == null)
                return Failure("ROM not loaded.");

            uint pointerSlot = RebuildProducerCore.GetSoundTablePointer(rom);
            if (pointerSlot == 0
                || pointerSlot + 3 >= (uint)rom.Data.Length)
            {
                return Failure("This ROM has no valid Song Table pointer.");
            }

            uint oldBase = rom.p32(pointerSlot);
            if (!U.isSafetyOffset(oldBase))
                return Failure("The Song Table pointer is out of ROM bounds.");

            uint currentCount = CountEntries(rom, oldBase);
            if (currentCount == 0)
                return Failure("Cannot expand an empty Song Table because row 0 is unavailable.");
            if (newCount <= currentCount)
            {
                return Failure(
                    $"New count ({newCount}) must be greater than current count ({currentCount}).");
            }
            if (newCount > MaxSongCount)
            {
                return Failure(
                    $"New count ({newCount}) exceeds the maximum ({MaxSongCount}).");
            }

            if (!TryCaptureFullCache(
                    CoreState.CommentCache,
                    "comment",
                    out CacheState commentBefore,
                    out string cacheError))
            {
                return Failure(cacheError);
            }
            if (!TryCaptureFullCache(
                    CoreState.LintCache,
                    "lint",
                    out CacheState lintBefore,
                    out cacheError))
            {
                return Failure(cacheError);
            }

            byte[] romBefore = (byte[])rom.Data.Clone();
            Undo.UndoData undo = ROM.GetAmbientUndoData();
            int undoStart = undo?.list?.Count ?? 0;

            try
            {
                DataExpansionCore.ExpandResult result =
                    DataExpansionCore.ExpandTableTo(
                        rom,
                        pointerSlot,
                        EntrySize,
                        currentCount,
                        newCount,
                        new DataExpansionCore.ExpandOptions
                        {
                            Fill = DataExpansionCore.ExpandFill.First,
                            Repoint =
                                DataExpansionCore.ExpandRepoint.RawAndLdrAll,
                            FullZeroTerminatorRow = false,
                            RepointEtcCaches = false,
                        });

                if (!result.Success)
                {
                    RestoreFailure(
                        rom, romBefore, commentBefore, lintBefore,
                        undo, undoStart);
                    return result;
                }

                IReadOnlyList<uint> slots =
                    result.RepointedSlots ?? Array.Empty<uint>();
                if (slots.Count == 0)
                {
                    RestoreFailure(
                        rom, romBefore, commentBefore, lintBefore,
                        undo, undoStart);
                    return Failure(
                        "Song Table expansion found no references to repoint.");
                }

                bool canonicalCovered = false;
                foreach (uint slot in slots)
                {
                    if (slot == pointerSlot)
                    {
                        canonicalCovered = true;
                        break;
                    }
                }
                if (!canonicalCovered)
                {
                    RestoreFailure(
                        rom, romBefore, commentBefore, lintBefore,
                        undo, undoStart);
                    return Failure(
                        "Song Table expansion did not repoint the canonical pointer slot.");
                }
                if (slots.Count > MaxPlausibleRepointSlots)
                {
                    RestoreFailure(
                        rom, romBefore, commentBefore, lintBefore,
                        undo, undoStart);
                    return Failure(
                        $"Song Table expansion found an implausible {slots.Count} references.");
                }

                if (!RestoreFullCache(
                        CoreState.CommentCache, commentBefore)
                    || !RestoreFullCache(
                        CoreState.LintCache, lintBefore))
                {
                    RestoreFailure(
                        rom, romBefore, commentBefore, lintBefore,
                        undo, undoStart);
                    return Failure(
                        "Song Table expansion could not restore the transactional caches.");
                }

                uint oldTableSize = currentCount * EntrySize;
                var ranges = new[]
                {
                    new EtcCacheRange(oldBase, oldTableSize),
                    new EtcCacheRange(result.NewBaseAddress, oldTableSize),
                };

                if (!TryCaptureRanges(
                        CoreState.CommentCache,
                        commentBefore,
                        ranges,
                        "comment",
                        out EtcCacheSnapshot commentPatch,
                        out cacheError)
                    || !TryCaptureRanges(
                        CoreState.LintCache,
                        lintBefore,
                        ranges,
                        "lint",
                        out EtcCacheSnapshot lintPatch,
                        out cacheError))
                {
                    RestoreFailure(
                        rom, romBefore, commentBefore, lintBefore,
                        undo, undoStart);
                    return Failure(cacheError);
                }

                CoreState.CommentCache?.RepointEtcData(
                    oldBase, oldTableSize, result.NewBaseAddress);
                CoreState.LintCache?.RepointEtcData(
                    oldBase, oldTableSize, result.NewBaseAddress);

                if (undo != null)
                {
                    if (commentPatch != null)
                    {
                        undo.AddCachePatch(
                            Undo.EtcCacheSlot.Comment,
                            commentPatch);
                    }
                    if (lintPatch != null)
                    {
                        undo.AddCachePatch(
                            Undo.EtcCacheSlot.Lint,
                            lintPatch);
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                RestoreFailure(
                    rom, romBefore, commentBefore, lintBefore,
                    undo, undoStart);
                Log.Error(
                    "SongTableExpansionCore.Expand failed: " +
                    ex.ToString());
                return Failure("Song Table expansion failed: " + ex.Message);
            }
        }

        static uint CountEntries(ROM rom, uint baseAddr)
        {
            uint count = 0;
            while (count < MaxSongCount)
            {
                uint addr = baseAddr + count * EntrySize;
                if (addr + EntrySize - 1 >= (uint)rom.Data.Length)
                    break;
                if (!U.isPointer(rom.u32(addr)))
                    break;
                count++;
            }
            return count;
        }

        static bool TryCaptureFullCache(
            IEtcCache cache,
            string name,
            out CacheState state,
            out string error)
        {
            if (cache == null)
            {
                state = new CacheState(false, null);
                error = "";
                return true;
            }
            if (!cache.TryCaptureAll(out EtcCacheSnapshot snapshot)
                || !cache.TryRestoreAll(snapshot))
            {
                state = default;
                error =
                    $"Song Table expansion requires transactional {name} cache support.";
                return false;
            }

            state = new CacheState(true, snapshot);
            error = "";
            return true;
        }

        static bool TryCaptureRanges(
            IEtcCache cache,
            CacheState before,
            IReadOnlyList<EtcCacheRange> ranges,
            string name,
            out EtcCacheSnapshot snapshot,
            out string error)
        {
            snapshot = null;
            if (!before.HadCache)
            {
                error = "";
                return true;
            }
            if (cache == null
                || !cache.TryCaptureRanges(ranges, out snapshot))
            {
                error =
                    $"Song Table expansion could not capture the live {name} cache.";
                return false;
            }

            error = "";
            return true;
        }

        static void RestoreFailure(
            ROM rom,
            byte[] romBefore,
            CacheState commentBefore,
            CacheState lintBefore,
            Undo.UndoData undo,
            int undoStart)
        {
            if (rom.Data.Length != romBefore.Length)
                rom.write_resize_data((uint)romBefore.Length);
            Array.Copy(romBefore, rom.Data, romBefore.Length);

            RestoreFullCache(CoreState.CommentCache, commentBefore);
            RestoreFullCache(CoreState.LintCache, lintBefore);

            if (undo?.list != null && undo.list.Count > undoStart)
                undo.list.RemoveRange(
                    undoStart, undo.list.Count - undoStart);
        }

        static bool RestoreFullCache(
            IEtcCache cache,
            CacheState state)
        {
            if (!state.HadCache)
                return true;
            if (cache == null
                || state.Snapshot == null
                || !cache.TryRestoreAll(state.Snapshot))
            {
                Log.Error(
                    "Song Table cache restore failed because the live cache is unavailable or unsupported.");
                return false;
            }
            return true;
        }

        static DataExpansionCore.ExpandResult Failure(string error)
        {
            return new DataExpansionCore.ExpandResult
            {
                Success = false,
                Error = error,
            };
        }

        readonly struct CacheState
        {
            public CacheState(
                bool hadCache,
                EtcCacheSnapshot snapshot)
            {
                HadCache = hadCache;
                Snapshot = snapshot;
            }

            public bool HadCache { get; }
            public EtcCacheSnapshot Snapshot { get; }
        }
    }
}
