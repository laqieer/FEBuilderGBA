// SPDX-License-Identifier: GPL-3.0-or-later
// Headless tests for the Haiku Event editor detail panels (#947 bug #7).
//
// The three EventHaiku* views were stubs (only an "Address:" label, 0 input
// controls). These tests assert the panels are now populated: every view exposes
// input controls (NumericUpDown / ComboBox / IdFieldControl) and a Write button,
// the type-ID fields are IdFieldControls with the expected labels, and the FE8
// Route combo carries the 4 WinForms L_2_COMBO choices.
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests;

public class EventHaikuDetailPanelTests
{
    // ---- FE8 (EventHaikuView) -------------------------------------------------

    [AvaloniaFact]
    public void FE8_View_HasInputControls()
    {
        var view = new EventHaikuView();
        int numerics = view.GetLogicalDescendants().OfType<NumericUpDown>().Count();
        int idFields = view.GetLogicalDescendants().OfType<IdFieldControl>().Count();
        int combos = view.GetLogicalDescendants().OfType<ComboBox>().Count();
        // Stub had ZERO; the panel must now expose multiple inputs.
        Assert.True(numerics > 0, "FE8 Haiku panel has no NumericUpDown inputs");
        Assert.True(idFields >= 3, "FE8 Haiku panel missing IdFieldControls (Unit/Killer/Text)");
        Assert.True(combos >= 1, "FE8 Haiku panel missing the Route ComboBox");
    }

    [AvaloniaFact]
    public void FE8_View_HasWriteButton()
    {
        var view = new EventHaikuView();
        var write = view.FindControl<Button>("WriteButton");
        Assert.NotNull(write);
    }

    [AvaloniaFact]
    public void FE8_UnitAndTextAndKiller_AreIdFieldControls_WithLabels()
    {
        var view = new EventHaikuView();
        var unit = view.FindControl<IdFieldControl>("UnitNud");
        var killer = view.FindControl<IdFieldControl>("KillerUnitNud");
        var text = view.FindControl<IdFieldControl>("TextNud");
        Assert.NotNull(unit);
        Assert.NotNull(killer);
        Assert.NotNull(text);
        Assert.Equal("Unit:", unit!.Label);
        Assert.Equal("Killer Unit:", killer!.Label);
        Assert.Equal("Text:", text!.Label);
        // Text field must NOT show Pick (TextViewerView isn't pickable).
        Assert.False(text.ShowPick);
        Assert.True(unit.ShowPick);
    }

    [AvaloniaFact]
    public void FE8_RouteCombo_HasFourWinFormsChoices()
    {
        var view = new EventHaikuView();
        var combo = view.FindControl<ComboBox>("RouteCombo");
        Assert.NotNull(combo);
        Assert.Equal(4, combo!.Items.Count);
    }

    // ---- FE6 (EventHaikuFE6View) ----------------------------------------------

    [AvaloniaFact]
    public void FE6_View_HasInputControls()
    {
        var view = new EventHaikuFE6View();
        int numerics = view.GetLogicalDescendants().OfType<NumericUpDown>().Count();
        int idFields = view.GetLogicalDescendants().OfType<IdFieldControl>().Count();
        Assert.True(numerics > 0, "FE6 Haiku panel has no NumericUpDown inputs");
        // Unit + DeathText + FinalChapterText.
        Assert.True(idFields >= 3, "FE6 Haiku panel missing IdFieldControls");
    }

    [AvaloniaFact]
    public void FE6_TextFields_AreIdFieldControls_NoPick()
    {
        var view = new EventHaikuFE6View();
        var unit = view.FindControl<IdFieldControl>("UnitNud");
        var death = view.FindControl<IdFieldControl>("DeathTextNud");
        var final = view.FindControl<IdFieldControl>("FinalChapterTextNud");
        Assert.NotNull(unit);
        Assert.NotNull(death);
        Assert.NotNull(final);
        Assert.Equal("Death Text:", death!.Label);
        Assert.Equal("Final Chapter Text:", final!.Label);
        Assert.False(death.ShowPick);
        Assert.False(final.ShowPick);
    }

    [AvaloniaFact]
    public void FE6_View_HasWriteButton()
    {
        var view = new EventHaikuFE6View();
        Assert.NotNull(view.FindControl<Button>("WriteButton"));
    }

    // ---- FE7 (EventHaikuFE7View) ----------------------------------------------

    [AvaloniaFact]
    public void FE7_View_HasInputControls()
    {
        var view = new EventHaikuFE7View();
        int numerics = view.GetLogicalDescendants().OfType<NumericUpDown>().Count();
        int idFields = view.GetLogicalDescendants().OfType<IdFieldControl>().Count();
        Assert.True(numerics > 0, "FE7 Haiku panel has no NumericUpDown inputs");
        // Unit + Text.
        Assert.True(idFields >= 2, "FE7 Haiku panel missing IdFieldControls");
    }

    [AvaloniaFact]
    public void FE7_UnitAndText_AreIdFieldControls_WithLabels()
    {
        var view = new EventHaikuFE7View();
        var unit = view.FindControl<IdFieldControl>("UnitNud");
        var text = view.FindControl<IdFieldControl>("TextNud");
        Assert.NotNull(unit);
        Assert.NotNull(text);
        Assert.Equal("Unit:", unit!.Label);
        Assert.Equal("Text:", text!.Label);
        Assert.False(text.ShowPick);
    }

    [AvaloniaFact]
    public void FE7_View_HasWriteButton()
    {
        var view = new EventHaikuFE7View();
        Assert.NotNull(view.FindControl<Button>("WriteButton"));
    }

    [AvaloniaFact]
    public void FE7_EventPointer_IsPresent()
    {
        var view = new EventHaikuFE7View();
        Assert.NotNull(view.FindControl<NumericUpDown>("EventPointerBox"));
    }
}
