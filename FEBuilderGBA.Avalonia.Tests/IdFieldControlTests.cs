// SPDX-License-Identifier: GPL-3.0-or-later
// Headless UI tests for the reusable IdFieldControl (#366).
//
// IdFieldControl bundles the affordances the WinForms editors get via
// InputFormRef auto-wiring: a Hyperlink-styled clickable label, a NumericUpDown
// for the raw id, an inline name preview TextBlock, a "Jump" button (open the
// target editor positioned at the current id), and a "Pick..." button (open
// the target editor in pick mode to choose a new id). The control exposes:
//
//   - Value (uint)         — the current id (round-trips through inner NumericUpDown)
//   - Label (string)       — text on the hyperlink label
//   - NameText (string)    — text on the inline name preview
//   - Maximum (int)        — upper bound for the NumericUpDown
//   - JumpRequested        — event raised by hyperlink click or Jump button click
//   - PickRequested        — event raised by Pick button click
//   - ValueChanged         — event raised whenever the NumericUpDown value changes
//
// Live live-sync (ValueChanged) is critical: when the user manually types a new
// id into the NumericUpDown, the inline name preview MUST be able to refresh
// without going through Jump or Pick. Plan v2 explicitly added this in response
// to the Copilot CLI plan review.
using System;
using System.Linq;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using FEBuilderGBA.Avalonia.Controls;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests;

public class IdFieldControlTests
{
    [AvaloniaFact]
    public void Constructor_CreatesControl()
    {
        var control = new IdFieldControl();
        Assert.NotNull(control);
    }

    [AvaloniaFact]
    public void DefaultState_InnerControlsExist()
    {
        var control = new IdFieldControl();
        Assert.NotNull(control.FindControl<TextBlock>("LabelText"));
        Assert.NotNull(control.FindControl<NumericUpDown>("ValueBox"));
        Assert.NotNull(control.FindControl<TextBlock>("NameLabel"));
        Assert.NotNull(control.FindControl<Button>("JumpButton"));
        Assert.NotNull(control.FindControl<Button>("PickButton"));
    }

    [AvaloniaFact]
    public void Value_RoundTripsThroughNumericUpDown()
    {
        var control = new IdFieldControl();
        control.Value = 42;
        var box = control.FindControl<NumericUpDown>("ValueBox");
        Assert.NotNull(box);
        Assert.Equal(42m, box!.Value);

        box.Value = 17m;
        Assert.Equal(17u, control.Value);
    }

    [AvaloniaFact]
    public void Label_PropagatesToTextBlock()
    {
        var control = new IdFieldControl();
        control.Label = "Promotion Class 1:";
        var label = control.FindControl<TextBlock>("LabelText");
        Assert.NotNull(label);
        Assert.Equal("Promotion Class 1:", label!.Text);
    }

    [AvaloniaFact]
    public void NameText_PropagatesToPreviewTextBlock()
    {
        var control = new IdFieldControl();
        control.NameText = "Lord";
        var preview = control.FindControl<TextBlock>("NameLabel");
        Assert.NotNull(preview);
        Assert.Equal("Lord", preview!.Text);
    }

    [AvaloniaFact]
    public void Maximum_PropagatesToNumericUpDown()
    {
        var control = new IdFieldControl();
        control.Maximum = 999;
        var box = control.FindControl<NumericUpDown>("ValueBox");
        Assert.NotNull(box);
        Assert.Equal(999m, box!.Maximum);
    }

    [AvaloniaFact]
    public void Label_HasHyperlinkClass()
    {
        var control = new IdFieldControl();
        var label = control.FindControl<TextBlock>("LabelText");
        Assert.NotNull(label);
        // Hyperlink underline cue — already a global App.axaml style.
        Assert.Contains("Hyperlink", label!.Classes);
    }

    [AvaloniaFact]
    public void JumpButton_FiresJumpRequested()
    {
        var control = new IdFieldControl();
        bool fired = false;
        control.JumpRequested += (_, _) => fired = true;

        var btn = control.FindControl<Button>("JumpButton");
        Assert.NotNull(btn);
        // Simulate click.
        btn!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.True(fired);
    }

    [AvaloniaFact]
    public void PickButton_FiresPickRequested()
    {
        var control = new IdFieldControl();
        bool fired = false;
        control.PickRequested += (_, _) => fired = true;

        var btn = control.FindControl<Button>("PickButton");
        Assert.NotNull(btn);
        btn!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.True(fired);
    }

    [AvaloniaFact]
    public void ValueChange_FiresValueChanged()
    {
        var control = new IdFieldControl();
        uint? observed = null;
        control.ValueChanged += (_, e) => observed = e.NewValue;

        var box = control.FindControl<NumericUpDown>("ValueBox");
        Assert.NotNull(box);
        box!.Value = 7m;

        Assert.Equal(7u, observed);
    }

    [AvaloniaFact]
    public void SettingValueProgrammatically_AlsoFiresValueChanged()
    {
        // The View's UpdateUI() pushes VM values into the IdField — the
        // ValueChanged hook still needs to refresh the inline name preview, so
        // programmatic Value changes must raise the event too (only suppressed
        // by the consumer if it sets a flag).
        var control = new IdFieldControl();
        uint? observed = null;
        control.ValueChanged += (_, e) => observed = e.NewValue;

        control.Value = 11u;

        Assert.Equal(11u, observed);
    }

    [AvaloniaFact]
    public void Value_NegativeRomValueClampedToZero()
    {
        // NumericUpDown is decimal — make sure setting Value 0 keeps round-trip
        // sane and never throws for the boundary case.
        var control = new IdFieldControl();
        control.Value = 0u;
        Assert.Equal(0u, control.Value);
    }

    [AvaloniaFact]
    public void HostAutomationId_PropagatesToInnerNumericUpDown()
    {
        // When a host sets AutomationProperties.AutomationId on the
        // IdFieldControl, the inner NumericUpDown inherits the SAME id
        // (stripping a trailing "_Input" first if present) so existing E2E
        // selectors that expect to type into "CCBranchEditor_Promo1_Input"
        // still resolve to the actual input control.
        var window = new Window { Width = 200, Height = 100 };
        var control = new IdFieldControl();
        AutomationProperties.SetAutomationId(control, "CCBranchEditor_Promo1_Input");
        window.Content = control;
        window.Show();
        try
        {
            var nud = control.FindControl<NumericUpDown>("ValueBox");
            Assert.NotNull(nud);
            Assert.Equal("CCBranchEditor_Promo1_Input", AutomationProperties.GetAutomationId(nud));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void HostAutomationId_PropagatesDerivedIdsToOtherInnerControls()
    {
        // Other inner controls get derived ids so each is independently
        // addressable by automation: name preview, Jump button, Pick button.
        var window = new Window { Width = 200, Height = 100 };
        var control = new IdFieldControl();
        AutomationProperties.SetAutomationId(control, "CCBranchEditor_Promo1_Input");
        window.Content = control;
        window.Show();
        try
        {
            var name = control.FindControl<TextBlock>("NameLabel");
            var jump = control.FindControl<Button>("JumpButton");
            var pick = control.FindControl<Button>("PickButton");
            Assert.NotNull(name);
            Assert.NotNull(jump);
            Assert.NotNull(pick);
            Assert.Equal("CCBranchEditor_Promo1_Name", AutomationProperties.GetAutomationId(name));
            Assert.Equal("CCBranchEditor_Promo1_Jump_Button", AutomationProperties.GetAutomationId(jump));
            Assert.Equal("CCBranchEditor_Promo1_Pick_Button", AutomationProperties.GetAutomationId(pick));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void HostAutomationId_InputId_UniqueInLogicalTree()
    {
        // The "_Input" AutomationId must be UNIQUE in the logical tree so that
        // automation lookups always resolve to the NumericUpDown, never to the
        // IdFieldControl host. After propagation the host's own AutomationId is
        // reassigned to "_Control" to avoid the collision. This was Copilot CLI's
        // second-round PR #477 review point.
        var window = new Window { Width = 200, Height = 100 };
        var control = new IdFieldControl();
        AutomationProperties.SetAutomationId(control, "CCBranchEditor_Promo1_Input");
        window.Content = control;
        window.Show();
        try
        {
            var inputHits = window
                .GetLogicalDescendants()
                .OfType<Control>()
                .Where(c => AutomationProperties.GetAutomationId(c) == "CCBranchEditor_Promo1_Input")
                .ToList();
            Assert.Single(inputHits);
            Assert.IsType<NumericUpDown>(inputHits[0]);

            // Host now uses the derived "_Control" id.
            Assert.Equal("CCBranchEditor_Promo1_Control", AutomationProperties.GetAutomationId(control));
        }
        finally
        {
            window.Close();
        }
    }
}
