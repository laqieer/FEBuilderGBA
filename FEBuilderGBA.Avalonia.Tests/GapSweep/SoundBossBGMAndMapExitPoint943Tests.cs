// SPDX-License-Identifier: GPL-3.0-or-later
// Issue #943 regression tests for two small Avalonia editor parity bugfixes:
//
//  Bug #16 — SoundBossBGMViewerViewModel (and the lockstep ListParityHelper
//  golden builder) used a fixed 8-digit hex for the song ID:
//    "Song:0x{songId:X08}"  (e.g. "Song:0x0000001B")
//  WinForms SoundBossBGMForm uses U.ToHexString (variable-width):
//    "{unitHex} {unitName} : {U.ToHexString(songId)}"  (e.g. "1B Eirika : 1B")
//  Fix: match WinForms format in both files; song-NAME resolution is a
//  separate global Avalonia gap (NameResolver returns a placeholder) and is
//  intentionally out of scope here.
//
//  Bug #9 — MapExitPointView.OnMapSelected left the detail panel showing the
//  previously-selected map's stale Address/coords/Direction/Flag when the
//  newly selected map had an EMPTY exit-point sub-list (SetItems on an empty
//  list fires no row-selection, so UpdateDetailUI never ran).
//  Fix: ClearExitPointEntry() + UpdateDetailUI() are called when exits.Count == 0.
//
// Marked [Collection("SharedState")] for the VM tests that mutate CoreState.ROM.
using System.IO;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests.GapSweep;

// ======================================================================
// Bug #16 — Boss BGM song-label format
// ======================================================================

/// <summary>
/// Source-scan assertions proving the #16 format fix is permanent — the
/// two files that must stay in lockstep contain the correct WinForms-style
/// format and do NOT contain the old fixed-8-digit hex variant.
/// These tests run without a real ROM so they pass everywhere (incl. CI).
/// </summary>
public class SoundBossBGMFormatTests
{
    static string FindRepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
            dir = Path.GetDirectoryName(dir);
        if (dir == null)
            throw new InvalidOperationException("Could not find FEBuilderGBA.sln from test base directory");
        return dir;
    }

    /// <summary>
    /// SoundBossBGMViewerViewModel must NOT contain the old "Song:0x{songId:X08}"
    /// format string that produced fixed-8-digit hex (e.g. "Song:0x0000001B").
    /// </summary>
    [Fact]
    public void ViewModel_DoesNotContainOldFixedHexFormat()
    {
        string repoRoot = FindRepoRoot();
        string path = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "ViewModels",
            "SoundBossBGMViewerViewModel.cs");
        Assert.True(File.Exists(path), $"File not found: {path}");
        string source = File.ReadAllText(path);
        Assert.DoesNotContain("Song:0x{songId:X08}", source);
        Assert.DoesNotContain("Song:0x{songId:X07}", source);
    }

    /// <summary>
    /// SoundBossBGMViewerViewModel MUST contain the WinForms-matching format:
    /// " : {U.ToHexString(songId)}" (variable-width, colon separator).
    /// </summary>
    [Fact]
    public void ViewModel_ContainsWinFormsStyleFormat()
    {
        string repoRoot = FindRepoRoot();
        string path = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "ViewModels",
            "SoundBossBGMViewerViewModel.cs");
        string source = File.ReadAllText(path);
        // The exact corrected interpolation.
        Assert.Contains(" : {U.ToHexString(songId)}", source);
    }

    /// <summary>
    /// ListParityHelper.BuildSoundBossBGMList must NOT contain the old
    /// "Song:0x{songId:X08}" format string (lockstep with the VM).
    /// </summary>
    [Fact]
    public void ListParityHelper_DoesNotContainOldFixedHexFormat()
    {
        string repoRoot = FindRepoRoot();
        string path = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Services",
            "ListParityHelper.cs");
        Assert.True(File.Exists(path), $"File not found: {path}");
        string source = File.ReadAllText(path);
        // The method name is BuildSoundBossBGMList — assert the specific old format.
        Assert.DoesNotContain("Song:0x{songId:X08}", source);
    }

    /// <summary>
    /// ListParityHelper.BuildSoundBossBGMList MUST contain the WinForms-matching
    /// format to stay in lockstep with SoundBossBGMViewerViewModel.
    /// </summary>
    [Fact]
    public void ListParityHelper_ContainsWinFormsStyleFormat()
    {
        string repoRoot = FindRepoRoot();
        string path = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Services",
            "ListParityHelper.cs");
        string source = File.ReadAllText(path);
        Assert.Contains(" : {U.ToHexString(songId)}", source);
    }
}

// ======================================================================
// Bug #9 — MapExitPoint stale detail on empty sub-list
// ======================================================================

/// <summary>
/// Unit tests for MapExitPointViewModel.ClearExitPointEntry (#9).
/// These do NOT require a ROM — they exercise the VM in isolation.
/// </summary>
[Collection("SharedState")]
public class MapExitPointClearEntryTests
{
    /// <summary>
    /// ClearExitPointEntry must zero all detail fields and gate CanWrite=false.
    /// This guards against the stale-data regression where selecting a map with
    /// an empty sub-list left the detail panel showing the previous map's values.
    /// </summary>
    [Fact]
    public void ClearExitPointEntry_ZerosAllDetailFields_AndDisablesWrite()
    {
        var vm = new MapExitPointViewModel();

        // Simulate previously-loaded row (non-zero values from a prior selection).
        vm.CurrentAddr = 0x08801234u;
        vm.SelectedAddressDisplay = 0x08801234u;
        vm.BlockSize = 4u;
        vm.ExitX = 0x12u;
        vm.ExitY = 0x34u;
        vm.EscapeMethod = 0x03u;
        vm.FlagId = 0x07u;
        vm.CanWrite = true;

        // Act: switching to a map with no exit points clears the detail panel.
        vm.ClearExitPointEntry();

        // Assert: all detail fields are zeroed.
        Assert.Equal(0u, vm.CurrentAddr);
        Assert.Equal(0u, vm.SelectedAddressDisplay);
        Assert.Equal(0u, vm.BlockSize);
        Assert.Equal(0u, vm.ExitX);
        Assert.Equal(0u, vm.ExitY);
        Assert.Equal(0u, vm.EscapeMethod);
        Assert.Equal(0u, vm.FlagId);
        // Write must be gated (no stale save).
        Assert.False(vm.CanWrite);
    }

    /// <summary>
    /// ClearExitPointEntry is idempotent — calling it on an already-zeroed VM
    /// must leave all fields at zero and CanWrite=false without throwing.
    /// </summary>
    [Fact]
    public void ClearExitPointEntry_OnAlreadyZeroedVm_IsIdempotent()
    {
        var vm = new MapExitPointViewModel();
        // All fields are 0 / false by default after construction.

        vm.ClearExitPointEntry(); // must not throw

        Assert.Equal(0u, vm.CurrentAddr);
        Assert.Equal(0u, vm.SelectedAddressDisplay);
        Assert.Equal(0u, vm.BlockSize);
        Assert.Equal(0u, vm.ExitX);
        Assert.Equal(0u, vm.ExitY);
        Assert.Equal(0u, vm.EscapeMethod);
        Assert.Equal(0u, vm.FlagId);
        Assert.False(vm.CanWrite);
    }

    /// <summary>
    /// ClearExitPointEntry must NOT touch the filter/map state:
    /// FilterIndex, SelectedMapSlotAddr, IsBlank, IsAllocated are
    /// orthogonal to the per-row detail panel.
    /// </summary>
    [Fact]
    public void ClearExitPointEntry_DoesNotTouchMapSelectionState()
    {
        var vm = new MapExitPointViewModel();
        vm.FilterIndex = 1u;
        vm.SelectedMapSlotAddr = 0x00800004u;
        vm.IsBlank = true;
        vm.IsAllocated = false;

        vm.ClearExitPointEntry();

        // Map-selection state is untouched.
        Assert.Equal(1u, vm.FilterIndex);
        Assert.Equal(0x00800004u, vm.SelectedMapSlotAddr);
        Assert.True(vm.IsBlank);
        Assert.False(vm.IsAllocated);
    }
}
