// SPDX-License-Identifier: GPL-3.0-or-later
// Navigation manifest for SongTrackViewModel (#412 / #374 Phase 4).
//
// Mirrors the three wired WF `SongTrackForm` cross-editor jump callsites that
// the Avalonia view actually triggers after PR #412:
//   - SongExchangeButton_Click -> SongExchangeForm
//       (`InputFormRef.JumpForm<SongExchangeForm>(songId)` — passes the song
//        index)
//   - AllTracksLabel_Click     -> SongTrackAllChangeTrackForm
//       (`InputFormRef.JumpFormLow<SongTrackAllChangeTrackForm>()` then
//        `f.Init((uint)P4.Value, this.Tracks)` — WF passes the instrument-
//        set pointer + the full Tracks list. The Avalonia callsite passes
//        `_vm.InstrumentAddr` since `Navigate<T>(uint)` cannot carry a
//        Tracks payload today; surfacing Tracks requires extending the
//        navigation seam and is out of scope for #412.)
//   - TrackLabelN_Click        -> SongTrackChangeTrackForm
//       (`InputFormRef.JumpFormLow<SongTrackChangeTrackForm>()` then
//        `f.Init((uint)P4.Value, this.Tracks[no])` — WF passes the
//        instrument-set pointer + the single clicked track. The Avalonia
//        callsite passes the track's resolved DATA offset
//        (`TrackInfo.DataOffset`, mirroring WF's `SongUtil.Track.basepointer`).)
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
