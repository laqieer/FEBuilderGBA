// SPDX-License-Identifier: GPL-3.0-or-later
//
// Cross-platform glyph-rasterizer seam (#796).
//
// This interface is the System.Drawing-free seam that lets the Core
// translation-font auto-generator (ToolTranslateROMCore.ImportFonts) produce
// brand-new GBA font tiles without depending on WinForms. The WinForms build
// has always done this with System.Drawing.Bitmap + GDI DrawString
// (ImageUtil.AutoGenerateFont); the SkiaSharp implementation
// (FEBuilderGBA.SkiaSharp.SkiaFontRasterizer) reproduces that algorithm
// byte-for-byte on every platform.
namespace FEBuilderGBA
{
    /// <summary>
    /// Rasterizes a single character into a GBA font tile, replacing the
    /// WinForms-only <c>ImageUtil.AutoGenerateFont</c> + <c>Image4ToByte</c>
    /// pipeline with a platform-neutral seam.
    /// </summary>
    public interface IFontRasterizer
    {
        /// <summary>
        /// Rasterize <paramref name="character"/> into one GBA font tile.
        /// </summary>
        /// <param name="font">Typeface / size selector (see <see cref="FontSpec"/>).</param>
        /// <param name="character">The single character to render (a 1-length
        /// string; surrogate pairs are passed whole).</param>
        /// <param name="isItemFont">When <c>true</c> renders the item-font
        /// variant (glyph + outline ring); when <c>false</c> renders the plain
        /// text/serif variant.</param>
        /// <param name="verticalOffset">Extra vertical pixel shift applied to
        /// the composited glyph, matching the WF
        /// <c>AutoGenerateFont(..., verticalOffset, ...)</c> argument.</param>
        /// <param name="glyphWidth">Receives the advance width in pixels
        /// (1..16), measured as the rightmost glyph column + 1, matching the WF
        /// <c>out_width</c> result.</param>
        /// <returns>
        /// Exactly <b>64 bytes</b> describing a 16x16 GBA font tile. The tile
        /// holds <b>256 pixels at 2 bits per pixel</b> (a 4-colour sub-palette),
        /// packed <b>4 pixels per byte</b> (256 px / 4 = 64 bytes); the low two
        /// bits are the left-most pixel of each group. Stored values are
        /// <b>palette indices</b>, not colour channels:
        /// <list type="bullet">
        ///   <item><description>text font (<paramref name="isItemFont"/> =
        ///   false): 0 = background, 3 = foreground.</description></item>
        ///   <item><description>item font (<paramref name="isItemFont"/> =
        ///   true): 0 = background, 2 = glyph fill, 3 = outline.</description></item>
        /// </list>
        /// Implementations never return null — an unresolvable family falls back
        /// to the platform default typeface, and an unrenderable character
        /// yields an all-background (all-zero) tile with
        /// <paramref name="glyphWidth"/> = 1.
        /// </returns>
        byte[] RasterizeGlyph(FontSpec font, string character, bool isItemFont,
            int verticalOffset, out int glyphWidth);
    }
}
