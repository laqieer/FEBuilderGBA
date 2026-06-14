// SPDX-License-Identifier: GPL-3.0-or-later
// #1121 — Android multi-target. These desktop-focused tests guard the
// refactor that moved the GapSweep-type-dependent App methods into the
// desktop-only App.GapSweep.cs partial. They assert the desktop build is
// behaviourally unchanged: the GapSweep static properties still live on App,
// ParseArgs still populates them, and RunGapSweepStandalone (now in the
// partial, reached via InternalsVisibleTo) still returns null for non-gap-sweep
// args. The Tests project targets net9.0, the same TFM the desktop build uses,
// so this exercises exactly the code path the desktop ships.
using System;
using System.Reflection;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests;

/// <summary>
/// Desktop-parity guard for the #1121 GapSweep partial extraction. None of
/// these touch the Avalonia application object — they reach the static
/// gap-sweep surface directly via the <c>InternalsVisibleTo</c> seam.
/// </summary>
public class AndroidMultiTargetDesktopParityTests : IDisposable
{
    // Snapshot + restore the gap-sweep statics so ordering with other
    // suites (RunAllSweepTests etc.) can't leak state between tests.
    readonly string? _mode;
    readonly string? _out;
    readonly string? _repoRoot;

    public AndroidMultiTargetDesktopParityTests()
    {
        _mode = App.GapSweepMode;
        _out = App.GapSweepOut;
        _repoRoot = App.GapSweepRepoRoot;
        App.GapSweepMode = null;
        App.GapSweepOut = null;
        App.GapSweepRepoRoot = null;
    }

    public void Dispose()
    {
        App.GapSweepMode = _mode;
        App.GapSweepOut = _out;
        App.GapSweepRepoRoot = _repoRoot;
    }

    [Fact]
    public void GapSweep_static_properties_still_exposed_on_App()
    {
        // The static gap-sweep surface stays in App.axaml.cs (desktop + the
        // properties are plain string?/bool, assigned by ParseArgs). If the
        // refactor accidentally moved one into the android-excluded partial
        // without the desktop TFM seeing it, the desktop build would break;
        // assert each property still resolves on the App type.
        Type appType = typeof(App);
        foreach (string name in new[]
                 {
                     nameof(App.GapSweepMode),
                     nameof(App.GapSweepOut),
                     nameof(App.GapSweepDryRun),
                     nameof(App.GapSweepRepoRoot),
                     nameof(App.GapSweepWfDir),
                     nameof(App.GapSweepAvDir),
                     nameof(App.GapSweepRomTag),
                     nameof(App.GapSweepLanguages),
                 })
        {
            PropertyInfo? prop = appType.GetProperty(name, BindingFlags.Public | BindingFlags.Static);
            Assert.True(prop != null, $"App.{name} should remain a public static property on the desktop build.");
        }
    }

    [Fact]
    public void RunGapSweepStandalone_returns_null_for_non_gapsweep_args()
    {
        // The desktop entry point (Program.Main) calls this first; null means
        // "no gap-sweep flag, continue normal Avalonia boot". The method lives
        // in the desktop-only App.GapSweep.cs partial now — this proves it is
        // still reachable + behaves identically for the normal launch path.
        int? code = App.RunGapSweepStandalone(new[] { "--rom", "foo.gba" });
        Assert.Null(code);
    }

    [Fact]
    public void RunGapSweepStandalone_returns_null_for_empty_args()
    {
        int? code = App.RunGapSweepStandalone(Array.Empty<string>());
        Assert.Null(code);
    }

    [Fact]
    public void GapSweep_flag_parsing_sets_mode_via_RunGapSweepStandalone_path()
    {
        // A gap-sweep flag without --out must NOT return null (it enters the
        // sweep), and must surface a non-success exit code (2 = missing --out)
        // rather than launching the UI. This confirms ParseArgs (still in
        // App.axaml.cs) and RunGapSweep (now in the partial) are wired together
        // on the desktop TFM after the split.
        int? code = App.RunGapSweepStandalone(new[] { "--gap-sweep-density" });
        Assert.NotNull(code);
        Assert.Equal(2, code!.Value); // "--gap-sweep-* requires --out=<path>"
        Assert.Equal("density", App.GapSweepMode);
    }
}
