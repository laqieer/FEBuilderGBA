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

    // ====================================================================
    // #360 final closure — 22 additional fields across 10 editors.
    // Each field gets 3 named tests: IsIdFieldControl, HasExpectedLabel,
    // AutomationIdResolvesToNumericUpDownUniquely. Text-id fields also
    // assert ShowPick == false inside HasExpectedLabel.
    // ====================================================================

    // ---- WU1 (#360 final): OPClassDemoFE7View.ClassId ----------------

    [AvaloniaFact]
    public void OPClassDemoFE7_ClassIdBox_IsIdFieldControl()
    {
        var view = new OPClassDemoFE7View();
        Assert.NotNull(view.FindControl<IdFieldControl>("ClassIdBox"));
    }

    [AvaloniaFact]
    public void OPClassDemoFE7_ClassIdBox_HasExpectedLabel()
    {
        var view = new OPClassDemoFE7View();
        var ctrl = view.FindControl<IdFieldControl>("ClassIdBox");
        Assert.NotNull(ctrl);
        Assert.Equal("Class ID:", ctrl!.Label);
    }

    [AvaloniaFact]
    public void OPClassDemoFE7_ClassId_AutomationIdResolvesToNumericUpDownUniquely()
    {
        var view = new OPClassDemoFE7View();
        view.Show();
        try { AssertAutomationIdResolvesToNumericUpDown(view, "OPClassDemoFE7_ClassId_Input"); }
        finally { view.Close(); }
    }

    // ---- WU1 (#360 final): OPClassDemoFE7UView.ClassId ---------------

    [AvaloniaFact]
    public void OPClassDemoFE7U_ClassIdBox_IsIdFieldControl()
    {
        var view = new OPClassDemoFE7UView();
        Assert.NotNull(view.FindControl<IdFieldControl>("ClassIdBox"));
    }

    [AvaloniaFact]
    public void OPClassDemoFE7U_ClassIdBox_HasExpectedLabel()
    {
        var view = new OPClassDemoFE7UView();
        var ctrl = view.FindControl<IdFieldControl>("ClassIdBox");
        Assert.NotNull(ctrl);
        Assert.Equal("Class ID:", ctrl!.Label);
    }

    [AvaloniaFact]
    public void OPClassDemoFE7U_ClassId_AutomationIdResolvesToNumericUpDownUniquely()
    {
        var view = new OPClassDemoFE7UView();
        view.Show();
        try { AssertAutomationIdResolvesToNumericUpDown(view, "OPClassDemoFE7U_ClassId_Input"); }
        finally { view.Close(); }
    }

    // ---- WU1 (#360 final): SoundRoomViewerView.SongId ----------------

    [AvaloniaFact]
    public void SoundRoomViewer_SongIdBox_IsIdFieldControl()
    {
        var view = new SoundRoomViewerView();
        Assert.NotNull(view.FindControl<IdFieldControl>("SongIdBox"));
    }

    [AvaloniaFact]
    public void SoundRoomViewer_SongIdBox_HasExpectedLabel()
    {
        var view = new SoundRoomViewerView();
        var ctrl = view.FindControl<IdFieldControl>("SongIdBox");
        Assert.NotNull(ctrl);
        Assert.Equal("Song ID:", ctrl!.Label);
    }

    [AvaloniaFact]
    public void SoundRoomViewer_SongId_AutomationIdResolvesToNumericUpDownUniquely()
    {
        var view = new SoundRoomViewerView();
        view.Show();
        try { AssertAutomationIdResolvesToNumericUpDown(view, "SoundRoomViewer_SongId_Input"); }
        finally { view.Close(); }
    }

    // ---- WU1 (#360 final): EDSensekiCommentView.UnitId ---------------

    [AvaloniaFact]
    public void EDSensekiComment_UnitIdBox_IsIdFieldControl()
    {
        var view = new EDSensekiCommentView();
        Assert.NotNull(view.FindControl<IdFieldControl>("UnitIdBox"));
    }

    [AvaloniaFact]
    public void EDSensekiComment_UnitIdBox_HasExpectedLabel()
    {
        var view = new EDSensekiCommentView();
        var ctrl = view.FindControl<IdFieldControl>("UnitIdBox");
        Assert.NotNull(ctrl);
        Assert.Equal("Unit ID:", ctrl!.Label);
    }

    [AvaloniaFact]
    public void EDSensekiComment_UnitId_AutomationIdResolvesToNumericUpDownUniquely()
    {
        var view = new EDSensekiCommentView();
        view.Show();
        try { AssertAutomationIdResolvesToNumericUpDown(view, "EDSensekiComment_UnitId_Input"); }
        finally { view.Close(); }
    }

    // ---- WU1 (#360 final): ItemShopViewerView.ItemId -----------------

    [AvaloniaFact]
    public void ItemShopViewer_ItemIdBox_IsIdFieldControl()
    {
        var view = new ItemShopViewerView();
        Assert.NotNull(view.FindControl<IdFieldControl>("ItemIdBox"));
    }

    [AvaloniaFact]
    public void ItemShopViewer_ItemIdBox_HasExpectedLabel()
    {
        var view = new ItemShopViewerView();
        var ctrl = view.FindControl<IdFieldControl>("ItemIdBox");
        Assert.NotNull(ctrl);
        Assert.Equal("Item ID:", ctrl!.Label);
    }

    [AvaloniaFact]
    public void ItemShopViewer_ItemId_AutomationIdResolvesToNumericUpDownUniquely()
    {
        var view = new ItemShopViewerView();
        view.Show();
        try { AssertAutomationIdResolvesToNumericUpDown(view, "ItemShopViewer_ItemId_Input"); }
        finally { view.Close(); }
    }

    // ---- WU1 (#360 final): ItemEffectivenessViewerView.ClassId -------

    [AvaloniaFact]
    public void ItemEffectivenessViewer_ClassIdBox_IsIdFieldControl()
    {
        var view = new ItemEffectivenessViewerView();
        Assert.NotNull(view.FindControl<IdFieldControl>("ClassIdBox"));
    }

    [AvaloniaFact]
    public void ItemEffectivenessViewer_ClassIdBox_HasExpectedLabel()
    {
        var view = new ItemEffectivenessViewerView();
        var ctrl = view.FindControl<IdFieldControl>("ClassIdBox");
        Assert.NotNull(ctrl);
        Assert.Equal("Class:", ctrl!.Label);
    }

    [AvaloniaFact]
    public void ItemEffectivenessViewer_ClassId_AutomationIdResolvesToNumericUpDownUniquely()
    {
        var view = new ItemEffectivenessViewerView();
        view.Show();
        try { AssertAutomationIdResolvesToNumericUpDown(view, "ItemEffectivenessViewer_ClassId_Input"); }
        finally { view.Close(); }
    }

    // ---- WU1 (#360 final): ItemPromotionViewerView.ClassId -----------

    [AvaloniaFact]
    public void ItemPromotionViewer_ClassIdBox_IsIdFieldControl()
    {
        var view = new ItemPromotionViewerView();
        Assert.NotNull(view.FindControl<IdFieldControl>("ClassIdBox"));
    }

    [AvaloniaFact]
    public void ItemPromotionViewer_ClassIdBox_HasExpectedLabel()
    {
        var view = new ItemPromotionViewerView();
        var ctrl = view.FindControl<IdFieldControl>("ClassIdBox");
        Assert.NotNull(ctrl);
        Assert.Equal("Class:", ctrl!.Label);
    }

    [AvaloniaFact]
    public void ItemPromotionViewer_ClassId_AutomationIdResolvesToNumericUpDownUniquely()
    {
        var view = new ItemPromotionViewerView();
        view.Show();
        try { AssertAutomationIdResolvesToNumericUpDown(view, "ItemPromotionViewer_ClassId_Input"); }
        finally { view.Close(); }
    }

    // ====================================================================
    // WU2-unit (#360 final): SupportPartner1/2 (6 unit fields).
    // ====================================================================

    [AvaloniaFact]
    public void SupportTalk_SupportPartner1Nud_IsIdFieldControl()
    {
        var view = new SupportTalkView();
        Assert.NotNull(view.FindControl<IdFieldControl>("SupportPartner1Nud"));
    }

    [AvaloniaFact]
    public void SupportTalk_SupportPartner1Nud_HasExpectedLabel()
    {
        var view = new SupportTalkView();
        var ctrl = view.FindControl<IdFieldControl>("SupportPartner1Nud");
        Assert.NotNull(ctrl);
        Assert.Equal("Support Partner 1:", ctrl!.Label);
        Assert.True(ctrl.ShowPick); // unit fields keep Pick visible
    }

    [AvaloniaFact]
    public void SupportTalk_SupportPartner1_AutomationIdResolvesToNumericUpDownUniquely()
    {
        var view = new SupportTalkView();
        view.Show();
        try { AssertAutomationIdResolvesToNumericUpDown(view, "SupportTalk_SupportPartner1Nud_Input"); }
        finally { view.Close(); }
    }

    [AvaloniaFact]
    public void SupportTalk_SupportPartner2Nud_IsIdFieldControl()
    {
        var view = new SupportTalkView();
        Assert.NotNull(view.FindControl<IdFieldControl>("SupportPartner2Nud"));
    }

    [AvaloniaFact]
    public void SupportTalk_SupportPartner2Nud_HasExpectedLabel()
    {
        var view = new SupportTalkView();
        var ctrl = view.FindControl<IdFieldControl>("SupportPartner2Nud");
        Assert.NotNull(ctrl);
        Assert.Equal("Support Partner 2:", ctrl!.Label);
        Assert.True(ctrl.ShowPick);
    }

    [AvaloniaFact]
    public void SupportTalk_SupportPartner2_AutomationIdResolvesToNumericUpDownUniquely()
    {
        var view = new SupportTalkView();
        view.Show();
        try { AssertAutomationIdResolvesToNumericUpDown(view, "SupportTalk_SupportPartner2Nud_Input"); }
        finally { view.Close(); }
    }

    [AvaloniaFact]
    public void SupportTalkFE6_SupportPartner1Nud_IsIdFieldControl()
    {
        var view = new SupportTalkFE6View();
        Assert.NotNull(view.FindControl<IdFieldControl>("SupportPartner1Nud"));
    }

    [AvaloniaFact]
    public void SupportTalkFE6_SupportPartner1Nud_HasExpectedLabel()
    {
        var view = new SupportTalkFE6View();
        var ctrl = view.FindControl<IdFieldControl>("SupportPartner1Nud");
        Assert.NotNull(ctrl);
        Assert.Equal("Support Partner 1:", ctrl!.Label);
        Assert.True(ctrl.ShowPick);
    }

    [AvaloniaFact]
    public void SupportTalkFE6_SupportPartner1_AutomationIdResolvesToNumericUpDownUniquely()
    {
        var view = new SupportTalkFE6View();
        view.Show();
        try { AssertAutomationIdResolvesToNumericUpDown(view, "SupportTalkFE6_SupportPartner1Nud_Input"); }
        finally { view.Close(); }
    }

    [AvaloniaFact]
    public void SupportTalkFE6_SupportPartner2Nud_IsIdFieldControl()
    {
        var view = new SupportTalkFE6View();
        Assert.NotNull(view.FindControl<IdFieldControl>("SupportPartner2Nud"));
    }

    [AvaloniaFact]
    public void SupportTalkFE6_SupportPartner2Nud_HasExpectedLabel()
    {
        var view = new SupportTalkFE6View();
        var ctrl = view.FindControl<IdFieldControl>("SupportPartner2Nud");
        Assert.NotNull(ctrl);
        Assert.Equal("Support Partner 2:", ctrl!.Label);
        Assert.True(ctrl.ShowPick);
    }

    [AvaloniaFact]
    public void SupportTalkFE6_SupportPartner2_AutomationIdResolvesToNumericUpDownUniquely()
    {
        var view = new SupportTalkFE6View();
        view.Show();
        try { AssertAutomationIdResolvesToNumericUpDown(view, "SupportTalkFE6_SupportPartner2Nud_Input"); }
        finally { view.Close(); }
    }

    [AvaloniaFact]
    public void SupportTalkFE7_SupportPartner1Nud_IsIdFieldControl()
    {
        var view = new SupportTalkFE7View();
        Assert.NotNull(view.FindControl<IdFieldControl>("SupportPartner1Nud"));
    }

    [AvaloniaFact]
    public void SupportTalkFE7_SupportPartner1Nud_HasExpectedLabel()
    {
        var view = new SupportTalkFE7View();
        var ctrl = view.FindControl<IdFieldControl>("SupportPartner1Nud");
        Assert.NotNull(ctrl);
        Assert.Equal("Support Partner 1:", ctrl!.Label);
        Assert.True(ctrl.ShowPick);
    }

    [AvaloniaFact]
    public void SupportTalkFE7_SupportPartner1_AutomationIdResolvesToNumericUpDownUniquely()
    {
        var view = new SupportTalkFE7View();
        view.Show();
        try { AssertAutomationIdResolvesToNumericUpDown(view, "SupportTalkFE7_SupportPartner1Nud_Input"); }
        finally { view.Close(); }
    }

    [AvaloniaFact]
    public void SupportTalkFE7_SupportPartner2Nud_IsIdFieldControl()
    {
        var view = new SupportTalkFE7View();
        Assert.NotNull(view.FindControl<IdFieldControl>("SupportPartner2Nud"));
    }

    [AvaloniaFact]
    public void SupportTalkFE7_SupportPartner2Nud_HasExpectedLabel()
    {
        var view = new SupportTalkFE7View();
        var ctrl = view.FindControl<IdFieldControl>("SupportPartner2Nud");
        Assert.NotNull(ctrl);
        Assert.Equal("Support Partner 2:", ctrl!.Label);
        Assert.True(ctrl.ShowPick);
    }

    [AvaloniaFact]
    public void SupportTalkFE7_SupportPartner2_AutomationIdResolvesToNumericUpDownUniquely()
    {
        var view = new SupportTalkFE7View();
        view.Show();
        try { AssertAutomationIdResolvesToNumericUpDown(view, "SupportTalkFE7_SupportPartner2Nud_Input"); }
        finally { view.Close(); }
    }

    // ====================================================================
    // WU2-text (#360 final): SupportTalk TextId C/B/A (9 text fields).
    // ShowPick must be false because TextViewerView is not IPickableEditor.
    // ====================================================================

    [AvaloniaFact]
    public void SupportTalk_TextIdCNud_IsIdFieldControl()
    {
        var view = new SupportTalkView();
        Assert.NotNull(view.FindControl<IdFieldControl>("TextIdCNud"));
    }

    [AvaloniaFact]
    public void SupportTalk_TextIdCNud_HasExpectedLabelAndShowPickFalse()
    {
        var view = new SupportTalkView();
        var ctrl = view.FindControl<IdFieldControl>("TextIdCNud");
        Assert.NotNull(ctrl);
        Assert.Equal("C Support Text:", ctrl!.Label);
        Assert.False(ctrl.ShowPick); // text-id fields hide Pick
    }

    [AvaloniaFact]
    public void SupportTalk_TextIdC_AutomationIdResolvesToNumericUpDownUniquely()
    {
        var view = new SupportTalkView();
        view.Show();
        try { AssertAutomationIdResolvesToNumericUpDown(view, "SupportTalk_TextIdCNud_Input"); }
        finally { view.Close(); }
    }

    [AvaloniaFact]
    public void SupportTalk_TextIdBNud_IsIdFieldControl()
    {
        var view = new SupportTalkView();
        Assert.NotNull(view.FindControl<IdFieldControl>("TextIdBNud"));
    }

    [AvaloniaFact]
    public void SupportTalk_TextIdBNud_HasExpectedLabelAndShowPickFalse()
    {
        var view = new SupportTalkView();
        var ctrl = view.FindControl<IdFieldControl>("TextIdBNud");
        Assert.NotNull(ctrl);
        Assert.Equal("B Support Text:", ctrl!.Label);
        Assert.False(ctrl.ShowPick);
    }

    [AvaloniaFact]
    public void SupportTalk_TextIdB_AutomationIdResolvesToNumericUpDownUniquely()
    {
        var view = new SupportTalkView();
        view.Show();
        try { AssertAutomationIdResolvesToNumericUpDown(view, "SupportTalk_TextIdBNud_Input"); }
        finally { view.Close(); }
    }

    [AvaloniaFact]
    public void SupportTalk_TextIdANud_IsIdFieldControl()
    {
        var view = new SupportTalkView();
        Assert.NotNull(view.FindControl<IdFieldControl>("TextIdANud"));
    }

    [AvaloniaFact]
    public void SupportTalk_TextIdANud_HasExpectedLabelAndShowPickFalse()
    {
        var view = new SupportTalkView();
        var ctrl = view.FindControl<IdFieldControl>("TextIdANud");
        Assert.NotNull(ctrl);
        Assert.Equal("A Support Text:", ctrl!.Label);
        Assert.False(ctrl.ShowPick);
    }

    [AvaloniaFact]
    public void SupportTalk_TextIdA_AutomationIdResolvesToNumericUpDownUniquely()
    {
        var view = new SupportTalkView();
        view.Show();
        try { AssertAutomationIdResolvesToNumericUpDown(view, "SupportTalk_TextIdANud_Input"); }
        finally { view.Close(); }
    }

    [AvaloniaFact]
    public void SupportTalkFE6_TextCNud_IsIdFieldControl()
    {
        var view = new SupportTalkFE6View();
        Assert.NotNull(view.FindControl<IdFieldControl>("TextCNud"));
    }

    [AvaloniaFact]
    public void SupportTalkFE6_TextCNud_HasExpectedLabelAndShowPickFalse()
    {
        var view = new SupportTalkFE6View();
        var ctrl = view.FindControl<IdFieldControl>("TextCNud");
        Assert.NotNull(ctrl);
        Assert.Equal("C Support Text:", ctrl!.Label);
        Assert.False(ctrl.ShowPick);
    }

    [AvaloniaFact]
    public void SupportTalkFE6_TextC_AutomationIdResolvesToNumericUpDownUniquely()
    {
        var view = new SupportTalkFE6View();
        view.Show();
        try { AssertAutomationIdResolvesToNumericUpDown(view, "SupportTalkFE6_TextCNud_Input"); }
        finally { view.Close(); }
    }

    [AvaloniaFact]
    public void SupportTalkFE6_TextBNud_IsIdFieldControl()
    {
        var view = new SupportTalkFE6View();
        Assert.NotNull(view.FindControl<IdFieldControl>("TextBNud"));
    }

    [AvaloniaFact]
    public void SupportTalkFE6_TextBNud_HasExpectedLabelAndShowPickFalse()
    {
        var view = new SupportTalkFE6View();
        var ctrl = view.FindControl<IdFieldControl>("TextBNud");
        Assert.NotNull(ctrl);
        Assert.Equal("B Support Text:", ctrl!.Label);
        Assert.False(ctrl.ShowPick);
    }

    [AvaloniaFact]
    public void SupportTalkFE6_TextB_AutomationIdResolvesToNumericUpDownUniquely()
    {
        var view = new SupportTalkFE6View();
        view.Show();
        try { AssertAutomationIdResolvesToNumericUpDown(view, "SupportTalkFE6_TextBNud_Input"); }
        finally { view.Close(); }
    }

    [AvaloniaFact]
    public void SupportTalkFE6_TextANud_IsIdFieldControl()
    {
        var view = new SupportTalkFE6View();
        Assert.NotNull(view.FindControl<IdFieldControl>("TextANud"));
    }

    [AvaloniaFact]
    public void SupportTalkFE6_TextANud_HasExpectedLabelAndShowPickFalse()
    {
        var view = new SupportTalkFE6View();
        var ctrl = view.FindControl<IdFieldControl>("TextANud");
        Assert.NotNull(ctrl);
        Assert.Equal("A Support Text:", ctrl!.Label);
        Assert.False(ctrl.ShowPick);
    }

    [AvaloniaFact]
    public void SupportTalkFE6_TextA_AutomationIdResolvesToNumericUpDownUniquely()
    {
        var view = new SupportTalkFE6View();
        view.Show();
        try { AssertAutomationIdResolvesToNumericUpDown(view, "SupportTalkFE6_TextANud_Input"); }
        finally { view.Close(); }
    }

    [AvaloniaFact]
    public void SupportTalkFE7_TextCNud_IsIdFieldControl()
    {
        var view = new SupportTalkFE7View();
        Assert.NotNull(view.FindControl<IdFieldControl>("TextCNud"));
    }

    [AvaloniaFact]
    public void SupportTalkFE7_TextCNud_HasExpectedLabelAndShowPickFalse()
    {
        var view = new SupportTalkFE7View();
        var ctrl = view.FindControl<IdFieldControl>("TextCNud");
        Assert.NotNull(ctrl);
        Assert.Equal("C Support Text:", ctrl!.Label);
        Assert.False(ctrl.ShowPick);
    }

    [AvaloniaFact]
    public void SupportTalkFE7_TextC_AutomationIdResolvesToNumericUpDownUniquely()
    {
        var view = new SupportTalkFE7View();
        view.Show();
        try { AssertAutomationIdResolvesToNumericUpDown(view, "SupportTalkFE7_TextCNud_Input"); }
        finally { view.Close(); }
    }

    [AvaloniaFact]
    public void SupportTalkFE7_TextBNud_IsIdFieldControl()
    {
        var view = new SupportTalkFE7View();
        Assert.NotNull(view.FindControl<IdFieldControl>("TextBNud"));
    }

    [AvaloniaFact]
    public void SupportTalkFE7_TextBNud_HasExpectedLabelAndShowPickFalse()
    {
        var view = new SupportTalkFE7View();
        var ctrl = view.FindControl<IdFieldControl>("TextBNud");
        Assert.NotNull(ctrl);
        Assert.Equal("B Support Text:", ctrl!.Label);
        Assert.False(ctrl.ShowPick);
    }

    [AvaloniaFact]
    public void SupportTalkFE7_TextB_AutomationIdResolvesToNumericUpDownUniquely()
    {
        var view = new SupportTalkFE7View();
        view.Show();
        try { AssertAutomationIdResolvesToNumericUpDown(view, "SupportTalkFE7_TextBNud_Input"); }
        finally { view.Close(); }
    }

    [AvaloniaFact]
    public void SupportTalkFE7_TextANud_IsIdFieldControl()
    {
        var view = new SupportTalkFE7View();
        Assert.NotNull(view.FindControl<IdFieldControl>("TextANud"));
    }

    [AvaloniaFact]
    public void SupportTalkFE7_TextANud_HasExpectedLabelAndShowPickFalse()
    {
        var view = new SupportTalkFE7View();
        var ctrl = view.FindControl<IdFieldControl>("TextANud");
        Assert.NotNull(ctrl);
        Assert.Equal("A Support Text:", ctrl!.Label);
        Assert.False(ctrl.ShowPick);
    }

    [AvaloniaFact]
    public void SupportTalkFE7_TextA_AutomationIdResolvesToNumericUpDownUniquely()
    {
        var view = new SupportTalkFE7View();
        view.Show();
        try { AssertAutomationIdResolvesToNumericUpDown(view, "SupportTalkFE7_TextANud_Input"); }
        finally { view.Close(); }
    }

    // ====================================================================
    // Generic ShowPick sanity test — verify the new IdFieldControl
    // property actually toggles PickButton.IsVisible (covers the
    // OnPropertyChanged path used by AXAML ShowPick="False" bindings).
    // ====================================================================

    [AvaloniaFact]
    public void IdFieldControl_ShowPick_DefaultsTrue_PickButtonVisible()
    {
        var ctrl = new IdFieldControl();
        Assert.True(ctrl.ShowPick); // default value
        var pickButton = ctrl.FindControl<Button>("PickButton");
        Assert.NotNull(pickButton);
        Assert.True(pickButton!.IsVisible);
    }

    [AvaloniaFact]
    public void IdFieldControl_ShowPick_FalseHidesPickButton()
    {
        var ctrl = new IdFieldControl();
        ctrl.ShowPick = false;
        var pickButton = ctrl.FindControl<Button>("PickButton");
        Assert.NotNull(pickButton);
        Assert.False(pickButton!.IsVisible);
    }

    // ====================================================================
    // #950 T4 — secondary type-ID fields migrated to IdFieldControl.
    //   Tier 1: MonsterItemViewer Item 2..5 (item), MonsterProbabilityViewer
    //           Class ID 1..5 (class), SummonUnitViewer Summoned Unit (unit).
    //   Tier 2: ClassOPDemo / OPClassDemoViewer Display Weapon B14 (class).
    //   Tier 3: EventCond TALK Unit 1/2 (unit) + OBJECT Chest Item (item).
    // ====================================================================

    // ---- Tier 1: MonsterItemViewer Item 2..5 (DropRate/Unknown1..3Box) ----

    [AvaloniaTheory]
    [InlineData("DropRateBox", "Item 2:")]
    [InlineData("Unknown1Box", "Item 3:")]
    [InlineData("Unknown2Box", "Item 4:")]
    [InlineData("Unknown3Box", "Item 5:")]
    public void MonsterItemViewer_SecondaryItemBoxes_AreIdFieldControls(string name, string label)
    {
        var view = new MonsterItemViewerView();
        var ctrl = view.FindControl<IdFieldControl>(name);
        Assert.NotNull(ctrl);
        Assert.Equal(label, ctrl!.Label);
        Assert.True(ctrl.ShowPick); // item fields keep Pick visible
    }

    [AvaloniaTheory]
    [InlineData("MonsterItemViewer_Item_Item2_Input")]
    [InlineData("MonsterItemViewer_Item_Item3_Input")]
    [InlineData("MonsterItemViewer_Item_Item4_Input")]
    [InlineData("MonsterItemViewer_Item_Item5_Input")]
    public void MonsterItemViewer_SecondaryItem_AutomationIdResolvesToNumericUpDownUniquely(string automationId)
    {
        var view = new MonsterItemViewerView();
        view.Show();
        try { AssertAutomationIdResolvesToNumericUpDown(view, automationId); }
        finally { view.Close(); }
    }

    // ---- Tier 1: MonsterProbabilityViewer Class ID 1..5 -------------------

    [AvaloniaTheory]
    [InlineData("ClassId1Box", "Class ID 1:")]
    [InlineData("ClassId2Box", "Class ID 2:")]
    [InlineData("ClassId3Box", "Class ID 3:")]
    [InlineData("ClassId4Box", "Class ID 4:")]
    [InlineData("ClassId5Box", "Class ID 5:")]
    public void MonsterProbabilityViewer_ClassIdBoxes_AreIdFieldControls(string name, string label)
    {
        var view = new MonsterProbabilityViewerView();
        var ctrl = view.FindControl<IdFieldControl>(name);
        Assert.NotNull(ctrl);
        Assert.Equal(label, ctrl!.Label);
        Assert.True(ctrl.ShowPick); // class fields keep Pick visible
    }

    [AvaloniaTheory]
    [InlineData("MonsterProbabilityViewer_ClassId1_Input")]
    [InlineData("MonsterProbabilityViewer_ClassId2_Input")]
    [InlineData("MonsterProbabilityViewer_ClassId3_Input")]
    [InlineData("MonsterProbabilityViewer_ClassId4_Input")]
    [InlineData("MonsterProbabilityViewer_ClassId5_Input")]
    public void MonsterProbabilityViewer_ClassId_AutomationIdResolvesToNumericUpDownUniquely(string automationId)
    {
        var view = new MonsterProbabilityViewerView();
        view.Show();
        try { AssertAutomationIdResolvesToNumericUpDown(view, automationId); }
        finally { view.Close(); }
    }

    // ---- Tier 1: SummonUnitViewer Summoned Unit (UnknownBox) --------------

    [AvaloniaFact]
    public void SummonUnitViewer_SummonedUnitBox_IsIdFieldControl()
    {
        var view = new SummonUnitViewerView();
        var ctrl = view.FindControl<IdFieldControl>("UnknownBox");
        Assert.NotNull(ctrl);
        Assert.Equal("Summoned Unit:", ctrl!.Label);
        Assert.True(ctrl.ShowPick);
    }

    [AvaloniaFact]
    public void SummonUnitViewer_SummonedUnit_AutomationIdResolvesToNumericUpDownUniquely()
    {
        var view = new SummonUnitViewerView();
        view.Show();
        try { AssertAutomationIdResolvesToNumericUpDown(view, "SummonUnitViewer_SummonedUnit_Input"); }
        finally { view.Close(); }
    }

    // ---- Tier 2: ClassOPDemo / OPClassDemoViewer Display Weapon (B14) -----

    [AvaloniaFact]
    public void ClassOPDemo_DisplayWeaponBox_IsIdFieldControl()
    {
        var view = new ClassOPDemoView();
        var ctrl = view.FindControl<IdFieldControl>("DisplayWeaponBox");
        Assert.NotNull(ctrl);
        Assert.Equal("Class ID:", ctrl!.Label);
        Assert.True(ctrl.ShowPick);
    }

    [AvaloniaFact]
    public void ClassOPDemo_DisplayWeapon_AutomationIdResolvesToNumericUpDownUniquely()
    {
        var view = new ClassOPDemoView();
        view.Show();
        try { AssertAutomationIdResolvesToNumericUpDown(view, "ClassOPDemo_DisplayWeapon_Input"); }
        finally { view.Close(); }
    }

    [AvaloniaFact]
    public void OPClassDemoViewer_DisplayWeaponBox_IsIdFieldControl()
    {
        var view = new OPClassDemoViewerView();
        var ctrl = view.FindControl<IdFieldControl>("DisplayWeaponBox");
        Assert.NotNull(ctrl);
        // #951 layout-clip fix: the original caption was merged into the
        // hyperlink Label (and the redundant separate caption Label removed)
        // when the control moved to columns 0..2 for full width.
        Assert.Equal("Display Weapon (Class):", ctrl!.Label);
        Assert.True(ctrl.ShowPick);
    }

    [AvaloniaFact]
    public void OPClassDemoViewer_DisplayWeapon_AutomationIdResolvesToNumericUpDownUniquely()
    {
        var view = new OPClassDemoViewerView();
        view.Show();
        try { AssertAutomationIdResolvesToNumericUpDown(view, "OPClassDemoViewer_DisplayWeapon_Input"); }
        finally { view.Close(); }
    }

    // ---- Tier 3: EventCond TALK Unit 1/2 + OBJECT Chest Item -------------

    [AvaloniaTheory]
    [InlineData("Unit1Box")]
    [InlineData("Unit2Box")]
    public void EventCond_TalkUnitBoxes_AreIdFieldControls(string name)
    {
        var view = new EventCondView();
        var ctrl = view.FindControl<IdFieldControl>(name);
        Assert.NotNull(ctrl);
        Assert.True(ctrl!.ShowPick); // unit fields keep Pick visible
    }

    [AvaloniaFact]
    public void EventCond_ChestItemBox_IsIdFieldControl()
    {
        var view = new EventCondView();
        var ctrl = view.FindControl<IdFieldControl>("ChestItemBox");
        Assert.NotNull(ctrl);
        Assert.True(ctrl!.ShowPick); // item field keeps Pick visible
    }

    [AvaloniaTheory]
    [InlineData("EventCond_Unit1_Input")]
    [InlineData("EventCond_Unit2_Input")]
    [InlineData("EventCond_ChestItem_Input")]
    public void EventCond_TalkObject_AutomationIdResolvesToNumericUpDownUniquely(string automationId)
    {
        var view = new EventCondView();
        view.Show();
        try { AssertAutomationIdResolvesToNumericUpDown(view, automationId); }
        finally { view.Close(); }
    }

    // ---- Helpers ------------------------------------------------------

    static void AssertAutomationIdResolvesToNumericUpDown(Control view, string automationId)
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
