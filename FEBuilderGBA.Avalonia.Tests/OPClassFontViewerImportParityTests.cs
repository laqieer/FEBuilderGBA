// SPDX-License-Identifier: GPL-3.0-or-later
// #999 parity test for the OP Class Font Editor's new Import PNG path.
//
// The View's ImportPng_Click opens an OS file dialog we cannot drive headless,
// so this is a source-text parity test (same pattern as the other
// *ParityTests in this project): it asserts the axaml exposes the new
// Import PNG button by its AutomationId and that the code-behind wires the
// wait-icon-style shared-palette remap (LoadAndRemapToExistingPalette) into the
// Core write-back seam (OPClassFontImportCore.Import).
using System;
using System.IO;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    public class OPClassFontViewerImportParityTests
    {
        static string? FindRepoRoot()
        {
            string start = AppDomain.CurrentDomain.BaseDirectory;
            for (DirectoryInfo? dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "FEBuilderGBA.sln")))
                    return dir.FullName;
            }
            return null;
        }

        static string ReadView(string ext)
        {
            string? repoRoot = FindRepoRoot();
            Assert.NotNull(repoRoot);
            string path = Path.Combine(repoRoot!, "FEBuilderGBA.Avalonia", "Views",
                "OPClassFontViewerView" + ext);
            Assert.True(File.Exists(path), $"Missing {path}");
            return File.ReadAllText(path);
        }

        [Fact]
        public void Axaml_Has_ImportPng_Button()
        {
            string axaml = ReadView(".axaml");
            Assert.Contains("OPClassFontViewer_ImportPng_Button", axaml);
            Assert.Contains("Click=\"ImportPng_Click\"", axaml);
            Assert.Contains("Import PNG", axaml);
        }

        [Fact]
        public void CodeBehind_Wires_Core_Import_And_Remap()
        {
            string cs = ReadView(".axaml.cs");
            // The handler exists and calls the Core write-back seam.
            Assert.Contains("ImportPng_Click", cs);
            Assert.Contains("OPClassFontImportCore.Import", cs);
            // Shared-palette remap (wait-icon pattern), NOT quantize-to-fresh.
            Assert.Contains("LoadAndRemapToExistingPalette", cs);
            // Reads the shared op_class_font_palette via the rom-aware overload.
            Assert.Contains("op_class_font_palette_pointer", cs);
            // Runs under the view's undo scope (Begin/Commit/Rollback).
            Assert.Contains("_undoService.Begin", cs);
            Assert.Contains("_undoService.Commit", cs);
            Assert.Contains("_undoService.Rollback", cs);
        }
    }
}
