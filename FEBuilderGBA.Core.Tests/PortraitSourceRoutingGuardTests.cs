// SPDX-License-Identifier: GPL-3.0-or-later
//
// #2032 source guards (Revision 9/10): method/descriptor-SCOPED source-text checks
// that the portrait-only call sites route through the new portrait-safe Core helpers
// and never through the generic FindAndWriteData-backed writers, WITHOUT asserting
// anything about the rest of each file (other, legitimately-non-portrait descriptors
// in ImageImportValidator.cs — chapter title, battle BG/terrain, CG — keep calling the
// generic writers and must NOT be touched by this fix or flagged by these guards).
//
// These are plain source-text scans (balanced-brace block extraction), modeled on the
// existing FEBuilderGBA.Tests/Unit/BrowserE2EHookSourceGuardTests.cs pattern, so they
// do not depend on compiling/loading the CLI or Avalonia assemblies from this project.
using System;
using System.IO;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    public class PortraitSourceRoutingGuardTests
    {
        const string GenericCompressed = "ImageImportCore.WriteCompressedToROM(";
        const string GenericRaw = "ImageImportCore.WriteRawToROM(";
        const string GenericPalette = "ImageImportCore.WritePaletteToROM(";
        const string GenericFindAndWrite = "ImageImportCore.FindAndWriteData(";
        const string GenericFindFreeSpace = ".FindFreeSpace(";
        const string GenericRelocate = "ImageImportCore.WriteCompressedInPlaceOrRelocate(";

        const string PortraitCompressed = "ImageImportCore.WriteCompressedPortraitToROM(";
        const string PortraitRaw = "ImageImportCore.WriteRawPortraitAppendAndRepoint(";
        const string PortraitPalette = "ImageImportCore.WritePortraitPaletteToROM(";
        const string PortraitHalfbodyPalette = "ImageImportCore.WriteHalfbodyPortraitPaletteToROM(";
        const string PortraitEntryPalette = "ImageImportCore.WritePortraitEntryPaletteToROM(";
        const string PortraitTargetHeader = "ImageImportCore.TryGetPortraitTargetHeader(";

        [Fact]
        public void Cli_ImportPortraitFromFile_UsesPortraitSafeHelpers_AndRealUndoTransaction()
        {
            string source = File.ReadAllText(Path.Combine(RepoRoot(), "FEBuilderGBA.CLI", "Program.cs"));
            string body = ExtractMethodBody(source, "static string ImportPortraitFromFile(ROM rom, string pngPath, uint portraitAddr)");

            AssertNoGenericWriters(body, "CLI ImportPortraitFromFile");
            Assert.Contains(PortraitCompressed, body, StringComparison.Ordinal);
            Assert.Contains(PortraitRaw, body, StringComparison.Ordinal);
            Assert.Contains(PortraitPalette, body, StringComparison.Ordinal);
            Assert.Contains(PortraitHalfbodyPalette, body, StringComparison.Ordinal);
            Assert.Contains(PortraitTargetHeader, body, StringComparison.Ordinal);
            Assert.True(
                body.IndexOf("preserveHalfbodyPalette", StringComparison.Ordinal)
                < body.IndexOf(PortraitCompressed, StringComparison.Ordinal),
                "CLI must capture halfbody palette mode before rewriting D0.");

            // Revision 9/10 CLI transaction contract.
            Assert.Contains("ReferenceEquals(rom, CoreState.ROM)", body, StringComparison.Ordinal);
            Assert.Contains("NewUndoData(", body, StringComparison.Ordinal);
            Assert.Contains("ROM.BeginUndoScope(", body, StringComparison.Ordinal);
            Assert.Contains(".Push(ud)", body, StringComparison.Ordinal);
            Assert.Contains(".RunUndo()", body, StringComparison.Ordinal);
            Assert.Contains("RestoreExactRomLengthAfterUndo(", body, StringComparison.Ordinal);
        }

        [Fact]
        public void Avalonia_ImageImportValidator_ImportPortraitMethod_UsesPortraitSafeHelpers()
        {
            string source = File.ReadAllText(Path.Combine(
                RepoRoot(), "FEBuilderGBA.Avalonia", "Services", "ImageImportValidator.cs"));
            string body = ExtractMethodBody(source, "static string ImportPortrait(string file, byte[] pal, uint addr)");

            AssertNoGenericWriters(body, "ImageImportValidator.ImportPortrait");
            Assert.Contains(PortraitCompressed, body, StringComparison.Ordinal);
            Assert.Contains(PortraitPalette, body, StringComparison.Ordinal);
            Assert.Contains(PortraitHalfbodyPalette, body, StringComparison.Ordinal);
            Assert.Contains("ImageImportCore.IsHalfbodyPortraitEntry(", body, StringComparison.Ordinal);
            Assert.True(
                body.IndexOf("ImageImportCore.IsHalfbodyPortraitEntry(", StringComparison.Ordinal)
                < body.IndexOf(PortraitCompressed, StringComparison.Ordinal),
                "Validator must capture halfbody palette mode before rewriting D0.");
        }

        [Fact]
        public void Avalonia_ImageImportValidator_PortraitViewerPaletteDescriptor_UsesPortraitSafeHelper()
        {
            string source = File.ReadAllText(Path.Combine(
                RepoRoot(), "FEBuilderGBA.Avalonia", "Services", "ImageImportValidator.cs"));

            // Scope strictly to the "PortraitViewer_Palette" descriptor's ImportPalette
            // lambda body — NOT the whole file — so other descriptors (chapter title,
            // battle BG/terrain, CG) that legitimately still call the generic writers
            // for their own non-portrait purposes are never touched by this guard.
            int descriptorIdx = source.IndexOf("Name = \"PortraitViewer_Palette\"", StringComparison.Ordinal);
            Assert.True(descriptorIdx >= 0, "PortraitViewer_Palette descriptor not found");
            int importPaletteIdx = source.IndexOf("ImportPalette = pal =>", descriptorIdx, StringComparison.Ordinal);
            Assert.True(importPaletteIdx >= 0, "PortraitViewer_Palette.ImportPalette lambda not found");

            string body = ExtractBraceBlockAfter(source, importPaletteIdx);

            AssertNoGenericWriters(body, "PortraitViewer_Palette.ImportPalette");
            Assert.Contains(PortraitEntryPalette, body, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("PortraitImportHelper.cs")]
        [InlineData("ImagePortraitView.axaml.cs")]
        [InlineData("ImagePortraitFE6View.axaml.cs")]
        [InlineData("PortraitViewerView.axaml.cs")]
        public void Avalonia_DirectPortraitCallSites_NeverUseGenericWriters(string fileName)
        {
            // These files are ENTIRELY portrait-scoped (no non-portrait writer usage to
            // preserve), so a whole-file scan is safe and matches the plan's "including
            // the direct portrait paths" requirement.
            string dir = fileName.EndsWith(".axaml.cs", StringComparison.Ordinal) ? "Views" : "Services";
            string path = Path.Combine(RepoRoot(), "FEBuilderGBA.Avalonia", dir, fileName);
            string source = File.ReadAllText(path);

            AssertNoGenericWriters(source, fileName);

            bool usesAnyPortraitHelper =
                source.Contains(PortraitCompressed, StringComparison.Ordinal) ||
                source.Contains(PortraitRaw, StringComparison.Ordinal) ||
                source.Contains(PortraitPalette, StringComparison.Ordinal) ||
                source.Contains(PortraitHalfbodyPalette, StringComparison.Ordinal) ||
                source.Contains(PortraitEntryPalette, StringComparison.Ordinal);
            Assert.True(usesAnyPortraitHelper, $"{fileName}: expected at least one portrait-safe helper call");
        }

        [Fact]
        public void Avalonia_PortraitImportHelper_UsesBoundedHeaderProbeForBothRawFormatDecisions()
        {
            string source = File.ReadAllText(Path.Combine(
                RepoRoot(), "FEBuilderGBA.Avalonia", "Services", "PortraitImportHelper.cs"));
            int searchFrom = 0;
            int count = 0;
            while (true)
            {
                int probe = source.IndexOf(
                    PortraitTargetHeader, searchFrom, StringComparison.Ordinal);
                if (probe < 0) break;
                count++;
                searchFrom = probe + PortraitTargetHeader.Length;
            }
            Assert.Equal(2, count);
            Assert.DoesNotContain("LZ77.iscompress(", source, StringComparison.Ordinal);
        }

        [Fact]
        public void Avalonia_PortraitViewer_CapturesHalfbodyPaletteModeBeforeRewritingD0()
        {
            string source = File.ReadAllText(Path.Combine(
                RepoRoot(), "FEBuilderGBA.Avalonia", "Views", "PortraitViewerView.axaml.cs"));
            string body = ExtractMethodBody(
                source, "void ImportImageFromFile(string filePath)");

            AssertNoGenericWriters(body, "PortraitViewerView.ImportImageFromFile");
            Assert.Contains(PortraitCompressed, body, StringComparison.Ordinal);
            Assert.Contains(PortraitPalette, body, StringComparison.Ordinal);
            Assert.Contains(PortraitHalfbodyPalette, body, StringComparison.Ordinal);
            Assert.True(
                body.IndexOf("ImageImportCore.IsHalfbodyPortraitEntry(", StringComparison.Ordinal)
                < body.IndexOf(PortraitCompressed, StringComparison.Ordinal),
                "PortraitViewer must capture halfbody palette mode before rewriting D0.");
        }

        static void AssertNoGenericWriters(string body, string scopeName)
        {
            Assert.DoesNotContain(GenericCompressed, body, StringComparison.Ordinal);
            Assert.DoesNotContain(GenericRaw, body, StringComparison.Ordinal);
            Assert.DoesNotContain(GenericPalette, body, StringComparison.Ordinal);
            Assert.DoesNotContain(GenericFindAndWrite, body, StringComparison.Ordinal);
            Assert.DoesNotContain(GenericFindFreeSpace, body, StringComparison.Ordinal);
            Assert.DoesNotContain(GenericRelocate, body, StringComparison.Ordinal);
        }

        /// <summary>
        /// Finds <paramref name="signatureAnchor"/> in <paramref name="source"/> and
        /// returns the balanced-brace block starting at the first '{' at/after it.
        /// </summary>
        static string ExtractMethodBody(string source, string signatureAnchor)
        {
            int idx = source.IndexOf(signatureAnchor, StringComparison.Ordinal);
            Assert.True(idx >= 0, $"Anchor not found: {signatureAnchor}");
            return ExtractBraceBlockAfter(source, idx);
        }

        static string ExtractBraceBlockAfter(string source, int fromIndex)
        {
            int braceStart = source.IndexOf('{', fromIndex);
            Assert.True(braceStart >= 0, "Opening brace not found after anchor");
            int depth = 0;
            int i = braceStart;
            for (; i < source.Length; i++)
            {
                if (source[i] == '{') depth++;
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0) { i++; break; }
                }
            }
            Assert.True(depth == 0, "Unbalanced braces while extracting block");
            return source.Substring(braceStart, i - braceStart);
        }

        static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "FEBuilderGBA.CLI"))
                    && Directory.Exists(Path.Combine(dir.FullName, "FEBuilderGBA.Avalonia"))
                    && Directory.Exists(Path.Combine(dir.FullName, "FEBuilderGBA.Core.Tests")))
                    return dir.FullName;
                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate repository root.");
        }
    }
}
