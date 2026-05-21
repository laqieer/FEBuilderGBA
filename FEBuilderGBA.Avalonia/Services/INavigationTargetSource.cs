// SPDX-License-Identifier: GPL-3.0-or-later
// FEBuilderGBA gap-sweep tooling (#374) — Phase 4: headless navigation parity seam.
//
// The legacy WinForms GUI navigates between editors via
// `InputFormRef.JumpForm<TForm>(addressOrId)` — every cross-editor jump is a
// direct method call against a concrete generic type. The Avalonia GUI
// implements equivalent jumps via `WindowManager.Navigate<TView>(addr)` from
// the View code-behind, but the migration was "vibe coded" and several jump
// callsites were dropped, mis-wired to wrong targets, or pass the wrong
// address (issues #359, #360, #362, #363, #365).
//
// Live UIAutomation/FlaUI tests of the actual click handlers would be flaky
// in CI and add a Windows-only dependency to the Avalonia.Tests project (which
// is intentionally cross-platform). The chosen alternative is a DECLARATIVE
// MANIFEST SEAM: each ViewModel that exposes cross-editor jumps implements
// this interface to enumerate (CommandName, TargetViewType, TargetAddress)
// triples that mirror its actual navigation callsites. The Phase 4 scanner
// reads these manifests via reflection, cross-references them against the
// Roslyn-scanned WinForms `InputFormRef.JumpForm<T>(addr)` callsites, and
// emits a parity report.
//
// The interface is PURELY ADDITIVE — implementing it does NOT change any
// existing navigation behavior. The actual click handlers continue to call
// `WindowManager.Navigate<T>(addr)` as before; the manifest is a parallel
// declarative record of those callsites that test code and the scanner can
// introspect without driving the UI.
//
// Known broken jumps (tracked in issues #359/#360/#362/#363/#365) are encoded
// as manifest rows with a non-null `IssueRef` field. Phase 4 tests treat those
// as expected-skipped cases so CI stays green; when the fix PR for an issue
// lands, removing the `Skip` flag on the corresponding test flips it into a
// regression assertion.
using System;
using System.Collections.Generic;

namespace FEBuilderGBA.Avalonia.Services
{
    /// <summary>
    /// One declarative navigation target the ViewModel exposes — corresponds to a
    /// concrete `WindowManager.Navigate&lt;T&gt;(addr)` callsite (or to a known-broken
    /// callsite still pending fix). Used as a record so equality is structural,
    /// which simplifies test assertions over manifest contents.
    /// </summary>
    /// <param name="CommandName">
    /// Stable identifier for the navigation command — e.g. `"JumpToMoveCost"`,
    /// `"JumpToEffectiveness"`. Convention is the click-handler method name (sans
    /// `_Click`) so manifest entries trace cleanly to code. Used by the scanner's
    /// reports and by the test cross-reference to disambiguate multiple jumps
    /// originating from the same source editor.
    /// </param>
    /// <param name="TargetViewType">
    /// Concrete Avalonia View Type (e.g. `typeof(ClassEditorView)`) — the actual
    /// type passed to `WindowManager.Navigate&lt;T&gt;`. Tests assert this resolves to
    /// a real Avalonia view class so a renamed/deleted view fails fast at test
    /// time instead of at runtime.
    /// </param>
    /// <param name="TargetAddress">
    /// The address (or item index, depending on the editor's convention) that
    /// the navigation will pass to `NavigateTo`. NULL when the jump opens the
    /// target without pre-selecting (e.g. "open Patches list"). For dynamic
    /// addresses computed at click time, manifests should declare a sentinel
    /// like `0u` — the test asserts the target view exists, not that the
    /// address is correct (that's a runtime behavior test, out of scope here).
    /// </param>
    /// <param name="IssueRef">
    /// GitHub issue reference (e.g. `"#359"`) when this navigation is known to
    /// be broken or missing. The scanner reports these as `KnownGap` rows
    /// rather than `Match`; the test infrastructure renders them as `Skip`
    /// cases so CI stays green until the fix lands.
    /// </param>
    public record NavigationTarget(
        string CommandName,
        Type TargetViewType,
        uint? TargetAddress,
        string? IssueRef = null);

    /// <summary>
    /// Declarative manifest of the cross-editor jumps a ViewModel exposes.
    /// Implemented by ViewModels that surface jump buttons whose click handlers
    /// route through `WindowManager.Navigate&lt;T&gt;`. The implementation is purely
    /// metadata — instances of the VM do NOT need to be live (ROM loaded, etc.)
    /// for the manifest to be valid, so the scanner can instantiate VMs with the
    /// default parameterless constructor and call <see cref="GetNavigationTargets"/>
    /// without side effects.
    ///
    /// Implementations should:
    /// <list type="number">
    ///   <item>Return one entry per cross-editor jump callsite in the View code-behind.</item>
    ///   <item>NOT alter the actual navigation behavior — this is a declarative
    ///     parallel record.</item>
    ///   <item>Set <see cref="NavigationTarget.IssueRef"/> when the jump is tracked
    ///     by an open issue (known-broken), so Phase 4 tests render it as Skip.</item>
    /// </list>
    /// </summary>
    public interface INavigationTargetSource
    {
        /// <summary>
        /// Enumerate every cross-editor jump this ViewModel exposes. Each row
        /// mirrors a `WindowManager.Navigate&lt;T&gt;(addr)` callsite in the paired
        /// View code-behind. Order is not significant — Phase 4 sorts by command
        /// name for determinism. Implementations MUST return a stable list across
        /// instances (no random/conditional content) so reports diff cleanly.
        /// </summary>
        IReadOnlyList<NavigationTarget> GetNavigationTargets();
    }
}
