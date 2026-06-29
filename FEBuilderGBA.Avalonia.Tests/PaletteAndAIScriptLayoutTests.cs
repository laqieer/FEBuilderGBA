using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// Pure-XML layout structure tests guarding two cosmetic regressions:
    ///
    /// #1689 — AIScriptView's clickable "Detail Address" link
    /// (<c>AutomationId="AIScript_DetailAddress_Label"</c>) had a fixed
    /// <c>Width="140"</c> that clipped the formatted <c>0x00000000</c> address
    /// under wider macOS fonts. The fix swaps <c>Width</c> → <c>MinWidth</c> so
    /// the link auto-grows and never clips.
    ///
    /// #1690 — ImagePalletView's 16 index labels
    /// (<c>AutomationId="ImagePallet_IndexN_Label"</c>, N = 1..16) had a fixed
    /// <c>Width="20"</c> that cropped the 2-digit indices 10..16 under macOS
    /// fonts, made worse by the adjacent black swatch <c>Image</c>. The fix
    /// widens each to <c>Width="30"</c> (matching the sibling
    /// ImageUnitPaletteView index column).
    ///
    /// No ROM, no Avalonia runtime — fast and deterministic.
    /// </summary>
    public class PaletteAndAIScriptLayoutTests
    {
        private static string FindProjectRoot()
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 12; i++)
            {
                if (File.Exists(Path.Combine(dir, "FEBuilderGBA.sln"))) return dir;
                string? parent = Path.GetDirectoryName(dir);
                if (parent == null || parent == dir) break;
                dir = parent;
            }
            string cwd = Directory.GetCurrentDirectory();
            for (int i = 0; i < 12; i++)
            {
                if (File.Exists(Path.Combine(cwd, "FEBuilderGBA.sln"))) return cwd;
                string? parent = Path.GetDirectoryName(cwd);
                if (parent == null || parent == cwd) break;
                cwd = parent;
            }
            throw new InvalidOperationException("Could not find project root (FEBuilderGBA.sln)");
        }

        private static string ViewsDir()
            => Path.Combine(FindProjectRoot(), "FEBuilderGBA.Avalonia", "Views");

        // The AutomationId attribute appears in XML as the attached-property
        // attribute "AutomationProperties.AutomationId". In .NET LINQ-to-XML this
        // dotted, unprefixed name is a single LocalName (NOT namespace-split), so
        // we match the FULL "AutomationProperties.AutomationId" string — mirroring
        // the existing GapSweep/ToolTranslateROMParityTests helper.
        private static string? GetAutomationId(XElement e)
            => e.Attributes()
                .FirstOrDefault(a => a.Name.LocalName == "AutomationProperties.AutomationId")?.Value;

        private static string? GetAttr(XElement e, string localName)
            => e.Attributes().FirstOrDefault(a => a.Name.LocalName == localName)?.Value;

        [Fact]
        public void AIScript_DetailAddressLabel_UsesMinWidthNotFixedWidth()
        {
            string path = Path.Combine(ViewsDir(), "AIScriptView.axaml");
            Assert.True(File.Exists(path), $"AIScriptView.axaml not found: {path}");

            XDocument doc = XDocument.Load(path);

            XElement? label = doc.Descendants()
                .FirstOrDefault(e => GetAutomationId(e) == "AIScript_DetailAddress_Label");
            Assert.True(label != null,
                "Element with AutomationId 'AIScript_DetailAddress_Label' not found in AIScriptView.axaml.");

            // #1689: the clickable address link must NOT have a fixed Width
            // (that clips the formatted address on macOS) ...
            string? width = GetAttr(label!, "Width");
            Assert.True(string.IsNullOrEmpty(width),
                $"AIScript_DetailAddress_Label must NOT declare a fixed Width (found Width=\"{width}\"). " +
                "Use MinWidth so the link auto-grows and never clips (#1689).");

            // ... and MUST have a MinWidth >= 140 so it stays at least as wide as before.
            string? minWidth = GetAttr(label!, "MinWidth");
            Assert.False(string.IsNullOrEmpty(minWidth),
                "AIScript_DetailAddress_Label must declare a MinWidth (>= 140) so it auto-grows (#1689).");
            Assert.True(double.TryParse(minWidth, out double mw),
                $"AIScript_DetailAddress_Label MinWidth must be numeric (found \"{minWidth}\").");
            Assert.True(mw >= 140,
                $"AIScript_DetailAddress_Label MinWidth must be >= 140 (found {mw}).");
        }

        [Fact]
        public void ImagePallet_IndexLabels_AreWideEnoughForTwoDigits()
        {
            string path = Path.Combine(ViewsDir(), "ImagePalletView.axaml");
            Assert.True(File.Exists(path), $"ImagePalletView.axaml not found: {path}");

            XDocument doc = XDocument.Load(path);

            var indexLabelRe = new Regex(@"^ImagePallet_Index(\d+)_Label$");
            var indexLabels = doc.Descendants()
                .Where(e =>
                {
                    string? id = GetAutomationId(e);
                    return id != null && indexLabelRe.IsMatch(id);
                })
                .ToList();

            // #1690: all 16 palette index labels must be present.
            Assert.Equal(16, indexLabels.Count);

            var offenders = new List<string>();
            foreach (var label in indexLabels)
            {
                string id = GetAutomationId(label)!;
                string? width = GetAttr(label, "Width");
                string? minWidth = GetAttr(label, "MinWidth");

                // The old clipping value Width="20" must be gone.
                if (width == "20")
                {
                    offenders.Add($"{id}: still has the clipping Width=\"20\".");
                    continue;
                }

                // Robust per Copilot review: accept either a Width >= 30 OR a MinWidth >= 30.
                bool wideEnough = false;
                if (!string.IsNullOrEmpty(width) && double.TryParse(width, out double w) && w >= 30)
                    wideEnough = true;
                if (!string.IsNullOrEmpty(minWidth) && double.TryParse(minWidth, out double mw) && mw >= 30)
                    wideEnough = true;

                if (!wideEnough)
                    offenders.Add($"{id}: Width=\"{width ?? "(none)"}\" MinWidth=\"{minWidth ?? "(none)"}\" " +
                                  "— needs Width >= 30 OR MinWidth >= 30 so 2-digit indices (10..16) are not cropped.");
            }

            Assert.True(offenders.Count == 0,
                "Palette index labels too narrow (2-digit indices 10..16 will be cropped by the adjacent swatch) " +
                "— give each Width >= 30 or MinWidth >= 30 (#1690):\n" + string.Join("\n", offenders));
        }
    }
}
