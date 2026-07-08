// SPDX-License-Identifier: GPL-3.0-or-later
using System.IO;
using System.Reflection;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Services;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// #1911 — the Portrait Import Wizard showed no per-character thumbnails, so the
    /// constant "Quantized preview" of the loaded source image looked like it had
    /// "taken over" every character's portrait. The fix (a) gives the wizard list the
    /// same per-id thumbnails as the main Portrait editor and (b) repairs the shared
    /// <see cref="ListIconLoaders.PortraitLoader"/>, which resolved the portrait id via
    /// <c>U.atoh(items[index].name)</c> — but the portrait VMs format labels as
    /// <c>"0x{i:X2} &lt;name&gt;"</c>, and <c>U.atoh</c> truncates at the <c>'x'</c> to
    /// <c>0</c> for EVERY row, so the whole icon column collapsed to portrait 0
    /// (blank on FE8U). The loader now prefers <c>AddrResult.tag</c>, else strips the
    /// optional <c>0x</c> prefix before hex-parsing the label id — fixing the
    /// <c>"0x.."</c> callers while keeping the un-prefixed <c>"NN"</c> callers
    /// (whose <c>tag</c> is 0) working.
    /// </summary>
    public class WizardPortraitThumbnailTests
    {
        // ------------------------------------------------------------------
        // Behavioural: the id resolver PREFERS AddrResult.tag, else strips an optional
        // "0x" and hex-parses the leading label token. FAILS on the old code (plain
        // U.atoh -> 0 for "0x.." labels) AND on a tag-only fix (which regresses the
        // un-prefixed BuildList callers whose tag is 0). Pure / ROM-free.
        // ------------------------------------------------------------------
        [Fact]
        public void ResolvePortraitId_PrefersTag_ElseStrips0xAndHexParsesLabel()
        {
            // tag populated (ImagePortrait / wizard / PortraitViewer / FE6): use it
            // verbatim — even if the label would parse to something else (robust to
            // any future label-format change).
            Assert.Equal(9u, ListIconLoaders.ResolvePortraitId(new AddrResult(0x100, "0x09 Ross", 9)));
            Assert.Equal(9u, ListIconLoaders.ResolvePortraitId(new AddrResult(0x100, "0xFF garbage", 9)));
            Assert.Equal(0x1Du, ListIconLoaders.ResolvePortraitId(new AddrResult(0x100, "0x1D", 0x1D)));

            // tag == 0 -> fall back to the LABEL. Covers the "0x{i:X2}" prefixed form
            // AND the un-prefixed "{i:X2}" form (PortraitViewer / FE6 / UnitIncreaseHeight
            // via EditorFormRef.BuildList, whose id lives only in the label). A tag-only
            // resolver would regress these to portrait 0.
            Assert.Equal(9u, ListIconLoaders.ResolvePortraitId(new AddrResult(0x100, "0x09 Ross", 0)));
            Assert.Equal(0u, ListIconLoaders.ResolvePortraitId(new AddrResult(0x100, "0x00 Enemy", 0)));
            Assert.Equal(9u, ListIconLoaders.ResolvePortraitId(new AddrResult(0x100, "09 Ross", 0)));
            Assert.Equal(0x0Au, ListIconLoaders.ResolvePortraitId(new AddrResult(0x100, "0A Portrait", 0)));
            Assert.Equal(0x7Fu, ListIconLoaders.ResolvePortraitId(new AddrResult(0x100, "7F Foo", 0)));

            // Regression witnesses — why neither helper works alone on the label:
            //  * plain U.atoh truncates "0x09" at the 'x' -> 0 (the #1911 defect).
            Assert.Equal(0u, U.atoh("0x09 Ross"));
            //  * U.atoi0x DECIMAL-parses the un-prefixed "0A" label -> 0 for ids >= 0x0A.
            Assert.Equal(0u, U.atoi0x("0A Portrait"));
        }

        // ------------------------------------------------------------------
        // Source-scan guard: PortraitLoader resolves via ResolvePortraitId (which
        // prefers tag, else strips the optional "0x"), NOT by the plain label parse
        // that mis-read "0x09" as 0. Guards against reverting to U.atoh(items[index].name).
        // ------------------------------------------------------------------
        [Fact]
        public void PortraitLoader_ResolvesViaResolvePortraitId_TagThenStrip0x()
        {
            string src = ReadSource("FEBuilderGBA.Avalonia", "Services", "ListIconLoaders.cs");
            Assert.Contains("uint portraitId = ResolvePortraitId(items[index]);", src);
            Assert.DoesNotContain("uint portraitId = U.atoh(items[index].name);", src);
            Assert.Contains("if (item.tag != 0) return item.tag;", src);
            Assert.Contains("label.StartsWith(\"0x\"", src);
        }

        // ------------------------------------------------------------------
        // Source-scan guard: the wizard list uses per-character thumbnails
        // (SetItemsWithIcons + PortraitLoader), not text-only SetItems.
        // ------------------------------------------------------------------
        [Fact]
        public void Wizard_LoadList_UsesPerCharacterThumbnails()
        {
            string src = ReadSource("FEBuilderGBA.Avalonia", "Views", "ImagePortraitImporterView.axaml.cs");
            Assert.Contains("EntryList.SetItemsWithIcons(items, i => ListIconLoaders.PortraitLoader(items, i));", src);
            Assert.DoesNotContain("EntryList.SetItems(items);", src);
        }

        // ------------------------------------------------------------------
        // Guard: the generic-enemy portrait list must NOT use PortraitLoader.
        // Its rows index the generic_enemy_portrait table (4-byte entries with
        // their own image pointer + palette at +0x20), NOT the 28-byte main
        // portrait table that PortraitLoader/LoadPortraitMini read — so a
        // portrait-id icon would render an unrelated main portrait (#1911 review).
        // ------------------------------------------------------------------
        [Fact]
        public void GenericEnemyPortraitList_DoesNotUsePortraitLoader()
        {
            string src = ReadSource("FEBuilderGBA.Avalonia", "Views", "ImageGenericEnemyPortraitView.axaml.cs");
            Assert.DoesNotContain("ListIconLoaders.PortraitLoader", src);
            Assert.Contains("EntryList.SetItems(items);", src);
        }

        // ------------------------------------------------------------------
        // ReadSource: walk up from the test assembly to the repo root
        // (FEBuilderGBA.sln) and read a source file. Mirrors the helper in
        // ListIconLoadersFirstRowTests.
        // ------------------------------------------------------------------
        private static string ReadSource(params string[] pathSegments)
        {
            string? dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            for (int i = 0; i < 10 && dir != null; i++)
            {
                if (File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
                {
                    string full = Path.Combine(dir, Path.Combine(pathSegments));
                    if (File.Exists(full))
                        return File.ReadAllText(full);
                    Assert.Fail($"Source file not found: {full}");
                }
                dir = Path.GetDirectoryName(dir);
            }
            Assert.Fail("Could not locate FEBuilderGBA.sln from test assembly");
            return string.Empty;
        }
    }
}
