// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using System.Reflection;
using FEBuilderGBA;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Tests.Unit
{
    /// <summary>
    /// #1924 — WinForms `Program.InitSystem()` captured `CoreState.TextEncoding`
    /// (from `OptionForm.textencoding()`) BEFORE `AutoUpdateTBLOption()` auto-detected
    /// the ROM's TBL and rewrote `Config["func_textencoding"]`, so `ReBuildSystemTextEncoder()`
    /// built the decoder from the PREVIOUS ROM's encoding. The first FE6 load (or any FE6 load
    /// preceded by a different-encoding ROM) decoded with the wrong table → garbage kana/kanji.
    ///
    /// The fix moves the capture to AFTER `AutoUpdateTBLOption()`. The invariant it restores:
    /// after a full `Program.LoadROM` (which runs `InitSystem`), the captured
    /// `CoreState.TextEncoding` must EQUAL the encoding auto-detected into
    /// `Config["func_textencoding"]` for the CURRENT ROM — not a stale prior value.
    ///
    /// This exercises the real WinForms load path end-to-end (like RebuildProducerWFParityTests),
    /// so it fails on the pre-fix capture-before-detect ordering. ROM-gated: skips cleanly when
    /// the (gitignored) FE6/FE8U ROMs are absent (e.g. CI).
    /// </summary>
    [Collection("SharedState")]
    public class FE6TextEncodingInitOrderTests
    {
        readonly ITestOutputHelper _output;
        public FE6TextEncodingInitOrderTests(ITestOutputHelper output) { _output = output; }

        [Fact]
        public void LoadRom_CapturesDetectedEncoding_NotStalePrior()
        {
            string? root = FindRepoRootWithRoms();
            if (root == null) { _output.WriteLine("SKIP: no ancestor has roms/FE6.gba"); return; }
            string fe6 = Path.Combine(root, "roms", "FE6.gba");

            var savedBaseDir = CoreState.BaseDirectory;
            var savedEnc = CoreState.TextEncoding;
            string? savedCfg = null;
            try
            {
                CoreState.BaseDirectory = root;
                ForceCommandLineMode();
                BootstrapWinFormsProgram(root);
                savedCfg = Program.Config.at("func_textencoding");

                // Baseline: a real FE6 load establishes Program.ROM + valid form state
                // (so the later ClearCacheDataCount doesn't NRE on a null ROM).
                Assert.True(Program.LoadROM(fe6, ""), "FE6 must load (baseline)");
                Assert.Equal(6, Program.ROM.RomInfo.version);

                // Simulate a stale prior state: a DIFFERENT encoding than FE6 auto-detects,
                // with the OptionForm cache cleared so the pre-fix early read (InitSystem, the
                // capture that USED to sit before AutoUpdateTBLOption) would pick up THIS value.
                // The reporter's real trigger is the PREVIOUS ROM's detected encoding; forcing
                // it makes the regression deterministic regardless of which sample ROMs exist.
                const int StalePrior = 5; // any value != FE6's detected TBL
                Program.Config["func_textencoding"] = StalePrior.ToString();
                InputFormRef.ClearCacheDataCount();

                // Reload FE6: InitSystem auto-detects FE6's TBL (rewriting Config away from the
                // stale value) and captures CoreState.TextEncoding AFTER that (the fix). Pre-fix
                // it captured the stale prior value BEFORE detection -> wrong table -> garbage.
                Assert.True(Program.LoadROM(fe6, ""), "FE6 must reload");
                Assert.Equal(6, Program.ROM.RomInfo.version);

                int detected = (int)U.atoi(Program.Config.at("func_textencoding"));
                _output.WriteLine($"stale={StalePrior}, FE6 detected={detected}, CoreState.TextEncoding={(int)CoreState.TextEncoding}");
                Assert.NotEqual(StalePrior, detected);                // detection changed it (regression scenario exercised)
                Assert.Equal(detected, (int)CoreState.TextEncoding);  // fix: captured == detected (pre-fix would be StalePrior)
            }
            finally
            {
                if (savedCfg != null) Program.Config["func_textencoding"] = savedCfg;
                InputFormRef.ClearCacheDataCount();
                CoreState.TextEncoding = savedEnc;
                CoreState.BaseDirectory = savedBaseDir;
            }
        }

        // ---- Test harness (mirrors RebuildProducerWFParityTests) ------------------

        static void ForceCommandLineMode()
        {
            try
            {
                PropertyInfo? p = typeof(Program).GetProperty(
                    "IsCommandLine", BindingFlags.Public | BindingFlags.Static);
                p?.SetValue(null, true, BindingFlags.NonPublic | BindingFlags.Instance, null, null, null);
            }
            catch { /* non-fatal: InitSystem falls back to the synchronous ClearCache */ }
        }

        static void BootstrapWinFormsProgram(string repoRoot)
        {
            Type prog = typeof(Program);
            PropertyInfo? baseDirProp = prog.GetProperty(
                "BaseDirectory", BindingFlags.Public | BindingFlags.Static);
            baseDirProp?.SetValue(null, repoRoot);

            PropertyInfo? configProp = prog.GetProperty(
                "Config", BindingFlags.Public | BindingFlags.Static);
            if (configProp != null && configProp.GetValue(null) == null)
            {
                Type? configType = prog.Assembly.GetType("FEBuilderGBA.ConfigWinForms");
                if (configType != null)
                {
                    object? cfg = Activator.CreateInstance(configType, nonPublic: true);
                    if (cfg != null)
                    {
                        MethodInfo? load = configType.GetMethod("Load", new[] { typeof(string) });
                        load?.Invoke(cfg, new object[] { Path.Combine(repoRoot, "config", "config.xml") });
                        configProp.SetValue(null, cfg);
                        CoreState.Config = (Config)cfg;
                    }
                }
            }
        }

        // The gitignored ROM lives in the user's main checkout, an ancestor of the worktree.
        static string? FindRepoRootWithRoms()
        {
            string? dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            for (int i = 0; i < 16 && dir != null; i++)
            {
                if (File.Exists(Path.Combine(dir, "FEBuilderGBA.sln"))
                    && File.Exists(Path.Combine(dir, "roms", "FE6.gba")))
                {
                    return dir;
                }
                dir = Path.GetDirectoryName(dir);
            }
            return null;
        }
    }
}
