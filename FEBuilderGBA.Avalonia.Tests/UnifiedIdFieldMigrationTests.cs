// SPDX-License-Identifier: GPL-3.0-or-later
// Headless integration tests for the #360 v2 plan migration slice.
//
// Verifies that each of the 10 id fields in the 9 migrated editors:
//   (a) is an IdFieldControl instance (not a raw NumericUpDown);
//   (b) has the expected Label text;
//   (c) has its AutomationId resolving to exactly one NumericUpDown after Show().
//
// Three explicit per-field test cases × 10 fields = 30 named test cases.
// Plan v2.1 (issue #360) pinned this enumeration approach in response to
// Copilot CLI review point 2 so dual-field editors (SummonsDemonKing has
// both UnitIdBox and ClassIdBox) aren't undercounted.
using System.Linq;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests;

public class UnifiedIdFieldMigrationTests
{
    // ---- WU1: Item IDs (3 editors) ------------------------------------

    [AvaloniaFact]
    public void ItemRandomChest_ItemIdBox_IsIdFieldControl()
    {
        var view = new ItemRandomChestView();
        Assert.NotNull(view.FindControl<IdFieldControl>("ItemIdBox"));
    }

    [AvaloniaFact]
    public void ItemRandomChest_ItemIdBox_HasExpectedLabel()
    {
        var view = new ItemRandomChestView();
        var ctrl = view.FindControl<IdFieldControl>("ItemIdBox");
        Assert.NotNull(ctrl);
        Assert.Equal("Item:", ctrl!.Label);
    }

    [AvaloniaFact]
    public void ItemRandomChest_ItemId_AutomationIdResolvesToNumericUpDownUniquely()
    {
        var view = new ItemRandomChestView();
        view.Show();
        try { AssertAutomationIdResolvesToNumericUpDown(view, "ItemRandomChest_ItemId_Input"); }
        finally { view.Close(); }
    }

    [AvaloniaFact]
    public void ItemWeaponEffectViewer_ItemIdBox_IsIdFieldControl()
    {
        var view = new ItemWeaponEffectViewerView();
        Assert.NotNull(view.FindControl<IdFieldControl>("ItemIdBox"));
    }

    [AvaloniaFact]
    public void ItemWeaponEffectViewer_ItemIdBox_HasExpectedLabel()
    {
        var view = new ItemWeaponEffectViewerView();
        var ctrl = view.FindControl<IdFieldControl>("ItemIdBox");
        Assert.NotNull(ctrl);
        Assert.Equal("Item ID:", ctrl!.Label);
    }

    [AvaloniaFact]
    public void ItemWeaponEffectViewer_ItemId_AutomationIdResolvesToNumericUpDownUniquely()
    {
        var view = new ItemWeaponEffectViewerView();
        view.Show();
        try { AssertAutomationIdResolvesToNumericUpDown(view, "ItemWeaponEffectViewer_ItemId_Input"); }
        finally { view.Close(); }
    }

    [AvaloniaFact]
    public void MonsterItemViewer_ItemIdBox_IsIdFieldControl()
    {
        var view = new MonsterItemViewerView();
        Assert.NotNull(view.FindControl<IdFieldControl>("ItemIdBox"));
    }

    [AvaloniaFact]
    public void MonsterItemViewer_ItemIdBox_HasExpectedLabel()
    {
        var view = new MonsterItemViewerView();
        var ctrl = view.FindControl<IdFieldControl>("ItemIdBox");
        Assert.NotNull(ctrl);
        Assert.Equal("Item ID:", ctrl!.Label);
    }

    [AvaloniaFact]
    public void MonsterItemViewer_ItemId_AutomationIdResolvesToNumericUpDownUniquely()
    {
        var view = new MonsterItemViewerView();
        view.Show();
        try { AssertAutomationIdResolvesToNumericUpDown(view, "MonsterItemViewer_ItemId_Input"); }
        finally { view.Close(); }
    }

    // ---- WU2: Unit IDs (4 editors / 4 fields) -------------------------

    [AvaloniaFact]
    public void LinkArenaDenyUnitViewer_UnitIdBox_IsIdFieldControl()
    {
        var view = new LinkArenaDenyUnitViewerView();
        Assert.NotNull(view.FindControl<IdFieldControl>("UnitIdBox"));
    }

    [AvaloniaFact]
    public void LinkArenaDenyUnitViewer_UnitIdBox_HasExpectedLabel()
    {
        var view = new LinkArenaDenyUnitViewerView();
        var ctrl = view.FindControl<IdFieldControl>("UnitIdBox");
        Assert.NotNull(ctrl);
        Assert.Equal("Unit ID:", ctrl!.Label);
    }

    [AvaloniaFact]
    public void LinkArenaDenyUnitViewer_UnitId_AutomationIdResolvesToNumericUpDownUniquely()
    {
        var view = new LinkArenaDenyUnitViewerView();
        view.Show();
        try { AssertAutomationIdResolvesToNumericUpDown(view, "LinkArenaDenyUnitViewer_UnitId_Input"); }
        finally { view.Close(); }
    }

    [AvaloniaFact]
    public void SummonUnitViewer_UnitIdBox_IsIdFieldControl()
    {
        var view = new SummonUnitViewerView();
        Assert.NotNull(view.FindControl<IdFieldControl>("UnitIdBox"));
    }

    [AvaloniaFact]
    public void SummonUnitViewer_UnitIdBox_HasExpectedLabel()
    {
        var view = new SummonUnitViewerView();
        var ctrl = view.FindControl<IdFieldControl>("UnitIdBox");
        Assert.NotNull(ctrl);
        // Original Hyperlink TextBlock said "Summoner:" — preserve it per plan v2.1.
        Assert.Equal("Summoner:", ctrl!.Label);
    }

    [AvaloniaFact]
    public void SummonUnitViewer_UnitId_AutomationIdResolvesToNumericUpDownUniquely()
    {
        var view = new SummonUnitViewerView();
        view.Show();
        try { AssertAutomationIdResolvesToNumericUpDown(view, "SummonUnitViewer_UnitId_Input"); }
        finally { view.Close(); }
    }

    [AvaloniaFact]
    public void EDView_UnitIdBox_IsIdFieldControl()
    {
        var view = new EDView();
        // EDView restructured to 3 tabs per #411 - the original "UnitIdBox"
        // is now the Retreat tab's "Retreat_UnitIdBox".
        Assert.NotNull(view.FindControl<IdFieldControl>("Retreat_UnitIdBox"));
    }

    [AvaloniaFact]
    public void EDView_UnitIdBox_HasExpectedLabel()
    {
        var view = new EDView();
        var ctrl = view.FindControl<IdFieldControl>("Retreat_UnitIdBox");
        Assert.NotNull(ctrl);
        // Label updated to match WF "登場ユニット" / "Appearing Unit" (#411).
        Assert.Equal("Appearing Unit", ctrl!.Label);
    }

    [AvaloniaFact]
    public void EDView_UnitId_AutomationIdResolvesToNumericUpDownUniquely()
    {
        var view = new EDView();
        view.Show();
        // EDView restructured to 3 tabs per #411 - the AutomationId is now
        // ED_Retreat_UnitId_Input (one per tab: Retreat / Epithet / Epilogue).
        try { AssertAutomationIdResolvesToNumericUpDown(view, "ED_Retreat_UnitId_Input"); }
        finally { view.Close(); }
    }

    [AvaloniaFact]
    public void SummonsDemonKingViewer_UnitIdBox_IsIdFieldControl()
    {
        var view = new SummonsDemonKingViewerView();
        Assert.NotNull(view.FindControl<IdFieldControl>("UnitIdBox"));
    }

    [AvaloniaFact]
    public void SummonsDemonKingViewer_UnitIdBox_HasExpectedLabel()
    {
        var view = new SummonsDemonKingViewerView();
        var ctrl = view.FindControl<IdFieldControl>("UnitIdBox");
        Assert.NotNull(ctrl);
        Assert.Equal("Unit ID:", ctrl!.Label);
    }

    [AvaloniaFact]
    public void SummonsDemonKingViewer_UnitId_AutomationIdResolvesToNumericUpDownUniquely()
    {
        var view = new SummonsDemonKingViewerView();
        view.Show();
        try { AssertAutomationIdResolvesToNumericUpDown(view, "SummonsDemonKingViewer_UnitId_Input"); }
        finally { view.Close(); }
    }

    // ---- WU3: Class IDs (3 editors / 3 fields) ------------------------

    [AvaloniaFact]
    public void SummonsDemonKingViewer_ClassIdBox_IsIdFieldControl()
    {
        var view = new SummonsDemonKingViewerView();
        Assert.NotNull(view.FindControl<IdFieldControl>("ClassIdBox"));
    }

    [AvaloniaFact]
    public void SummonsDemonKingViewer_ClassIdBox_HasExpectedLabel()
    {
        var view = new SummonsDemonKingViewerView();
        var ctrl = view.FindControl<IdFieldControl>("ClassIdBox");
        Assert.NotNull(ctrl);
        Assert.Equal("Class ID:", ctrl!.Label);
    }

    [AvaloniaFact]
    public void SummonsDemonKingViewer_ClassId_AutomationIdResolvesToNumericUpDownUniquely()
    {
        var view = new SummonsDemonKingViewerView();
        view.Show();
        try { AssertAutomationIdResolvesToNumericUpDown(view, "SummonsDemonKingViewer_ClassId_Input"); }
        finally { view.Close(); }
    }

    [AvaloniaFact]
    public void ArenaClassViewer_ClassIdBox_IsIdFieldControl()
    {
        var view = new ArenaClassViewerView();
        Assert.NotNull(view.FindControl<IdFieldControl>("ClassIdBox"));
    }

    [AvaloniaFact]
    public void ArenaClassViewer_ClassIdBox_HasExpectedLabel()
    {
        var view = new ArenaClassViewerView();
        var ctrl = view.FindControl<IdFieldControl>("ClassIdBox");
        Assert.NotNull(ctrl);
        Assert.Equal("Class ID:", ctrl!.Label);
    }

    [AvaloniaFact]
    public void ArenaClassViewer_ClassId_AutomationIdResolvesToNumericUpDownUniquely()
    {
        var view = new ArenaClassViewerView();
        view.Show();
        try { AssertAutomationIdResolvesToNumericUpDown(view, "ArenaClassViewer_ClassId_Input"); }
        finally { view.Close(); }
    }

    [AvaloniaFact]
    public void OPClassDemoFE8U_ClassIdBox_IsIdFieldControl()
    {
        var view = new OPClassDemoFE8UView();
        Assert.NotNull(view.FindControl<IdFieldControl>("ClassIdBox"));
    }

    [AvaloniaFact]
    public void OPClassDemoFE8U_ClassIdBox_HasExpectedLabel()
    {
        var view = new OPClassDemoFE8UView();
        var ctrl = view.FindControl<IdFieldControl>("ClassIdBox");
        Assert.NotNull(ctrl);
        Assert.Equal("Class ID:", ctrl!.Label);
    }

    [AvaloniaFact]
    public void OPClassDemoFE8U_ClassId_AutomationIdResolvesToNumericUpDownUniquely()
    {
        var view = new OPClassDemoFE8UView();
        view.Show();
        try { AssertAutomationIdResolvesToNumericUpDown(view, "OPClassDemoFE8U_ClassId_Input"); }
        finally { view.Close(); }
    }

    // ---- Helpers ------------------------------------------------------

    static void AssertAutomationIdResolvesToNumericUpDown(Window view, string automationId)
    {
        // GetLogicalDescendants can enumerate the same Control instance twice
        // when it lives inside a TabControl item (the logical tree traverses
        // both Header and Content slots), so deduplicate by reference before
        // asserting uniqueness. Same instance still counts as one match.
        var hits = view.GetLogicalDescendants()
            .OfType<Control>()
            .Where(c => AutomationProperties.GetAutomationId(c) == automationId)
            .Distinct()
            .ToList();
        Assert.Single(hits);
        Assert.IsType<NumericUpDown>(hits[0]);
    }
}
