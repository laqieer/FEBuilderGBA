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
            // Source-text check: locate MapEditorView.axaml.cs relative to repo root.
            // Test binary lives under FEBuilderGBA.Avalonia.Tests/bin/<config>/net9.0/.
            string baseDir = AppContext.BaseDirectory;
            DirectoryInfo? probe = new DirectoryInfo(baseDir);
            string? viewPath = null;
            for (int i = 0; i < 8 && probe != null; i++)
            {
                string candidate = Path.Combine(probe.FullName, "FEBuilderGBA.Avalonia", "Views", "MapEditorView.axaml.cs");
                if (File.Exists(candidate))
                {
                    viewPath = candidate;
                    break;
                }
                probe = probe.Parent;
            }
            Assert.NotNull(viewPath);
            string src = File.ReadAllText(viewPath!);
            Assert.Contains("ExportCsv_Click", src);
            Assert.Contains("MapExportCsv.Serialize", src);
        }

        [Fact]
        public void Axaml_ContainsExportCsvButton()
        {
            string baseDir = AppContext.BaseDirectory;
            DirectoryInfo? probe = new DirectoryInfo(baseDir);
            string? axamlPath = null;
            for (int i = 0; i < 8 && probe != null; i++)
            {
                string candidate = Path.Combine(probe.FullName, "FEBuilderGBA.Avalonia", "Views", "MapEditorView.axaml");
                if (File.Exists(candidate))
                {
                    axamlPath = candidate;
                    break;
                }
                probe = probe.Parent;
            }
            Assert.NotNull(axamlPath);
            string src = File.ReadAllText(axamlPath!);
            Assert.Contains("MapEditor_ExportCsv_Button", src);
            Assert.Contains("Export Map (CSV)", src);
        }
    }
}
