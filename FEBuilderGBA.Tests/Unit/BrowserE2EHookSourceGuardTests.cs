using System;
using System.IO;

namespace FEBuilderGBA.Tests.Unit;

public class BrowserE2EHookSourceGuardTests
{
    [Fact]
    public void BrowserTestHooks_are_entirely_E2E_HOOKS_gated()
    {
        string source = File.ReadAllText(Path.Combine(RepoRoot(), "FEBuilderGBA.Browser", "TestHooks.cs"));

        Assert.StartsWith("#if E2E_HOOKS", source.TrimStart('\uFEFF', '\r', '\n', ' ', '\t'));
        Assert.Contains("public static partial class TestHooks", source, StringComparison.Ordinal);
        Assert.Contains("[JSExport]", source, StringComparison.Ordinal);
        Assert.EndsWith("#endif", source.TrimEnd(), StringComparison.Ordinal);
    }

    [Fact]
    public void MainJs_exposes_test_hooks_only_when_e2e_query_flag_is_set()
    {
        string source = File.ReadAllText(Path.Combine(RepoRoot(), "FEBuilderGBA.Browser", "wwwroot", "main.js"));

        int e2eFlag = source.IndexOf("get('e2e') === '1'", StringComparison.Ordinal);
        int guardedBlock = source.IndexOf("if (e2e)", StringComparison.Ordinal);
        int hookExpose = source.IndexOf("globalThis.__febTest = ex.TestHooks", StringComparison.Ordinal);
        int runMain = source.IndexOf("await dotnetRuntime.runMain", StringComparison.Ordinal);

        Assert.True(e2eFlag >= 0, "main.js should parse the ?e2e=1 query flag.");
        Assert.True(guardedBlock > e2eFlag, "__febTest setup should be gated by if (e2e).");
        Assert.True(hookExpose > guardedBlock, "__febTest should be assigned only inside the e2e block.");
        Assert.True(hookExpose < runMain, "Assembly exports must be acquired before runMain.");
    }

    static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "FEBuilderGBA.Browser"))
                && Directory.Exists(Path.Combine(dir.FullName, "FEBuilderGBA.Tests")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
