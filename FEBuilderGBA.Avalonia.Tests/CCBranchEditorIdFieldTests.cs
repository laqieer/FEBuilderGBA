// SPDX-License-Identifier: GPL-3.0-or-later
// Headless integration tests for CCBranchEditorView's IdFieldControl wiring (#366).
//
// These tests verify the view-level integration of the reusable IdFieldControl:
//   - Promo1Box and Promo2Box are IdFieldControls (not raw NumericUpDowns)
//   - AutomationIds CCBranchEditor_Promo1_Input / CCBranchEditor_Promo2_Input
//     are still discoverable so existing E2E selectors keep working
//   - The inner NumericUpDown is reachable so users can keep typing values
using System.Linq;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests;

public class CCBranchEditorIdFieldTests
{
    [AvaloniaFact]
    public void View_CanInstantiate()
    {
        var view = new CCBranchEditorView();
        Assert.NotNull(view);
    }

    [AvaloniaFact]
    public void Promo1Box_IsIdFieldControl()
    {
        var view = new CCBranchEditorView();
        var ctrl = view.FindControl<IdFieldControl>("Promo1Box");
        Assert.NotNull(ctrl);
    }

    [AvaloniaFact]
    public void Promo2Box_IsIdFieldControl()
    {
        var view = new CCBranchEditorView();
        var ctrl = view.FindControl<IdFieldControl>("Promo2Box");
        Assert.NotNull(ctrl);
    }

    [AvaloniaFact]
    public void Promo1Box_HasExpectedLabel()
    {
        var view = new CCBranchEditorView();
        var ctrl = view.FindControl<IdFieldControl>("Promo1Box");
        Assert.NotNull(ctrl);
        Assert.Equal("Promotion Class 1:", ctrl!.Label);
    }

    [AvaloniaFact]
    public void Promo2Box_HasExpectedLabel()
    {
        var view = new CCBranchEditorView();
        var ctrl = view.FindControl<IdFieldControl>("Promo2Box");
        Assert.NotNull(ctrl);
        Assert.Equal("Promotion Class 2:", ctrl!.Label);
    }

    [AvaloniaFact]
    public void Promo1Box_HasExpectedMaximum()
    {
        var view = new CCBranchEditorView();
        var ctrl = view.FindControl<IdFieldControl>("Promo1Box");
        Assert.NotNull(ctrl);
        Assert.Equal(255, ctrl!.Maximum);
    }

    [AvaloniaFact]
    public void Promo1_AutomationId_StillDiscoverable()
    {
        // AutomationId set on the IdFieldControl host element. Existing E2E
        // selectors looking for CCBranchEditor_Promo1_Input must still resolve
        // a Control so the test asserts the AutomationId is present on the
        // logical tree.
        var view = new CCBranchEditorView();
        var hits = view.GetLogicalDescendants()
            .OfType<Control>()
            .Where(c => AutomationProperties.GetAutomationId(c) == "CCBranchEditor_Promo1_Input")
            .ToList();
        Assert.NotEmpty(hits);
    }

    [AvaloniaFact]
    public void Promo2_AutomationId_StillDiscoverable()
    {
        var view = new CCBranchEditorView();
        var hits = view.GetLogicalDescendants()
            .OfType<Control>()
            .Where(c => AutomationProperties.GetAutomationId(c) == "CCBranchEditor_Promo2_Input")
            .ToList();
        Assert.NotEmpty(hits);
    }

    [AvaloniaFact]
    public void Promo1Box_InnerNumericUpDown_Reachable()
    {
        // The IdFieldControl's internal NumericUpDown is named ValueBox.
        // After loading the view, the descendants of Promo1Box must include it
        // so user input still works.
        var view = new CCBranchEditorView();
        var ctrl = view.FindControl<IdFieldControl>("Promo1Box");
        Assert.NotNull(ctrl);
        var nud = ctrl!.GetLogicalDescendants().OfType<NumericUpDown>().FirstOrDefault();
        Assert.NotNull(nud);
    }

    [AvaloniaFact]
    public void Promo1Box_ValueChange_UpdatesNameText()
    {
        // ValueChanged hook in CCBranchEditorView wires NameText to NameResolver.
        // Without a ROM, NameResolver returns "???" (the safe fallback) — but it
        // still updates the NameText property when value changes, which is the
        // contract we're verifying here.
        var view = new CCBranchEditorView();
        var ctrl = view.FindControl<IdFieldControl>("Promo1Box");
        Assert.NotNull(ctrl);
        // Initial NameText comes from VM.PromoName1 — empty without ROM.
        ctrl!.Value = 5u;
        // The handler runs synchronously via OnPropertyChanged → RaiseEvent →
        // CCBranchEditorView.Promo1_ValueChanged → ctrl.NameText = ...
        // Assert the NameText is not the empty string (it's at least "???").
        Assert.False(string.IsNullOrEmpty(ctrl.NameText));
    }
}
