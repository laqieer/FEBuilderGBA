// SPDX-License-Identifier: GPL-3.0-or-later
// #1124 / #1859 — AutoSaveService sidecar path redirect. Desktop keeps the sidecar
// next to the ROM; mobile (Android content:// / iOS security-scoped URI, no writable
// parent) redirects into app-private {CoreState.BaseDirectory}/autosave. The internal
// overload injects the mobile flag + base dir so the mobile branch is exercised on a
// desktop test host (reached via the existing InternalsVisibleTo seam in the csproj).
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
        string result = AutoSaveService.ComputeSidecarPath(rom, isMobile: false, baseDir: "/ignored");

        string expectedDir = Path.GetDirectoryName(rom)!;
        string expected = Path.Combine(expectedDir, "FE8U.autosave.gba");
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Mobile_RedirectsIntoBaseDirectoryAutosave()
    {
        string baseDir = Path.Combine(Path.GetTempPath(), "febuilder_autosave_test");
        // SAF content:// / iOS security-scoped URIs have no meaningful local parent
        // dir; only the file name matters for the redirected sidecar.
        string rom = "content://com.android.providers/document/FE8U.gba";
        string result = AutoSaveService.ComputeSidecarPath(rom, isMobile: true, baseDir: baseDir);

        string expected = Path.Combine(baseDir, "autosave", "FE8U.autosave.gba");
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NullOrEmptyRomFilename_ReturnsNull(bool isMobile)
    {
        Assert.Null(AutoSaveService.ComputeSidecarPath(null, isMobile, "/base"));
        Assert.Null(AutoSaveService.ComputeSidecarPath("", isMobile, "/base"));
    }

    [Fact]
    public void PublicOverload_OnDesktopHost_MatchesDesktopBranch()
    {
        // The desktop test host has OperatingSystem.IsAndroid()/IsIOS() == false, so
        // the public single-arg overload must equal the explicit desktop branch.
        string rom = Path.Combine("roms", "FE8U.gba");
        string viaPublic = AutoSaveService.ComputeSidecarPath(rom);
        string viaDesktopBranch = AutoSaveService.ComputeSidecarPath(rom, isMobile: false, baseDir: CoreState.BaseDirectory);
        Assert.Equal(viaDesktopBranch, viaPublic);
    }
}
