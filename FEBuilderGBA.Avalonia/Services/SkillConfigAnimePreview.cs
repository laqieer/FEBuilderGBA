// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
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
    /// result is CACHED by (ROM instance, anime pointer) — re-selecting the same
    /// skill or scrubbing frames re-uses the cached decode. Keying on the ROM
    /// reference too means a ROM swap that happens to reuse the same pointer
    /// value can never render the previous ROM's stale frames. The cache is
    /// invalidated (disposed) by the host on a pointer change, a successful
    /// Write/import that can mutate the slot, and on window close.
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
        ROM? _decodedRom;
        SkillAnimeExportResult? _cached;

        /// <summary>Frame count of the currently-cached animation (0 when none).</summary>
        public int FrameCount => _cached?.Frames?.Count ?? 0;

        /// <summary>
        /// Decode the animation at <paramref name="animePointer"/> once and cache
        /// it (keyed on the ROM instance + pointer); subsequent calls with the same
        /// ROM and pointer re-use the cache. Returns true when at least one frame is
        /// available.
        /// </summary>
        public bool Load(ROM rom, uint animePointer)
        {
            if (rom == null || animePointer == 0) { Clear(); return false; }
            if (ReferenceEquals(_decodedRom, rom) && _decodedPointer == animePointer && _cached != null)
                return _cached.Frames.Count > 0;
            Clear();                                  // dispose any prior cache first
            var r = SkillSystemsAnimeExportCore.ExportSkillAnimation(rom, animePointer);
            _decodedRom = rom;
            _decodedPointer = animePointer;
            _cached = (string.IsNullOrEmpty(r.Error) && r.Frames.Count > 0) ? r : null;
            return _cached != null;
        }

        /// <summary>
        /// Bounds-checked frame -> a NEW Avalonia Bitmap (null if OOB / no image).
        /// The returned bitmap owns its own pixel buffer and is safe to keep after
        /// the cache is cleared.
        /// </summary>
        public Bitmap? TryGetFrameBitmap(int frameIndex)
        {
            IImage? img = SkillSystemsAnimeExportCore.GetFrameImage(_cached, frameIndex);
            return img == null ? null : IconBitmapBuilder.FromImage(img);
        }

        /// <summary>
        /// Bounds-checked frame -> the CACHED <see cref="IImage"/> (NOT a clone; the
        /// cache still owns it and disposes it in <see cref="Clear"/>). For callers
        /// that build their own Avalonia bitmap from an IImage and do NOT take
        /// ownership — e.g. <c>GbaImageControl.SetImage(IImage)</c>, which copies the
        /// pixels synchronously. Returns null when OOB / no image. #1115.
        /// </summary>
        public IImage? TryGetFrameImage(int frameIndex)
            => SkillSystemsAnimeExportCore.GetFrameImage(_cached, frameIndex);

        /// <summary>
        /// Dispose each UNIQUE cached frame IImage (IImage : IDisposable) and
        /// reset. Safe: <see cref="TryGetFrameBitmap"/> copies pixels into a NEW
        /// WriteableBitmap, so a displayed bitmap survives disposal of its source
        /// IImage. The Core export caches one IImage per OBJ id, so duplicate
        /// frames share an instance — the HashSet de-dups by REFERENCE
        /// (<see cref="ReferenceEqualityComparer"/>) so it can never mistake two
        /// distinct images for one and skip a dispose (leak).
        /// </summary>
        public void Clear()
        {
            if (_cached?.Frames != null)
            {
                var seen = new HashSet<IImage>(ReferenceEqualityComparer.Instance);
                foreach (var f in _cached.Frames)
                    if (f.Image != null && seen.Add(f.Image))
                        try { f.Image.Dispose(); } catch { /* swallow */ }
            }
            _cached = null;
            _decodedPointer = U.NOT_FOUND;
            _decodedRom = null;
        }
    }
}
