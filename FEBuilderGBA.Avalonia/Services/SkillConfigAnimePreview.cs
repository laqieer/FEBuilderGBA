// SPDX-License-Identifier: GPL-3.0-or-later
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Controls;
using Bitmap = global::Avalonia.Media.Imaging.Bitmap;

namespace FEBuilderGBA.Avalonia.Services
{
    /// <summary>
    /// Shared per-view helper that renders the SkillConfig "Display example"
    /// per-frame preview via the cross-platform READ-ONLY
    /// <see cref="FEBuilderGBA.SkillSystemsAnimeExportCore"/> seam (#1010).
    ///
    /// The decode is expensive (LZ77-decompresses OBJ+TSA per frame) so the
    /// result is CACHED by anime pointer — re-selecting the same skill or
    /// scrubbing frames re-uses the cached decode. The cache is invalidated
    /// (disposed) by the host on a pointer change, a successful Write/import
    /// that can mutate the slot, and on window close.
    ///
    /// Each cached <see cref="FEBuilderGBA.SkillAnimeFrame.Image"/> is an
    /// <see cref="FEBuilderGBA.IImage"/> (which is <c>IDisposable</c>);
    /// <see cref="Clear"/> disposes each UNIQUE frame image exactly once.
    /// <see cref="TryGetFrameBitmap"/> copies pixels into a NEW Avalonia
    /// WriteableBitmap, so a displayed bitmap survives disposal of its source
    /// IImage (it is independent of the cache lifetime).
    /// </summary>
    public sealed class SkillConfigAnimePreview
    {
        uint _decodedPointer = U.NOT_FOUND;
        SkillAnimeExportResult _cached;

        /// <summary>Frame count of the currently-cached animation (0 when none).</summary>
        public int FrameCount => _cached?.Frames?.Count ?? 0;

        /// <summary>
        /// Decode the animation at <paramref name="animePointer"/> once and cache
        /// it; subsequent calls with the same pointer re-use the cache. Returns
        /// true when at least one frame is available.
        /// </summary>
        public bool Load(ROM rom, uint animePointer)
        {
            if (rom == null || animePointer == 0) { Clear(); return false; }
            if (_decodedPointer == animePointer && _cached != null) return _cached.Frames.Count > 0;
            Clear();                                  // dispose any prior cache first
            var r = SkillSystemsAnimeExportCore.ExportSkillAnimation(rom, animePointer);
            _decodedPointer = animePointer;
            _cached = (string.IsNullOrEmpty(r.Error) && r.Frames.Count > 0) ? r : null;
            return _cached != null;
        }

        /// <summary>
        /// Bounds-checked frame -> a NEW Avalonia Bitmap (null if OOB / no image).
        /// The returned bitmap owns its own pixel buffer and is safe to keep after
        /// the cache is cleared.
        /// </summary>
        public Bitmap TryGetFrameBitmap(int frameIndex)
        {
            var img = SkillSystemsAnimeExportCore.GetFrameImage(_cached, frameIndex);
            return img == null ? null : IconBitmapBuilder.FromImage(img);
        }

        /// <summary>
        /// Dispose each UNIQUE cached frame IImage (IImage : IDisposable) and
        /// reset. Safe: <see cref="TryGetFrameBitmap"/> copies pixels into a NEW
        /// WriteableBitmap, so a displayed bitmap survives disposal of its source
        /// IImage. The Core export caches one IImage per OBJ id, so duplicate
        /// frames share an instance — a HashSet de-dups them by reference (the
        /// concrete IImage type uses default reference equality).
        /// </summary>
        public void Clear()
        {
            if (_cached?.Frames != null)
            {
                var seen = new HashSet<IImage>();        // ReferenceEqualityComparer not needed — distinct refs
                foreach (var f in _cached.Frames)
                    if (f.Image != null && seen.Add(f.Image))
                        try { f.Image.Dispose(); } catch { /* swallow */ }
            }
            _cached = null;
            _decodedPointer = U.NOT_FOUND;
        }
    }
}
