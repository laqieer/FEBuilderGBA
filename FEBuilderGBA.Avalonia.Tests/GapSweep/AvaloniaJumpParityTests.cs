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

    [Fact(Skip = "Tracked in #359 — Pointers/Movement/Terrain fields need jump buttons.")]
    [Trait("KnownGap", "359")]
    public void Issue359_ClassEditor_PointerJumpsImplemented()
    {
        // When fixed: the ClassEditor's BattleAnime / MoveCostRain /
        // MoveCostSnow / TerrainAvoid / TerrainDef / TerrainRes pointer
        // fields will each have a working Jump button. The manifest will
        // drop the IssueRef tag for these entries, and the scanner will
        // classify them as Match instead of KnownGap.
        var manifests = JumpParityScanner.ScanAvManifests(typeof(INavigationTargetSource).Assembly);
        var classEditorEntries = manifests.Where(m => m.SourceVm == "ClassEditorViewModel").ToList();
        var stillBroken = classEditorEntries.Where(e =>
                e.Command is "JumpToBattleAnime"
                          or "JumpToMoveCostRain"
                          or "JumpToMoveCostSnow"
                          or "JumpToTerrainAvoid"
                          or "JumpToTerrainDef"
                          or "JumpToTerrainRes"
            && e.IssueRef == "#359")
            .ToList();
        Assert.Empty(stillBroken);
    }

    [Fact(Skip = "Tracked in #360 — id/address fields need jump/pick/preview.")]
    [Trait("KnownGap", "360")]
    public void Issue360_SupportTalk_UnitIdJumpsImplemented()
    {
        // When fixed: SupportTalk's Partner1/Partner2 unit-id fields will
        // each have a working Jump-to-Unit button.
        var manifests = JumpParityScanner.ScanAvManifests(typeof(INavigationTargetSource).Assembly);
        var stillBroken = manifests.Where(m =>
                m.SourceVm == "SupportTalkViewModel"
                && (m.Command == "JumpToPartner1" || m.Command == "JumpToPartner2")
                && m.IssueRef == "#360")
            .ToList();
        Assert.Empty(stillBroken);
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

    [Fact(Skip = "Tracked in #363 — Item Effectiveness jump uses wrong address + preview icons.")]
    [Trait("KnownGap", "363")]
    public void Issue363_ItemEditor_EffectivenessAddressAndIcons()
    {
        // When fixed: the vanilla Item Effectiveness jump will compute the
        // correct address and the class preview icons will render correctly.
        var manifests = JumpParityScanner.ScanAvManifests(typeof(INavigationTargetSource).Assembly);
        var stillBroken = manifests.Where(m =>
                m.SourceVm == "ItemEditorViewModel"
                && m.Command == "JumpToEffectivenessVanilla"
                && m.IssueRef == "#363")
            .ToList();
        Assert.Empty(stillBroken);
    }

    [Fact(Skip = "Tracked in #365 — CC Branch Editor shows wrong Upstream Chain.")]
    [Trait("KnownGap", "365")]
    public void Issue365_CCBranchEditor_UpstreamChainCorrect()
    {
        // When fixed: the Upstream Chain panel will compute the correct
        // before-promotion list AND the Promotion Class fields will gain
        // jump buttons.
        var manifests = JumpParityScanner.ScanAvManifests(typeof(INavigationTargetSource).Assembly);
        var stillBroken = manifests.Where(m =>
                m.SourceVm == "CCBranchEditorViewModel"
                && m.IssueRef == "#365")
            .ToList();
        Assert.Empty(stillBroken);
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
