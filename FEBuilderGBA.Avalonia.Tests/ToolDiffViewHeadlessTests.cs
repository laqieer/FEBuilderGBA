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
    /// Headless Avalonia tests for ToolDiffView. Verifies the view contains the
    /// two diff tabs (2-ROM, 3-ROM) and exposes the expected AutomationIds.
    /// Ref #371
    /// </summary>
    public class ToolDiffViewHeadlessTests
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
        public void ToolDiffView_CanInstantiate()
        {
            var view = new ToolDiffView();
            Assert.NotNull(view);
        }

        [AvaloniaFact]
        public void ToolDiffView_HasTabControl()
        {
            var view = new ToolDiffView();
            var tabs = view.GetLogicalDescendants().OfType<TabControl>().FirstOrDefault();
            Assert.NotNull(tabs);
        }

        [AvaloniaFact]
        public void ToolDiffView_HasTwoTabs()
        {
            var view = new ToolDiffView();
            view.Show();
            view.Close();
            var items = view.GetLogicalDescendants().OfType<TabItem>().ToList();
            Assert.Equal(2, items.Count);
        }

        [AvaloniaFact]
        public void ToolDiffView_TabHeaders_Match()
        {
            var view = new ToolDiffView();
            view.Show();
            view.Close();
            var headers = view.GetLogicalDescendants().OfType<TabItem>()
                .Select(t => t.Header?.ToString()).ToList();
            Assert.Contains("2-ROM Diff", headers);
            Assert.Contains("3-ROM Diff", headers);
        }

        [AvaloniaFact]
        public void ToolDiffView_AutomationIds_AllPresent()
        {
            var view = new ToolDiffView();
            view.Show();
            view.Close();
            var ids = CollectAutomationIds(view);

            // Diff2 tab
            Assert.Contains("ToolDiff_OtherPath_Input", ids);
            Assert.Contains("ToolDiff_OtherBrowse_Button", ids);
            Assert.Contains("ToolDiff_RecoverMissMatch_Input", ids);
            Assert.Contains("ToolDiff_CollectFreeSpace_Check", ids);
            Assert.Contains("ToolDiff_MakeBinPatch_Button", ids);

            // Diff3 tab
            Assert.Contains("ToolDiff_AFilePath_Input", ids);
            Assert.Contains("ToolDiff_BFilePath_Input", ids);
            Assert.Contains("ToolDiff_ABrowse_Button", ids);
            Assert.Contains("ToolDiff_BBrowse_Button", ids);
            Assert.Contains("ToolDiff_MakeBinPatch3_Button", ids);

            // Common
            Assert.Contains("ToolDiff_Status_Label", ids);
        }
    }
}
