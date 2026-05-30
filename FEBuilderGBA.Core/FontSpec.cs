// SPDX-License-Identifier: GPL-3.0-or-later
//
// Cross-platform font selection descriptor (#796).
//
// FontSpec is a platform-neutral description of the TrueType / OpenType font
// that the translation-font auto-generator should rasterize glyphs with. It
// carries everything an IFontRasterizer implementation needs to resolve a
// concrete typeface without depending on System.Drawing.Font (WinForms-only)
// or any specific rendering backend. The SkiaSharp implementation
// (FEBuilderGBA.SkiaSharp.SkiaFontRasterizer) consumes this struct on every
// platform.
#nullable enable
using System;

namespace FEBuilderGBA
{
    /// <summary>
    /// Platform-neutral font selection used by <see cref="IFontRasterizer"/>.
    ///
    /// Resolution order (mirrors WF <c>ImageUtil.LoadFontFromFile</c> +
    /// <c>SKTypeface</c> fallbacks): when <see cref="FontFileData"/> is set the
    /// rasterizer loads the embedded bytes (deterministic across OSes, ideal for
    /// tests); otherwise when <see cref="FontFilePath"/> points at an existing
    /// file it loads that; otherwise it resolves <see cref="FamilyName"/> from
    /// the system font set; and if none of those produce a face it falls back to
    /// the platform default typeface (it never throws).
    /// </summary>
    public readonly struct FontSpec
    {
        /// <summary>System font family name (e.g. "Arial", "Noto Sans"). Used
        /// only when neither <see cref="FontFileData"/> nor
        /// <see cref="FontFilePath"/> is supplied.</summary>
        public string FamilyName { get; init; }

        /// <summary>Em size in points, matching the WinForms
        /// <c>System.Drawing.Font</c> size passed to
        /// <c>ImageUtil.AutoGenerateFont</c>.</summary>
        public float Size { get; init; }

        /// <summary>Embedded .ttf/.otf bytes. Highest priority — loaded via an
        /// in-memory stream so the same glyph renders identically on
        /// Windows / Linux / macOS (used by the golden-byte tests).</summary>
        public byte[]? FontFileData { get; init; }

        /// <summary>Path to a .ttf/.otf file on disk. Second priority, used when
        /// <see cref="FontFileData"/> is null.</summary>
        public string? FontFilePath { get; init; }

        /// <summary>Render the bold weight of the resolved family.</summary>
        public bool Bold { get; init; }

        /// <summary>Render the italic slant of the resolved family.</summary>
        public bool Italic { get; init; }
    }
}
