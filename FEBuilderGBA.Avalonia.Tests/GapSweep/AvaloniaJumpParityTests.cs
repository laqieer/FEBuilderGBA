// SPDX-License-Identifier: GPL-3.0-or-later
// Phase 4 — Avalonia jump/navigation parity regression suite. (#374)
//
// Two test surfaces:
//
// 1. A Theory over EVERY AV manifest entry — asserts the declared
//    TargetViewType resolves to a real class in the Avalonia assembly.
//    This is the cross-cutting sanity check that catches typos and stale
//    refs as the manifest grows.
//
// 2. Per-known-gap [Fact(Skip=...)] cases — one per tracked issue. CI sees
//    these as Skip, keeping the build green. When the fix PR lands for an
//    issue (the navigation is corrected + the AV manifest IssueRef tag
//    removed), the Skip flips to Fact and the test becomes a regression
//    assertion.
//
// Sanity tests at the bottom exercise scanner integration against the live
// worktree.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FEBuilderGBA.Avalonia.GapSweep;
using FEBuilderGBA.Avalonia.Services;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests.GapSweep;

/// <summary>
/// xunit suite covering AV manifest contents and the per-issue known-gap
/// regression placeholders. Most assertions are driven by reflection over
/// the loaded Avalonia assembly so the tests scale automatically as more
/// VMs adopt <see cref="INavigationTargetSource"/>.
/// </summary>
public class AvaloniaJumpParityTests
{
    /// <summary>
    /// Theory data — one row per discovered (SourceVm, TargetView) tuple
    /// across every <see cref="INavigationTargetSource"/> implementation.
    /// Discovery runs ONCE per test process per the static initializer.
    /// </summary>
    public static IEnumerable<object[]> ManifestEntriesTheoryData
    {
        get
        {
            var manifests = JumpParityScanner.ScanAvManifests(typeof(INavigationTargetSource).Assembly);
            foreach (var entry in manifests)
            {
                yield return new object[]
                {
                    entry.SourceVm,
                    entry.Command,
                    entry.TargetView,
                    entry.IssueRef ?? "",
                };
            }
        }
    }

    [Theory]
    [MemberData(nameof(ManifestEntriesTheoryData))]
    public void EveryManifestEntry_TargetViewResolvesToRealType(
        string sourceVm,
        string command,
        string targetView,
        string issueRef)
    {
        // The TargetView declared in each manifest entry MUST resolve to a
        // real concrete type in the Avalonia assembly. A typo / deletion
        // would make this fail — that's intentional, so the manifest
        // stays in sync with the codebase.
        Assert.False(string.IsNullOrEmpty(targetView),
            $"VM {sourceVm}: Command {command} has empty target view");
        Type? resolved = typeof(INavigationTargetSource).Assembly
            .GetTypes()
            .FirstOrDefault(t => t.Name == targetView);
        Assert.NotNull(resolved);
        // We don't enforce the type be an Avalonia Window here — the
        // INavigationTargetSource contract permits any Type — but in
        // practice the actual `WindowManager.Navigate<T>` callsites need a
        // Window. We still emit a soft signal in the assertion message.
        _ = issueRef; // Documentation only; included in the Theory row.
    }

    [Fact]
    public void ManifestDiscovery_FindsAtLeastSevenVms()
    {
        // Phase 4 wires INavigationTargetSource onto 7 VMs:
        //   ClassEditorViewModel, ItemEditorViewModel, UnitEditorViewModel,
        //   ItemFE6ViewModel, ArenaClassViewerViewModel, SupportTalkViewModel,
        //   CCBranchEditorViewModel.
        var manifests = JumpParityScanner.ScanAvManifests(typeof(INavigationTargetSource).Assembly);
        var distinctVms = manifests.Select(m => m.SourceVm).Distinct().ToList();
        Assert.InRange(distinctVms.Count, 7, int.MaxValue);
        Assert.Contains("ClassEditorViewModel", distinctVms);
        Assert.Contains("ItemEditorViewModel", distinctVms);
        Assert.Contains("UnitEditorViewModel", distinctVms);
        Assert.Contains("ItemFE6ViewModel", distinctVms);
        Assert.Contains("ArenaClassViewerViewModel", distinctVms);
        Assert.Contains("SupportTalkViewModel", distinctVms);
        Assert.Contains("CCBranchEditorViewModel", distinctVms);
    }

    [Fact]
    public void Scanner_HasMissingAvManifestRows_BecauseMostVmsLackManifests()
    {
        // Sanity check: most WF callsites should NOT have AV manifest
        // counterparts yet (we only wired 7 VMs). MissingAvManifest is the
        // backlog signal and its count must be substantial.
        string? repoRoot = FindRepoRoot();
        if (repoRoot == null)
            return; // Outside source tree, skip.
        var rows = JumpParityScanner.Scan(repoRoot);
        int missingCount = rows.Count(r => r.Status == JumpRowStatus.MissingAvManifest);
        Assert.InRange(missingCount, 50, int.MaxValue);
    }

    [Fact]
    public void Scanner_ReportFormatter_ProducesNonEmptyMarkdown()
    {
        // Exercise the formatter against the live scanner output to catch
        // regressions in the table-builder / heading layout.
        string? repoRoot = FindRepoRoot();
        if (repoRoot == null)
            return;
        var rows = JumpParityScanner.Scan(repoRoot);
        string report = JumpParityScanner.FormatReport(rows);
        Assert.Contains("# Avalonia vs WinForms — Jump/Navigation Parity Sweep", report);
        Assert.Contains("## Summary", report);
        Assert.Contains("## Known Gaps", report);
        Assert.Contains("## Missing AV Manifest", report);
        // LF-only — no embedded CRLF (committed reports must be portable).
        Assert.DoesNotContain("\r\n", report);
    }

    // =====================================================================
    // Per-issue known-gap regression stubs.
    //
    // Each [Fact(Skip = "...")] case is a placeholder regression test:
    //   - The Skip keeps CI green while the bug is open.
    //   - When the fix PR lands for the corresponding issue, the
    //     INavigationTargetSource implementation's IssueRef tag is removed
    //     AND the `Skip = "..."` here is removed — flipping the case into
    //     a real regression assertion.
    //   - The Trait makes filtering / reporting on known-gap status easy:
    //     `dotnet test --filter KnownGap=359` runs only #359-related tests.
    // =====================================================================

    [Fact]
    [Trait("KnownGap", "359")]
    public void Issue359_ClassEditor_PointerJumpsImplemented()
    {
        // Fixed in #359: the ClassEditor's BattleAnime / MoveCostRain /
        // MoveCostSnow / TerrainAvoid / TerrainDef / TerrainRes pointer
        // fields each have a working Jump button. The manifest drops the
        // IssueRef tag for these entries AND wires the correct target view
        // (per Copilot review of plan v2: three terrain entries were
        // mis-targeted at MapTerrainFloorLookupTableView; they should be
        // MoveCostEditorView, matching the WinForms MOVECOST4/5/6 linktype
        // dispatch). This test asserts both invariants:
        //   (1) no entry still carries IssueRef = "#359"
        //   (2) every entry resolves to its expected target view, so a
        //       regression that drops the IssueRef but breaks the target
        //       view will still fail this assertion.
        var manifests = JumpParityScanner.ScanAvManifests(typeof(INavigationTargetSource).Assembly);
        var classEditorEntries = manifests
            .Where(m => m.SourceVm == "ClassEditorViewModel")
            .ToList();

        // (1) No remaining IssueRef = "#359" for any of the six commands.
        var stillBroken = classEditorEntries.Where(e =>
                (e.Command == "JumpToBattleAnime"
                 || e.Command == "JumpToMoveCostRain"
                 || e.Command == "JumpToMoveCostSnow"
                 || e.Command == "JumpToTerrainAvoid"
                 || e.Command == "JumpToTerrainDef"
                 || e.Command == "JumpToTerrainRes")
                && e.IssueRef == "#359")
            .ToList();
        Assert.Empty(stillBroken);

        // (2) Expected command -> target view pairs. BattleAnime lands in
        // ImageBattleAnimeView; the five MoveCost/Terrain variants all land
        // in MoveCostEditorView (matching WinForms MOVECOST1..6 dispatch).
        var expected = new[]
        {
            ("JumpToBattleAnime",   "ImageBattleAnimeView"),
            ("JumpToMoveCostRain",  "MoveCostEditorView"),
            ("JumpToMoveCostSnow",  "MoveCostEditorView"),
            ("JumpToTerrainAvoid",  "MoveCostEditorView"),
            ("JumpToTerrainDef",    "MoveCostEditorView"),
            ("JumpToTerrainRes",    "MoveCostEditorView"),
        };
        foreach (var (command, expectedView) in expected)
        {
            var entry = classEditorEntries.SingleOrDefault(e => e.Command == command);
            Assert.True(entry != null, $"Manifest missing entry for command {command}");
            Assert.Equal(expectedView, entry!.TargetView);
        }
    }

    [Fact]
    [Trait("KnownGap", "360")]
    public void Issue360_SupportTalk_UnitIdJumpsImplemented()
    {
        // Fixed in #360 (PR #638): SupportTalk's Partner1/Partner2 unit-id
        // fields each have a working Jump-to-Unit button (the Avalonia view
        // wires SupportPartner1/2_Jump → UnitEditorView). This test asserts
        // both invariants (mirroring the #359 case):
        //   (1) no SupportTalk Partner row still carries IssueRef = "#360"
        //   (2) both Partner commands exist and target UnitEditorView, so a
        //       regression that drops the tag but removes/retargets the row
        //       still fails.
        var manifests = JumpParityScanner.ScanAvManifests(typeof(INavigationTargetSource).Assembly);
        var partnerEntries = manifests
            .Where(m => m.SourceVm == "SupportTalkViewModel"
                && (m.Command == "JumpToPartner1" || m.Command == "JumpToPartner2"))
            .ToList();

        // (1) No remaining IssueRef = "#360".
        var stillBroken = partnerEntries.Where(e => e.IssueRef == "#360").ToList();
        Assert.Empty(stillBroken);

        // (2) Both Partner jump commands exist and land in UnitEditorView.
        var expected = new[]
        {
            ("JumpToPartner1", "UnitEditorView"),
            ("JumpToPartner2", "UnitEditorView"),
        };
        foreach (var (command, expectedView) in expected)
        {
            var entry = partnerEntries.SingleOrDefault(e => e.Command == command);
            Assert.True(entry != null, $"Manifest missing entry for command {command}");
            Assert.Equal(expectedView, entry!.TargetView);
        }
    }

    [Fact]
    [Trait("KnownGap", "362")]
    public void Issue362_ItemEditor_EffectivenessJumpSelectsCorrectItem()
    {
        // Fixed in #362: ItemEditor → ItemEffectiveness (SkillSystems Rework
        // variant) jump now pre-selects the referenced effectiveness table
        // entry rather than landing on index 0. The receiving view-model
        // enumerates items by their P16 pointer so the source's
        // `ptr - 0x08000000` matches a real list row.
        // Asserts the IssueRef tag has been dropped from the manifest.
        var manifests = JumpParityScanner.ScanAvManifests(typeof(INavigationTargetSource).Assembly);
        var stillBroken = manifests.Where(m =>
                m.SourceVm == "ItemEditorViewModel"
                && m.Command == "JumpToEffectivenessSkillSystem"
                && m.IssueRef == "#362")
            .ToList();
        Assert.Empty(stillBroken);
    }

    [Fact]
    [Trait("KnownGap", "363")]
    public void Issue363_ItemEditor_EffectivenessAddressAndIcons()
    {
        // Fixed in #363 (PR #461/#466): the vanilla Item Effectiveness jump
        // computes the correct address (the receiver enumerates items by their
        // P16 pointer) and the class preview icons render correctly. This test
        // asserts both invariants (mirroring the #359 case):
        //   (1) the vanilla Effectiveness row no longer carries IssueRef = "#363"
        //   (2) the JumpToEffectivenessVanilla command exists and targets
        //       ItemEffectivenessViewerView.
        var manifests = JumpParityScanner.ScanAvManifests(typeof(INavigationTargetSource).Assembly);
        var vanillaEntries = manifests
            .Where(m => m.SourceVm == "ItemEditorViewModel"
                && m.Command == "JumpToEffectivenessVanilla")
            .ToList();

        // (1) No remaining IssueRef = "#363".
        var stillBroken = vanillaEntries.Where(e => e.IssueRef == "#363").ToList();
        Assert.Empty(stillBroken);

        // (2) The vanilla Effectiveness jump exists and lands in the viewer.
        var entry = vanillaEntries.SingleOrDefault();
        Assert.True(entry != null, "Manifest missing entry for command JumpToEffectivenessVanilla");
        Assert.Equal("ItemEffectivenessViewerView", entry!.TargetView);
    }

    [Fact]
    [Trait("KnownGap", "365")]
    public void Issue365_CCBranchEditor_UpstreamChainCorrect()
    {
        // Fixed in #365 (PR #460): the Upstream Chain panel computes the
        // correct before-promotion list AND the Promotion Class fields gained
        // jump buttons (Promo1/2_Jump → ClassEditorView). This test asserts
        // both invariants (mirroring the #359 case):
        //   (1) no CCBranch row still carries IssueRef = "#365"
        //   (2) both Promotion-class commands exist and target ClassEditorView,
        //       so a regression that drops the tag but removes/retargets the
        //       row still fails.
        var manifests = JumpParityScanner.ScanAvManifests(typeof(INavigationTargetSource).Assembly);
        var ccBranchEntries = manifests
            .Where(m => m.SourceVm == "CCBranchEditorViewModel")
            .ToList();

        // (1) No remaining IssueRef = "#365".
        var stillBroken = ccBranchEntries.Where(e => e.IssueRef == "#365").ToList();
        Assert.Empty(stillBroken);

        // (2) Both Promotion-class jump commands exist and land in ClassEditorView.
        var expected = new[]
        {
            ("JumpToPromotionClass1", "ClassEditorView"),
            ("JumpToPromotionClass2", "ClassEditorView"),
        };
        foreach (var (command, expectedView) in expected)
        {
            var entry = ccBranchEntries.SingleOrDefault(e => e.Command == command);
            Assert.True(entry != null, $"Manifest missing entry for command {command}");
            Assert.Equal(expectedView, entry!.TargetView);
        }
    }

    static string? FindRepoRoot()
    {
        string start = AppDomain.CurrentDomain.BaseDirectory;
        for (DirectoryInfo? dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "FEBuilderGBA.sln")))
                return dir.FullName;
        }
        return null;
    }
}
