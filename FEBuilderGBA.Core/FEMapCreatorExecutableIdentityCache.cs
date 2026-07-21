// SPDX-License-Identifier: GPL-3.0-or-later
namespace FEBuilderGBA
{
    /// <summary>
    /// Per-instance (NEVER static/shared/process-wide) reuse cache for the relatively expensive
    /// SHA-256 executable-content hash computed by <see cref="FEMapCreatorProfileCore.Validate"/>.
    /// The Avalonia Options view re-validates on every keystroke typed into either FEMapCreator
    /// textbox (#1978 Slice 2 review follow-up); without this cache, editing the assets-root field
    /// alone would still re-hash the entire (unchanged) executable on every character typed. This
    /// cache is keyed by (normalized executable path, file size, last-write-UTC ticks): the SHA-256
    /// is only recomputed when any of those three change, which is a cheap `File.Exists`+`FileInfo`
    /// stat performed on every call regardless of cache hit/miss.
    ///
    /// Intentionally NOT static: each <c>OptionsViewModel</c> (and therefore each Options dialog
    /// session) owns its own instance, so there is no cross-session/cross-ROM staleness or leakage
    /// risk — a freshly opened Options dialog always starts with an empty cache and hashes once on
    /// first display. This cache is purely a UI-responsiveness optimization for the on-demand status
    /// display; it must NEVER be used by authoritative discovery, mapping persistence, or
    /// <see cref="FEMapCreatorTilesetMappingStoreCore.Lookup"/>. Those paths always perform a full,
    /// uncached re-stat + re-hash because a same-length replacement can preserve its timestamp and
    /// must not be mistaken for the prior executable.
    /// </summary>
    internal sealed class FEMapCreatorExecutableIdentityCache
    {
        string? _cachedNormalizedPath;
        long _cachedSizeBytes;
        long _cachedLastWriteUtcTicks;
        string? _cachedSha256;

        /// <summary>
        /// Test-only instrumentation: counts actual SHA-256 (re)computations performed by this
        /// cache instance. Never read by production code paths, never logged, never surfaced to
        /// the UI or persisted — it exists solely so tests can assert that repeated calls with an
        /// unchanged executable identity do not repeatedly hash.
        /// </summary>
        internal int HashComputeCount { get; private set; }

        /// <summary>
        /// Returns the SHA-256 content hash (lowercase hex) of <paramref name="normalizedPath"/>,
        /// reusing the previously computed hash when the path/size/last-write-time triple is
        /// unchanged since the last successful call, or recomputing (and caching) it otherwise.
        /// Always performs a fresh, cheap stat; only the hash itself is ever reused. Returns false
        /// with <paramref name="error"/> set when the path is blank, missing, or unreadable — in
        /// that case the cache is invalidated so a later valid path is never served a stale hit.
        /// </summary>
        internal bool TryGetOrCompute(string normalizedPath, out long sizeBytes, out long lastWriteUtcTicks, out string sha256Hex, out string error)
        {
            if (!FileContentIdentityCore.TryStat(normalizedPath, out sizeBytes, out lastWriteUtcTicks, out error))
            {
                sha256Hex = "";
                Invalidate();
                return false;
            }

            if (_cachedSha256 != null
                && _cachedNormalizedPath != null
                && string.Equals(_cachedNormalizedPath, normalizedPath, FEMapCreatorLauncherCore.PathComparison)
                && _cachedSizeBytes == sizeBytes
                && _cachedLastWriteUtcTicks == lastWriteUtcTicks)
            {
                sha256Hex = _cachedSha256;
                return true;
            }

            if (!FileContentIdentityCore.TryComputeHashOnly(normalizedPath, out sha256Hex, out error))
            {
                Invalidate();
                return false;
            }

            HashComputeCount++;
            _cachedNormalizedPath = normalizedPath;
            _cachedSizeBytes = sizeBytes;
            _cachedLastWriteUtcTicks = lastWriteUtcTicks;
            _cachedSha256 = sha256Hex;
            return true;
        }

        void Invalidate()
        {
            _cachedNormalizedPath = null;
            _cachedSha256 = null;
        }
    }
}
