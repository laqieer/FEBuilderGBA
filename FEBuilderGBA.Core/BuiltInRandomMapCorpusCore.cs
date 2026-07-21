// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace FEBuilderGBA
{
    /// <summary>
    /// Deterministic, per-call evidence pool gathered from every ROM map that shares the
    /// current map's <see cref="TilesetFingerprint"/>. Built fresh by
    /// <see cref="BuiltInRandomMapCorpusCore.TryBuildCorpus"/> on every call — there is
    /// intentionally no static/process-wide cache, so evidence from one ROM or one stale
    /// in-memory ROM edit can never leak into a later call against a different ROM instance
    /// or a re-loaded one.
    /// </summary>
    public sealed class BuiltInRandomMapTilesetCorpus
    {
        internal BuiltInRandomMapTilesetCorpus(
            TilesetFingerprint fingerprint,
            IReadOnlyList<uint> contributingMapIds,
            IReadOnlyList<ushort> candidates,
            IReadOnlyDictionary<ushort, long> frequency,
            IReadOnlyDictionary<ushort, long> borderFrequency,
            IReadOnlyDictionary<ushort, IReadOnlySet<ushort>> horizontalAdjacency,
            IReadOnlyDictionary<ushort, IReadOnlySet<ushort>> verticalAdjacency,
            byte[] objData,
            byte[] paletteData,
            byte[] configData,
            long totalCells)
        {
            Fingerprint = fingerprint;
            ContributingMapIds = contributingMapIds;
            Candidates = candidates;
            Frequency = frequency;
            BorderFrequency = borderFrequency;
            HorizontalAdjacency = horizontalAdjacency;
            VerticalAdjacency = verticalAdjacency;
            ObjData = objData;
            PaletteData = paletteData;
            ConfigData = configData;
            TotalCells = totalCells;
        }

        /// <summary>The tileset identity every contributing map shared with the requested current map.</summary>
        public TilesetFingerprint Fingerprint { get; }

        /// <summary>
        /// Ascending map ids (in the same order <see cref="MapSettingCore.MakeMapIDList"/>
        /// enumerates them) that matched <see cref="Fingerprint"/> and contributed evidence.
        /// </summary>
        public IReadOnlyList<uint> ContributingMapIds { get; }

        /// <summary>Distinct MAR values observed anywhere in the corpus, ascending.</summary>
        public IReadOnlyList<ushort> Candidates { get; }

        /// <summary>Occurrence count of each MAR value across every contributing map cell.</summary>
        public IReadOnlyDictionary<ushort, long> Frequency { get; }

        /// <summary>Occurrence count of each MAR value restricted to contributing maps' outer border cells.</summary>
        public IReadOnlyDictionary<ushort, long> BorderFrequency { get; }

        /// <summary>
        /// For a MAR value placed to the west, the set of MAR values directly observed
        /// immediately to its east in at least one contributing map.
        /// </summary>
        public IReadOnlyDictionary<ushort, IReadOnlySet<ushort>> HorizontalAdjacency { get; }

        /// <summary>
        /// For a MAR value placed to the north, the set of MAR values directly observed
        /// immediately to its south in at least one contributing map.
        /// </summary>
        public IReadOnlyDictionary<ushort, IReadOnlySet<ushort>> VerticalAdjacency { get; }

        /// <summary>Decompressed OBJ bytes shared by every contributing map (fingerprint-equal implies byte-identical).</summary>
        public byte[] ObjData { get; }

        /// <summary>Palette bytes shared by every contributing map.</summary>
        public byte[] PaletteData { get; }

        /// <summary>Decompressed chipset config bytes shared by every contributing map.</summary>
        public byte[] ConfigData { get; }

        /// <summary>Total cell count summed across every contributing map (evidence volume, not output size).</summary>
        public long TotalCells { get; }

        /// <summary>True when the strict model has at least one recorded directional pair.</summary>
        public bool HasStrictAdjacencyEvidence => HorizontalAdjacency.Count > 0 || VerticalAdjacency.Count > 0;

        /// <summary>
        /// Construct a corpus directly from already-computed evidence, for solver unit tests
        /// that don't need a full ROM + LZ77 round trip. Not part of the public API surface —
        /// production callers must go through <see cref="BuiltInRandomMapCorpusCore.TryBuildCorpus"/>
        /// so every corpus is provably derived from a real, current ROM scan.
        /// </summary>
        internal static BuiltInRandomMapTilesetCorpus CreateForTesting(
            TilesetFingerprint fingerprint,
            IReadOnlyList<uint> contributingMapIds,
            IReadOnlyList<ushort> candidates,
            IReadOnlyDictionary<ushort, long> frequency,
            IReadOnlyDictionary<ushort, long> borderFrequency,
            IReadOnlyDictionary<ushort, IReadOnlySet<ushort>> horizontalAdjacency,
            IReadOnlyDictionary<ushort, IReadOnlySet<ushort>> verticalAdjacency,
            byte[] objData,
            byte[] paletteData,
            byte[] configData,
            long totalCells) =>
            new BuiltInRandomMapTilesetCorpus(
                fingerprint, contributingMapIds, candidates, frequency, borderFrequency,
                horizontalAdjacency, verticalAdjacency, objData, paletteData, configData, totalCells);
    }

    /// <summary>
    /// Builds a <see cref="BuiltInRandomMapTilesetCorpus"/> by scanning every ROM map in
    /// deterministic ascending map-index order and accumulating adjacency/frequency evidence
    /// from every map whose decoded tileset fingerprint exactly matches the requested current
    /// map. Pure function of its (rom, mapSettingAddr) input — no caching, no shared state.
    /// </summary>
    public static class BuiltInRandomMapCorpusCore
    {
        /// <summary>
        /// Build the corpus for the map currently loaded at <paramref name="currentMapSettingAddr"/>.
        /// Maps that fail to resolve (bad PLIST, truncated LZ77, etc.) are silently skipped —
        /// one damaged map entry elsewhere in the ROM never fails the whole corpus build.
        /// </summary>
        public static bool TryBuildCorpus(
            ROM rom,
            uint currentMapSettingAddr,
            out BuiltInRandomMapTilesetCorpus corpus,
            out string error) =>
            TryBuildCorpus(
                rom,
                currentMapSettingAddr,
                CancellationToken.None,
                out corpus,
                out error);

        public static bool TryBuildCorpus(
            ROM rom,
            uint currentMapSettingAddr,
            CancellationToken cancellationToken,
            out BuiltInRandomMapTilesetCorpus corpus,
            out string error)
        {
            corpus = null;
            error = "";
            cancellationToken.ThrowIfCancellationRequested();

            if (!BuiltInRandomMapTilesetCore.TryResolveMapTileset(rom, currentMapSettingAddr, out MapTilesetSnapshot current, out error))
                return false;

            var contributingMapIds = new List<uint>();
            var frequency = new SortedDictionary<ushort, long>();
            var borderFrequency = new SortedDictionary<ushort, long>();
            var horizontal = new SortedDictionary<ushort, SortedSet<ushort>>();
            var vertical = new SortedDictionary<ushort, SortedSet<ushort>>();
            long totalCells = 0;

            List<AddrResult> mapList = MapSettingCore.MakeMapIDList(rom);
            foreach (AddrResult entry in mapList)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!BuiltInRandomMapTilesetCore.TryResolveMapTileset(rom, entry.addr, out MapTilesetSnapshot snapshot, out _))
                    continue;
                if (snapshot.Fingerprint != current.Fingerprint)
                    continue;

                contributingMapIds.Add(entry.tag);
                AccumulateMap(
                    snapshot,
                    frequency,
                    borderFrequency,
                    horizontal,
                    vertical,
                    ref totalCells,
                    cancellationToken);
            }

            var candidates = new List<ushort>(frequency.Keys);
            candidates.Sort();

            corpus = new BuiltInRandomMapTilesetCorpus(
                current.Fingerprint,
                contributingMapIds,
                candidates,
                frequency,
                borderFrequency,
                ToReadOnlyAdjacency(horizontal),
                ToReadOnlyAdjacency(vertical),
                current.ObjData,
                current.PaletteData,
                current.ConfigData,
                totalCells);
            return true;
        }

        static void AccumulateMap(
            MapTilesetSnapshot snapshot,
            SortedDictionary<ushort, long> frequency,
            SortedDictionary<ushort, long> borderFrequency,
            SortedDictionary<ushort, SortedSet<ushort>> horizontal,
            SortedDictionary<ushort, SortedSet<ushort>> vertical,
            ref long totalCells,
            CancellationToken cancellationToken)
        {
            int width = snapshot.Width;
            int height = snapshot.Height;
            byte[] mapData = snapshot.MapData;

            ushort ReadMar(int x, int y)
            {
                int off = MapEditorTilesetCore.GetMapDataOffset(width, x, y);
                return (ushort)(mapData[off] | (mapData[off + 1] << 8));
            }

            for (int y = 0; y < height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (int x = 0; x < width; x++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ushort mar = ReadMar(x, y);
                    frequency.TryGetValue(mar, out long count);
                    frequency[mar] = count + 1;
                    totalCells++;

                    if (x == 0 || y == 0 || x == width - 1 || y == height - 1)
                    {
                        borderFrequency.TryGetValue(mar, out long borderCount);
                        borderFrequency[mar] = borderCount + 1;
                    }

                    if (x + 1 < width)
                    {
                        ushort east = ReadMar(x + 1, y);
                        AddAdjacency(horizontal, mar, east);
                    }
                    if (y + 1 < height)
                    {
                        ushort south = ReadMar(x, y + 1);
                        AddAdjacency(vertical, mar, south);
                    }
                }
            }
        }

        static void AddAdjacency(SortedDictionary<ushort, SortedSet<ushort>> map, ushort key, ushort value)
        {
            if (!map.TryGetValue(key, out SortedSet<ushort> set))
            {
                set = new SortedSet<ushort>();
                map[key] = set;
            }
            set.Add(value);
        }

        static IReadOnlyDictionary<ushort, IReadOnlySet<ushort>> ToReadOnlyAdjacency(SortedDictionary<ushort, SortedSet<ushort>> source)
        {
            var result = new SortedDictionary<ushort, IReadOnlySet<ushort>>();
            foreach (KeyValuePair<ushort, SortedSet<ushort>> kv in source)
                result[kv.Key] = kv.Value;
            return result;
        }
    }
}
