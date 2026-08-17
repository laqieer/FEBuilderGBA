using System;
using System.IO;

namespace FEBuilderGBA.Avalonia.Tests;

/// <summary>
/// Regression guard for #2098: the desktop host must opt into
/// ShutdownMode.OnMainWindowClose so closing the shell does not leave
/// secondary editor windows orphaned under the default OnLastWindowClose mode.
/// </summary>
public class DesktopLifetimeShutdownModeTests
{
    static string RepoRoot()
    {
        string dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
            dir = Path.GetDirectoryName(dir)!;
        return dir ?? throw new InvalidOperationException("Cannot find solution root");
    }

    [Fact]
    public void Program_StartsDesktopLifetime_WithMainWindowCloseShutdownMode()
    {
        string repo = RepoRoot();
        string program = File.ReadAllText(Path.Combine(repo, "FEBuilderGBA.Avalonia", "Program.cs"));

        Assert.Contains("StartWithClassicDesktopLifetime(args, ShutdownMode.OnMainWindowClose)", program);
    }
}
