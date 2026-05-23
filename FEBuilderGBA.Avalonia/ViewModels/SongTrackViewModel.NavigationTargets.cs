// SPDX-License-Identifier: GPL-3.0-or-later
// Navigation manifest for SongTrackViewModel (#412 / #374 Phase 4).
//
// Mirrors the three wired WF `SongTrackForm` cross-editor jump callsites that
// the Avalonia view actually triggers after PR #412:
//   - SongExchangeButton_Click -> SongExchangeForm
//       (`InputFormRef.JumpForm<SongExchangeForm>(songId)` — passes the song
//        index)
//   - AllTracksLabel_Click     -> SongTrackAllChangeTrackForm
//       (`InputFormRef.JumpFormLow<SongTrackAllChangeTrackForm>()` — modal
//        dialog scoped to the current song's header address)
//   - TrackLabelN_Click        -> SongTrackChangeTrackForm
//       (`InputFormRef.JumpFormLow<SongTrackChangeTrackForm>()` — modal
//        dialog scoped to the clicked track index)
//
// The remaining three WF callsites — `SongTrackImportMidiForm`,
// `SongTrackImportWaveForm`, `SongTrackImportSelectInstrumentForm` — are
// launched implicitly from `ImportButton_Click` based on file extension. The
// AV view does NOT yet wire these flows (the underlying SongUtil.Import*
// paths remain WinForms-coupled), so the manifest deliberately omits them.
// `JumpParityScanner` will report those three callsites as
// `MissingAvManifest`, which is the truthful state. This matches the
// `INavigationTargetSource` contract requirement that every manifest row
// mirror a real, wired AV navigation callsite (Copilot CLI plan review v1
// concern #2 — manifest entries must not lie about behavior). When a future
// PR wires the import dispatch path, three new rows are added to this
// manifest in the same PR.
//
// `TargetAddress: 0u` is the sentinel for "manifest declares the jump but
// the runtime address is determined at click time" — matches the precedent
// used by `EventUnitViewModel.NavigationTargets.cs`.
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.Views;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public partial class SongTrackViewModel : INavigationTargetSource
    {
        public IReadOnlyList<NavigationTarget> GetNavigationTargets()
        {
            return new[]
            {
                new NavigationTarget(
                    CommandName: "JumpToSongExchange",
                    TargetViewType: typeof(SongExchangeView),
                    TargetAddress: 0u),
                new NavigationTarget(
                    CommandName: "JumpToSongTrackAllChangeTrack",
                    TargetViewType: typeof(SongTrackAllChangeTrackView),
                    TargetAddress: 0u),
                new NavigationTarget(
                    CommandName: "JumpToSongTrackChangeTrack",
                    TargetViewType: typeof(SongTrackChangeTrackView),
                    TargetAddress: 0u),
            };
        }
    }
}
