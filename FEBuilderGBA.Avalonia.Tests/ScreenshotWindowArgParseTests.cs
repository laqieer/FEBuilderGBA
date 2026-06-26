// SPDX-License-Identifier: GPL-3.0-or-later
// #1467 — guard the --screenshot-window / --screenshot-out flags added to
// App.ParseArgs for the PR screenshot proof. These render a single
// ROM-independent editor Window (the Log Viewer) to a PNG with the real
// desktop Skia rasterizer, so a no-ROM / locked environment can capture a real
// screenshot. The flags must round-trip through ParseArgs into the public
// static properties; assert that here (reflection, like the GapSweep parity
// tests) so a future arg-parse refactor can't silently drop them.
using System;
using System.Reflection;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests;

public class ScreenshotWindowArgParseTests : IDisposable
{
    readonly string? _savedName;
    readonly string? _savedOut;

    public ScreenshotWindowArgParseTests()
    {
        _savedName = App.ScreenshotWindowName;
        _savedOut = App.ScreenshotWindowOut;
        App.ScreenshotWindowName = null;
        App.ScreenshotWindowOut = null;
    }

    public void Dispose()
    {
        App.ScreenshotWindowName = _savedName;
        App.ScreenshotWindowOut = _savedOut;
    }

    [Fact]
    public void ParseArgs_PopulatesScreenshotWindowFlags()
    {
        MethodInfo? parse = typeof(App).GetMethod(
            "ParseArgs", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(parse);

        parse!.Invoke(null, new object[]
        {
            new[] { "--screenshot-window=LogViewerView", "--screenshot-out=out/log.png" },
        });

        Assert.Equal("LogViewerView", App.ScreenshotWindowName);
        Assert.Equal("out/log.png", App.ScreenshotWindowOut);
    }

    [Fact]
    public void ScreenshotWindowProperties_ArePublicStaticOnApp()
    {
        Type appType = typeof(App);
        Assert.NotNull(appType.GetProperty(
            nameof(App.ScreenshotWindowName), BindingFlags.Public | BindingFlags.Static));
        Assert.NotNull(appType.GetProperty(
            nameof(App.ScreenshotWindowOut), BindingFlags.Public | BindingFlags.Static));
    }
}
