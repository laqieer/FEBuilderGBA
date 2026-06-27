// SPDX-License-Identifier: GPL-3.0-or-later
// Headless structural tests for ExtraUnitView (FE8J Extra Unit editor, issue #1599).
//
// Asserts the new editable Flag ID TextBox exists and that the P0 (Unit Data
// Pointer) box is read-only display only.
using System.Linq;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    public class ExtraUnitViewHeadlessTests
    {
        static T? FindById<T>(Control root, string id) where T : Control =>
            root.GetLogicalDescendants().OfType<T>()
                .FirstOrDefault(c => AutomationProperties.GetAutomationId(c) == id);

        [AvaloniaFact]
        public void View_CanInstantiate()
        {
            var view = new ExtraUnitView();
            Assert.NotNull(view);
        }

        [AvaloniaFact]
        public void View_HasEditableFlagBox()
        {
            var view = new ExtraUnitView();

            // By name (regenerated from the axaml Name="FlagIdBox").
            var byName = view.FindControl<TextBox>("FlagIdBox");
            Assert.NotNull(byName);
            Assert.False(byName!.IsReadOnly);

            // And by AutomationId.
            var byId = FindById<TextBox>(view, "ExtraUnit_FlagId_Input");
            Assert.NotNull(byId);
        }

        [AvaloniaFact]
        public void View_P0Box_IsReadOnly()
        {
            var view = new ExtraUnitView();
            var p0 = view.FindControl<TextBox>("P0Box");
            Assert.NotNull(p0);
            // P0 is the read-only unit-data pointer display (WinForms parity).
            Assert.True(p0!.IsReadOnly);
        }
    }
}
