// SPDX-License-Identifier: GPL-3.0-or-later
using System;

namespace FEBuilderGBA
{
    /// <summary>
    /// Typed result for one strict-face rasterization attempt.  Missing or
    /// all-background glyphs are semantic input outcomes; native/font-engine
    /// faults are environment/font failures.
    /// </summary>
    public enum FontLibraryFaceFailureKind
    {
        None,
        MissingGlyph,
        Font,
    }

    /// <summary>
    /// Strict, already-verified font bytes used to open one reusable package
    /// face.  Unlike <see cref="FontSpec"/>, this contract has no family/path or
    /// platform-default fallback.
    /// </summary>
    public readonly struct FontLibraryFaceSpec
    {
        public byte[] FontFileData { get; init; }
        public string ExpectedFamily { get; init; }
        public string ExpectedVersion { get; init; }
        public float Size { get; init; }
    }

    /// <summary>Factory for a strict disposable font-library face.</summary>
    public interface IFontLibraryFaceFactory
    {
        /// <summary>
        /// Open the supplied bytes exactly once. Invalid/null/default/fallback
        /// faces must fail rather than silently selecting a system font.
        /// </summary>
        bool TryOpenFace(
            FontLibraryFaceSpec spec,
            out IFontLibraryFace face,
            out string error);
    }

    /// <summary>
    /// Reusable strict face used for every scalar in one job. Implementations
    /// own native resources and must be disposed.
    /// </summary>
    public interface IFontLibraryFace : IDisposable
    {
        /// <summary>True only when the loaded face maps this scalar to a real glyph.</summary>
        bool HasGlyph(int unicodeScalar);

        /// <summary>
        /// Rasterize with the shared engine math. Missing and all-background
        /// glyphs return false; no fallback face is consulted.
        /// </summary>
        bool TryRasterizeGlyph(
            int unicodeScalar,
            bool isItemFont,
            int verticalOffset,
            out byte[] engineTile,
            out int glyphWidth,
            out string error);

        /// <summary>
        /// Typed companion to the compatibility overload. Implementations that
        /// can distinguish a semantic missing/blank glyph should override this
        /// member. The default preserves existing implementations and
        /// fail-closes an untyped failure as a font-engine failure.
        /// </summary>
        bool TryRasterizeGlyph(
            int unicodeScalar,
            bool isItemFont,
            int verticalOffset,
            out byte[] engineTile,
            out int glyphWidth,
            out FontLibraryFaceFailureKind failureKind,
            out string error)
        {
            bool success = TryRasterizeGlyph(
                unicodeScalar,
                isItemFont,
                verticalOffset,
                out engineTile,
                out glyphWidth,
                out error);
            failureKind = success
                ? FontLibraryFaceFailureKind.None
                : FontLibraryFaceFailureKind.Font;
            return success;
        }
    }
}
