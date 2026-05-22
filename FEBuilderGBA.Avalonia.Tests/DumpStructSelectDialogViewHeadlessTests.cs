// SPDX-License-Identifier: GPL-3.0-or-later
// Headless Avalonia tests for DumpStructSelectDialogView (#439).
//
// Verifies structural mechanics of the upgraded dialog: every action button
// has an AutomationId, the Address label exists, and Init(addr) updates the
// label text.
using System.Collections.Generic;
using System.Linq;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    public class DumpStructSelectDialogViewHeadlessTests
    {
        static List<(Control Control, string AutomationId)> CollectAutomationIds(Control root)
        {
            var result = new List<(Control, string)>();
            foreach (var child in root.GetLogicalDescendants().OfType<Control>())
            {
                string id = AutomationProperties.GetAutomationId(child);
                if (!string.IsNullOrEmpty(id))
                    result.Add((child, id));
            }
            return result;
        }

        [AvaloniaFact]
        public void View_CanInstantiate()
        {
            var view = new DumpStructSelectDialogView();
            Assert.NotNull(view);
        }

        [AvaloniaFact]
        public void View_Has_AddrLabel()
        {
            var view = new DumpStructSelectDialogView();
            var label = view.FindControl<TextBlock>("AddrLabel");
            Assert.NotNull(label);
        }

        [AvaloniaFact]
        public void View_HasAllElevenActionButtons()
        {
            var view = new DumpStructSelectDialogView();
            var ids = CollectAutomationIds(view).Select(p => p.AutomationId).ToList();
            // The 11 action buttons mirror the WinForms Designer. AutomationIds
            // end with `_Button` per the scripts/validate-automation-ids.ps1
            // naming convention.
            string[] expectedButtonIds =
            {
                "DumpStructSelectDialog_Binary_Button",
                "DumpStructSelectDialog_CopyPointer_Button",
                "DumpStructSelectDialog_CopyClipboard_Button",
                "DumpStructSelectDialog_CopyLittleEndian_Button",
                "DumpStructSelectDialog_CopyNoDollGBARadBreakPoint_Button",
                "DumpStructSelectDialog_EAALL_Button",
                "DumpStructSelectDialog_CSV_Button",
                "DumpStructSelectDialog_TSVALL_Button",
                "DumpStructSelectDialog_STRUCT_Button",
                "DumpStructSelectDialog_NMM_Button",
                "DumpStructSelectDialog_Import_Button",
            };
            foreach (string expected in expectedButtonIds)
            {
                Assert.Contains(expected, ids);
            }
        }

        [AvaloniaFact]
        public void View_HasFiveGroupLabels()
        {
            // Group section headers mirror WinForms label1/2/3/4/6.
            var view = new DumpStructSelectDialogView();
            var ids = CollectAutomationIds(view).Select(p => p.AutomationId).ToList();
            // AutomationIds end with `_Label` per the
            // scripts/validate-automation-ids.ps1 naming convention.
            string[] expectedLabelIds =
            {
                "DumpStructSelectDialog_HexEditorGroup_Label",
                "DumpStructSelectDialog_ClipboardGroup_Label",
                "DumpStructSelectDialog_DataExportGroup_Label",
                "DumpStructSelectDialog_DataStructureGroup_Label",
                "DumpStructSelectDialog_ImportGroup_Label",
            };
            foreach (string expected in expectedLabelIds)
            {
                Assert.Contains(expected, ids);
            }
        }

        [AvaloniaFact]
        public void View_HasCancelButton()
        {
            // Cancel button mirrors WinForms U.AddCancelButton.
            var view = new DumpStructSelectDialogView();
            var ids = CollectAutomationIds(view).Select(p => p.AutomationId).ToList();
            Assert.Contains("DumpStructSelectDialog_Cancel_Button", ids);
        }

        [AvaloniaFact]
        public void Init_SetsAddressLabel()
        {
            var view = new DumpStructSelectDialogView();
            view.Init(0x1234);
            var label = view.FindControl<TextBlock>("AddrLabel");
            Assert.NotNull(label);
            Assert.Contains("00001234", label!.Text ?? "");
        }

        [AvaloniaFact]
        public void View_NamedControlCount_AtLeastEighteen()
        {
            // Density parity: WinForms Designer has 18 named controls + 1
            // runtime Cancel = 19 total. AV should match within ≤25% delta.
            // 18 controls: 5 group labels + 11 buttons + Cancel + Address label = 18.
            var view = new DumpStructSelectDialogView();
            var ids = CollectAutomationIds(view).Select(p => p.AutomationId).ToList();
            // De-dup and assert the named-control count.
            int distinct = ids.Distinct().Count();
            Assert.InRange(distinct, 18, int.MaxValue);
        }
    }
}
