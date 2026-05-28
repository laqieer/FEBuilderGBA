// SPDX-License-Identifier: GPL-3.0-or-later
// Parity test for MapEditorView's "Export Map (CSV)" button (#658 slice B).
//
// Asserts the button exists with the expected AutomationId so screen readers
// and UI-automation tooling can discover it, and verifies the click handler is
// present in the code-behind source (source-text check — pure-managed assert
// that does not require the headless render thread).
using System;
using System.IO;
using System.Linq;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    public class MapEditorExportCsvParityTests
    {
        static T? FindByAutomationId<T>(Control root, string automationId) where T : Control
        {
            foreach (var descendant in root.GetLogicalDescendants())
            {
                if (descendant is T candidate)
                {
                    var aid = AutomationProperties.GetAutomationId(candidate);
                    if (aid == automationId) return candidate;
                }
            }
            return null;
        }

        [AvaloniaFact]
        public void View_Hosts_ExportCsvButton()
        {
            var view = new MapEditorView();
            var btn = FindByAutomationId<Button>(view, "MapEditor_ExportCsv_Button");
            Assert.NotNull(btn);
            Assert.Equal("Export Map (CSV)", btn!.Content);
        }

        [Fact]
        public void CodeBehind_ContainsExportCsvClickHandler()
        {
            // Source-text check: locate MapEditorView.axaml.cs via the repo-root walk
            // shared with other parity tests in this project (e.g.,
            // DumpStructSelectDialogParityTests.FindRepoRoot).
            string? repoRoot = FindRepoRoot();
            if (repoRoot == null) return; // gracefully skip when sln is not reachable
            string viewPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views", "MapEditorView.axaml.cs");
            Assert.True(File.Exists(viewPath), $"MapEditorView.axaml.cs missing at {viewPath}");
            string src = File.ReadAllText(viewPath);
            Assert.Contains("ExportCsv_Click", src);
            Assert.Contains("MapExportCsv.Serialize", src);
        }

        [Fact]
        public void Axaml_ContainsExportCsvButton()
        {
            string? repoRoot = FindRepoRoot();
            if (repoRoot == null) return; // gracefully skip when sln is not reachable
            string axamlPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views", "MapEditorView.axaml");
            Assert.True(File.Exists(axamlPath), $"MapEditorView.axaml missing at {axamlPath}");
            string src = File.ReadAllText(axamlPath);
            Assert.Contains("MapEditor_ExportCsv_Button", src);
            Assert.Contains("Export Map (CSV)", src);
        }

        /// <summary>
        /// Walk upward from the test assembly's output directory looking for
        /// <c>FEBuilderGBA.sln</c>. Mirrors the shared idiom used by
        /// <c>DumpStructSelectDialogParityTests.FindRepoRoot</c> and
        /// <c>L10nCoverageTest.FindRepoRoot</c> so layout shifts in test
        /// output don't break this fixture.
        /// </summary>
        static string? FindRepoRoot()
        {
            string start = AppContext.BaseDirectory;
            for (DirectoryInfo? dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "FEBuilderGBA.sln")))
                    return dir.FullName;
            }
            return null;
        }
    }
}
