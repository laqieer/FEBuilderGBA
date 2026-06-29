// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for the app-level CJK/glyph font fallbacks registered in Program.cs (#1692).
//
// CreateFontManagerOptions() only constructs a FontManagerOptions POCO — it does
// NOT require Avalonia platform/headless init — so these tests call it directly.
using System.Linq;
using Avalonia.Media;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    public class FontFallbackTests
    {
        [Fact]
        public void CreateFontManagerOptions_HasNonEmptyFallbacks()
        {
            var options = Program.CreateFontManagerOptions();

            Assert.NotNull(options.FontFallbacks);
            Assert.True(options.FontFallbacks!.Count > 0);
        }

        [Fact]
        public void CreateFontManagerOptions_DoesNotSetDefaultFamilyName()
        {
            // DefaultFamilyName is intentionally left null so the primary look
            // (especially on Windows) is unchanged and to avoid the known
            // empty/invalid-family startup crash (Avalonia #10614 / #12140).
            var options = Program.CreateFontManagerOptions();

            Assert.Null(options.DefaultFamilyName);
        }

        [Fact]
        public void CreateFontManagerOptions_IncludesCjkFamilies()
        {
            var options = Program.CreateFontManagerOptions();

            var familyNames = options.FontFallbacks!
                .Select(f => f.FontFamily.Name)
                .ToList();

            // One representative per platform: macOS, Windows, Linux.
            Assert.Contains("PingFang SC", familyNames);
            Assert.Contains("Microsoft YaHei", familyNames);
            Assert.Contains("Noto Sans CJK SC", familyNames);
        }
    }
}
