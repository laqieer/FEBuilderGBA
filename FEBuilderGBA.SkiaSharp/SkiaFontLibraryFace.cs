// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using FEBuilderGBA.Core;
using SkiaSharp;

namespace FEBuilderGBA.SkiaSharp
{
    /// <summary>Strict SkiaSharp 3.119 font-face factory for package builds.</summary>
    public sealed class SkiaFontLibraryFaceFactory : IFontLibraryFaceFactory
    {
        public bool TryOpenFace(
            FontLibraryFaceSpec spec,
            out IFontLibraryFace face,
            out string error)
        {
            face = null;
            error = "";
            if (spec.FontFileData == null || spec.FontFileData.Length == 0)
            {
                error = "Verified FontFileData is required.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(spec.ExpectedFamily)
                || string.IsNullOrWhiteSpace(spec.ExpectedVersion))
            {
                error =
                    "Declared non-empty font family and version are required.";
                return false;
            }
            if (float.IsNaN(spec.Size)
                || float.IsInfinity(spec.Size)
                || spec.Size <= 0
                || spec.Size > 256)
            {
                error = "Font size must be finite and in (0, 256].";
                return false;
            }

            MemoryStream fontStream = null;
            SKTypeface typeface = null;
            try
            {
                if (!SfntFontIdentityCore.TryRead(
                    spec.FontFileData,
                    out SfntFontIdentity identity,
                    out string identityError))
                {
                    error = "Could not read exact SFNT identity: "
                        + identityError;
                    return false;
                }
                if (!string.Equals(
                    identity.Family,
                    spec.ExpectedFamily,
                    StringComparison.Ordinal)
                    || !string.Equals(
                        identity.Version,
                        spec.ExpectedVersion,
                        StringComparison.Ordinal))
                {
                    error = "Declared font family/version does not match "
                        + "the verified bytes (actual family '"
                        + identity.Family + "', version '"
                        + identity.Version + "').";
                    return false;
                }

                fontStream = new MemoryStream(
                    spec.FontFileData, writable: false);
                typeface = SKTypeface.FromStream(fontStream);
                if (typeface == null)
                {
                    fontStream.Dispose();
                    error = "Skia rejected FontFileData; fallback is forbidden.";
                    return false;
                }
                SKTypeface defaultTypeface = SKTypeface.Default;
                bool isDefaultReference =
                    ReferenceEquals(typeface, defaultTypeface);
                if (isDefaultReference)
                {
                    // A process-global default is not owned by this factory and
                    // is never evidence that the verified bytes were opened.
                    // A distinct wrapper for that native face is ours, however,
                    // and must release its own native reference.
                    SKTypeface rejectedTypeface = typeface;
                    typeface = null;
                    if (!isDefaultReference)
                        rejectedTypeface.Dispose();
                    fontStream.Dispose();
                    error =
                        "Skia returned its default typeface; fallback is forbidden.";
                    return false;
                }
                if (string.IsNullOrWhiteSpace(typeface.FamilyName)
                    || !string.Equals(
                        typeface.FamilyName,
                        spec.ExpectedFamily,
                        StringComparison.Ordinal))
                {
                    typeface.Dispose();
                    fontStream.Dispose();
                    error =
                        "Skia returned a null/default/fallback family instead of the declared exact family.";
                    return false;
                }

                // FromStream either decodes these exact bytes or returns null. It
                // never performs family/default resolution, which is the key
                // strictness distinction from the legacy IFontRasterizer. Keep
                // the source alive for the face lifetime as Skia may lazily read
                // font tables on some native builds.
                face = new SkiaFontLibraryFace(
                    typeface, fontStream, spec.Size);
                typeface = null;
                fontStream = null;
                return true;
            }
            catch (Exception ex)
            {
                try { typeface?.Dispose(); }
                catch { }
                try { fontStream?.Dispose(); }
                catch { }
                error = "Could not open FontFileData: " + ex.Message;
                return false;
            }
        }
    }

    /// <summary>
    /// One native SKTypeface reused for the whole job. Glyph coverage uses
    /// SKTypeface.GetGlyph(int), the availability API exposed by SkiaSharp 3.119.4.
    /// </summary>
    public sealed class SkiaFontLibraryFace : IFontLibraryFace
    {
        SKTypeface _typeface;
        MemoryStream _fontStream;
        readonly float _size;

        internal SkiaFontLibraryFace(
            SKTypeface typeface,
            MemoryStream fontStream,
            float size)
        {
            _typeface = typeface
                ?? throw new ArgumentNullException(nameof(typeface));
            _fontStream = fontStream
                ?? throw new ArgumentNullException(nameof(fontStream));
            _size = size;
        }

        public bool HasGlyph(int unicodeScalar)
        {
            if (_typeface == null || !IsUnicodeScalar(unicodeScalar))
                return false;
            // Do not collapse a native/font-engine exception into a semantic
            // missing-glyph result; the builder classifies such exceptions as
            // exit-1 font failures.
            return _typeface.GetGlyph(unicodeScalar) != 0;
        }

        public bool TryRasterizeGlyph(
            int unicodeScalar,
            bool isItemFont,
            int verticalOffset,
            out byte[] engineTile,
            out int glyphWidth,
            out string error)
            => TryRasterizeGlyph(
                unicodeScalar,
                isItemFont,
                verticalOffset,
                out engineTile,
                out glyphWidth,
                out _,
                out error);

        public bool TryRasterizeGlyph(
            int unicodeScalar,
            bool isItemFont,
            int verticalOffset,
            out byte[] engineTile,
            out int glyphWidth,
            out FontLibraryFaceFailureKind failureKind,
            out string error)
        {
            engineTile = Array.Empty<byte>();
            glyphWidth = 0;
            failureKind = FontLibraryFaceFailureKind.None;
            error = "";
            if (_typeface == null)
            {
                failureKind = FontLibraryFaceFailureKind.Font;
                error = "Font face is disposed.";
                return false;
            }
            try
            {
                if (!HasGlyph(unicodeScalar))
                {
                    failureKind = FontLibraryFaceFailureKind.MissingGlyph;
                    error = "The verified font has no glyph for U+"
                        + unicodeScalar.ToString("X4") + ".";
                    return false;
                }
                byte[] tile =
                    SkiaFontRasterizer
                        .RasterizeDeterministicWithTypeface(
                    _typeface,
                    _size,
                    unicodeScalar,
                    isItemFont,
                    verticalOffset,
                    out int width);
                if (tile == null || tile.Length != 64
                    || width < 1 || width > 16)
                {
                    failureKind = FontLibraryFaceFailureKind.Font;
                    error = "The glyph rasterizer returned invalid engine geometry.";
                    return false;
                }
                bool any = false;
                for (int i = 0; i < tile.Length; i++)
                {
                    if (tile[i] != 0)
                    {
                        any = true;
                        break;
                    }
                }
                if (!any)
                {
                    failureKind = FontLibraryFaceFailureKind.MissingGlyph;
                    error = "The glyph rasterized to an all-background tile.";
                    return false;
                }
                engineTile = tile;
                glyphWidth = width;
                return true;
            }
            catch (Exception ex)
            {
                failureKind = FontLibraryFaceFailureKind.Font;
                error = "Glyph rasterization failed: " + ex.Message;
                return false;
            }
        }

        public void Dispose()
        {
            SKTypeface typeface = _typeface;
            _typeface = null;
            MemoryStream fontStream = _fontStream;
            _fontStream = null;
            try
            {
                typeface?.Dispose();
            }
            finally
            {
                fontStream?.Dispose();
            }
        }

        static bool IsUnicodeScalar(int value)
            => value >= 0
                && value <= 0x10FFFF
                && (value < 0xD800 || value > 0xDFFF);
    }
}
