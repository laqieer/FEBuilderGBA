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
    /// (blank on FE8U). The loader now reads the exact id from <c>AddrResult.tag</c>.
    /// </summary>
    public class WizardPortraitThumbnailTests
    {
        // ------------------------------------------------------------------
        // Behavioural: the id resolver reads tag, NOT the "0x.."-prefixed label.
        // This FAILS on the old code (which parsed the label via U.atoh -> 0).
        // Pure / ROM-free, so it runs everywhere in CI.
        // ------------------------------------------------------------------
        [Fact]
        public void ResolvePortraitId_UsesTag_NotLabelPrefix()
        {
            // Real label format produced by the portrait VMs: "0x{i:X2} <name>",
            // with the exact id in tag (new AddrResult(addr, name, (uint)i)).
            Assert.Equal(9u, ListIconLoaders.ResolvePortraitId(new AddrResult(0x100, "0x09 Ross", 9)));
            Assert.Equal(0x1Du, ListIconLoaders.ResolvePortraitId(new AddrResult(0x100, "0x1D", 0x1D)));
            Assert.Equal(0u, ListIconLoaders.ResolvePortraitId(new AddrResult(0x100, "0x00 Enemy", 0)));
            Assert.Equal(0x7Fu, ListIconLoaders.ResolvePortraitId(new AddrResult(0x100, "0x7F Foo", 0x7F)));

            // Regression witness: the OLD label-parse path collapses every
            // "0x.."-prefixed row to 0 — this is exactly the #1911 defect, so a
            // tag-based resolver is required (a mere label parse cannot be correct).
            Assert.Equal(0u, U.atoh("0x09 Ross"));
            Assert.Equal(0u, U.atoh("0x1D"));
            Assert.Equal(0u, U.atoh("0x7F Foo"));
        }

        // ------------------------------------------------------------------
        // Source-scan guard: PortraitLoader resolves via tag (ResolvePortraitId),
        // NOT by parsing the label. Guards against reverting to U.atoh(name).
        // ------------------------------------------------------------------
        [Fact]
        public void PortraitLoader_ResolvesById_NotByLabelParse()
        {
            string src = ReadSource("FEBuilderGBA.Avalonia", "Services", "ListIconLoaders.cs");
            Assert.Contains("uint portraitId = ResolvePortraitId(items[index]);", src);
            Assert.DoesNotContain("uint portraitId = U.atoh(items[index].name);", src);
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
