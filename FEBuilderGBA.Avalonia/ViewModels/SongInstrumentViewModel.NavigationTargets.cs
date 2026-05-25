// SPDX-License-Identifier: GPL-3.0-or-later
// Navigation manifest for SongInstrumentViewModel (#387 / #374 Phase 4).
//
// `SongInstrumentForm` has exactly ONE WF cross-editor jump callsite:
//   - `ImportGBAWave` -> `InputFormRef.JumpFormLow<SongInstrumentImportWaveForm>()`
//     (launched when the user clicks any of the N00/N08/N10/N18
//     ImportButton controls to choose a .wav file).
//
// The Avalonia view does NOT yet wire this flow (the underlying GBA-wave
// import path remains WinForms-coupled — file dialog + raw-byte
// dispatch). The manifest is deliberately empty, mirroring the
// SongTrack precedent (PR #558) for deferred WF-only flows.
//
// `JumpParityScanner` will report the single WF callsite as
// `MissingAvManifest`, which is the truthful state — matches the
// `INavigationTargetSource` contract requirement that every manifest row
// mirror a real, wired AV navigation callsite. When a future PR wires
// the WindowManager.Navigate path, one row is added to this manifest in
// the same PR.
using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public partial class SongInstrumentViewModel : INavigationTargetSource
    {
        public IReadOnlyList<NavigationTarget> GetNavigationTargets()
        {
            return Array.Empty<NavigationTarget>();
        }
    }
}
