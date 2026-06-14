// SPDX-License-Identifier: GPL-3.0-or-later
// #1124 — AutoSaveService sidecar path redirect. Desktop keeps the sidecar next
// to the ROM; Android (content:// ROM, no writable parent) redirects into
// app-private {CoreState.BaseDirectory}/autosave. The internal overload injects
// the platform flag + base dir so the Android branch is exercised on a desktop
// test host (reached via the existing InternalsVisibleTo seam in the csproj).
using System.IO;
using FEBuilderGBA.Avalonia.Services;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests;

public class AutoSaveSidecarPathTests
{
    [Fact]
    public void Desktop_KeepsSidecarBesideRom()
    {
        string rom = Path.Combine("roms", "FE8U.gba");
        string result = AutoSaveService.ComputeSidecarPath(rom, isAndroid: false, baseDir: "/ignored");

        string expectedDir = Path.GetDirectoryName(rom)!;
        string expected = Path.Combine(expectedDir, "FE8U.autosave.gba");
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Android_RedirectsIntoBaseDirectoryAutosave()
    {
        string baseDir = Path.Combine(Path.GetTempPath(), "febuilder_autosave_test");
        // SAF content:// URIs have no meaningful local parent dir; only the file
        // name matters for the redirected sidecar.
        string rom = "content://com.android.providers/document/FE8U.gba";
        string result = AutoSaveService.ComputeSidecarPath(rom, isAndroid: true, baseDir: baseDir);

        string expected = Path.Combine(baseDir, "autosave", "FE8U.autosave.gba");
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NullOrEmptyRomFilename_ReturnsNull(bool isAndroid)
    {
        Assert.Null(AutoSaveService.ComputeSidecarPath(null, isAndroid, "/base"));
        Assert.Null(AutoSaveService.ComputeSidecarPath("", isAndroid, "/base"));
    }

    [Fact]
    public void PublicOverload_OnDesktopHost_MatchesDesktopBranch()
    {
        // The desktop test host has OperatingSystem.IsAndroid() == false, so the
        // public single-arg overload must equal the explicit desktop branch.
        string rom = Path.Combine("roms", "FE8U.gba");
        string viaPublic = AutoSaveService.ComputeSidecarPath(rom);
        string viaDesktopBranch = AutoSaveService.ComputeSidecarPath(rom, isAndroid: false, baseDir: CoreState.BaseDirectory);
        Assert.Equal(viaDesktopBranch, viaPublic);
    }
}
