using System.Collections.Generic;
using System.Linq;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using FEBuilderGBA.Avalonia.Views;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// Headless Avalonia tests for ToolLZ77View.
    /// Verifies the TabControl has the 4 expected tabs (Decompress, Compress,
    /// Erase, Base64) and that key AutomationIds are present so the view is
    /// driveable from automation (UIAutomation, test harness, screenshots).
    /// Ref #371
    /// </summary>
    public class ToolLZ77ViewHeadlessTests
    {
        static List<string> CollectAutomationIds(Control root)
        {
            var result = new List<string>();
            foreach (var child in root.GetLogicalDescendants().OfType<Control>())
            {
                var id = AutomationProperties.GetAutomationId(child);
                if (!string.IsNullOrEmpty(id))
                    result.Add(id);
            }
            return result;
        }

        [AvaloniaFact]
        public void ToolLZ77View_CanInstantiate()
        {
            var view = new ToolLZ77View();
            Assert.NotNull(view);
        }

        [AvaloniaFact]
        public void ToolLZ77View_TabControl_Exists()
        {
            var view = new ToolLZ77View();
            var tabs = view.GetLogicalDescendants().OfType<TabControl>().FirstOrDefault();
            Assert.NotNull(tabs);
        }

        [AvaloniaFact]
        public void ToolLZ77View_TabControl_HasSixTabs()
        {
            var view = new ToolLZ77View();
            var tabs = view.GetLogicalDescendants().OfType<TabControl>().FirstOrDefault();
            Assert.NotNull(tabs);
            // Force template realization
            view.Show();
            view.Close();
            var items = tabs.GetLogicalDescendants().OfType<TabItem>().ToList();
            Assert.Equal(6, items.Count);
        }

        [AvaloniaFact]
        public void ToolLZ77View_TabHeaders_Match()
        {
            var view = new ToolLZ77View();
            view.Show();
            view.Close();
            var tabs = view.GetLogicalDescendants().OfType<TabItem>().ToList();
            var headers = tabs.Select(t => t.Header?.ToString()).ToList();
            Assert.Contains("Decompress", headers);
            Assert.Contains("Compress", headers);
            Assert.Contains("Erase", headers);
            Assert.Contains("Base64", headers);
            Assert.Contains("Move", headers);
            Assert.Contains("Recompress", headers);
        }

        [AvaloniaFact]
        public void ToolLZ77View_AutomationIds_AllRequiredPresent()
        {
            var view = new ToolLZ77View();
            view.Show();
            view.Close();
            var ids = CollectAutomationIds(view);

            // Per-tab "fire" buttons — these are the user-facing actions and MUST exist.
            Assert.Contains("ToolLZ77_DecompressFire_Button", ids);
            Assert.Contains("ToolLZ77_CompressFire_Button", ids);
            Assert.Contains("ToolLZ77_ZeroClear_Button", ids);
            Assert.Contains("ToolLZ77_Base64TextToFile_Button", ids);
            Assert.Contains("ToolLZ77_FileToBase64Text_Button", ids);
            Assert.Contains("ToolLZ77_Move_Button", ids);
            Assert.Contains("ToolLZ77_Recompress_Button", ids);

            // Input fields
            Assert.Contains("ToolLZ77_DecompressSrc_Input", ids);
            Assert.Contains("ToolLZ77_DecompressDest_Input", ids);
            Assert.Contains("ToolLZ77_DecompressAddress_Input", ids);
            Assert.Contains("ToolLZ77_CompressSrc_Input", ids);
            Assert.Contains("ToolLZ77_CompressDest_Input", ids);
            Assert.Contains("ToolLZ77_ZeroClearFrom_Input", ids);
            Assert.Contains("ToolLZ77_ZeroClearTo_Input", ids);
            Assert.Contains("ToolLZ77_Base64Text_Input", ids);
            Assert.Contains("ToolLZ77_MoveFrom_Input", ids);
            Assert.Contains("ToolLZ77_MoveTo_Input", ids);
            Assert.Contains("ToolLZ77_MoveLength_Input", ids);

            // Tabs
            Assert.Contains("ToolLZ77_Move_Tab", ids);
            Assert.Contains("ToolLZ77_Recompress_Tab", ids);

            // Status row
            Assert.Contains("ToolLZ77_Status_Label", ids);
        }

        [AvaloniaFact]
        public void ToolLZ77View_StatusLabel_Exists()
        {
            var view = new ToolLZ77View();
            view.Show();
            view.Close();
            var statusLabel = view.GetLogicalDescendants()
                .OfType<TextBlock>()
                .FirstOrDefault(c => AutomationProperties.GetAutomationId(c) == "ToolLZ77_Status_Label");
            Assert.NotNull(statusLabel);
        }
    }
}
